---
name: authoring-promptugui-xml
description: Use when authoring or editing PromptUGUI `.ui.xml` files (XML-driven uGUI for Unity 6+) — defining `<Screen>` / `<Template>` / `<Variant>`, anchor / size / margin layout, built-in controls, or `<Icon>` / i18n markup. For C# event / data wire-up (`Get<T>`, R3, `BindItems`), see scripting-promptugui-csharp; for loading `.ui.xml` / `.po` / icons via Unity Addressables, see using-promptugui-addressables.
---

# Authoring PromptUGUI `.ui.xml`

PromptUGUI is a Unity 6+ package that turns compact XML files into runtime uGUI hierarchies. The description file is **pure structure + named handles** — no logic, no data binding expressions. All event/data wiring happens C#-side via `Get<T>(id)` and R3 `Observable<T>`; see the **scripting-promptugui-csharp** skill for that side.

This skill covers everything you need to write or edit a `.ui.xml` correctly. Read top-to-bottom once; afterwards the **Quick Reference** at the end is enough.

## Validation & feedback loop (run after every write)

Every `.ui.xml` write MUST be verified before reporting the work done. Three steps, in order — each catches a different layer of mistake:

### 1. XSD validate every `.ui.xml`

```
xmllint --noout --schema Assets/PromptUGUI.gen.xsd <path/to/your.ui.xml>
```

- Default schema location: `Assets/PromptUGUI.gen.xsd`. It's generated from the user's `ControlRegistry` (so it knows their custom C# controls) plus a project-wide scan for `<Template name="...">` definitions (so Template invocations like `<TitledPanel/>` are recognized too).
- **Auto-regen on `.ui.xml` save**: Unity's AssetPostprocessor regenerates the XSD whenever any `.ui.xml` is added/moved/deleted. As long as you call `refresh_unity` after editing, `xmllint` will see fresh Template tags. **C# control registration changes are NOT auto-picked-up** — for those, ask the user to run `Tools → PromptUGUI → Schema → Generate XSD`.
- If user not install unity mcp, u can ignore template tags error in XSD.
- **If the file does not exist, STOP.** Tell the user (in their language) to run the Editor menu `Tools → PromptUGUI → Schema → Generate XSD`.

### 2. UIXmlLint CLI (catches semantic mistakes XSD can't express)

```
dotnet run --project .lint/UIXmlLint -- <path/to/your.ui.xml>
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/   # 整个目录递归
```

- No Unity required — pure .NET, runs anywhere `dotnet` is installed.
- Surfaces context-dependent rules that XSD can't easily express, e.g. **`anchor` / `margin` on a direct child of `<VStack>` / `<HStack>` / `<Grid>`** (`PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`). Unity logs these as warnings (so `UI.Open()` doesn't break), but the CLI promotes them to errors with non-zero exit code so they don't slip through.
- Exit 0 = clean. Exit 1 = at least one parse error or rule violation; STOP and fix before reporting done.
- Rule code lives in `Runtime/Core/Lint/` and is shared with `ScreenInstantiator`'s warning path — same logic, one source of truth.

### 3. Unity MCP live feedback

XSD catches structural errors and a couple of identity constraints — element/attribute names, attribute patterns (`<Icon name>`), and **duplicate `id=` within the same Screen / Template body** (via `xs:unique`). Unity still catches the rest — parser semantic errors (anchor/size conflicts, missing `ref=`, Template namespace clashes), runtime hot-reload errors.

After every `.ui.xml` write:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error","warning"])
# Notice: this is a CoplayDev/unity-mcp, user may use official unity mcp.
```

**If MCP for Unity is unavailable** (call fails / no Unity instance): Note that the Unity MCP connection is prone to disconnection; therefore, we must first take the following steps:

- Check the user's MCP configuration files. If no Unity MCP installation is detected, issue a warning to the user indicating that MCP for Unity needs to be installed; however, this should be treated strictly as a warning—do not halt operations.
- If an installation is detected, this indicates that the user has not launched Unity or the MCP server. In this case, you must **STOP** and instruct the user to open the Unity Editor and ensure that the MCP server is running.

**DO NOT USE** `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")` unless the user explicitly allows it during an alignment step — pops a modal confirmation dialog in Unity ("Are you sure you want to reimport all assets..."). The MCP call itself returns immediately, but **every subsequent MCP call will be blocked by the unclosed modal** until someone manually dismisses it in the Unity window. Recovering from an accidental trigger requires user intervention.

## File anatomy

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Import src="common/Buttons" as="ui"/>
  <Screen   name="MainMenu"> ... </Screen>
  <Template name="TitledPanel"> ... </Template>
</PromptUGUI>
```

| Element                                                | Role                                                      | Notes                                                                                                                                                                                                        |
| ------------------------------------------------------ | --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `<PromptUGUI version="1">`                             | Root, **always**.                                         | NOT `<UI>`. `version="1"` is required.                                                                                                                                                                       |
| `<Import src="..." [as="ns"]/>`                        | Pull templates from another file.                         | Top-level only. `as=` adds namespace prefix.                                                                                                                                                                 |
| `<Screen name="..." [canvas="..."] [reference="..."]>` | A complete UI scene; opened by code with `UI.Open(name)`. | One Screen = one Canvas. Names unique across all loaded files. `canvas="overlay\|camera\|world"`, default `overlay`. Optional `reference="WxH"` (+ `.variant`) switches CanvasScaler to ScaleWithScreenSize. |
| `<Template name="...">`                                | Reusable subtree, expanded at parse time.                 | Body must have **exactly one root element**.                                                                                                                                                                 |

`<Import>`, `<Screen>`, `<Template>` are the **only** elements allowed at the top level. Comments use standard `<!-- -->`.

## Built-in primitives (14)

**默认视觉主题**：白底 sliced + #323232 深字（与 Unity 6 `GameObject → UI → …` 创建出来的标准 prefab 一致）。所有控件的颜色/sprite 都能通过 `color=` / `sprite=` 属性 override；想要彻底深色主题项目级覆写 `ProceduralBuilders` 的常量，或用 Variant 方式 `color.dark="..."`。

Pre-registered on `UI.Registry`. Use as XML tags by name:

| Tag            | Notes                                                                                                                                                                                                                                                                                   | Tag-specific attributes                                                                                                                                                                                                                                                                                                                            |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<Frame>`      | Empty container; optional `RectMask2D` via `mask="rect"`.                                                                                                                                                                                                                               | `mask` (`rect`), `maskPadding` (`T,R,B,L`, "\_" placeholder; only with `mask="rect"`)                                                                                                                                                                                                                                                              |
| `<SafeArea>`   | Stretches to `Screen.safeArea` (notch / status bar / home indicator). Auto-reacts to rotation, window resize, Device Simulator. **Rejects** `anchor` / `size` / `width` / `height` / `margin` / `pivot` (incl. `.variant`); see "Safe area" section below.                              | —                                                                                                                                                                                                                                                                                                                                                  |
| `<Image>`      | uGUI Image; loads sprites from `Resources`. Optional `RectMask2D` via `mask="rect"`, or stencil `Mask` via `mask="self"` (Image's own sprite becomes the mask shape).                                                                                                                   | `sprite`, `color` (`#RRGGBB[AA]`), `type` (`simple` / `sliced` / `tiled` / `filled`; **omit to auto-pick `sliced` when sprite has a non-zero border, else `simple`**), `mask` (`rect` / `self`), `showMask` (bool, default `true`; only with `mask="self"`), `maskPadding` (`T,R,B,L`; only with `mask="rect"`)                                    |
| `<Text>`       | TMP_Text. Has text-content shorthand: `<Text>Hello</Text>` ≡ `<Text text="Hello"/>`.                                                                                                                                                                                                    | `text`, `fontSize` (int), `color`, `align` (`left` / `center` / `right`), `wrap` (bool), `raycastTarget` (bool), `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table)                                |
| `<VStack>`     | Vertical layout group. Default `childAlign="upper-center"` (cross-axis centered).                                                                                                                                                                                                       | `spacing` (float), `padding` (`T,R,B,L` 1/2/4 components; `"_"` = 0 placeholder, e.g. `padding="6,_,_,_"`), `childAlign` (`upper/middle/lower-left/center/right`; `center` alias for `middle-center`)                                                                                                                                              |
| `<HStack>`     | Horizontal layout group. Default `childAlign="middle-left"` (cross-axis centered).                                                                                                                                                                                                      | Same as VStack.                                                                                                                                                                                                                                                                                                                                    |
| `<Grid>`       | Grid layout group, fixed columns.                                                                                                                                                                                                                                                       | `columns` (int), `cellSize` (`WxH`), `spacing` (single or `H,V`), `padding`                                                                                                                                                                                                                                                                        |
| `<Btn>`        | Image + Button + R3 `OnClick`. `<Btn>开始</Btn>` shorthand creates an internal TMP label child. Use as **template root** or registered prefab tag for any clickable. **不写 size 时自动按文字宽 + 左右 16 padding、上下 max(44, 文字高+12) 自适应**；无 text（icon-only）回退到 80×44。 | `color`, `sprite`, `fontSize` (int, applied to the auto-label only; other Text attrs like `align` / `wrap` require an explicit `<Text>` child), `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table) |
| `<Icon>`       | Sprite from a project-level SpriteSet; by-name lookup, package-time pruning.                                                                                                                                                                                                            | `name` (required, `ns:icon-name`), `color` (`#RRGGBB[AA]`), `size` (`WxH` / `native`; 拉伸用 `anchor="stretch"`)                                                                                                                                                                                                                                   |
| `<Toggle>`     | Image + uGUI Toggle + auto label. R3 `OnValueChanged: bool`. `<Toggle>静音</Toggle>` shorthand sets the label. Same `group=` name → mutual exclusion. **不要给单个 Toggle 写 `group=`** — uGUI ToggleGroup 默认要求至少一个 active，单成员组一旦点上就锁死。                            | `text`, `isOn` (bool, default false), `group` (string, mutual-exclusion key), `color`, `sprite` (Resources path for checkmark sprite), `font`                                                                                                                                                                                                      |
| `<Slider>`     | Image + uGUI Slider. R3 `OnValueChanged: float`.                                                                                                                                                                                                                                        | `min` (float), `max` (float), `value` (float), `wholeNumbers` (bool), `direction` (`horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical`), `color`, `sprite`                                                                                                                                                                       |
| `<Dropdown>`   | TMP_Dropdown. R3 `OnSelected: int`. Options pushed C#-side via `BindOptions(...)`.                                                                                                                                                                                                      | `value` (int initial index), `color`, `sprite`, `font`                                                                                                                                                                                                                                                                                             |
| `<ScrollList>` | ScrollRect + Mask. Items pushed C#-side via `BindItems(...)`. `itemTemplate` references a `<Template name=...>` or registered Control class.                                                                                                                                            | `itemTemplate` (required tag name), `direction` (`vertical` / `horizontal`), `spacing` (float), `padding`, `color`, `sprite`                                                                                                                                                                                                                       |
| `<InputField>` | TMP_InputField；R3 `OnValueChanged` / `OnEndEdit` / `OnSubmit: string`。`<InputField>初始文本</InputField>` 短手设 `text=`。                                                                                                                                                            | `text`, `placeholder`, `contentType` (`standard`/`autocorrected`/`integer-number`/`decimal-number`/`alphanumeric`/`name`/`email`/`password`/`pin`/`custom`), `lineType` (`single`/`multi-newline`/`multi-submit`), `characterLimit` (int), `readOnly` (bool), `color`, `sprite`, `font`, `tr` (placeholder)/`ctx`                                  |

`<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` are reference implementations. For project-specific differentiation (pixel border, press feedback, custom popup chrome) subclass and override `OnAttached` — see scripting-promptugui-csharp.

### `<Icon>`

References a sprite from a project-level SpriteSet (shared icons, by-name lookup, package-time pruning).

```xml
<Icon name="ui:settings" color="#ffffff"/>
<Icon name="art:gold-coin" size="48x48"/>
<Icon name="ui:bell" color.dark="#fff"/>
```

| Attribute | Required | Default   | Notes                                                                                                                                                                                                                                                                                                                |
| --------- | -------- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `name`    | yes      | —         | Format `ns:icon-name`. `ns` (set name) is strict `[A-Za-z0-9_-]+`; `icon-name` mirrors the filesystem path under `sourceFolder` (no extension) — `/`-separated, may contain spaces, `&`, parens, commas, apostrophes, etc. Only the `:` delimiter is forbidden. Example: `solar:Bold Duotone/Map & Location/Radar 2` |
| `color`   | no       | `#ffffff` | Multiply tint on the underlying Image. White preserves a colored PNG; non-white tints a mono-mask PNG                                                                                                                                                                                                                |
| `size`    | no       | `native`  | Numeric / `WxH` / `native` (Icon-only — reads sprite pixel dimensions). For "fill the parent" use `anchor="stretch"` (free-positioning) or wrap the Icon in a V/HStack and use `width="stretch"` / `height="stretch"` (LayoutGroup)                                                                                  |

**Discovering available icons** — to find which `setName:icon-name` combinations are valid in the current project, run from the project root:

```bash
# 1) List every SpriteSet (setName → source folder)
find . -name "*.asset" -not -path "*/Library/*" -not -path "*/Temp/*" \
  -exec grep -l "PromptUGUI.Application.SpriteSet" {} \; 2>/dev/null \
| while IFS= read -r f; do
    n=$(grep -m1 "^  setName:" "$f" | awk '{print $2}')
    g=$(grep -m1 "^  sourceFolder:" "$f" | grep -oP 'guid: \K[a-f0-9]+')
    if [ -n "$g" ]; then
      p=$(grep -rl "^guid: $g$" --include="*.meta" . 2>/dev/null | head -1)
      echo "$n -> ${p%.meta}"
    else
      echo "$n -> (sourceFolder not set)"
    fi
  done
# example: solar -> Samples~/MainMenu/Icons

# 2) Search a known SpriteSet by keyword (relative path under sourceFolder, no extension)
cd <sourceFolder> && find . -iname "*<keyword>*.png" | sed 's|^\./||; s|\.png$||'
```

Icon name in XML = PNG path **relative to the SpriteSet's sourceFolder**, with `/` as separator and no extension. So `Arrow Right.png` directly under a set with `setName: solar` is `<Icon name="solar:Arrow Right"/>`; `Combat/heart.png` is `<Icon name="ui:Combat/heart"/>`. The bare basename (`ui:heart`) is also accepted as a shortcut **as long as it is unambiguous across the source folder** — when two PNGs in different subfolders share a basename you must use the path form, and the sync tool will error pointing at the candidates if XML still references the bare name. External packs (Font Awesome, Solar Icons, etc.) drop in as a folder of PNGs; create an SpriteSet ScriptableObject (`Create → PromptUGUI → Sprite Set`) pointing at it, set `setName`, then `Tools → PromptUGUI → Sprite → Sync Atlases (All Sets)` packs only the icons referenced from `.ui.xml` (plus `SpriteSet.alwaysInclude` entries).

**Variant overrides on literal `<Icon>`**: `<Icon name="ui:sun" name.dark="ui:moon"/>` — the scanner reads both `name` and every `name.<variant>` value, so each candidate sprite is packed.

**Template-Param-driven icon names**: the sync tool follows two recognized substitution shapes inside a `<Template>` body (also applies to `name.<variant>` overrides):

- Full placeholder — `<Icon name="{{iconName}}"/>`. Treats each invocation arg (`<MyIcon iconName="solar:Bell Bing"/>`) as a complete `set:icon` ref. Param `default=` also counts.
- Partial placeholder — `<Icon name="solar:{{x}}"/>`. Treats each invocation arg as the icon-name half, paired with the literal `solar` set.

Anything else inside a Template body (`{{a}}:{{b}}`, `solar:{{a}}-{{b}}`, multi-placeholder) is unanalyzable — the syncer logs a warning. Same for forwarded args (one Template's Param fed verbatim into another's). For unanalyzable cases, list final values in `SpriteSet.alwaysInclude`. Outside a `<Template>` (a literal `<Icon name="ui:{{x}}"/>` directly in a Screen) is always unanalyzable too.

### Safe area

Mobile devices have unsafe insets — notch, status bar, home indicator, gesture bar. Wrap your UI in `<SafeArea>` to stay inside `Screen.safeArea`. Backgrounds that should bleed to the device edges stay outside, as siblings of `<SafeArea>`:

```xml
<Screen name="Login">
  <Image id="bg" anchor="stretch" color="#0B1828"/>
  <SafeArea>
    <HStack id="brandBar" anchor="top-left" width="320" height="56" margin="24,_,_,24">
      ...
    </HStack>
  </SafeArea>
</Screen>
```

Rules:

- `<SafeArea>` is always stretched to the safe area; it does **not** accept `anchor`, `size`, `width`, `height`, `margin`, or `pivot` (including their `.variant` override forms). Writing any of those is a parse error.
- To add inner padding inside the safe area, wrap content in `<Frame anchor="stretch" margin="..."/>` _inside_ the `<SafeArea>`.
- Place `<SafeArea>` as a direct child of `<Screen>`. Nesting another `<SafeArea>` inside one is harmless but redundant (the inner one collapses to the outer one's rect).
- Don't put `<SafeArea>` inside `<VStack>` / `<HStack>` / `<Grid>` — the layout group will override its anchor math.
- Reacts automatically to screen rotation, window resize, and Unity 6's Device Simulator. No code-side wiring needed.

## uGUI 对照表

每个 PromptUGUI tag 在运行时落成一组 GameObject + Unity 组件。调试时在 Hierarchy 里按这张表把 XML 节点和实际 GO 对上；理解控件原理时把 XML 当成「这套 GO + 组件 + 默认绑线」的简写。

**Tag → GO 结构 / 组件**

| Tag            | 根节点组件                                                                                               | 自动子节点                                                                                                                                                                   | R3 事件源                                                                                          |
| -------------- | -------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `<Frame>`      | `RectTransform` 单独                                                                                     | —                                                                                                                                                                            | —                                                                                                  |
| `<Image>`      | `Image` + (lazy) `PointerEventRelay`（被 hover/press trigger 引用为源时挂上）                            | —                                                                                                                                                                            | `OnPointerEnter` / `OnPointerExit` / `OnPointerDown` ← Relay                                       |
| `<Text>`       | `TextMeshProUGUI`                                                                                        | —                                                                                                                                                                            | —                                                                                                  |
| `<VStack>`     | `VerticalLayoutGroup`（硬编码 `childControlWidth/Height=true`、`childForceExpand*=false`）               | —                                                                                                                                                                            | —                                                                                                  |
| `<HStack>`     | `HorizontalLayoutGroup`（同 VStack）                                                                     | —                                                                                                                                                                            | —                                                                                                  |
| `<Grid>`       | `GridLayoutGroup`（`constraint=FixedColumnCount`）                                                       | —                                                                                                                                                                            | —                                                                                                  |
| `<Btn>`        | `Image` + `Button`（`targetGraphic=Image`）+ (lazy) `PointerEventRelay`                                  | `Label`(`TMP_Text`, stretch 撑满) — **lazy**：写了 `text=` 才创建                                                                                                            | `OnClick` ← `Button.onClick`；`OnPointerEnter/Exit/Down` ← Relay                                   |
| `<Icon>`       | `Image`（`preserveAspect=true`, `raycastTarget=false`）                                                  | —                                                                                                                                                                            | —                                                                                                  |
| `<Toggle>`     | `Toggle`（`targetGraphic=Background`, `graphic=Checkmark`）                                              | `Background`(`Image`, left-middle 锚 20×20) → 内嵌 `Checkmark`(`Image`, 居中 20×20)；`Label`(`TMP_Text`, 右侧水平 stretch)                                                   | `OnValueChanged` ← `Toggle.onValueChanged`                                                         |
| `<Slider>`     | `Slider`                                                                                                 | `Background`(`Image`)；`Fill Area` → `Fill`(`Image`)；`Handle Slide Area` → `Handle`(`Image`)                                                                                | `OnValueChanged` ← `Slider.onValueChanged`                                                         |
| `<Dropdown>`   | `Image` + `TMP_Dropdown`                                                                                 | `Label` + `Arrow` + `Template`（默认 inactive，内含 `Viewport` / `Content` / `Item` / `Scrollbar` 完整下拉子树）                                                             | `OnSelected` ← `TMP_Dropdown.onValueChanged`                                                       |
| `<ScrollList>` | `Image` + `ScrollRect`                                                                                   | `Viewport`(`Image` + `Mask` stencil) → `Content`(V/H `LayoutGroup` + `ContentSizeFitter`)；按 `direction` 再加一个 `Scrollbar`                                               | 无独立事件；C# 端 `BindItems(...)` 推数据                                                          |
| `<InputField>` | `Image` + `TMP_InputField`                                                                               | `Text Area`(`RectMask2D`) → `Placeholder`(`TMP_Text`, italic 半透明) + `Text`(`TMP_Text`)                                                                                    | `OnValueChanged` / `OnEndEdit` / `OnSubmit` ← `TMP_InputField.*`                                   |
| `<SafeArea>`   | `RectTransform` + `SafeAreaTracker`（内部 `MonoBehaviour`，订阅设备 safeArea / 旋转 / Device Simulator） | —                                                                                                                                                                            | —                                                                                                  |
| `<Trigger>`    | `RectTransform` 单独（无视觉、无 layout 行为，仅作 wrapper 划定事件源 scope）                            | —                                                                                                                                                                            | `OnFire` ← R3 `Subject<Unit>`，由 `on=`（open/loop/click/hover-enter/hover-exit/press/manual）触发 |
| `<Animation>`  | `RectTransform` + `CanvasGroup`（继承自 Trigger；CanvasGroup 给 `fade=` 用，由 `ApplyCommon` 懒加载）    | `_offsetProxy`(`RectTransform`，anchor stretch、margin=0、pivot=0.5,0.5) — XML 子节点全 parent 到这一层；LitMotion 驱动它的 anchoredPosition / localScale / localEulerAngles | `OnFire` ← 继承 Trigger；同时由 `on=` 触发 LitMotion `MotionHandle[]`                              |

**Common attribute → uGUI 落点**（实现在 `Control.ApplyCommon`；对所有 tag 生效，`<SafeArea>` 例外，整套 anchor/size/margin/pivot 都被拒绝）

| XML                         | uGUI 落点                                                                                                                                                                                                                                                                                                           |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `anchor`                    | `RectTransform.anchorMin` / `anchorMax`，并按 anchor 推导默认 `pivot`                                                                                                                                                                                                                                               |
| `size` / `width` / `height` | 父级不是 LayoutGroup：经 `MarginResolver` 写到 `RectTransform.sizeDelta`。父级是 `<VStack>` / `<HStack>`：写到 `LayoutElement.preferredWidth` / `preferredHeight` + 对应 `flexible*=0`（按轴路由，未写的轴留 `-1` 哨兵）。父级是 `<Grid>`：**被 GridLayoutGroup 接管**（cellSize 由 parent 决定，子节点写了也无视） |
| `margin`                    | `RectTransform.anchoredPosition` + `sizeDelta`（`MarginResolver` 按 anchor 自动反号；stretched 轴专门吃 margin）                                                                                                                                                                                                    |
| `pivot="x,y"`               | `RectTransform.pivot`（不写则从 anchor 推）                                                                                                                                                                                                                                                                         |
| `hidden="true"`             | `GameObject.SetActive(false)`                                                                                                                                                                                                                                                                                       |
| `interactable="false"`      | `CanvasGroup.interactable=false` + `blocksRaycasts=false`（首次访问按需 add `CanvasGroup`；级联到所有后代，比 `Selectable.interactable` 范围更大）                                                                                                                                                                  |

**不变量与易踩坑**

- 纯容器（`<Frame>` / `<*Stack>` / `<Grid>` / `<SafeArea>`）根上**没有** `Image`，本身不可见 —— 想要底色得自己塞一个 `<Image anchor="stretch"/>` 子节点。
- `<Btn>` 的 Label 是 lazy：写 `<Btn/>`（无 `text=`、无子 `<Text>`、无内联文本）不会有 Label 子 GO；之后 C# 设 `BtnInstance.Text = "x"` 才会现场补一个。
- `<Toggle>` 的 `targetGraphic` / `graphic` 在 `OnAttached` 内已绑死（Background / Checkmark），外部别再设；`group=` 不直接绑 Unity `ToggleGroup`，而是落到 `Screen.ToggleGroups.GetOrCreate(name)` 这个 Screen 范围的共享池里。
- `<ScrollList>` 的 item 子节点在 `OnAttached` 阶段是空的，必须在 C# 端 `BindItems(observable, (slot, item) => ...)` 之后才出现；hot-reload 后也要重新 Bind。
- `font="<type>"` 不是字体文件路径，而是 `PromptUGUISettings.fonts[]` 登记的**字体类型 key**（如 `"default"` / `"title"`），通过 `ResolveFont(locale, type)` 才解析到 `TMP_FontAsset`，并在 `UI.Locale.Changed` 时自动重赋。
- 内置 `<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>` 的 `sprite=` 走 `UI.ResolveSprite(value)` 双语法分流:含 `:` 的值(`sprite="ui:dialog"`)走 `UI.SpriteResolver` → SpriteSet/atlas 通道(包时按 XML scan 剪枝);无 `:` 的值(`sprite="ui/dialog"`)走 `Resources.Load<Sprite>(value)`(适合一次性 / 原型期 sprite)。bare path 支持 `#sliceName` 后缀,从多 sprite 切片纹理里按名取子 sprite,例如 `sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"` 走 `Resources.LoadAll<Sprite>(path)` 找 `name==sliceName`;`#` 之前的 `.png`/`.jpg`/`.jpeg`/`.tga`/`.psd` 扩展名会被剥掉,写不写后缀都行。`<Icon>` 仍强制 `ns:name` 形式,只走 SpriteResolver 通道。自定义 Control subclass 用同一 `UI.ResolveSprite(value)` 入口即可。
- `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` 是参考实现 —— 想要像素描边、按下反馈、自定义下拉 chrome，subclass 并 override `OnAttached`，不要改这几个 Control 本体。

## Common attributes (any tag)

| Attribute                  | Format                       | Notes                                                                                                                                                                                     |
| -------------------------- | ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id="..."`                 | string                       | Unique within Screen / Template instance scope. Lift to dedicated handle for `Get<T>`.                                                                                                    |
| `anchor="..."`             | preset                       | See "Anchor system" below. Default `top-left`.                                                                                                                                            |
| `size="WxH"`               | `240x80`                     | Both dimensions in pixels (numeric only — keywords `stretch` / `N%` are **not** accepted here, use per-axis attrs). **Forbidden on stretched axes.**                                      |
| `width="W"` / `height="H"` | float / `stretch[*N]` / `N%` | Numeric is base. `stretch` / `stretch*N` is LayoutGroup-only — see "Stretch keyword". `N%` is free-positioning-only — see "Fractional %". **Numeric forbidden on stretched anchor axes.** |
| `margin="..."`             | 1/2/4 floats                 | "Distance from anchor inward, positive". `"_"` = 0 placeholder.                                                                                                                           |
| `pivot="x,y"`              | `0..1, 0..1`                 | Defaults derive from `anchor`; rarely needed.                                                                                                                                             |
| `hidden="true"`            | bool                         | Initial `SetActive(false)`.                                                                                                                                                               |
| `interactable="false"`     | bool                         | Initial `CanvasGroup.interactable=false` + `blocksRaycasts=false`.                                                                                                                        |

`padding` and `spacing` are **NOT** universal — only on `<VStack>` / `<HStack>` / `<Grid>`.

`anchor` and `margin` are **NOT** available on `<VStack>` / `<HStack>` / `<Grid>`.

**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes: controls that report an intrinsic content size (`<Btn>`、`<Icon>`) auto-attach a `LayoutElement` with that size as preferred (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44); controls without intrinsic size (e.g. `<Image>` 没 sprite 时) get no `LayoutElement` and fall back to whatever their components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.

**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. Omitting all size attributes + `anchor` 不 stretch + 控件有 intrinsic content size（`<Btn>`、`<Icon>`）→ `sizeDelta` 默认为 native content size（避免 0×0 不可见）。其他控件保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。

**Stretch keyword** (LayoutGroup-only) — `width="stretch"` / `height="stretch"` on a V/HStack child maps to `LayoutElement.preferredX=0, flexibleX=1`. The LayoutGroup grows the child to fill that axis.

- Multiple sibling stretches share remaining space by equal weight (`flexibleX` is additive). Two `stretch` siblings → each gets half.
- **Weighted form** `stretch*N` for non-equal splits. `<Frame width="stretch"/> <Btn width="stretch*2"/> <Frame width="stretch"/>` gives a 1:2:1 weight split → 25/50/25. `N` must be > 0 (e.g. `stretch*0.5` halves the weight).
- Forbidden outside V/HStack (parse error). Use `anchor="X-stretch"` + margin for free-positioning, or `N%` for fractional sizing.
- Variant-overridable: `width="240" width.mobile="stretch"` flips between fixed and flex.

**Fractional `%`** (free-positioning only) — `width="50%"` / `height="33.3%"` on a child of `<Frame>` / `<Screen>` / `<SafeArea>` maps to uGUI's native anchor fractions. The `anchor=` preset decides where in the parent the fraction sits:

| `anchor` horizontal   | `width="50%"` becomes                         |
| --------------------- | --------------------------------------------- |
| `*-left`              | anchorMin.x=0, anchorMax.x=0.5 (left half)    |
| `*-center` / `center` | anchorMin.x=0.25, anchorMax.x=0.75 (centered) |
| `*-right`             | anchorMin.x=0.5, anchorMax.x=1 (right half)   |

Vertical: same idea (`top` → upper, `bottom` → lower, `center` → middle).

```xml
<Frame anchor="top-stretch" height="60">
  <Btn anchor="center"      width="50%" height="46"/>             <!-- 50% wide, centered -->
  <Btn anchor="center-left" width="30%" height="46" margin="0,16,0,16"/>  <!-- left 30% minus 16px each side -->
</Frame>
```

- Range `(0%, 100%]`. `0%` / `>100%` are parse errors (almost always a typo); `100%` is allowed but equivalent to `anchor=stretch` on that axis.
- `margin` further insets _within_ the fractional range (so `width="50%" margin="0,16"` = 50% minus 32px total, still centered).
- Forbidden inside `<VStack>` / `<HStack>` / `<Grid>` (parse error with guidance). LayoutGroup is weight-based, not percentage-based — use `stretch*N` + spacer siblings there.
- Forbidden combined with `anchor="X-stretch"` on the same axis (existing "stretched-axis can't have width" rule).

**Inside `<Grid>`**, the parent's `cellSize` is authoritative — a child's `size` is silently ignored.

**Cross-axis alignment** of layout-group children is set on the parent via `childAlign` (defaults: VStack `upper-center`, HStack `middle-left`). Override the whole group, not per child — uGUI LayoutGroup doesn't support per-child cross-axis alignment.

## Anchor system: 4×4 grid

`anchor="<vertical>-<horizontal>"`:

|             | left         | center         | right         | stretch        |
| ----------- | ------------ | -------------- | ------------- | -------------- |
| **top**     | top-left     | top-center     | top-right     | top-stretch    |
| **center**  | center-left  | center         | center-right  | center-stretch |
| **bottom**  | bottom-left  | bottom-center  | bottom-right  | bottom-stretch |
| **stretch** | stretch-left | stretch-center | stretch-right | stretch        |

Aliases: `center` = `center-center`; `stretch` = `fill` = `stretch-stretch`.

**Hard rule (parse-time error if violated):** if an axis is `stretch`, you MUST use `margin` for that axis and MUST NOT supply `size` / `width` / `height` for it.

```xml
<!-- Top-right corner button, 16px from edges, 240x80 -->
<Btn anchor="top-right" size="240x80" margin="16"/>

<!-- Top toolbar, full width, 64px tall, 8px horizontal margin -->
<Frame anchor="top-stretch" height="64" margin="0,8,_,8"/>

<!-- Right side panel, full height, 200px wide -->
<Frame anchor="stretch-right" width="200" margin="16,0,16,_"/>

<!-- Full-screen background -->
<Image anchor="stretch" sprite="bg/main"/>

<!-- INVALID — stretched axis with size: parse error -->
<Frame anchor="top-stretch" size="200x64"/>
```

`margin` semantics: always **inward from the anchor**, regardless of which corner. `top-right margin="16"` = 16px down + 16px left. The implementation handles sign conversion internally.

## Templates

```xml
<Template name="TitledPanel">
  <Param name="title"/>
  <Param name="closable" default="true"/>

  <VStack padding="16" spacing="8">
    <HStack height="32">
      <Text fontSize="20">{{title}}</Text>
      <Btn if="{{closable}}" id="close" color="#888888"/>
    </HStack>
    <Slot/>
  </VStack>
</Template>
```

Rules:

- `<Param>` must come **before** any body element. `default` makes the parameter optional; missing default = required.
- Body must have **exactly one** root element (here: the outer `<VStack>`).
- `{{paramName}}` substitutes inside attribute values and text content. **Pure string substitution** — no expressions, no `{{a + b}}`.
- `if="{{p}}"` drops the element when the substituted value is falsy (empty, `false`, `0`, `null`). Only `if=` is allowed; no `else`, no `for`.
- `<Slot/>` appears **at most once**. Children passed at the call site replace it.

**Calling a template** is identical to using a built-in:

```xml
<TitledPanel id="bagPanel" anchor="center" size="600x400"
             title="背包" closable="true">
  <Grid columns="6" spacing="4" cellSize="64x64">
    <Image sprite="icon/sword"/>
    <Image sprite="icon/shield"/>
  </Grid>
</TitledPanel>
```

The grid (and its Image children) are injected at the `<Slot/>` position.

## Variants: runtime layout switching

Variants are named flags, **toggled C#-side** with `UI.Variants.Set("mobile", true)` (see scripting-promptugui-csharp). Multiple flags can be active simultaneously. Toggling re-applies attributes on all open Screens **without rebuilding GameObjects**.

### Inline override — 90% of usage

Append `.variantName` to **any** attribute. The base value applies when no variant is active; per-variant values override:

```xml
<VStack id="menu"
        anchor="center" size="480x320"
        anchor.mobile="bottom-stretch"
        size.mobile="" height.mobile="400"
        margin.mobile="_,16,80,16">
  <Btn size="240x64"
       size.mobile="" width.mobile="stretch" height.mobile="72">开始</Btn>
</VStack>
```

The `size.mobile=""` clears the base `size=` under that variant — required because mobile flips one axis to anchor-stretch (`anchor.mobile="bottom-stretch"`), which forbids any width on the same axis. Per-axis `width.mobile=` / `height.mobile=` then provide the new dimensions cleanly.

**Last-active-wins** — declaration order matters. With `<X size="100" size.mobile="200" size.tablet="150"/>`, if both `mobile` and `tablet` are active, `tablet` wins because it was declared after.

Variant overrides on `<Icon name="...">` swap the sprite at runtime: `<Icon name="ui:sun" name.dark="ui:moon"/>`.

### Block form — only `<Add>`

For inserting elements per variant (no `Remove`, no `Replace` — use `hidden.var="true"` and inline overrides instead):

```xml
<Screen name="Game">
  <Frame id="root" anchor="stretch"/>

  <Variant when="mobile">
    <Add into="#root" at="end">
      <Image id="virtualJoystick" anchor="bottom-left"
             size="160x160" margin="_,_,40,40"/>
    </Add>
  </Variant>

  <Variant when="pc">
    <Add into="#root">
      <Image id="minimap" anchor="bottom-right"
             size="200x200" margin="_,16,16,_"/>
    </Add>
  </Variant>
</Screen>
```

`<Add>`:

- `into="#id"` targets a node by id; `into="@root"` targets the Screen root.
- `at="start" | "end" | <integer>` — defaults to `"end"`.
- Strategy: instantiated **once on first activation**, then only `SetActive`-toggled. Subscriptions and references survive variant flips.

### Variants you CANNOT vary

- `id` — identity must be stable
- The tag name itself
- `<Param default>` values

Trying to write `id.mobile="..."` or `default.mobile="..."` is a parse error.

## i18n & Fonts (XML markup)

Source text goes directly inside `<Text>` / `<Btn>` and serves as the msgid for extraction. Translation happens at runtime — see the **scripting-promptugui-csharp** skill for the `UI.Locale.Set` / `UI.Tr` C# calls that switch language, and the **using-promptugui-addressables** skill if your `.po` files ship via Addressables.

```xml
<!-- Source text = msgid; zero keys -->
<Text>Start Game</Text>
<Btn>Settings</Btn>

<!-- Do not translate -->
<Text tr="false">{{playerName}}</Text>

<!-- Same msgid, different meanings; ctx becomes msgctxt -->
<Btn ctx="door">Open</Btn>
<Btn ctx="file-menu">Open</Btn>

<!-- Font type comes from Settings; default is "default" -->
<Text font="title">Settings</Text>
<Text font="damage" fontSize="96">9999!</Text>

<!-- Combined with the existing Variant system -->
<Text font="title" font.zh-Hans="title-cn">Settings</Text>
```

**Reserved variant namespace**: the library auto-manages two namespaces — authors must NOT reuse these names for business state:

- **Locale**: `UI.Locale.Set("zh-Hans")` internally registers `zh-Hans` (any locale code passed to `UI.Locale.Set`) as an active Variant.
- **Orientation**: `portrait` and `landscape` are toggled automatically by a global tracker based on `Screen.width` vs `Screen.height` (equal dims → `landscape`, matching the CanvasScaler `match` auto-derivation). They are mutually exclusive. Use them as overrides — e.g. `<Screen reference="1920x1080" reference.portrait="1080x1920">`, `<Btn width="240" width.portrait="stretch"/>`. Portrait-locked games can ignore them (base values apply when no override exists, `landscape` overrides simply never fire). Users who want to fully self-manage can set `UI.Orientation.AutoTrack = false`.

### Inline sprites / TMP rich text

`<Text>` does not allow mixing text + child elements by default. To inline TMP tags like `<sprite>` / `<color>`, wrap them in CDATA:

```xml
<Text><![CDATA[Gold: <sprite name="coin"/>{{count}}]]></Text>
<Text><![CDATA[<color=#ff0>Warning</color>: out of stock]]></Text>
```

The extractor pulls each CDATA block as a single complete msgid; runtime translation preserves the tags.

## Import & namespaces

```xml
<Import src="common/Buttons"/>          <!-- merge templates into local namespace -->
<Import src="common/Panels" as="ui"/>   <!-- prefix-qualified -->

<Screen name="X">
  <PrimaryButton/>          <!-- from Buttons (unqualified) -->
  <ui.TitledPanel/>         <!-- from Panels, must use prefix -->
</Screen>
```

- `src` is an opaque key passed to the user's `UI.SourceResolver` (`Func<string, Awaitable<string>>`) — could be a Resources path, an Addressables key, anything. The library never touches the filesystem itself.
- Imports merge **recursively**; cycles are detected.
- Imported files cannot contain `<Screen>` — only `<Template>`.
- Same-named templates from two imports without `as=` → conflict error. Resolve with `as="ns"` on one of them.

There's also a **commons pool** populated C#-side that's merged into every Screen automatically — see scripting-promptugui-csharp.

## Canvas / scaler attributes on `<Screen>`

```xml
<Screen name="MainMenu" canvas="overlay" reference="1920x1080">...</Screen>

<!-- 横屏 PC + 竖屏手机一份 XML -->
<Screen name="MainMenu"
        reference="1920x1080"
        reference.mobile="1080x1920">...</Screen>
```

- `canvas="overlay|camera|world"`, default `overlay`. Picks the runtime `Canvas.renderMode` for this Screen. Everything else (worldCamera, sortingOrder) is configured C#-side via `UI.CanvasConfigurator`.
- `reference="WxH"` → CanvasScaler 切到 `ScaleWithScreenSize`，referenceResolution 即该值。`matchWidthOrHeight` 按朝向自动推断：W ≥ H 锁宽（0），H > W 锁高（1）。
- 未设 / `reference=""` → 保留默认 `ConstantPixelSize, scaleFactor=1` 行为；XML 数字直接 = 设备像素。
- `.variant` 形态：`reference.mobile="..."` 同其他属性 variant 规则；变体切换时 CanvasScaler 立即重应用。
- 要 `match=0.5` 折中或改 `referencePixelsPerUnit`：走 `UI.CanvasConfigurator` 手改。**不要在两条路径同时改 CanvasScaler** —— variant flip 时 XML 路径会覆盖 configurator 的改动。

## Mask & clipping

PromptUGUI never auto-enables masking — you must opt in via `mask=`. Two reasons: (1) stencil Mask isn't free (extra SetPass call, breaks batching with elements outside the mask); (2) "decorative background that lets children overflow" is a legit, common pattern.

| Want                                                          | Recipe                                                                                                                             | Component used                          |
| ------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| Pure container, no clip                                       | `<Frame/>` (current default)                                                                                                       | none                                    |
| Cheap rectangular clip (viewport-style)                       | `<Frame mask="rect"/>` or `<Image mask="rect" sprite="..."/>`                                                                      | `RectMask2D`                            |
| Sprite-shape clip + sprite drawn (rounded card)               | `<Image sprite="round" mask="self"/>`                                                                                              | stencil `Mask`, `showMaskGraphic=true`  |
| Sprite-shape clip + sprite hidden (viewport with shaped mask) | `<Image sprite="round-mask" mask="self" showMask="false"/>`                                                                        | stencil `Mask`, `showMaskGraphic=false` |
| Decorated outer frame + different inner clip shape            | Nest two `<Image>` — outer has `sprite=` only; inner has `mask="self" sprite=` (different shape) + `margin=` to control inner size | none on outer, stencil on inner         |

**Variant overrides** on `mask` / `showMask` / `maskPadding` are rejected in v1 (`PUI-MASK-VARIANT`) — switching mask mode means `AddComponent`/`Destroy` at runtime, which we don't support. If you need per-variant clipping, split into two Screens or use `<Add into=…>`.

## Common mistakes (XML)

| Symptom                                                                             | Cause                                                                                                                                                                                              | Fix                                                                                                                                                                                                                                                             |
| ----------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `cannot specify width/size on a horizontally-stretched axis`                        | `<X anchor="top-stretch" width="200"/>`                                                                                                                                                            | Either change anchor, or drop `width`. The stretched axis takes its size from `margin`.                                                                                                                                                                         |
| `<Text>` renders one character per line (vertical)                                  | `<Text>` under a non-LayoutGroup parent (`<Btn>` / `<Frame>` / `<Screen>`) with `anchor="center"` and no `width` / `height` — `sizeDelta` defaults to `(0,0)`, so TMP wraps every glyph at width 0 | Give the Text width: `anchor="stretch"` + `margin` to fill the parent (offset siblings like `<Icon>` with margin), or set `width="..."` explicitly. Inside a `<VStack>` / `<HStack>` this doesn't happen — the LayoutGroup expands the child on the cross axis. |
| Ghost element on variant toggle                                                     | `<Add>` instantiated and never deactivated                                                                                                                                                         | This is by design (Strategy C). Use `hidden.variant` if you need a node to disappear.                                                                                                                                                                           |
| Parser silently merges children                                                     | Wrote `<Btn>开始 <Image/> </Btn>` (text + element mix)                                                                                                                                             | Pick one: text shorthand OR child elements. Mixed content is rejected.                                                                                                                                                                                          |
| Variant changes one attribute but not another                                       | `attr.variant` declared before `attr` (base) in the SAME element                                                                                                                                   | Fine — declaration order is per-attribute. Just verify the right `.variant` exists.                                                                                                                                                                             |
| `'stretch' on width/height is only valid inside <VStack>/<HStack>`                  | `<Btn width="stretch"/>` under a `<Frame>` (or other non-LayoutGroup parent)                                                                                                                       | Either wrap the Btn in a stack, or switch to free-positioning: `anchor="X-stretch"` + `margin`                                                                                                                                                                  |
| `size 'stretchx72' is numeric-only...`                                              | Trying to put `stretch` or `%` keyword inside compact `size=`                                                                                                                                      | `size=` is numeric-only. Use per-axis: `width="stretch" height="72"` or `width="50%"`                                                                                                                                                                           |
| `'%' (fractional) ... cannot be used inside <VStack>/<HStack>/<Grid>`               | `<Btn width="50%"/>` inside a VStack/HStack/Grid                                                                                                                                                   | LayoutGroup is weight-based: use `stretch*N` + spacer siblings (e.g. spacer/stretch\*2/spacer = 25/50/25), or move the child to a `<Frame>` parent                                                                                                              |
| `stretch*0` / `stretch*-1` / `stretch*` rejected                                    | Invalid weight after `stretch*`                                                                                                                                                                    | Weight must be a positive number, e.g. `stretch*2` / `stretch*0.5`                                                                                                                                                                                              |
| `'150%' must be in (0%, 100%]`                                                      | Percentage out of range                                                                                                                                                                            | Allowed range is `(0%, 100%]`. For "wider than parent", redesign the layout (likely a typo)                                                                                                                                                                     |
| UI 在不同屏上视觉大小不一（4K 上变邮票、手机上变巨人）                              | `<Screen>` 没设 `reference=`，走默认 `ConstantPixelSize, scaleFactor=1`，XML 数字直接 = 设备像素                                                                                                   | 在 `<Screen>` 上加 `reference="1920x1080"`（或你的设计分辨率），切到 `ScaleWithScreenSize`                                                                                                                                                                      |
| `<Image sprite="ns:name"/>` 显示白图,控制台报 "UI.SpriteResolver is not registered" | 启动期未注册 SpriteResolver,`ns:name` 路径走 UI.SpriteResolver 找不到 atlas                                                                                                                        | 在 `UI.LoadDocumentAsync` / `UI.Open` 之前调一次 `SpriteResolverHelpers.UseSpriteSetResolver(spriteSets)`(或 `UseAddressableSpriteSetResolver` 走 Addressables)                                                                                                 |

## Quick reference (cheatsheet)

```
VALIDATE      every .ui.xml write  →  xmllint --noout --schema Assets/PromptUGUI.gen.xsd <file>
              schema missing       →  ask user to run Tools → PromptUGUI → Schema → Generate XSD
MCP FEEDBACK  every .ui.xml write  →  refresh_unity + read_console (error,warning)
              MCP missing          →  ask user to open Unity + connect MCP for Unity

ROOT          <PromptUGUI version="1"> ... </PromptUGUI>
TOP LEVEL     <Import src="" [as=""]/>  <Screen name="" [canvas="overlay|camera|world"]>  <Template name="">

BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
              <Toggle> <Slider> <Dropdown> <ScrollList> <InputField>
TEXT SHORT    <Text>Hi</Text> ≡ <Text text="Hi"/>     (also <Btn>, <Toggle>, <InputField>)

COMMON ATTRS  id  anchor  size|width|height  margin  pivot  hidden  interactable
STACK-ONLY    padding  spacing                                    (VStack/HStack/Grid)

ANCHOR        "<v>-<h>"     v ∈ {top, center, bottom, stretch}
                            h ∈ {left, center, right, stretch}
ALIASES       center  =  center-center
              stretch | fill  =  stretch-stretch

SIZE          size="WxH"          numeric only (no keywords)
              width="W" / height="H"   numeric, or "stretch[*N]" (LG only), or "N%" (free-positioning only)
              FORBIDDEN on anchor-stretched axis
STRETCH KW    "stretch"        → LayoutElement.flexible*=1   (LayoutGroup child only)
              "stretch*N"      → LayoutElement.flexible*=N   (N > 0; for 1:2:1 splits etc.)
              Free-positioning equivalent: anchor="...-stretch" + margin
FRACTIONAL %  "N%"             → anchorMin/Max sub-range     (free-positioning child only)
              Range (0%, 100%]. anchor= preset decides where the fraction sits
              (left=[0,f], center=[(1-f)/2,(1+f)/2], right=[1-f,1]; same for top/center/bottom)
              In LayoutGroup → parse error (use stretch*N + spacer siblings)

MARGIN        "X" | "V,H" | "T,R,B,L"     "_" = 0 placeholder
              Always inward from anchor (positive)

TEMPLATE      <Template name="X">
                <Param name="p" [default=""]/>
                <body-with-exactly-one-root>
                  ...{{p}}...           string substitute
                  <Y if="{{p}}"/>       drop element when falsy
                  <Slot/>               inject children (max 1)
                </body>
              </Template>

VARIANT INL   attr.variantName="..."     last-active-wins
VARIANT BLK   <Variant when="name">
                <Add into="#id|@root" at="start|end|N">...</Add>
              </Variant>
NEVER VARY    id, tag name, <Param default>

IMPORT        <Import src="..." [as="ns"]/>
USE           <ns.TagName/>             (when prefixed)

SCREEN ATTRS  canvas="overlay|camera|world"    default overlay; renderMode only
              reference="WxH"                  ScaleWithScreenSize; unset = ConstantPixelSize
                                               .variant overrides supported (reference.mobile=...)

I18N XML      <Text>...</Text>                 extract + translate
              <Text tr="false">...</Text>      skip
              <Text font="title">...</Text>    font type
              <Text ctx="door">Open</Text>     msgctxt disambiguation
```

## Triggers and Animations

`<Trigger>` is the base — it subscribes to an event (open / loop / click / manual) and exposes an `OnFire` stream to C#. `<Animation>` extends Trigger by also playing a LitMotion animation on fire.

### `<Trigger>` — declarative event hook

```xml
<Trigger id="bonus" on="click@bonus-btn">
  <Frame><Btn id="bonus-btn">领取</Btn></Frame>
</Trigger>
```

`on=` values:

| Value              | Fires when                                                                                                                   |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------- |
| `open`             | Once when Screen opens (default if `on=` is omitted)                                                                         |
| `loop`             | (Animation only) Fires once on open and enables looping (default yoyo)                                                       |
| `click`            | The unique `<Btn>` inside this Trigger's subtree is clicked (uses Unity `Button.onClick`)                                    |
| `click@<id>`       | The `<Btn>` matching `<id>` inside the subtree is clicked                                                                    |
| `hover-enter`      | Pointer enters the unique `<Btn>` or `<Image>` in this Trigger's subtree (uGUI `IPointerEnterHandler`)                       |
| `hover-enter@<id>` | Pointer enters the `<Btn>` or `<Image>` with `<id>` inside the subtree                                                       |
| `hover-exit`       | Pointer leaves the unique `<Btn>` or `<Image>` (`IPointerExitHandler`)                                                       |
| `hover-exit@<id>`  | Pointer leaves the `<Btn>` or `<Image>` with `<id>`                                                                          |
| `press`            | Pointer pressed down on the unique `<Btn>` or `<Image>` (`IPointerDownHandler`). Instantaneous — release / long-press are v2 |
| `press@<id>`       | Pointer pressed down on the `<Btn>` or `<Image>` with `<id>`                                                                 |
| `manual`           | Does not auto-fire; C# must call `Fire()`                                                                                    |

**Pointer-event source range**: only `<Btn>` and `<Image>` can be `hover-enter` / `hover-exit` / `press` event sources. They both default to `raycastTarget=true`, which is what Unity's EventSystem requires for dispatching pointer events. Using `@<id>` to reference `<Icon>` (hardcoded `raycastTarget=false`), `<Text>` (default `false`), `<Frame>` (no Graphic to receive raycasts), or any other control as a pointer source → runtime error `"id 'X' is a Y, not supported as pointer event source. Use <Btn> or <Image>."`

**Caveat — `raycastTarget="false"` silently breaks pointer triggers**: if you set `<Image raycastTarget="false">` and then reference that Image via `on="hover-enter@..."`, the pointer event never reaches the GameObject — the trigger silently never fires. No error is raised. Keep `raycastTarget=true` on any Image you want to trigger pointer events from.

**`click` vs `press`**:

- `click` uses Unity's `Button.onClick` (drag-cancel / disabled-state handling). **`<Btn>` only.**
- `press` is the raw `IPointerDownHandler` event. **Works on both `<Btn>` and `<Image>`.**
- Use `click` for button activation; use `press` for instant visual feedback on press (scale 0.95 etc.).

Subscribe in C#:

```csharp
screen.Get<Trigger>("bonus").OnFire
    .Subscribe(_ => Game.AwardBonus())
    .AddTo(screen);
```

### `<Animation>` — LitMotion-driven effects

Three exclusive attribute families. Each `<Animation>` uses **exactly one** family.

#### Family A — Preset (opinionated bundle)

```xml
<Animation type="fadein" duration="0.3s">
  <Text>Welcome</Text>
</Animation>
```

Valid `type=` values: `fadein` / `fadeout` / `slidein-left` / `slidein-right` / `slidein-up` / `slidein-down` / `slideout-left` / `slideout-right` / `slideout-up` / `slideout-down` / `scalein` / `scaleout` / `pulse` / `bounce` / `shake`

#### Family B — Low-level transform (compose any combination)

```xml
<Animation translate="0,-50:0,0" fade="0:1" duration="0.4s" easing="out-back">
  <Frame>...</Frame>
</Animation>
```

Attributes (any combination):

| Attribute   | Format                     | Notes                                                                      |
| ----------- | -------------------------- | -------------------------------------------------------------------------- |
| `translate` | `"x1,y1:x2,y2"`            | Offset from→to in pixels. Omitting `from` (e.g. `":50,0"`) means from=zero |
| `scale`     | `"s:s"` or `"sx,sy:sx,sy"` | Scale from→to; single value applies to both x and y                        |
| `rotate`    | `"d1:d2"`                  | Z-axis rotation in degrees                                                 |
| `fade`      | `"a1:a2"`                  | Alpha from→to (0..1)                                                       |

Transform attributes always target the Animation's inner `_offsetProxy` GO — they cannot be redirected with `target=`.

#### Family C — Text effect

```xml
<!-- Count-up number -->
<Animation count="0:100000" format="{0:N0}" duration="2s">
  <Text>0</Text>
</Animation>

<!-- Per-character color wave -->
<Animation char-color="1,1,1,1:1,0.8,0.2,1" char-stagger="0.05s" duration="0.4s">
  <Text>VICTORY</Text>
</Animation>
```

| Attribute                                               | Notes                                                                                             |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `count="from:to"` + `format="{0:N0}"`                   | Animates a number; writes formatted string into `<Text>` (LitMotion `BindToText`)                 |
| `char-color="r,g,b,a:r,g,b,a"` + `char-stagger="0.05s"` | Per-char color wave (`BindToTMPCharColor`); each char's motion is delayed by `i * stagger`        |
| `target="@id"`                                          | Resolves a `<Text id="id">` in screen-global scope when the target is outside the wrapper subtree |

Text family default: looks for the unique `<Text>` in the subtree. Multiple `<Text>` descendants without `target=` → parse error.

#### Common attributes (all families)

| Attribute  | Default     | Notes                                                                                        |
| ---------- | ----------- | -------------------------------------------------------------------------------------------- |
| `duration` | `0.3s`      | Supports `0.3s` / `300ms` / bare float (seconds)                                             |
| `delay`    | `0s`        | Delay before motion starts                                                                   |
| `easing`   | `out-cubic` | See easing table below                                                                       |
| `loop`     | (none)      | `true` (infinite restart) / `yoyo` (infinite back-and-forth) / `count:N` (N times then stop) |
| `on`       | `open`      | Same as `<Trigger>`                                                                          |

**Easing values:** `linear` / `in-cubic` / `out-cubic` / `in-out-cubic` / `in-quad` / `out-quad` / `in-out-quad` / `in-quart` / `out-quart` / `in-out-quart` / `in-quint` / `out-quint` / `in-out-quint` / `out-back` / `out-elastic` / `out-bounce`

### Rules and parse errors

- Three families are mutually exclusive: writing both `type=` and `translate=` → parse error
- `count=` and `char-color=` are mutually exclusive within the text family
- `on="click"` requires a unique `<Btn>` descendant; multiple → use `on="click@<id>"` to disambiguate; zero `<Btn>` → error

### Patterns

**Menu entry stagger** (v1 has no stagger sugar — write siblings with explicit delays):

```xml
<VStack>
  <Animation type="slidein-left" delay="0.0s"><Btn>A</Btn></Animation>
  <Animation type="slidein-left" delay="0.05s"><Btn>B</Btn></Animation>
  <Animation type="slidein-left" delay="0.10s"><Btn>C</Btn></Animation>
</VStack>
```

**Score popup (count + char-color combo):** Nest animations sharing the same `<Text>`:

```xml
<Animation count="0:1000" format="{0:N0}" duration="2s">
  <Animation char-color="1,1,1,1:1,0.8,0.2,1" char-stagger="0.05s" delay="2s" duration="0.4s">
    <Text id="score">0</Text>
  </Animation>
</Animation>
```

**Caveats:**

- `char-color` assumes Text content doesn't change during animation; concurrent `count` + `char-color` on the same `<Text>` may produce wrong-char colors as text length changes
- `<Animation>` adds a `CanvasGroup` and an inner `_offsetProxy` GameObject (transparent to layout, but visible in the Hierarchy)
- `on="open"` fires once at Screen open; Variant ReSolve does **not** re-fire

## Worked end-to-end example (XML)

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">

  <Template name="MenuButton">
    <Param name="label"/>
    <Param name="color" default="#3B82F6"/>
    <Btn color="{{color}}" size="240x64"
         size.mobile="" width.mobile="stretch" height.mobile="72">
      <Text anchor="center" fontSize="24" color="#FFFFFF">{{label}}</Text>
    </Btn>
  </Template>

  <Screen name="MainMenu" reference="1920x1080" reference.mobile="1080x1920">
    <Image anchor="stretch" sprite="bg/main"/>

    <VStack id="menu" anchor="center" size="280x240" spacing="12"
            anchor.mobile="bottom-stretch"
            size.mobile="" height.mobile="320"
            margin.mobile="_,16,40,16">
      <MenuButton id="play"     label="开始游戏"/>
      <MenuButton id="settings" label="设置"/>
      <MenuButton id="quit"     label="退出" color="#DC2626"/>
    </VStack>

    <Variant when="mobile">
      <Add into="@root">
        <Image id="logo" anchor="top-center" size="180x60"
               margin="40,_,_,_" sprite="ui/logo"/>
      </Add>
    </Variant>
  </Screen>

</PromptUGUI>
```

For the C# side that loads this document, opens the Screen, and wires `screen.Get<Btn>("play").OnClick`, see the **scripting-promptugui-csharp** skill. Note: `id="play"` on `<MenuButton id="play"/>` is automatically transferred to the template body's single root element (the `<Btn>`), so `screen.Get<Btn>("play")` resolves directly without a path. Use a path (`"play/inner"`) only when reaching into an element that has its own id **inside** the template body.
