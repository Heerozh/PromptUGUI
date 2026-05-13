---
name: authoring-promptugui-xml
description: Use when authoring or editing PromptUGUI `.ui.xml` files (XML-driven uGUI for Unity 6+), defining `<Screen>` / `<Template>` / `<Variant>`, or wiring the C# `Get<T>` + R3 event bridge that drives them.
---

# Authoring PromptUGUI `.ui.xml`

PromptUGUI is a Unity 6+ package that turns compact XML files into runtime uGUI hierarchies. The description file is **pure structure + named handles** — no logic, no data binding expressions. All event/data wiring happens C#-side via `Get<T>(id)` and R3 `Observable<T>`.

This skill covers everything you need to write or edit a `.ui.xml` correctly. Read top-to-bottom once; afterwards the **Quick Reference** at the end is enough.

## Validation & feedback loop (run after every write)

Every `.ui.xml` write — and every `.cs` write that touches PromptUGUI — MUST be verified before reporting the work done. Two steps, in order:

### 1. XSD validate every `.ui.xml`

```
xmllint --noout --schema Assets/PromptUGUI.gen.xsd <path/to/your.ui.xml>
```

- Default schema location: `Assets/PromptUGUI.gen.xsd`. It's generated from the user's `ControlRegistry` (so it knows their custom C# controls) plus a project-wide scan for `<Template name="...">` definitions (so Template invocations like `<TitledPanel/>` are recognized too).
- **Auto-regen on `.ui.xml` save**: Unity's AssetPostprocessor regenerates the XSD whenever any `.ui.xml` is added/moved/deleted. As long as you call `refresh_unity` after editing, `xmllint` will see fresh Template tags. **C# control registration changes are NOT auto-picked-up** — for those, ask the user to run `Tools → PromptUGUI → Schema → Generate XSD`.
- If user not install unity mcp, u can ignore template tags error in XSD.
- **If the file does not exist, STOP.** Tell the user (in their language) to run the Editor menu `Tools → PromptUGUI → Schema → Generate XSD`.

### 2. Unity MCP live feedback (xml AND C# writes)

XSD only catches structural errors; Unity catches the rest — parser semantic errors (anchor/size conflicts, id collisions, missing `ref=`, Template namespace clashes), C# compile failures, runtime hot-reload errors.

After every `.ui.xml` or `.cs` write:

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

| Element                              | Role                                                      | Notes                                                                                                                |
| ------------------------------------ | --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `<PromptUGUI version="1">`           | Root, **always**.                                         | NOT `<UI>`. `version="1"` is required.                                                                               |
| `<Import src="..." [as="ns"]/>`      | Pull templates from another file.                         | Top-level only. `as=` adds namespace prefix.                                                                         |
| `<Screen name="..." [canvas="..."]>` | A complete UI scene; opened by code with `UI.Open(name)`. | One Screen = one Canvas. Names unique across all loaded files. `canvas="overlay\|camera\|world"`, default `overlay`. |
| `<Template name="...">`              | Reusable subtree, expanded at parse time.                 | Body must have **exactly one root element**.                                                                         |

`<Import>`, `<Screen>`, `<Template>` are the **only** elements allowed at the top level. Comments use standard `<!-- -->`.

## Built-in primitives (14)

**默认视觉主题**：白底 sliced + #323232 深字（与 Unity 6 `GameObject → UI → …` 创建出来的标准 prefab 一致）。所有控件的颜色/sprite 都能通过 `color=` / `sprite=` 属性 override；想要彻底深色主题项目级覆写 `ProceduralBuilders` 的常量，或用 Variant 方式 `color.dark="..."`。

Pre-registered on `UI.Registry`. Use as XML tags by name:

| Tag            | Notes                                                                                                                                                                                                                                                        | Tag-specific attributes                                                                                                                                                                                                                                                                                             |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<Frame>`      | Empty container (RectTransform only).                                                                                                                                                                                                                        | —                                                                                                                                                                                                                                                                                                                   |
| `<SafeArea>`   | Stretches to `Screen.safeArea` (notch / status bar / home indicator). Auto-reacts to rotation, window resize, Device Simulator. **Rejects** `anchor` / `size` / `width` / `height` / `margin` / `pivot` (incl. `.variant`); see "Safe area" section below.   | —                                                                                                                                                                                                                                                                                                                   |
| `<Image>`      | uGUI Image; loads sprites from `Resources`.                                                                                                                                                                                                                  | `sprite` (resource path), `color` (`#RRGGBB[AA]`), `type` (`simple` / `sliced` / `tiled` / `filled`)                                                                                                                                                                                                                |
| `<Text>`       | TMP_Text. Has text-content shorthand: `<Text>Hello</Text>` ≡ `<Text text="Hello"/>`.                                                                                                                                                                         | `text`, `fontSize` (int), `color`, `align` (`left` / `center` / `right`), `wrap` (bool), `raycastTarget` (bool), `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table) |
| `<VStack>`     | Vertical layout group. Default `childAlign="upper-center"` (cross-axis centered).                                                                                                                                                                            | `spacing` (float), `padding` (`T,R,B,L` 1/2/4 components), `childAlign` (`upper/middle/lower-left/center/right`; `center` alias for `middle-center`)                                                                                                                                                                |
| `<HStack>`     | Horizontal layout group. Default `childAlign="middle-left"` (cross-axis centered).                                                                                                                                                                           | Same as VStack.                                                                                                                                                                                                                                                                                                     |
| `<Grid>`       | Grid layout group, fixed columns.                                                                                                                                                                                                                            | `columns` (int), `cellSize` (`WxH`), `spacing` (single or `H,V`), `padding`                                                                                                                                                                                                                                         |
| `<Btn>`        | Image + Button + R3 `OnClick`. `<Btn>开始</Btn>` shorthand creates an internal TMP label child. Use as **template root** or registered prefab tag for any clickable.                                                                                         | `color`, `sprite`, `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table)                                                                                               |
| `<Icon>`       | Sprite from a project-level IconSet; by-name lookup, package-time pruning.                                                                                                                                                                                   | `name` (required, `ns:icon-name`), `color` (`#RRGGBB[AA]`), `size` (`WxH` / `native`; 拉伸用 `anchor="stretch"`)                                                                                                                                                                                                    |
| `<Toggle>`     | Image + uGUI Toggle + auto label. R3 `OnValueChanged: bool`. `<Toggle>静音</Toggle>` shorthand sets the label. Same `group=` name → mutual exclusion. **不要给单个 Toggle 写 `group=`** — uGUI ToggleGroup 默认要求至少一个 active，单成员组一旦点上就锁死。 | `text`, `isOn` (bool, default false), `group` (string, mutual-exclusion key), `color`, `sprite` (Resources path for checkmark sprite), `font`                                                                                                                                                                       |
| `<Slider>`     | Image + uGUI Slider. R3 `OnValueChanged: float`.                                                                                                                                                                                                             | `min` (float), `max` (float), `value` (float), `wholeNumbers` (bool), `direction` (`horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical`), `color`, `sprite`                                                                                                                                        |
| `<Dropdown>`   | TMP_Dropdown. R3 `OnSelected: int`. Options pushed via `BindOptions(Observable<IEnumerable<string \| DropdownOption>>)`.                                                                                                                                     | `value` (int initial index), `color`, `sprite`, `font`                                                                                                                                                                                                                                                              |
| `<ScrollList>` | ScrollRect + Mask. Items pushed via `BindItems(Observable<IReadOnlyList<T>>, Action<slot, T>)`. `itemTemplate` references a `<Template name=...>` or registered Control class.                                                                               | `itemTemplate` (required tag name), `direction` (`vertical` / `horizontal`), `spacing` (float), `padding`, `color`, `sprite`                                                                                                                                                                                        |
| `<InputField>` | TMP_InputField；R3 `OnValueChanged` / `OnEndEdit` / `OnSubmit: string`。`<InputField>初始文本</InputField>` 短手设 `text=`。                                                                                                                                 | `text`, `placeholder`, `contentType` (`standard`/`autocorrected`/`integer-number`/`decimal-number`/`alphanumeric`/`name`/`email`/`password`/`pin`/`custom`), `lineType` (`single`/`multi-newline`/`multi-submit`), `characterLimit` (int), `readOnly` (bool), `color`, `sprite`, `font`, `tr` (placeholder)/`ctx`   |

`<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` are reference implementations. For project-specific differentiation (pixel border, press feedback, custom popup chrome) subclass and override `OnAttached`.

### `<Icon>`

References a sprite from a project-level IconSet (shared icons, by-name lookup, package-time pruning).

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
# 1) List every IconSet (setName → source folder)
find . -name "*.asset" -not -path "*/Library/*" -not -path "*/Temp/*" \
  -exec grep -l "PromptUGUI.Application.IconSet" {} \; 2>/dev/null \
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

# 2) Search a known IconSet by keyword (relative path under sourceFolder, no extension)
cd <sourceFolder> && find . -iname "*<keyword>*.png" | sed 's|^\./||; s|\.png$||'
```

Icon name in XML = PNG path **relative to the IconSet's sourceFolder**, with `/` as separator and no extension. So `Arrow Right.png` directly under a set with `setName: solar` is `<Icon name="solar:Arrow Right"/>`; `Combat/heart.png` is `<Icon name="ui:Combat/heart"/>`. The bare basename (`ui:heart`) is also accepted as a shortcut **as long as it is unambiguous across the source folder** — when two PNGs in different subfolders share a basename you must use the path form, and the sync tool will error pointing at the candidates if XML still references the bare name. External packs (Font Awesome, Solar Icons, etc.) drop in as a folder of PNGs; create an IconSet ScriptableObject (`Create → PromptUGUI → Icon Set`) pointing at it, set `setName`, then `Tools → PromptUGUI → Icon → Sync Atlases (All Sets)` packs only the icons referenced from `.ui.xml` (plus `IconSet.alwaysInclude` entries).

**Variant overrides on literal `<Icon>`**: `<Icon name="ui:sun" name.dark="ui:moon"/>` — the scanner reads both `name` and every `name.<variant>` value, so each candidate sprite is packed.

**Template-Param-driven icon names**: the sync tool follows two recognized substitution shapes inside a `<Template>` body (also applies to `name.<variant>` overrides):

- Full placeholder — `<Icon name="{{iconName}}"/>`. Treats each invocation arg (`<MyIcon iconName="solar:Bell Bing"/>`) as a complete `set:icon` ref. Param `default=` also counts.
- Partial placeholder — `<Icon name="solar:{{x}}"/>`. Treats each invocation arg as the icon-name half, paired with the literal `solar` set.

Anything else inside a Template body (`{{a}}:{{b}}`, `solar:{{a}}-{{b}}`, multi-placeholder) is unanalyzable — the syncer logs a warning. Same for forwarded args (one Template's Param fed verbatim into another's). For unanalyzable cases, list final values in `IconSet.alwaysInclude`. Outside a `<Template>` (a literal `<Icon name="ui:{{x}}"/>` directly in a Screen) is always unanalyzable too.

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

| Tag            | 根节点组件                                                                                               | 自动子节点                                                                                                                     | R3 事件源                                                        |
| -------------- | -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------- |
| `<Frame>`      | `RectTransform` 单独                                                                                     | —                                                                                                                              | —                                                                |
| `<Image>`      | `Image`                                                                                                  | —                                                                                                                              | —                                                                |
| `<Text>`       | `TextMeshProUGUI`                                                                                        | —                                                                                                                              | —                                                                |
| `<VStack>`     | `VerticalLayoutGroup`（硬编码 `childControlWidth/Height=true`、`childForceExpand*=false`）               | —                                                                                                                              | —                                                                |
| `<HStack>`     | `HorizontalLayoutGroup`（同 VStack）                                                                     | —                                                                                                                              | —                                                                |
| `<Grid>`       | `GridLayoutGroup`（`constraint=FixedColumnCount`）                                                       | —                                                                                                                              | —                                                                |
| `<Btn>`        | `Image` + `Button`（`targetGraphic=Image`）                                                              | `Label`(`TMP_Text`, stretch 撑满) — **lazy**：写了 `text=` 才创建                                                              | `OnClick` ← `Button.onClick`                                     |
| `<Icon>`       | `Image`（`preserveAspect=true`, `raycastTarget=false`）                                                  | —                                                                                                                              | —                                                                |
| `<Toggle>`     | `Toggle`（`targetGraphic=Background`, `graphic=Checkmark`）                                              | `Background`(`Image`, left-middle 锚 20×20) → 内嵌 `Checkmark`(`Image`, 居中 20×20)；`Label`(`TMP_Text`, 右侧水平 stretch)     | `OnValueChanged` ← `Toggle.onValueChanged`                       |
| `<Slider>`     | `Slider`                                                                                                 | `Background`(`Image`)；`Fill Area` → `Fill`(`Image`)；`Handle Slide Area` → `Handle`(`Image`)                                  | `OnValueChanged` ← `Slider.onValueChanged`                       |
| `<Dropdown>`   | `Image` + `TMP_Dropdown`                                                                                 | `Label` + `Arrow` + `Template`（默认 inactive，内含 `Viewport` / `Content` / `Item` / `Scrollbar` 完整下拉子树）               | `OnSelected` ← `TMP_Dropdown.onValueChanged`                     |
| `<ScrollList>` | `Image` + `ScrollRect`                                                                                   | `Viewport`(`Image` + `Mask` stencil) → `Content`(V/H `LayoutGroup` + `ContentSizeFitter`)；按 `direction` 再加一个 `Scrollbar` | 无独立事件；C# 端 `BindItems(...)` 推数据                        |
| `<InputField>` | `Image` + `TMP_InputField`                                                                               | `Text Area`(`RectMask2D`) → `Placeholder`(`TMP_Text`, italic 半透明) + `Text`(`TMP_Text`)                                      | `OnValueChanged` / `OnEndEdit` / `OnSubmit` ← `TMP_InputField.*` |
| `<SafeArea>`   | `RectTransform` + `SafeAreaTracker`（内部 `MonoBehaviour`，订阅设备 safeArea / 旋转 / Device Simulator） | —                                                                                                                              | —                                                                |

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
- 内置 `<Image>` / `<Btn>` / `<Toggle>` 的 `sprite=` 走 `Resources.Load<Sprite>(value)`；要用 Addressables / Asset 引用的精灵得自己 subclass。`<Icon>` 是唯一走 `UI.IconResolver` 的入口。
- `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` 是参考实现 —— 想要像素描边、按下反馈、自定义下拉 chrome，subclass 并 override `OnAttached`，不要改这几个 Control 本体。

## Common attributes (any tag)

| Attribute                  | Format            | Notes                                                                                                                                                                              |
| -------------------------- | ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id="..."`                 | string            | Unique within Screen / Template instance scope. Lift to dedicated handle for `Get<T>`.                                                                                             |
| `anchor="..."`             | preset            | See "Anchor system" below. Default `top-left`.                                                                                                                                     |
| `size="WxH"`               | `240x80`          | Both dimensions in pixels (numeric only — keywords `stretch` / `N%` are **not** accepted here, use per-axis attrs). **Forbidden on stretched axes.**                               |
| `width="W"` / `height="H"` | float / `stretch[*N]` / `N%` | Numeric is base. `stretch` / `stretch*N` is LayoutGroup-only — see "Stretch keyword". `N%` is free-positioning-only — see "Fractional %". **Numeric forbidden on stretched anchor axes.** |
| `margin="..."`             | 1/2/4 floats      | "Distance from anchor inward, positive". `"_"` = 0 placeholder.                                                                                                                    |
| `pivot="x,y"`              | `0..1, 0..1`      | Defaults derive from `anchor`; rarely needed.                                                                                                                                      |
| `hidden="true"`            | bool              | Initial `SetActive(false)`.                                                                                                                                                        |
| `interactable="false"`     | bool              | Initial `CanvasGroup.interactable=false` + `blocksRaycasts=false`.                                                                                                                 |

`padding` and `spacing` are **NOT** universal — only on `<VStack>` / `<HStack>` / `<Grid>`.

`anchor` and `margin` are **NOT** available on `<VStack>` / `<HStack>` / `<Grid>`.

**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. Specifying only one axis (e.g. `width="100"`) leaves the other axis unconstrained, taking the child's intrinsic preferred size. Omitting all size attributes gets no `LayoutElement` — the child collapses to whatever its own components advertise (often 0 for an empty Frame), so write at least one axis when you need a visible footprint.

**Stretch keyword** (LayoutGroup-only) — `width="stretch"` / `height="stretch"` on a V/HStack child maps to `LayoutElement.preferredX=0, flexibleX=1`. The LayoutGroup grows the child to fill that axis.

- Multiple sibling stretches share remaining space by equal weight (`flexibleX` is additive). Two `stretch` siblings → each gets half.
- **Weighted form** `stretch*N` for non-equal splits. `<Frame width="stretch"/> <Btn width="stretch*2"/> <Frame width="stretch"/>` gives a 1:2:1 weight split → 25/50/25. `N` must be > 0 (e.g. `stretch*0.5` halves the weight).
- Forbidden outside V/HStack (parse error). Use `anchor="X-stretch"` + margin for free-positioning, or `N%` for fractional sizing.
- Variant-overridable: `width="240" width.mobile="stretch"` flips between fixed and flex.

**Fractional `%`** (free-positioning only) — `width="50%"` / `height="33.3%"` on a child of `<Frame>` / `<Screen>` / `<SafeArea>` maps to uGUI's native anchor fractions. The `anchor=` preset decides where in the parent the fraction sits:

| `anchor` horizontal | `width="50%"` becomes |
|---|---|
| `*-left`             | anchorMin.x=0, anchorMax.x=0.5 (left half)    |
| `*-center` / `center` | anchorMin.x=0.25, anchorMax.x=0.75 (centered) |
| `*-right`            | anchorMin.x=0.5, anchorMax.x=1 (right half)   |

Vertical: same idea (`top` → upper, `bottom` → lower, `center` → middle).

```xml
<Frame anchor="top-stretch" height="60">
  <Btn anchor="center"      width="50%" height="46"/>             <!-- 50% wide, centered -->
  <Btn anchor="center-left" width="30%" height="46" margin="0,16,0,16"/>  <!-- left 30% minus 16px each side -->
</Frame>
```

- Range `(0%, 100%]`. `0%` / `>100%` are parse errors (almost always a typo); `100%` is allowed but equivalent to `anchor=stretch` on that axis.
- `margin` further insets *within* the fractional range (so `width="50%" margin="0,16"` = 50% minus 32px total, still centered).
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

Variants are named flags, toggled C#-side: `UI.Variants.Set("mobile", true)`. Multiple flags can be active simultaneously. Toggling re-applies attributes on all open Screens **without rebuilding GameObjects**.

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

## i18n & Fonts

Source text goes directly inside `<Text>` / `<Btn>`. `UI.Locale.Set("en")` switches the language; locale switching rides the Variant pipeline, so already-open Screens auto-ReSolve.

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

C#:

```csharp
// Switch locale; swaps both the .po table and the font table
UI.Locale.Set("en");
UI.Locale.SetToSystemDefault();

// Strings extracted from code
var text = string.Format(c, UI.Tr("Total: {0:C}"), price);
```

**Reserved namespace**: `UI.Locale.Set("zh-Hans")` internally registers `zh-Hans` as an active Variant. Authors must NOT reuse a Variant of the same name to express anything other than locale state.

**.po file location**

By default `.po` files live in `Assets/Resources/PromptUGUI/i18n/<locale>/` or
`/PromptUGUI/i18n-custom/<locale>/`. Files anywhere under those paths are
picked up by `Resources.LoadAll<TextAsset>`; subfolder names are ignored.

When the project ships `.po` via Addressables, call
`UI.Locale.UseAddressableResolver()` at boot. The resolver loads every TextAsset
whose Addressables **label is `Locale:<locale>`** (so `Locale.Set("zh-Hans")`
loads everything labelled `Locale:zh-Hans`). Files can live anywhere. Only
available when `com.unity.addressables` ≥ 1.0 is installed (gated by
`PROMPTUGUI_HAS_ADDRESSABLES`).

```csharp
UI.Locale.UseAddressableResolver();
UI.Locale.Set("zh-Hans");                  // sync; UI shows msgid briefly during download
// or:
await UI.Locale.SetAsync("zh-Hans");       // awaits download + parse + ReSolve
```

**One-shot setup**: run `Tools → PromptUGUI → I18n → Setup Addressables for
Locale PO Files`. The menu scans every `.po` in the project, and for each one
whose parent folder matches a `PromptUGUISettings.locales[].locale` entry
(e.g. `Assets/Localization/zh-Hans/main.po`), it (1) applies the
`Locale:<locale>` label, and 23) scrubs any stale `Locale:*` label left over
from a previous folder location. Non-Locale labels you've set yourself (e.g.
`UI`, `Stage:1-1`) are preserved.

`Locale.Set` returns immediately after issuing the load. While the download is
in flight, open Screens briefly fall back to msgid text; when the load
completes the locale variant flips on and all open Screens re-resolve to the
translated strings. `SetAsync` returns only after that re-resolve completes —
use it when you need to read `UI.Tr(...)` immediately after switching locales.

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

There's also a **commons pool**: `await UI.LoadCommonLibraryAsync("ui/common", @as: null)` populates a global template pool merged into every screen automatically (no `<Import>` needed at the call site). Use this for project-wide shared widgets.

## C# code-side bridge

The XML doesn't bind data or events — it just creates handles. C# does the rest.

### Setup

```csharp
using PromptUGUI.Application;
using R3;

UI.UseResourcesResolver("UI");                             // sets SourceResolver rootPath + Editor hot-reload mapping
UI.Registry.Register<MyCustomControl>("MyTag", myPrefab);  // optional; built-ins are pre-registered

async void Start() {
    await UI.LoadCommonLibraryAsync("common/Buttons");     // optional
    await UI.LoadDocumentAsync("screens/MainMenu");        // load "{rootPath}/screens/MainMenu.ui.xml"; enables hot-reload
    // or, sync raw-XML form (no resolver, no hot-reload):
    // UI.LoadDocument("MainMenu", xmlString);
    var screen = UI.Open("MainMenu");
}
```

### Addressables resolver (optional)

If your project has `com.unity.addressables` installed, prefer to load `.ui.xml` files via Addressables instead of Resources:

```csharp
[SerializeField] AssetReferenceT<TextAsset> mainMenuXml;
// ...
UI.UseAddressableResolver();
await UI.LoadDocumentAsync(mainMenuXml);                   // forwards AssetGUID to the string pipeline
UI.Open("MainMenu");
```

prefer via a serialized `AssetReferenceT<TextAsset>` field (so authors drag the asset in the Inspector instead of typing a key).

or load via key:

```csharp
UI.UseAddressableResolver();
await UI.LoadDocumentAsync("UI/screens/MainMenu.ui.xml");   // src = Addressable key; enables hot-reload
UI.Open("MainMenu");
```

In Editor, saving a `.ui.xml` that's registered with Addressables auto-triggers hot-reload (same as Resources path). Player builds load via Addressables catalog. The `UseAddressableResolver` and `LoadDocumentAsync(AssetReferenceT<TextAsset>)` methods only exist when `com.unity.addressables` ≥ 1.0 is installed (gated by `PROMPTUGUI_HAS_ADDRESSABLES` compile symbol).

**Canvas configuration** (optional):

Each `Screen.Open()` creates its own root Canvas (+ `CanvasScaler` + `GraphicRaycaster`). The render mode comes from the XML `canvas` attribute on `<Screen>` (`overlay` / `camera` / `world`, default `overlay`). For everything _else_ — pinning a `worldCamera`, setting `sortingOrder` / `planeDistance`, swapping render mode at runtime, etc. — register a configurator. The configurator runs **after** the XML-declared mode is applied, so it can override anything:

```xml
<Screen name="WorldTooltip" canvas="camera"> ... </Screen>
```

```csharp
UI.CanvasConfigurator = (canvas, screenName) => {
    if (canvas.renderMode == RenderMode.ScreenSpaceCamera) {
        canvas.worldCamera = uiCamera;       // Camera ref must come from C# — not XML
        canvas.planeDistance = 10f;
    }
    canvas.sortingOrder = screenName == "Settings" ? 100 : 0;  // popups above main
};
```

The callback fires once per `Open()` (so also re-fires on hot-reload, since reload = close + reopen). The library never auto-creates Cameras — assigning `worldCamera` is the user's job. With no configurator and no `canvas=` attribute, every Screen is `ScreenSpaceOverlay`, `sortingOrder=0`.

**Icon system setup** (optional, only if your XML uses `<Icon>`):

```csharp
// Default helper: enumerate Resources/IconSets/ folder
IconResolverHelpers.UseSpriteAtlasIconResolver();
// Or pass an explicit list of IconSet ScriptableObjects:
IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { uiIconSet, artIconSet });
```

The helper builds a `(set:icon) → Sprite` lookup from each IconSet's SpriteAtlas.

**Addressables variant** (when `com.unity.addressables` ≥ 1.0 is installed):

```csharp
// Tag your IconSet assets in Addressables with label="IconSets".
// Addressables auto-pulls all the referenced SpriteAtlas as a dependency.
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver();
// Or custom label:
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver("MyIcons");
// Multiple labels — OR (Union, default): every IconSet tagged with "core" or "mobile"
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(
    new[] { "core", "mobile" });
// AND (Intersection): only IconSets tagged with BOTH "core" and "mobile"
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(
    new[] { "core", "mobile" }, UnityEngine.AddressableAssets.Addressables.MergeMode.Intersection);
```

Returns `Awaitable` — `await` it before opening any Screen that contains `<Icon>`, since `UI.IconResolver` is set inside the continuation. The loaded handle is held static and released on a second `UseAddressableSpriteAtlasIconResolver` call (swap label/locale). Only visible when `PROMPTUGUI_HAS_ADDRESSABLES` is defined.

`Sprite` references returned from `UI.IconResolver` are only valid while the current handle is held — releasing the handle (label swap, reset) unloads the underlying `SpriteAtlas`. Do not cache the returned `Sprite` in your own fields across such calls; resolve via `UI.IconResolver` (or rely on `<Icon name>` re-resolving on Variant changes) each time you need it.

To use a fully custom backend, set `UI.IconResolver` directly with your own `(key → Sprite)` lookup.

### Open / Close / Get

```csharp
var screen = UI.Open("MainMenu");                          // returns IScreen

var btn = screen.Get<Btn>("playBtn");                      // throws KeyNotFoundException if missing
IControl any = screen.Get("playBtn");                      // untyped fallback

// Path syntax for nested template instances:
//   <TitledPanel id="bagPanel"> ...inside template <Btn id="close"/>... </TitledPanel>
var close = screen.Get<Btn>("bagPanel/close");

UI.Close("MainMenu");                                      // destroys GameObjects
```

### Events & subscriptions

Control-level events are R3 `Observable<T>` — never `event` or `Action`:

```csharp
screen.Get<Btn>("playBtn").OnClick
      .Subscribe(_ => Game.Start())
      .AddTo(screen);          // disposed when Screen closes

screen.Get<Toggle>("muteAudio").OnValueChanged
      .Subscribe(b => AudioMixer.Mute = b).AddTo(screen);

screen.Get<Slider>("masterVol").OnValueChanged
      .Subscribe(v => AudioMixer.Master = v).AddTo(screen);

screen.Get<Dropdown>("quality").OnSelected
      .Subscribe(QualitySettings.SetQualityLevel).AddTo(screen);
```

`screen.Track(disposable)` (or the `.AddTo(screen)` extension) ties a subscription to Screen lifetime. Always do this — leaked R3 subscriptions hold the GameObject alive after Close.

### Screen-level hooks

`screen.RectTransformDimensionsChanged` same as Canvas's `screen.RootGameObject.RectTransformDimensionsChanged`

### List / option push

```csharp
screen.Get<Dropdown>("quality")
      .BindOptions(Observable.Return(new[] {"Low", "Medium", "High"}))
      .AddTo(screen);

screen.Get<ScrollList>("inv")
      .BindItems(player.Inventory, (IControl slot, Item item) => {
          slot.Get<Text>("label").TextValue = item.Name;
          slot.Get<Text>("count").TextValue = $"x{item.Count}";
      })
      .AddTo(screen);
```

`itemTemplate=` resolves to either a `<Template name="...">` (slot root is the template body) or a registered Control class (slot is that Control). Use `slot.Get<T>("childId")` inside the bind callback to reach into Template bodies.

### Variant switching at runtime

```csharp
UI.Variants.Set("mobile", true);    // all open Screens re-apply
UI.Variants.Set("mobile", false);
```

### Custom controls

```csharp
public sealed class MyControl : Control {
    UnityEngine.UI.Image _bg;

    public override void OnAttached() {
        _bg = GameObject.GetComponent<UnityEngine.UI.Image>()
              ?? GameObject.AddComponent<UnityEngine.UI.Image>();
    }

    [UIAttr] public string Color { set { /* parse hex, apply */ } }
    [UIAttr("backgroundSprite")] public string Sprite { set { /* ... */ } }
}

UI.Registry.Register<MyControl>("MyControl", optionalPrefab: null);
```

`[UIAttr]` (no name) maps to the camelCase of the property name (`Color` → `color`). `[UIAttr("foo")]` overrides. Supported types: `string` / `int` / `float` / `bool`. Use string + parse internally for everything else.

`[Bind]` on a field auto-wires a child component from a Prefab by child name. Useful when the control has a non-trivial Prefab structure.

## Common mistakes

| Symptom                                                            | Cause                                                                        | Fix                                                                                            |
| ------------------------------------------------------------------ | ---------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `cannot specify width/size on a horizontally-stretched axis`       | `<X anchor="top-stretch" width="200"/>`                                      | Either change anchor, or drop `width`. The stretched axis takes its size from `margin`.        |
| `<Text>` renders one character per line (vertical)                 | `<Text>` under a non-LayoutGroup parent (`<Btn>` / `<Frame>` / `<Screen>`) with `anchor="center"` and no `width` / `height` — `sizeDelta` defaults to `(0,0)`, so TMP wraps every glyph at width 0 | Give the Text width: `anchor="stretch"` + `margin` to fill the parent (offset siblings like `<Icon>` with margin), or set `width="..."` explicitly. Inside a `<VStack>` / `<HStack>` this doesn't happen — the LayoutGroup expands the child on the cross axis. |
| Element not found at runtime                                       | `id` only declared inside a `<Template>`, but accessed by flat name          | Use path: `screen.Get("templateId/innerId")`                                                   |
| Ghost element on variant toggle                                    | `<Add>` instantiated and never deactivated                                   | This is by design (Strategy C). Use `hidden.variant` if you need a node to disappear.          |
| Subscription survives Close → null refs                            | Forgot `.AddTo(screen)`                                                      | Always tie R3 subscriptions to Screen lifetime                                                 |
| Parser silently merges children                                    | Wrote `<Btn>开始 <Image/> </Btn>` (text + element mix)                       | Pick one: text shorthand OR child elements. Mixed content is rejected.                         |
| Variant changes one attribute but not another                      | `attr.variant` declared before `attr` (base) in the SAME element             | Fine — declaration order is per-attribute. Just verify the right `.variant` exists.            |
| Custom control's `[UIAttr]` ignored                                | Type other than string/int/float/bool                                        | Take a string param and parse internally (see `Btn.Color` for a hex example).                  |
| `'stretch' on width/height is only valid inside <VStack>/<HStack>` | `<Btn width="stretch"/>` under a `<Frame>` (or other non-LayoutGroup parent) | Either wrap the Btn in a stack, or switch to free-positioning: `anchor="X-stretch"` + `margin` |
| `size 'stretchx72' is numeric-only...`                             | Trying to put `stretch` or `%` keyword inside compact `size=`                | `size=` is numeric-only. Use per-axis: `width="stretch" height="72"` or `width="50%"`          |
| `'%' (fractional) ... cannot be used inside <VStack>/<HStack>/<Grid>` | `<Btn width="50%"/>` inside a VStack/HStack/Grid                            | LayoutGroup is weight-based: use `stretch*N` + spacer siblings (e.g. spacer/stretch\*2/spacer = 25/50/25), or move the child to a `<Frame>` parent |
| `stretch*0` / `stretch*-1` / `stretch*` rejected                   | Invalid weight after `stretch*`                                              | Weight must be a positive number, e.g. `stretch*2` / `stretch*0.5`                             |
| `'150%' must be in (0%, 100%]`                                     | Percentage out of range                                                      | Allowed range is `(0%, 100%]`. For "wider than parent", redesign the layout (likely a typo)    |

## Quick reference (cheatsheet)

```
VALIDATE      every .ui.xml write  →  xmllint --noout --schema Assets/PromptUGUI.gen.xsd <file>
              schema missing       →  ask user to run Tools → PromptUGUI → Schema → Generate XSD
MCP FEEDBACK  every .ui.xml/.cs write → refresh_unity + read_console (error,warning)
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

C# OPEN       await UI.LoadDocumentAsync("path"); UI.Open("ScreenName")
C# GET        screen.Get<Btn>("id")  /  "outerId/innerId"
C# EVENT      .OnClick / .OnValueChanged / .OnSelected   .Subscribe(...).AddTo(screen)
C# DATA       Dropdown.BindOptions(Observable<IEnumerable<string>>).AddTo(screen)
              ScrollList.BindItems(Observable<IReadOnlyList<T>>, (slot,t)=>...).AddTo(screen)
C# VARIANT    UI.Variants.Set("name", true)
XML CANVAS    <Screen name="X" canvas="overlay|camera|world">   default overlay; renderMode only
C# CANVAS     UI.CanvasConfigurator = (canvas, name) => { ... }  worldCamera / sortingOrder / overrides; runs after XML

## i18n
<Text>...</Text>                 extract + translate
<Text tr="false">...</Text>      skip
<Text font="title">...</Text>    font type
<Text ctx="door">Open</Text>     msgctxt disambiguation
UI.Tr("...")                     C# extraction entry point
UI.Locale.Set("zh-Hans")         switch locale (= switch .po + switch font)
UI.Locale.SetAsync("zh-Hans")    awaitable variant; completes after .po load + ReSolve
UI.Locale.UseAddressableResolver() load .po via Addressables, label = Locale:<locale>
                                  (set via Tools→PromptUGUI→I18n→Setup Addressables for Locale PO Files)
```

## Worked end-to-end example

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

  <Screen name="MainMenu">
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

```csharp
async void Start() {
    UI.UseResourcesResolver("UI");                                  // sets SourceResolver + Editor hot-reload mapping
    IconResolverHelpers.UseSpriteAtlasIconResolver(iconSets);       // pass icon set setting
    await UI.LoadDocumentAsync("screens/main");                     // enables hot-reload (resolver-backed src)

#if UNITY_IOS || UNITY_ANDROID
    UI.Variants.Set("mobile", true);
#endif

    var screen = UI.Open("MainMenu");

    screen.Get<Btn>("play").OnClick               // call-site id is transferred to template body root (a <Btn>)
          .Subscribe(_ => Game.Start()).AddTo(screen);

    screen.Get<Btn>("quit").OnClick
          .Subscribe(_ => Application.Quit()).AddTo(screen);
}
```

Note: `id="play"` on `<MenuButton id="play"/>` is automatically transferred to the template body's single root element (the `<Btn>`), so `screen.Get<Btn>("play")` resolves directly without a path. Use a path (`"play/inner"`) only when reaching into an element that has its own id **inside** the template body.
