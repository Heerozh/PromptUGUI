---
name: authoring-promptugui-xml
description: Use when authoring or editing PromptUGUI `.ui.xml` files (XML-driven uGUI for Unity 6+) —
If a task involves any XML-to-UI related content, it is highly likely to be based on this Skill.
---

# Authoring PromptUGUI `.ui.xml`

PromptUGUI is a Unity 6+ package that turns compact XML files into runtime uGUI hierarchies. The description file is **pure structure + named handles** — no logic, no data binding expressions. All event/data wiring happens C#-side via `Get<T>(id)` and R3 `Observable<T>`; see the **scripting-promptugui-csharp** skill for that side.

This skill covers everything you need to write or edit a `.ui.xml` correctly. Read top-to-bottom once; afterwards the **Quick Reference** at the end is enough.

## Validation & feedback loop (run after every write)

Every `.ui.xml` write MUST be verified before reporting the work done. Three steps, in order — each catches a different layer of mistake:

### 1. Full validate CLI for every `.ui.xml` (catches semantic mistakes XSD can't express)

```
dotnet run --project Library/PackageCache/com.promptugui.core@<hash>/.lint/UIXmlLint -- <path/to/your.ui.xml>
dotnet run --project Library/PackageCache/com.promptugui.core@<hash>/.lint/UIXmlLint -- Assets/   # 整个目录递归
```

- **Local / embedded packages have no `PackageCache` copy.** The `Library/PackageCache/com.promptugui.core@<hash>/…` path only exists when
  PromptUGUI is resolved from a registry. If `Packages/manifest.json` pins itwith a `file:` path (local / embedded package — common in symlinked or
  monorepo checkouts), there is no PackageCache copy; the `.lint/UIXmlLint` project (and the `Runtime/Core/Lint/` rule source) live at that `file:`
  source instead. Resolve the real base from the manifest: grep com.promptugui.core Packages/manifest.json # e.g. "file:../../PromptUGUI"
  dotnet run --project /.lint/UIXmlLint -- <path/to/your.ui.xml>
- No Unity required — pure .NET, runs anywhere `dotnet` is installed.
- Surfaces context-dependent rules that XSD can't easily express, e.g. **`anchor` / `margin` on a direct child of `<VStack>` / `<HStack>` / `<Grid>`** (`PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`), or a bare `state-*` trigger with no `<Btn>` / `<Tab>` / `<Toggle>` ancestor (`PUI-STATE-NO-SOURCE`). Unity logs these as warnings (so `UI.Open()` doesn't break), but the CLI promotes them to errors with non-zero exit code so they don't slip through.
- Exit 0 = clean. Exit 1 = at least one parse error or rule violation; STOP and fix before reporting done.
- Rule code lives in `Library/PackageCache/com.promptugui.core@<hash>/Runtime/Core/Lint/` and is shared with `ScreenInstantiator`'s warning path — same logic, one source of truth.

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

| Element                                                                                              | Role                                                      | Notes                                                                                                                                                                                                                                                                                         |
| ---------------------------------------------------------------------------------------------------- | --------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<PromptUGUI version="1">`                                                                           | Root, **always**.                                         | NOT `<UI>`. `version="1"` is required.                                                                                                                                                                                                                                                        |
| `<Import src="..." [as="ns"]/>`                                                                      | Pull templates from another file.                         | Top-level only. `as=` adds namespace prefix.                                                                                                                                                                                                                                                  |
| `<Theme name=... base=...?>`                                                                         | Top-level. Declares a named color theme.                  | `base` (optional) inherits tokens from another theme by name. Children must be `<Color>` only.                                                                                                                                                                                                |
| `<Color name=... value=...>`                                                                         | Inside `<Theme>`. Defines a named color token.            | `name` is kebab-case `[a-z0-9-]`. `value` is hex (`#rgb` / `#rrggbb` / `#rrggbbaa`) or a Unity CSS named color (`red` / `white` / ...).                                                                                                                                                       |
| `<Screen name="..." [canvas="..."] [reference="..."] [scale-mode="..."] [reference.portrait="..."]>` | A complete UI scene; opened by code with `UI.Open(name)`. | One Screen = one Canvas. Names unique across all loaded files. `canvas="overlay\|camera\|world"`, default `overlay`. Optional `reference="WxH"` (+ `.variant`) switches CanvasScaler to ScaleWithScreenSize. Optional `scale-mode="auto\|pixel"` (+ `.variant`); pixel = integer scaleFactor. |
| `<Template name="...">`                                                                              | Reusable subtree, expanded at parse time.                 | Body must have **exactly one root element**.                                                                                                                                                                                                                                                  |

`<Import>`, `<Theme>`, `<Screen>`, `<Template>` are the **only** elements allowed at the top level. Comments use standard `<!-- -->`.

## Built-in primitives (19)

**默认视觉主题**：白底 sliced + #323232 深字（同 Unity 6 标准 UI prefab）。`color=` / `sprite=` 单点 override，整体深色覆写 `ProceduralBuilders` 常量或用 Variant `color.dark="..."`。想完全去掉自带 sliced 底（只留纯色或透明）写 `sprite="none"`（等价于 `sprite=""`）——见下方"内置控件 `sprite=` 解析"说明。

Pre-registered on `UI.Registry`. Use as XML tags by name. 速查目录如下；各控件属性详见下方对应 `### <Tag>` 小节（`<Icon>` / `<SafeArea>` 的小节在本节末尾）：

| Tag | 用途 |
|---|---|
| `<Frame>` | 空容器，可选 `mask="rect"` |
| `<SafeArea>` | 撑满父级、按设备安全区内缩（见本节末 **Safe area** 小节） |
| `<Image>` | uGUI Image：sprite + 等比适配 + mask |
| `<RawImage>` | uGUI RawImage：C# 设 Texture（动态图）+ contain/cover 适配 + mask |
| `<Text>` | TMP 文本：富对齐 + i18n |
| `<VStack>` | 纵向布局组 |
| `<HStack>` | 横向布局组 |
| `<Grid>` | 固定列网格布局组 |
| `<Btn>` | Image+Button，`OnClick` / `OnState`，自适应尺寸 |
| `<Icon>` | 项目 SpriteSet 按名取图（见本节末 **`<Icon>`** 小节） |
| `<Toggle>` | Image+Toggle，`OnValueChanged:bool`，可互斥 |
| `<Slider>` | Image+Slider，`OnValueChanged:float` |
| `<Dropdown>` | TMP_Dropdown，`OnSelected:int`（`BindOptions`） |
| `<ScrollList>` | ScrollRect+Mask（`BindItems`） |
| `<InputField>` | TMP_InputField，`OnValueChanged` / `OnEndEdit` / `OnSubmit:string` |
| `<Progress>` | 只读线性进度条 |
| `<TabBar>` | 互斥选项卡容器 |
| `<Tab>` | 选项卡（`bind` 显隐 `<Frame>`） |
| `<Carousel>` | 翻页轮播卡 |
| `<Show>` | 按状态显隐子树的无视觉 wrapper |
| `<Markdown>` | Renders a Markdown document into a scrollable subtree  |

> `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<TabBar>` are reference implementations. For project-specific differentiation (pixel border, press feedback, custom popup chrome) subclass and override `OnAttached` — see scripting-promptugui-csharp.

### `<Frame>`

空容器；可选 `RectMask2D`（`mask="rect"`）。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `mask` | `rect` | — | 加 `RectMask2D` 裁剪子节点 |
| `maskPadding` | `T,R,B,L`（`_`=占位） | — | 仅 `mask="rect"` 时有效 |

### `<Image>`

uGUI Image，从 `Resources` 加载 sprite；可选 `RectMask2D`（`mask="rect"`）或 stencil `Mask`（`mask="self"`，用自身 sprite 当 mask 形状）。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `sprite` | sprite key | — | |
| `color` | hex / CSS named / theme token | — | runtime theme token 优先于字面量；见 **Color Tokens** |
| `type` | `simple` / `sliced` / `tiled` / `filled` / `contain` / `cover` | 自动 | 省略→sprite 有非零 border 取 `sliced`、否则 `simple`。`contain` / `cover` 是相对**父 rect** 的等比适配（`AspectRatioFitter`）：`contain` 内嵌留边、`cover` 填满溢出；尺寸设在**父级**，`cover` 时父级加 `mask="rect"` 裁切。适配模式下 Image 自身 `anchor` / `size` / `width` / `height` / `margin` 被接管失效（`PUI-IMAGE-FIT-GEOMETRY`）；不可 variant 覆盖（`PUI-IMAGE-FIT-VARIANT`）；勿直接作 `<VStack>` / `<HStack>` / `<Grid>` 子节点，套 `<Frame>` |
| `mask` | `rect` / `self` | — | |
| `showMask` | bool | `true` | 仅 `mask="self"` |
| `maskPadding` | `T,R,B,L` | — | 仅 `mask="rect"` |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |

### `<RawImage>`

uGUI `RawImage`，渲染**运行时动态加载的 `Texture`**（头像 / 下载图 / 截图 / `RenderTexture`）。图源**只能从 C# 设**——没有 `sprite` / SpriteSet、没有 XML 图源属性：`screen.Get<RawImage>("id").Texture = tex;`（见 scripting-promptugui-csharp）。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `color` | hex / CSS named / theme token | `#ffffff` | 乘色；无 texture 时仍按 color 画纯色 quad（要初始隐形写 `color="#00000000"`） |
| `type` | `contain` / `cover` | — | 等比适配，相对**父 rect**（同 `<Image>`）：`contain` 内嵌留边、`cover` 填满溢出；`cover` 裁切由父级 `mask="rect"` 负责。**仅这两个值**——`simple` / `sliced` / `tiled` / `filled` 是 sprite 专有，写了会 warning 并回退普通模式。适配模式下自身 `anchor` / `size` / `width` / `height` / `margin` 被 `AspectRatioFitter` 接管失效 |
| `tint` | `multiply` / `linear` | `multiply` | 见 **Tint blend modes** |
| `mask` | `rect` / `self` | — | 同 `<Image>` |
| `showMask` | bool | `true` | 仅 `mask="self"` |
| `maskPadding` | `T,R,B,L` | — | 仅 `mask="rect"` |

- texture 由 C# 在 Open 后设置，故 `size="native"`（读 texture 像素宽高）仅在实例化前已同步赋 texture 时有值；常态请显式写 `size` 或用 `type="contain"/"cover"`。
- 别在 `<VStack>` / `<HStack>` / `<Grid>` 直接子节点上用 fit 模式（`AspectRatioFitter` 与 LayoutGroup 抢布局）——套一层 `<Frame>`。

### `<Text>`

TMP_Text。文本简写：`<Text>Hello</Text>` ≡ `<Text text="Hello"/>`。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `text` | string | — | |
| `fontSize` | int | — | |
| `color` | hex / CSS named / theme token | — | 见 **Color Tokens** |
| `align` | TMP 对齐 | `left`+`middle` | 一个水平 token `left` / `center` / `right` / `justified` / `flush` / `geo`，和/或一个垂直 token `top` / `middle` / `bottom` / `baseline` / `midline` / `capline`，连字符或空格连接、顺序无关（`bottom-right` / `top-center` / `capline-flush`）；只给水平保持垂直 `middle`，只给垂直保持水平 `left`；未知 token = parse error |
| `wrap` | bool | `true` | `false`=不换行（NoWrap）；常配 `overflow="ellipsis"` 做单行省略号 |
| `overflow` | `overflow` / `ellipsis` / `truncate` | `overflow` | 文字塞不下框后的收尾：`overflow`=溢出框外（TMP 默认）、`ellipsis`=尾部 `…`、`truncate`=硬裁切无 `…`；未知值 = parse error |
| `raycastTarget` | bool | — | |
| `font` | string | `default` | Settings 里的 font type |
| `autosize` | bool | `false` | 开 TMP auto-size（仅 WD% 形式——字宽最多压 50%，字号不变） |
| `tr` | bool | `true` | `false`=跳过 i18n 提取 |
| `ctx` | string | — | msgctxt 消歧同 msgid |

**`autosize` / `overflow` 是两层，会叠加（不是二选一）**：TMP 先用 `autosize`「努力塞进去」（压字宽），塞不下再由 `overflow` 决定怎么收尾。单行省略号的标准写法：`<Text wrap="false" overflow="ellipsis"/>`；想先压扁再省略：`<Text wrap="false" autosize="true" overflow="ellipsis"/>`。**坑**：`ellipsis` / `truncate` 只在文本框「定宽」时才触发——若这个 `<Text>` 靠 native-size 撑开（框跟着文字变宽，如 Frame 里不写 `width`、或挂了 ContentSizeFitter），就永远不溢出、省略号也不出现；用 `anchor="stretch"`+`margin` 或显式 `width` 把宽度钉死。

### `<VStack>`

纵向布局组。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `spacing` | float | — | |
| `padding` | `T,R,B,L`（1/2/4 段，`_`=0） | — | 如 `padding="6,_,_,_"` |
| `childAlign` | `upper` / `middle` / `lower` - `left` / `center` / `right` | `upper-center` | `center`=`middle-center`（交叉轴居中） |

### `<HStack>`

横向布局组。属性同 `<VStack>`，默认 `childAlign="middle-left"`。

### `<Grid>`

固定列网格布局组。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `columns` | int | — | |
| `cellSize` | `WxH` | — | |
| `spacing` | single 或 `H,V` | — | |
| `padding` | `T,R,B,L` | — | |

### `<Btn>`

Image + Button + R3 `OnClick` / `OnState`。`<Btn>开始</Btn>` 简写生成内部 TMP label 子节点；可作 template root 或注册 prefab tag。不写 size 时按文字宽 + 左右 16 padding、上下 max(44, 文字高+12) 自适应；icon-only（无 text）回退 80×44。`interactable="false"` 同时驱动 `Button.interactable` → 进入 Disabled 态（`disabledColor` / `disabledModulate` 生效、`state-disabled` fire），叠加 CanvasGroup raycast block。状态化视觉见 [`reference/states.md`](reference/states.md)。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `color` | hex / CSS named / theme token | — | 背景基色，见 **Color Tokens** |
| `sprite` | sprite key | — | 背景图 |
| `pressedSprite` | sprite key | — | 按下换 bg `overrideSprite`、松开还原（不动 `sprite`）；自动切 Btn off uGUI ColorTint 避免双重变暗；`""` / `none`=不换；与 `pressedModulate` 叠加；见 states.md |
| `disabledSprite` | sprite key | — | Disabled 态换 bg `overrideSprite`（`interactable="false"` 或运行时 `Btn.Interactable=false`）；同 `pressedSprite` 契约；与 `pressedSprite` 共存（Disabled 优先）；仅 `<Btn>`；见 states.md |
| `hoverColor` · `pressedColor` · `disabledColor` | hex / CSS / token | — | **绝对**单态 bg 色（仅 targetGraphic，不扩散） |
| `hoverModulate` · `pressedModulate` · `disabledModulate` | hex / CSS / token | white | **相对**乘子，扩散到 bg + 所有子 Graphic |
| `fontSize` | int | — | 仅作用于自动 label；其它 Text 属性（`align` / `wrap`）需显式 `<Text>` 子节点 |
| `font` | string | `default` | Settings 里的 font type |
| `tr` | bool | `true` | `false`=跳过 i18n |
| `ctx` | string | — | msgctxt 消歧 |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |

### `<Toggle>`

Image + uGUI Toggle + 自动 label。R3 `OnValueChanged: bool`。`<Toggle>静音</Toggle>` 简写设 label。同 `group=` 名 → 互斥。**别给单个 Toggle 写 `group=`**（uGUI ToggleGroup 默认要求至少一个 active，单成员组点上即锁死）。不写 size 时按文字宽 + 左 23 checkmark 区 + 右 5 padding、上下 max(44, 文字高+12) 自适应；checkbox-only 回退 44×44。状态化视觉见 [`reference/states.md`](reference/states.md)。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `text` | string | — | |
| `isOn` | bool | `false` | |
| `group` | string | — | 互斥键 |
| `color` | hex / CSS / token | — | 见 **Color Tokens** |
| `sprite` | Resources 路径 | — | checkmark sprite |
| `font` | string | `default` | |
| `hoverColor` · `pressedColor` · `selectedColor` · `disabledColor` | hex / CSS / token | — | **绝对**单态 bg 色（仅 targetGraphic，不扩散）；`selectedColor`=勾选时 bg 基色（同 `<Tab>` 的 selection-aware base，hover/pressed/disabled 叠在上面）；见 states.md |
| `hoverModulate` · `pressedModulate` · `selectedModulate` · `disabledModulate` | hex / CSS / token | white | **相对**乘子，扩散到 bg + 所有子 Graphic；子节点 `stateReact="false"` 退出；见 states.md |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |

### `<Slider>`

Image + uGUI Slider。R3 `OnValueChanged: float`。不写 size 时按方向给默认：横向 160×44、纵向 44×160（长边视觉宽、短边 tap target）。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `min` · `max` · `value` | float | — | |
| `wholeNumbers` | bool | — | |
| `direction` | `horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical` | `horizontal` | |
| `color` | hex / CSS / token | — | 见 **Color Tokens** |
| `sprite` | sprite key | — | |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |

### `<Dropdown>`

TMP_Dropdown。R3 `OnSelected: int`。选项 C# 侧 `BindOptions(...)` 注入。不写 size 时默认 160×44（不读 caption 文字宽，避免每选一项改宽度）。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `value` | int | — | 初始索引 |
| `color` | hex / CSS / token | — | 见 **Color Tokens** |
| `sprite` | sprite key | — | |
| `font` | string | `default` | |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |
| `popupSprite` | sprite key | — | Skins the popup list background (the closed button keeps using `sprite`/`color`). |
| `popupColor` | hex / CSS / token | — | Tints the popup list background. |
| `popupMask` | sprite key | rounded | Popup viewport clip shape; same three states as `<ScrollList mask>` (`""` = square RectMask2D). |

### `<ScrollList>`

ScrollRect + Mask。项 C# 侧 `BindItems(...)` 注入。`itemTemplate` 引用 `<Template name=...>` 或注册的 Control 类。不写 size 时按方向给视口默认：纵向 160×200、横向 200×160；实战通常显式写 size。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `itemTemplate` | tag name | — | 必填 |
| `direction` | `vertical` / `horizontal` | `vertical` | |
| `spacing` | float | — | |
| `padding` | `T,R,B,L` | — | |
| `color` | hex / CSS / token | — | 见 **Color Tokens** |
| `sprite` | sprite key | — | |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |
| `frame` | sprite key | — | Border layer drawn above content & scrollbar, outside the mask — scrolling content never overlaps it. Lazily created. Note: `tint=` affects only the background, not the frame. |
| `frameColor` | hex / CSS / token | — | Tints the frame layer; setting it alone also activates the layer. |
| `mask` | sprite key | rounded | Viewport clip shape. `mask="custom#slice"` = stencil mask with that sprite (auto-sliced); `mask=""` = square `RectMask2D` clip (cheaper; keeps a transparent list's corners square). Unset keeps the built-in rounded mask. Unlike `<Image>`/`<Frame>`, `rect`/`self` are **not** keywords here — this `mask` takes a sprite key, so use `mask=""` for a square clip. |

### `<InputField>`

TMP_InputField；R3 `OnValueChanged` / `OnEndEdit` / `OnSubmit: string`。`<InputField>初始文本</InputField>` 简写设 `text=`。`interactable="false"` 同时驱动 `TMP_InputField.interactable` → 进入 Disabled 视觉态、阻止聚焦/输入，叠加 CanvasGroup raycast block。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `text` | string | — | |
| `placeholder` | string | — | |
| `contentType` | `standard` / `autocorrected` / `integer-number` / `decimal-number` / `alphanumeric` / `name` / `email` / `password` / `pin` / `custom` | — | |
| `lineType` | `single` / `multi-newline` / `multi-submit` | — | |
| `characterLimit` | int | — | |
| `readOnly` | bool | — | |
| `fontSize` | int | — | 输入文本**和** placeholder（TMP `pointSize`） |
| `textColor` | hex / CSS / token | — | **输入文字色**（区别于 `color`=背景） |
| `placeholderColor` | hex / CSS / token | — | placeholder 文字色 |
| `align` | TMP 双轴对齐（同 `<Text align>`） | — | 作用于文本 + placeholder |
| `caretColor` | hex / CSS / token | — | 同时开 TMP `customCaretColor` |
| `selectionColor` | hex / CSS / token | — | 选区高亮 |
| `caretWidth` | int (px) | — | |
| `color` | hex / CSS / token | — | **背景**；见 **Color Tokens** |
| `sprite` | sprite key | — | |
| `padding` | `T,R,B,L`（`_`=0） | 自动 | 字段边到文字的内距。**不设=自动跟随边框**：有 border sprite 时给默认内距、`sprite=""` / `"none"` 时为 0（无边框文字铺满、短字段不负高）；显式值优先 |
| `font` | string | `default` | |
| `tr` · `ctx` | bool · string | `true` · — | placeholder 的 i18n |
| `tint` | `multiply` / `linear` | — | 见 **Tint blend modes** |

### `<Progress>`

显示型线性进度条（只读，无 `OnValueChanged`）。一行配齐 frame / mask / bg / fill / mode / direction / value。**多图层 / mask×bg 组合 / lint 详见 [`reference/controls-progress.md`](reference/controls-progress.md)。**

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `value` | float `[0..1]` | `0` | |
| `fill` · `bg` · `frame` · `mask` | sprite key | — | 各图层 sprite |
| `fillColor` | hex / CSS / token | — | |
| `bgColor` | hex / CSS / token | — | 单独设也激活 bg 层 |
| `frameColor` | hex / CSS / token | — | 单独设也激活 frame 层 |
| `mode` | `scale` / `fill` | `scale` | |
| `direction` | `horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical` | `horizontal` | |
| `tint` | `multiply` / `linear` | — | 作用于 fill+bg+frame；见 **Tint blend modes** |

### `<TabBar>`

Tab 容器；私有 `ToggleGroup`（`allowSwitchOff=false`）+ `Horizontal` / `VerticalLayoutGroup`。纯布局无自身视觉（要背景条套 `<Image>`）。子节点可为直接 `<Tab>` 或含 `<Tab>` 的 Template wrapper（递归收集）。支持 `itemTemplate` + `BindItems`（同 `<ScrollList>`）。作为子节点的布局组——Tab 不能写 `anchor` / `margin`。**详见 [`reference/controls-tabs.md`](reference/controls-tabs.md)。**

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `direction` | `horizontal` / `vertical` | `horizontal` | |
| `spacing` | float | — | |
| `padding` | `T,R,B,L` | — | |
| `itemTemplate` | tag name | `Tab` | |

### `<Tab>`

`<TabBar>` 子节点；uGUI `Toggle` + 居中 TMP label + 可选左侧 icon。经 TabBar 的 ToggleGroup 自动互斥。`bind="frame_id"` 声明式在选中时显隐兄弟 `<Frame>`（lazy 解析缓存）；无 `bind=` → 只 fire `OnSelected`。接受嵌套子节点（Frame 式叠在 bg 上）；点穿子节点需 `raycastTarget="false"`（`<Icon>` 已是）。状态化视觉见 [`reference/states.md`](reference/states.md)；共享样式 / 动态卡详见 [`reference/controls-tabs.md`](reference/controls-tabs.md)。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `text` | string | — | |
| `isOn` | bool | `false` | |
| `bind` | id | — | 选中显隐的兄弟 `<Frame>` |
| `color` | hex / CSS / token | — | `#00000000`=透明但可点 |
| `font` | string | `default` | |
| `fontSize` | int | — | |
| `icon` | sprite key | — | 左对齐 24×24，间隙 4px |
| `sprite` | sprite key | — | 常态 bg；`""` / `none` 移除自带 9-slice 底 |
| `selectedSprite` | sprite key | — | 选中（`isOn`）时把 bg `overrideSprite` 换成它、取消选中还原——无独立 overlay；keyed on `isOn` 故 hover/press 不扰动；设了切 transition off ColorTint（同 `<Btn pressedSprite>`）；`""` / `none`=不换 |
| `hoverColor` · `pressedColor` · `selectedColor` · `disabledColor` | hex / CSS / token | — | **绝对**单态 bg 色（仅 targetGraphic，不扩散）；`selectedColor`=**选中时** bg 基色（hover/pressed/disabled 叠在上面）；见 states.md |
| `hoverModulate` · `pressedModulate` · `selectedModulate` · `disabledModulate` | hex / CSS / token | white | **相对**乘子，扩散到 bg + 所有子 Graphic；子节点 `stateReact="false"` 退出；见 states.md |
| `tint` | `multiply` / `linear` | — | 作用于 bg（`selectedSprite` 换的也是 bg sprite，故选中图也被 tint）；见 **Tint blend modes** |

### `<Carousel>`

水平翻页轮播卡容器：自动播放 + 拖动 + 无限循环 + 状态化指示点。卡片用 `itemTemplate` + C# `BindItems`（同 ScrollList / TabBar）或静态子卡。每张卡排成视口大小——卡片**不能**写 `anchor` / `margin` / `size`（`PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` / `PUI-CAROUSEL-CARD-SIZE`）。当前页 `current` 是**运行期独占状态**（resize / Variant / Theme 不重置）。**详见 [`reference/controls-carousel.md`](reference/controls-carousel.md)。**

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `itemTemplate` | tag / Template 名 | `Frame` | |
| `interval` | 秒 | `5` | `0`=关自动播放 |
| `loop` | bool | `true` | |
| `transition` | 秒 | `0.3` | 吸附补间 |
| `current` | int | — | 初始页；runtime-owned |
| `dots` | anchor 关键字（如 `bottom-center`） | — | 空 / `none`=不显示；非法 → runtime 回退 `bottom-center` + `PUI-CAROUSEL-DOTS-ANCHOR` |
| `dotSize` | `WxH` | `8x8` | |
| `dotSpacing` | float | `6` | |
| `dotMargin` | `T,R,B,L` | — | |
| `dotSprite` · `dotSelectedSprite` | sprite | — | 形状 / 当前态换图 |
| `dotColor` · `dotSelectedColor` · `dotHoverColor` · `dotPressedColor` | hex / CSS / token | — | 状态色 |
| `dotTint` | `multiply` / `linear` | — | |
| `dotTriSlice` | bool | `false` | 把单张 `dotSprite` 横向等比切成 3 段分摊到各点，整排连成一条（左帽 / 可平铺中段 / 右帽；2 点=左+右，N≥3=左+中×(N-2)+右）。sprite 须设计成 3 等宽段、中段可平铺、atlas 内不能旋转打包；源图 9-slice 边框按段保留；选中态走颜色无需额外切图 |

### `<Show>`

无视觉 wrapper（Trigger 派生）。其子树**仅在**最近祖先 `<Btn>` / `<Tab>` / `<Toggle>` 处于 `on="state-*"` 态时可见，否则隐藏（`SetActive`，不销毁——隐藏子树 + R3 订阅存活）。同一 source 下的兄弟 `<Show>` 互斥；无显式 `<Show>` 的状态回退到 `state-normal` 块。仅 `state-*` 合法（其它如 `on="click"` 报错）。裸 `state-*` 需 `<Btn>` / `<Tab>` / `<Toggle>` 祖先（`PUI-STATE-NO-SOURCE`）。按状态换美术，见 [`reference/states.md`](reference/states.md)。

| 属性 | 类型 / 取值 | 默认 | 说明 |
|---|---|---|---|
| `on` | `state-normal` / `state-hover` / `state-pressed` / `state-selected` / `state-disabled`（各支持 `@<id>`） | — | 必填；`state-selected` 仅 `<Tab>` / `<Toggle>` source 有意义（`<Btn>` 不 emit） |

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

**Discovering available icons** — 要查项目里有哪些 `setName:icon-name` 组合、以及 icon 名如何解析（相对 sourceFolder 路径、bare basename 简写、Template-Param 替换、sync 工具行为），见 [`reference/icons.md`](reference/icons.md)。

**Inline icons inside text** (`<sprite name="coin">` 图文混排，行内跟随文字换行) — tick **Generate Tmp Sprite Asset** on a SpriteSet, re-sync, then use native TMP `<sprite>` markup in any `<Text>` / `<Btn text>`; see [`reference/icons.md`](reference/icons.md) → "Inline sprites in text".

### Safe area

Wrap UI in `<SafeArea>` and put a `margin` on it to control inset. Per-edge `inset = max(designMargin_i, Screen.safeArea_i)` — the safe-area inset absorbs the design margin (not adds to it), so the same XML looks right on PC and on notched devices:

```xml
<Screen name="Lobby">
  <Image anchor="stretch" color="#08152C"/>           <!-- bleed background, sibling of SafeArea -->
  <SafeArea margin="6,6,6,6">
    <HStack id="topIcons" anchor="top-stretch" height="24"
            margin="0,0,_,_" spacing="4" childAlign="middle-right">...</HStack>
  </SafeArea>
</Screen>
```

- PC (no inset): you get exactly the `margin` you wrote (here, 6px on each edge).
- Notched device: the safe-area inset wins where it's bigger than your margin. E.g. iPhone 14 Pro (top inset ≈ 134, bottom ≈ 132 device px, sf=1): top=134, right=6, bottom=132, left=6.
- Design margin wins past the inset: `<SafeArea margin="200,_,_,_">` on the same device gives top=200 (your design value is bigger than 134).
- Unspecified edges (`_` or shorter than 4 components) default to 0 → that edge fully absorbs the device inset.

Other notes:

- `<SafeArea>` still rejects `anchor` / `size` / `width` / `height` / `pivot` (and their `.variant` forms). The container is always stretched to its parent; only `margin` is author-controlled.
- One `<SafeArea>` per `<Screen>`. Backgrounds that need to bleed past the safe area stay as siblings of `<SafeArea>`, not children.
- For "fixed gap below the safe area" (e.g. always 16px below the notch, never flush), nest a `<Frame anchor="stretch" margin="16,_,_,_"/>` inside the `<SafeArea>` instead of using the SafeArea's own margin.
- Don't put `<SafeArea>` inside `<VStack>` / `<HStack>` / `<Grid>` — the layout group will override its anchor math.
- Reacts automatically to screen rotation, window resize, Unity 6's Device Simulator, and Dynamic Island animations. No code-side wiring needed.

## uGUI 对照表

每个 PromptUGUI tag 在运行时落成一组 GameObject + Unity 组件。调试时在 Hierarchy 里按这张表把 XML 节点和实际 GO 对上；理解控件原理时把 XML 当成「这套 GO + 组件 + 默认绑线」的简写。

**Tag → GO 结构 / 组件**

| Tag            | 根节点组件                                                                                                                                                                                                                                                     | 自动子节点                                                                                                                                                                                                                                                                             | R3 事件源                                                                                                                   |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `<Frame>`      | `RectTransform` 单独；可选 `RectMask2D`（写 `mask="rect"` 时挂）                                                                                                                                                                                               | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<Image>`      | `Image` + (lazy) `PointerEventRelay`（被 hover/press trigger 引用为源时挂上）；可选 `RectMask2D`（`mask="rect"`）或 stencil `Mask`（`mask="self"`，用自身 sprite 作 mask 形状）                                                                                | —                                                                                                                                                                                                                                                                                      | `OnPointerEnter` / `OnPointerExit` / `OnPointerDown` ← Relay                                                                |
| `<RawImage>`   | `RawImage` + (lazy) `PointerEventRelay`；可选 `AspectRatioFitter`（`type=contain/cover`）/ `RectMask2D`（`mask="rect"`）/ stencil `Mask`（`mask="self"`）；图源 = C# `Texture` 属性                                                                            | —                                                                                                                                                                                                                                                                                      | `OnPointerEnter` / `OnPointerExit` / `OnPointerDown` ← Relay                                                                |
| `<Text>`       | `TextMeshProUGUI`                                                                                                                                                                                                                                              | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<VStack>`     | `VerticalLayoutGroup`（硬编码 `childControlWidth/Height=true`、`childForceExpand*=false`）                                                                                                                                                                     | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<HStack>`     | `HorizontalLayoutGroup`（同 VStack）                                                                                                                                                                                                                           | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<Grid>`       | `GridLayoutGroup`（`constraint=FixedColumnCount`）                                                                                                                                                                                                             | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<Btn>`        | `Image` + `Button`（`targetGraphic=Image`）+ (lazy) `PointerEventRelay`                                                                                                                                                                                        | `Label`(`TMP_Text`, stretch 撑满) — **lazy**：写了 `text=` 才创建                                                                                                                                                                                                                      | `OnClick` ← `Button.onClick`；`OnPointerEnter/Exit/Down` ← Relay                                                            |
| `<Icon>`       | `Image`（`preserveAspect=true`, `raycastTarget=false`）                                                                                                                                                                                                        | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<Toggle>`     | `Toggle`（`targetGraphic=Background`, `graphic=Checkmark`）                                                                                                                                                                                                    | `Background`(`Image`, left-middle 锚 20×20) → 内嵌 `Checkmark`(`Image`, 居中 20×20)；`Label`(`TMP_Text`, 右侧水平 stretch)                                                                                                                                                             | `OnValueChanged` ← `Toggle.onValueChanged`                                                                                  |
| `<Slider>`     | `Slider`                                                                                                                                                                                                                                                       | `Background`(`Image`)；`Fill Area` → `Fill`(`Image`)；`Handle Slide Area` → `Handle`(`Image`)                                                                                                                                                                                          | `OnValueChanged` ← `Slider.onValueChanged`                                                                                  |
| `<Dropdown>`   | `Image` + `TMP_Dropdown`                                                                                                                                                                                                                                       | `Label` + `Arrow` + `Template`（默认 inactive，内含 `Viewport` / `Content` / `Item` / `Scrollbar` 完整下拉子树）                                                                                                                                                                       | `OnSelected` ← `TMP_Dropdown.onValueChanged`                                                                                |
| `<ScrollList>` | `Image` + `ScrollRect`                                                                                                                                                                                                                                         | `Viewport`(`Image` + `Mask` stencil) → `Content`(V/H `LayoutGroup` + `ContentSizeFitter`)；按 `direction` 再加一个 `Scrollbar`                                                                                                                                                         | 无独立事件；C# 端 `BindItems(...)` 推数据                                                                                   |
| `<InputField>` | `Image` + `TMP_InputField`                                                                                                                                                                                                                                     | `Text Area`(`RectMask2D`) → `Placeholder`(`TMP_Text`, italic 半透明) + `Text`(`TMP_Text`)                                                                                                                                                                                              | `OnValueChanged` / `OnEndEdit` / `OnSubmit` ← `TMP_InputField.*`                                                            |
| `<Progress>`   | `RectTransform`（无 Graphic）                                                                                                                                                                                                                                  | `MaskWrapper`(`RectTransform`; 按需挂 `UnityImage` + `Mask`) → `Bg`(`Image`, 按需启用) + `Fill`(`Image`, 永远存在)；`Frame`(`Image`, 按需启用, `raycastTarget=false`)                                                                                                                  | —                                                                                                                           |
| `<TabBar>`     | `ToggleGroup` + `HorizontalLayoutGroup`（或 `VerticalLayoutGroup` 看 `direction=`）；无自身视觉，纯布局容器                                                                                                                                                    | XML 写的或 `BindItems` 推的 `<Tab>` children；视觉由 Tab 自管,共享样式靠 Template                                                                                                                                                                                                      | `OnSelectionChanged` ← per-Tab `OnValueChanged.Where(on => on)`                                                             |
| `<Carousel>`   | `RectTransform` + `CarouselView`（UIBehaviour，挂在 root；拖动事件从子卡冒泡上来）                                                                                                                                                        | `Viewport`(`UnityImage`+`RectMask2D`) → `Strip`(`RectTransform`，卡片的 parent)；`Indicator`(`RectTransform` + `HorizontalLayoutGroup`，点排)；每个 dot = `RectTransform`+`UnityImage`+`PuiButton`+`StateTintReactor` | 无独立 R3 事件；C# 端 `OnCurrentChanged: Observable<int>` + `BindItems(...)` |
| `<Tab>`        | `UnityImage`（bg, `targetGraphic`, supports `overrideSprite` swap while selected）+ `UnityToggle`（`graphic` 未设；配 `selectedSprite` 时 `transition=None`，否则 `transition=ColorTint`）；Toggle 的 `group` 在 `OnAttached` 用 transform-ancestor walk 找 TabBar 的 `ToggleGroup` | 可选 `Label`(`TMP_Text`, stretch fill, `Center` 对齐, raycast off, 懒建—写了 `text`/`fontSize`/`font` 才有)；可选 `Icon`(`Image`, 左 16px + 24×24, 懒建)；外加任意作者子节点（Frame 式叠放在 bg 上）；无 Overlay 自动子节点 | `OnValueChanged: bool` / `OnSelected: Unit`（只在 isOn=true 时 fire）                                                       |
| `<SafeArea>`   | `RectTransform` + `SafeAreaTracker`（内部 `MonoBehaviour`，订阅设备 safeArea / 旋转 / Device Simulator）                                                                                                                                                       | —                                                                                                                                                                                                                                                                                      | —                                                                                                                           |
| `<Trigger>`    | `RectTransform` 单独（无视觉、无 layout 行为，仅作 wrapper 划定事件源 scope）                                                                                                                                                                                  | —                                                                                                                                                                                                                                                                                      | `OnFire` ← R3 `Subject<Unit>`，由 `on=`（open/loop/click/hover-enter/hover-exit/press/manual）触发                          |
| `<Animation>`  | `RectTransform` + `CanvasGroup`（继承自 Trigger；CanvasGroup 给 `fade=` 用，由 `ApplyCommon` 懒加载）                                                                                                                                                          | `_offsetProxy`(`RectTransform`，anchor stretch、margin=0、pivot=0.5,0.5) — XML 子节点全 parent 到这一层；LitMotion 驱动它的 anchoredPosition / localScale / localEulerAngles                                                                                                           | `OnFire` ← 继承 Trigger；同时由 `on=` 触发 LitMotion `MotionHandle[]`                                                       |
| `<Show>`       | `RectTransform` 单独（继承自 Trigger；无视觉、无 layout）— 仅是一个按状态 `SetActive` 切换的 wrapper                                                                                                                                                           | —（作者子节点直接挂在它下面，整组随状态显隐）                                                                                                                                                                                                                                          | `OnFire` ← 继承 Trigger；不订阅 `OnState`，由最近 `<Btn>`/`<Tab>`/`<Toggle>` 祖先（`IStateSource`）的状态协调器统一驱动显隐 |

**Common attribute → uGUI 落点**（实现在 `Control.ApplyCommon`；对所有 tag 生效，`<SafeArea>` 例外，整套 anchor/size/width/height/pivot 都被拒绝）

| XML                         | uGUI 落点                                                                                                                                                                                                                                                                                                           |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `anchor`                    | `RectTransform.anchorMin` / `anchorMax`，并按 anchor 推导默认 `pivot`                                                                                                                                                                                                                                               |
| `size` / `width` / `height` | 父级不是 LayoutGroup：经 `MarginResolver` 写到 `RectTransform.sizeDelta`。父级是 `<VStack>` / `<HStack>`：写到 `LayoutElement.preferredWidth` / `preferredHeight` + 对应 `flexible*=0`（按轴路由，未写的轴留 `-1` 哨兵）。父级是 `<Grid>`：**被 GridLayoutGroup 接管**（cellSize 由 parent 决定，子节点写了也无视） |
| `margin`                    | `RectTransform.anchoredPosition` + `sizeDelta`（`MarginResolver` 按 anchor 自动反号；stretched 轴专门吃 margin）                                                                                                                                                                                                    |
| `pivot="x,y"`               | `RectTransform.pivot`（不写则从 anchor 推）                                                                                                                                                                                                                                                                         |
| `hidden="true"`             | `GameObject.SetActive(false)`                                                                                                                                                                                                                                                                                       |
| `interactable="false"`      | `CanvasGroup.interactable=false` + `blocksRaycasts=false`（首次访问按需 add `CanvasGroup`；级联到所有后代，比 `Selectable.interactable` 范围更大）                                                                                                                                                                  |

**不变量与易踩坑**

- 纯容器（`<Frame>` / `<*Stack>` / `<Grid>` / `<SafeArea>`）根上**没有** `Image`，本身不可见。要底色有两种写法,**优先用前者**:(1) 背景区域跟内容同区 → 直接拿 `<Image>` 当容器: `<Image sprite="...">...content...</Image>`(`<Image>` 是普通 Control,允许子节点,少一层节点);(2) 背景比内容更大(整屏底图 + 居中面板这类) → `<Image anchor="stretch"/>` 当**兄弟**放在内容之前(`<SafeArea>` 是唯一不可见还必须用兄弟模式的特例,因为它的 RectTransform 已经被 safeArea 偏移占用)。
- `<Btn>` 的 Label 是 lazy：写 `<Btn/>`（无 `text=`、无子 `<Text>`、无内联文本）不会有 Label 子 GO；之后 C# 设 `BtnInstance.Text = "x"` 才会现场补一个。
- `<Toggle>` 的 `targetGraphic` / `graphic` 在 `OnAttached` 内已绑死（Background / Checkmark），外部别再设；`group=` 不直接绑 Unity `ToggleGroup`，而是落到 `Screen.ToggleGroups.GetOrCreate(name)` 这个 Screen 范围的共享池里。
- `<ScrollList>` 的 item 子节点在 `OnAttached` 阶段是空的，必须在 C# 端 `BindItems(observable, (slot, item) => ...)` 之后才出现；hot-reload 后也要重新 Bind。
- `font="<type>"` 不是字体文件路径，而是 `PromptUGUISettings.fonts[]` 登记的**字体类型 key**（如 `"default"` / `"title"`），通过 `ResolveFont(locale, type)` 才解析到 `TMP_FontAsset`，并在 `UI.Locale.Changed` 时自动重赋。每个 type 还可在 Settings 里挂一个**可选的 TMP material preset**（如描边/发光）：`font="outline"` 这类 type 的 font 槽留空会继承该 locale `default` 的字体、只换 material，于是「同字体 + 不同材质」无需在 XML 里加任何属性。
- 内置 `<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>` 的 `sprite=` 走 `UI.ResolveSprite(value)` 双语法分流:含 `:` 的值(`sprite="ui:dialog"`)走 `UI.SpriteResolver` → SpriteSet/atlas 通道(包时按 XML scan 剪枝);无 `:` 的值(`sprite="ui/dialog"`)走 `Resources.Load<Sprite>(value)`(适合一次性 / 原型期 sprite)。bare path 支持 `#sliceName` 后缀,从多 sprite 切片纹理里按名取子 sprite,例如 `sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"` 走 `Resources.LoadAll<Sprite>(path)` 找 `name==sliceName`;`#` 之前的 `.png`/`.jpg`/`.jpeg`/`.tga`/`.psd` 扩展名会被剥掉,写不写后缀都行。`<Icon>` 仍强制 `ns:name` 形式,只走 SpriteResolver 通道。自定义 Control subclass 用同一 `UI.ResolveSprite(value)` 入口即可。**空值 `sprite=""` 与关键字 `sprite="none"` 等价**——都不解析任何贴图、把该 `sprite=` 所驱动的图清成 `null`,对所有内置控件及走同一入口的自定义 Control 一致生效。对自带默认 9-slice 底的 `<Btn>` / `<Tab>`,这会移除默认底、让控件退化为纯色实心矩形(颜色仍由 `color=` 控制;要透明就把 `color` 的 alpha 设 0,如 `color="#00000000"`);`<Toggle sprite="none">` 清的是 checkmark 图(box 底另算),`<Image sprite="none">` 清的是 Image 自身的图。`<Tab>` 的 `selectedSprite="none"` / `selectedSprite=""` 则表示"无 overrideSprite swap"（纯 no-op，不创建任何覆盖层）。

## Common attributes (any tag)

| Attribute | Format | Notes |
|---|---|---|
| `id="..."` | string | Unique within Screen / Template instance scope. Lift to dedicated handle for `Get<T>`. Inside a `<Template>` write a **literal** id — never `id="{{param}}"` (id is not substituted); instances are isolated per call. |
| `anchor="..."` | preset | See "Anchor system" below. Default `top-left`. |
| `size="WxH"` | `240x80` | Both dimensions in pixels (numeric only — keywords `stretch` / `N%` are **not** accepted here, use per-axis attrs). **Forbidden on stretched axes.** |
| `width="W"` / `height="H"` | float / `stretch[*N]` / `N%` | Numeric is base. `stretch` / `stretch*N` is LayoutGroup-only — see "Stretch keyword". `N%` is free-positioning-only — see "Fractional %". **Numeric forbidden on stretched anchor axes.** |
| `margin="..."` | 1/2/4 floats | Distance from anchor inward, positive. `"_"` = 0 placeholder. **4-component order `top,right,bottom,left`** (1 = all sides, 2 = `vertical,horizontal`). A margin only offsets from a side the `anchor` **consumes** — see **margin & consumed sides** below. |
| `pivot="x,y"` | `0..1, 0..1` | Defaults derive from `anchor`; rarely needed. |
| `hidden="true"` | bool | Initial `SetActive(false)`. |
| `interactable="false"` | bool | Initial `CanvasGroup.interactable=false` + `blocksRaycasts=false`. On `<Btn>` it **also** sets `Button.interactable=false` → the Btn enters its Disabled state (see `reference/states.md`). |
| `stateReact="false"` | bool (default `true`) | Opts this node **and its whole subtree** out of an ancestor `<Btn>` / `<Tab>` / `<Toggle>`'s `*Modulate` fan-out. Has no effect on `*Color` (absolute — bg only, never fanned out). See `reference/states.md`. |
| `scale="N"` / `scale="Nx"` / `scale="<r>r"` | float `N` / `Nx` (int) / `<r>r` (float) | Three forms: `N` = box-preserving (a render-density knob, **not** a resize knob); `Nx` = N physical px per design-unit (constant across factors, **doesn't grow with the window**); `<r>r` = `r×` the canvas factor **snapped to an integer** (**grows with the window yet stays pixel-aligned**). `scale="2"` ≠ `scale="2x"`. Full formulas / examples / caveats: see **Relative scale** / **Device-density** / **Canvas-relative snapped** below. |

**margin & consumed sides.** A margin only offsets from a side the `anchor` **consumes**: a **stretched** axis reads both its slots, a **point** anchor (`top` / `bottom` / `left` / `right`) reads only its own side, a **centered** axis reads neither. So `anchor="bottom-right" margin="60,_,_,_"` puts 60 in the **top** slot → silently dropped (a `bottom` anchor reads only the bottom slot; write `margin="_,_,60,_"` to push it up). The lint CLI flags a non-zero value on an unconsumed side as **`PUI-MARGIN-INERT-SIDE`** (CLI-only; 4-component + explicit-`anchor` form only — symmetric 1-/2-component shorthands always land on the consumed side and are not flagged).

`spacing` is **NOT** universal — only on `<VStack>` / `<HStack>` / `<Grid>`. `padding` is **per-control, not a common attribute** (each control that wants it declares its own): a layout-group inset on `<VStack>` / `<HStack>` / `<Grid>` / `<ScrollList>` / `<TabBar>`, and on `<InputField>` it means the inner space between the field edge and its text (auto-tracks the border — see the `<InputField>` row).

`anchor` and `margin` are **NOT** available on `<VStack>` / `<HStack>` / `<Grid>`.

**Inside `<VStack>` / `<HStack>`**, a child's explicit `size` / `width` / `height` is written to `LayoutElement.preferredX` **and `minX`** with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will neither stretch **nor shrink** it: the pinned `minX` means even a space-constrained group can't compress it (it overflows the group instead). This is what keeps a fixed-size trailing control at full size — e.g. an edit `<Btn size="12x12"/>` after an intrinsic-width `<Text>` in an `<HStack>`: when the text grows past the available width, the text (whose `minWidth` stays 0) gives way (compresses / ellipsizes), the button does **not**. Only `stretch` / `stretch*N` (`minX` stays `-1`, shrinkable to 0) and the native-fallback axes below remain compressible. **Per-axis native fallback** (CSS `inline-block` 直觉): each axis the author omits is independently filled from the control's intrinsic content size, so `<Btn width="100"/>` keeps `preferredWidth=100` and gets `preferredHeight=44` (Btn's default). Controls that report a native size: `<Btn>`、`<Toggle>`、`<Icon>`、`<Dropdown>`、`<Slider>`、`<ScrollList>`、`<InputField>`；`<Image>` 当 sprite 非空时 (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44; `<Toggle>静音</Toggle>` widens to fit text + 28 padding, default height 44; `<Dropdown/>` defaults 160×44; `<InputField/>` defaults 160×44; `<Slider/>` defaults 160×44 horizontal or 44×160 vertical; `<ScrollList/>` defaults 160×200 vertical or 200×160 horizontal; `<Image sprite="..."/>` defaults to `sprite.rect.size / pixelsPerUnit`); the native-filled axis keeps `flexibleX=-1` (the LE "no opinion" sentinel) so an intrinsic `ILayoutElement` can still contribute. 无 sprite 的 `<Image/>` 拿不到 native → 那一轴回到 `preferredX=-1` 哨兵，看自带 `ILayoutElement` 报告，空状态多半 0，要可见自己写 size。

**`<Text>` 是这条规则的例外 —— 在 V/HStack 里它不拍 native 快照。** Because TMP_Text is itself a live `ILayoutElement`, every axis the author omits is left at the `-1` sentinel so TMP drives it **dynamically**: a fixed-width or `width="stretch"` `wrap="true"` `<Text>` grows its **height to fit the wrapped content** (multi-line, and it re-measures whenever the text changes); a bare `<Text>Hi</Text>` with both axes omitted is sized entirely by TMP (no `LayoutElement` is attached). So **auto-height multiline labels come for free — just leave `height` off** (no placeholder-text trick needed). To pin a fixed height instead, write an explicit `height=`. (This only applies inside `<VStack>` / `<HStack>`; in free-positioning — next paragraph — `<Text>` still uses its native size, since `sizeDelta` is set once and an omitted-size label would otherwise be invisible.)

**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. **Per-axis native fallback**：`anchor` 该轴不 stretch + 该轴没写 size + 控件有 intrinsic content size（`<Btn>`、`<Toggle>`、`<Icon>`、`<Dropdown>`、`<Slider>`、`<ScrollList>`、`<InputField>`(默认 160×44)；`<Text>` 当 text 非空时取 TMP `preferredWidth/Height`；`<Image>` 当 sprite 非空时取 `sprite.rect.size / pixelsPerUnit`）→ 该轴 `sizeDelta` 用 native 兜底（避免 0 不可见）；写了的那一轴保留作者值。例：`<Text height="12">Lv. 45</Text>` 在 Frame 里 → 高 12 固定、宽按 TMP `preferredWidth` 自适应。空文本 `<Text/>` / 无 sprite 的 `<Image/>` 整体保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。

**`<Frame>` 默认 anchor 按轴 fill-or-fit**: 作者**没写** `anchor=` 时，Frame 按 size 是否存在分轴决定 —— 写过 `size`/`width`/`height` 的轴默认 top/left + 用作者写的值；没写的轴默认 stretch（填满父）。`<Frame/>` 两轴都 stretch（fill parent），`<Frame width="100"/>` X 轴固定 100、Y 轴 stretch，`<Frame size="100x50"/>` 两轴都 top-left 固定。镜像 CSS 块流：`<div style="width:100px">` 高度按 `auto` 撑开。**显式写 `anchor=`** 仍按原规则严格校验（`anchor="stretch"` + size attr 仍是 parse error）。其他控件维持 `(top, left)` 默认。

**Frame 在 `<VStack>` / `<HStack>` 里的 cross 轴也自动 fill**：上一条 anchor 默认对自由定位生效；放进 LayoutGroup 时 anchor 被接管，PromptUGUI 把同一份"按轴 stretch"意图沿用到 `LayoutElement` —— `<VStack><Frame height="180"/></VStack>` 的 Frame 横向 `preferred=0, flexible=1` 自动撑满 VStack 宽度，`<HStack><Frame width="180"/></HStack>` 同理纵向撑满。Btn/Toggle 等 `(top, left)` 默认的控件不受影响（在 VStack 里仍按 native preferred 宽显示，不会被强行拉满）。

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

## Layout group 放置配方（HStack / VStack）

| 目标布局                        | HStack 写法                                                                                            | 关键                                                        |
| ------------------------------- | ------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------- |
| 顶部右侧工具栏（按钮数可变）    | `<HStack anchor="top-stretch" height="<H>" margin="T,R,_,_" childAlign="middle-right" spacing="<S>">`  | **首选**。横跨整行，childAlign 推到右；加减按钮无需改 stack |
| 顶部左侧工具栏                  | `<HStack anchor="top-stretch" height="<H>" margin="T,_,_,L" spacing="<S>">`                            | 默认 `childAlign="middle-left"`，无需声明                   |
| 顶部居中工具栏                  | `<HStack anchor="top-stretch" height="<H>" margin="T,R,_,L" childAlign="middle-center" spacing="<S>">` | 或固定宽：`anchor="top-center" width="<W>"`                 |
| 顶部铺满（按钮等分整行）        | `<HStack anchor="top-stretch" height="<H>" margin="T,R,_,L">` + 每个 child `width="stretch"`           | child 用 `stretch*N` 实现 1:2:1 等加权                      |
| 左 logo + 右按钮组（split bar） | `<HStack anchor="top-stretch" ...>` body：`<Image .../>` → `<Frame width="stretch"/>`(spacer) → 按钮们 | spacer `width="stretch"` 吃光中间剩余空间                   |
| 底部工具栏                      | 把顶部配方的 anchor 改 `bottom-stretch`、margin 改 `_,R,B,L`                                           | 镜像；childAlign 同样适用                                   |

VStack 同理（纵轴，axis 翻转）：

| 目标布局                   | VStack 写法                                                                                            | 关键                                          |
| -------------------------- | ------------------------------------------------------------------------------------------------------ | --------------------------------------------- |
| 右侧垂直按钮列（数量可变） | `<VStack anchor="stretch-right" width="<W>" margin="T,R,B,_" childAlign="upper-center" spacing="<S>">` | **首选**。纵向 stretch，childAlign 控顶/中/底 |
| 右侧垂直按钮列（数量固定） | `<VStack anchor="top-right" width="<W>" height="<总高>" margin="T,R,_,_" spacing="<S>">`               | 总高 = Σchild + S×(N-1)                       |
| 居中垂直菜单               | `<VStack anchor="center" width="<W>" spacing="<S>">`                                                   | 自由定位 + 不写 height，沿 children 自然展开  |

⚠️ 反模式（lint / parser 不一定报但视觉上炸）：

- `<HStack anchor="top-right" height="56">` 没写 `width=` → 0 宽 rect，子按钮全挤在一起。

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
- **`id` is never substituted — use instance isolation, not a param.** `id` is a dedicated handle stored apart from attributes, so `{{param}}` does **not** apply to it: `id="{{sendId}}"` stays the literal string `{{sendId}}`. You don't need a parameterized id anyway — each template invocation gets its **own isolated id scope**, so a hard-coded `id="send"` is collision-free even when the template is used many times. Reach an inner id from C# with the path form `instanceId/send` (give the **invocation** an `id`). See **scripting-promptugui-csharp**.
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

## Color Tokens

Define named colors in `<Theme>` blocks; reference them by name in any color attribute.

### Authoring

```xml
<UIDocument>
  <Theme name="light">
    <Color name="primary"   value="#ff8800"/>
    <Color name="secondary" value="#0080ff"/>
    <Color name="label-fg"  value="#222222"/>
  </Theme>
  <Theme name="dark" base="light">
    <Color name="primary"  value="#cc6600"/>
    <Color name="label-fg" value="#e6e6e6"/>
    <!-- secondary inherits from base="light" -->
  </Theme>
</UIDocument>
```

- `<Theme>` MUST have `name`. Optional `base="other-theme"` makes missing tokens fall back along the chain.
- `<Color>` MUST have `name` (kebab-case, `[a-z0-9-]`) and `value` (hex / CSS-named, anything Unity's `ColorUtility.TryParseHtmlString` accepts).
- Theme XML loads via `UI.LoadCommonLibraryAsync(...)` at boot, or via `<Import src="themes/main"/>` from any screen's `.ui.xml` — both register the same `ThemeStore`.

### Reference

```xml
<Image color="primary"/>
<Text  color="label-fg" text="Hello"/>
<Btn   color="primary"  label="Buy"/>
```

Resolution order at runtime: current theme token → walk `base` chain → fall back to literal `ColorUtility.TryParseHtmlString` → if all fail, `ParseException` with node context.

### Shadow rule

If you register a token named `red`, then `color="red"` resolves to that token (NOT the CSS named color `red`). Token always wins over literal when both could parse. CSS named colors are rare in game UI; this trade-off is documented.

### Per-variant override

`color.dark="..."` works as expected for variant overrides — value goes through the same token → literal resolution chain.

### Alpha suffix (opacity)

Append `/<0..1>` to any color **reference** to set its opacity. The suffix REPLACES the resolved color's alpha (Unity `Color.a` semantics, like Flutter `withOpacity`) — it does not multiply:

```xml
<Frame color="black/0.5"/>     <!-- black backdrop at 50% opacity -->
<Image color="primary/0.8"/>   <!-- theme token's RGB, alpha forced to 0.8 -->
<Text  color="#ff0000/0.3" text="faint"/>
```

- Works on **every** color-valued attribute (one resolution chokepoint): `color`, the state colors `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`, `*Modulate`, variant overrides (`color.dark="primary/0.5"`), and `<Animation char-color="primary/1:primary/0">` (fade out).
- Replace, not multiply: a token whose own value carries alpha (e.g. `scrim = #00000080`) referenced as `scrim/1` comes out **fully opaque**.
- Value is a `0..1` float. Out of range or malformed (`/1.5`, `/abc`, `/`, no color before `/`) → `ParseException` with node context.
- Suffix is **reference-only**. Definition-side `<Color value="...">` does NOT take a suffix — bake alpha into the hex there (`value="#00000080"`) if you want a baked-alpha token.

### Error codes

- `<Theme>: missing required attribute 'name'`
- `<Color name="X" value="Y">: invalid color literal` — value didn't parse as hex and isn't a named color
- `<Color name="X">: token name must be kebab-case [a-z0-9-]`
- `<Theme name="X"> declares 'Y' twice`
- `duplicate <Theme name="X"> in 'src1' and 'src2'` — same theme name declared in two source files
- `<Theme name="X" base="Y">: base theme 'Y' not found`
- `<Theme> base cycle starting at 'X': ...`
- (Runtime, when value can't resolve) `<Image id='X'> attribute color="Y": unknown color token "Y" (no entry in theme 'Z', not a valid hex/named literal)`
- (Runtime, bad alpha suffix) `color "black/1.5": alpha 1.5 is out of range — must be 0..1`
- (Lint) `PUI-COLOR-LITERAL-INVALID` — static check: `color="#..."` literal that doesn't parse, or a hex literal with an out-of-range / malformed `/alpha` suffix

## Tint blend modes

`tint=` chooses how a control's `color` combines with its sprite. Available on these controls: `<Image>`, `<Icon>`, `<Btn>`, `<Toggle>`, `<Slider>`, `<Dropdown>`, `<ScrollList>`, `<InputField>`, `<Progress>`, `<Tab>`. On `<Tab>` `tint` applies to the bg; since `selectedSprite` now swaps the bg's own sprite, the selected sprite is tinted too. Not supported on `<Text>` (TMP uses its own shader, not the UI Image shader).

| `tint`               | Blend                                                                                                                                  | Use it for                                                                                   |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `multiply` (default) | `result = sprite × color` (Unity UI/Default). Omitting `tint` is identical to `multiply`.                                              | Normal colored sprites; darkening a sprite with `color`.                                     |
| `linear`             | Linear Light — sprite is the blend layer, `color` is the base; **128-gray in the sprite is neutral**, darker → black, lighter → white. | Grayscale sprites you want to recolor across the full range (can brighten, not only darken). |

```xml
<!-- grayscale sprite recolored with Linear Light -->
<Image src="card-grayscale" color="#ff8040" tint="linear"/>

<!-- default multiply (unchanged from before) -->
<Image src="card-color" color="#888888"/>
```

- `tint` is orthogonal to `color`: `color` can be a hex / CSS named / theme token; `tint` only picks the blend material.
- On `<Progress>` it applies to the fill, background, and frame layers together.
- Variants can switch it: `tint.dark="linear"` (goes through the normal `attr.var` → `ReSolve` path).
- Unknown values warn and fall back to `multiply`.

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
- `scale-mode="auto|pixel"` (+ `.variant`)：默认 `auto` = 上面 `reference` 的连续缩放语义。`pixel` 切到 `ConstantPixelSize` + 整数倍 `scaleFactor`（fit-inside 取小；屏幕 < 设计时 snap 到 1/2、1/4、1/8 等保 2x2 干净降采样）。**必须配 `reference="WxH"`**，否则运行期 `Debug.LogError` 并降级 `scaleFactor=1`。像素美术 / 等距图项目用 —— sprite 永远整数倍渲染到屏幕像素。项目级默认走 C# `UI.DefaultScaleMode = ScaleMode.Pixel`；具体 Screen 想退回连续缩放写 `scale-mode="auto"`。

### Relative scale (`scale="N"`) — box-preserving

`scale="N"` sets `RectTransform.localScale = (N, N, 1)` but is **box-preserving**: the element's declared `anchor` / `size` / `width` / `height` / `margin` describes its **visual box**, and `scale` only changes how finely its content is _rendered_ inside that box — **not** its on-screen size or position. `N=1` is identity; `N<1` renders finer (the content is laid out larger, then shrunk — crisper detail); `N>1` renders coarser. Same result on every screen / canvas factor.

Mechanically, the layout box stays put while the RectTransform is inflated by `1/N` so that `×N` lands back on the declared box. On a stretch (or `%`) axis the inflation lives in widened anchors, so Unity re-drives it on window/canvas resize for free; on a fixed-size axis `sizeDelta` is divided by `N`. `anchoredPosition` is unchanged.

> **This means `scale` is a render-density knob, not a resize knob.** To make an element visually smaller, give it a smaller `size` / `width` / `height` (or `%`) — don't reach for `scale`. Reach for `scale<1` when you want the _same footprint_ rendered with finer detail.

Primary use case is small text / detail UI inside a `scale-mode="pixel"` Screen. Pixel mode scales the whole Canvas by an integer factor (typically 3×/4×) to keep pixel-art crisp; great for sprites, but it locks small text out of finer-than-canvas detail. `scale="0.5"` on a label renders its glyphs at twice the canvas resolution (visually ≈ half the chunky integer step) **while keeping the label's box exactly as declared** — so a stretch-width `<Text scale="0.5">` wraps against its _full visual width_ instead of wrapping early. SDF text (TMP) stays readable; pixel-art sprites get blurred (so use this on text/UI, not on pixel-art `<Image>`s).

```xml
<!-- horizontal stretch label, rendered at 2× density; wraps against the full box, not half of it -->
<Frame width="40" height="50">
  <Tab anchor="stretch" sprite="" color="#0000">
    <Icon name="cog" anchor="top-center" size="24x24" margin="4,0,0,0"/>
    <Text anchor="top-stretch" margin="28,4,0,4" fontSize="12" scale="0.5"
          align="center" raycastTarget="false">Settings</Text>
  </Tab>
</Frame>
```

### Device-density (`scale="Nx"`)

`scale="Nx"` (N a **positive integer**) is the device-density form: `localScale = N / canvasFactor`, where `canvasFactor` is the live CanvasScaler factor. Because the factor cancels out, the element's content renders at exactly **N physical pixels per design-unit** regardless of which integer factor `scale-mode="pixel"` auto-computes (2 / 3 / 4 …), and it is **recomputed on canvas resize / device rotation**.

Why it exists: a pixel font's glyphs are crisp only when one source pixel maps to an integer number of physical pixels. A fixed multiplier like `scale="0.5"` breaks that under an odd factor (`3 × 0.5 = 1.5` px per source pixel → blur). `Nx` divides the factor out, so the on-screen result is always the integer N.

```xml
<!-- 12x12 CJK bitmap font in a scale-mode="pixel" canvas. At factor 3 it would be a
     chunky 36x36; scale="2x" renders it 24x24 and crisp at factor 2, 3 AND 4.
     Set fontSize to the font's native pixel height so 1 source pixel = 1 design-unit. -->
<Text fontSize="12" scale="2x" align="center">设置</Text>
```

Caveats:

- **N must be a positive integer.** `scale="1.5x"` is a parse error — use the plain multiplier `scale="1.5"` for non-aligned scaling. A non-integer N cannot be pixel-aligned.
- **`Nx` is truly crisp only in `scale-mode="pixel"`** (integer factor + `Canvas.pixelPerfect` snaps vertices). In `auto` mode the _size_ is still exactly N device-px per design-unit, but the element's position can land on a sub-pixel (auto mode does not snap), so text may be slightly soft.
- `Nx` only locks density. The font must also be authored so 1 source pixel = 1 design-unit — i.e. set `fontSize` to the font's native pixel height. A `fontSize` that differs from native still misaligns.
- Box-preserving behavior and the auto-bridge / LayoutGroup-skip caveats below apply identically to `Nx` (the inflation uses `1 / localScale`).

### Canvas-relative snapped (`scale="<r>r"`)

`scale="<r>r"` (r a **positive float**, lowercase `r`) scales the element to **r× the current canvas factor**, but snaps the result to the nearest integer net density so it stays pixel-aligned: `localScale = max(1, round(canvasFactor × r)) / canvasFactor`. The net physical-pixels-per-design-unit is `round(canvasFactor × r)` — an integer that **grows as the window grows** (unlike `Nx`, whose net is constant) while **never going off the pixel grid** (unlike a plain float, which blurs at odd factors). Recomputed on canvas resize / device rotation.

Why it exists: `scale="0.5"` follows the window but blurs at an odd factor (`3 × 0.5 = 1.5` px → off-grid); `scale="2x"` is always crisp but its size never grows with the window. `<r>r` gives the in-between: a smaller element that still responds to window size **and** stays crisp at every factor.

```xml
<!-- 12x12 CJK bitmap font: want it "about half" the chunky integer step, but crisp.
     0.5r → factor 2: net 1 (12px); factor 3: net 2 (24px); factor 4: net 2; factor 6: net 3.
     Always an integer net → pixel-aligned, and it grows as the window grows. -->
<Text fontSize="12" scale="0.5r" align="center">设置</Text>
```

Choosing between the three forms:

| Form                     | Net px/design-unit  | Grows with window? | Pixel-aligned?                | Use for                                                                   |
| ------------------------ | ------------------- | ------------------ | ----------------------------- | ------------------------------------------------------------------------- |
| `scale="N"` (float)      | `N × factor`        | yes                | only if `N×factor` is integer | render-density tweaks on SDF/TMP text; not pixel-art                      |
| `scale="Nx"` (N int)     | `N` (constant)      | no                 | yes (pixel mode)              | UI text at a fixed physical size across devices                           |
| `scale="<r>r"` (r float) | `round(factor × r)` | yes                | yes (pixel mode)              | small bitmap text/elements that scale with the window but must stay crisp |

Caveats:

- **Rounding is round-half-up**: `round(factor × r)` rounds `.5` up, so `0.5r` at factor 3 → net 2 (not 1), at factor 5 → net 3.
- **Clamped to a minimum net of 1**: when `round(factor × r) < 1` (e.g. `0.25r` at factor 1), the net floors at 1 — you can't go below one physical pixel per design-unit and stay aligned.
- **r may exceed 1** (`2r` grows twice as fast and stays aligned), and may be fractional (`0.25r`, `1.5r`). `r` must be positive; the suffix is lowercase `r` only — matching device-density's lowercase `x` (`0.5R` is a parse error).
- **Truly crisp only in `scale-mode="pixel"`** (integer factor + `Canvas.pixelPerfect`). In `auto` mode the net is still integer but position can land sub-pixel — same caveat as `Nx`.
- **Composes with `UI.PixelScalePowerOfTwo` / `UI.MinPixelScale`**: `<r>r` reads the final effective factor, so it snaps relative to whatever factor those settings produce.
- Box-preserving behavior and the auto-bridge / LayoutGroup-skip caveats below apply identically (the inflation uses `1 / localScale`).

**Where to put `scale`**:

- Directly on a `<Text>` / `<Image>` (single-element use) — works under any free-positioning parent (`<Frame>` / `<Screen>` / `<SafeArea>` / `<Tab>` / `<Toggle>`); anchor / margin / wrapping all behave against the visual box.
- Container `<Frame>` (for multi-element groups) — the whole subtree renders at density `N` inside the Frame's declared box.
- **A `<Text>` declaring `scale` (base or any variant override) as a direct child of `<VStack>` / `<HStack>` is auto-bridged** — the library inserts an invisible layout host (`"<id> [scale-host]"` in the Hierarchy; tag-named if no id) so the group measures the text's *visual* size (`TMP preferred × scale`): `width="stretch"` / fixed width works, the text wraps against its full inflated width, and the row height grows with the wrapped content (auto-height multiline keeps working, now at density — chat-message body text is the canonical use). Omitted axes stay TMP-driven; explicit `width=` / `height=` still pin the axis. Works for all three forms (`N` / `Nx` / `<r>r`) and recomputes on resize / Variant flips. C#-visible side effect: `Get<Text>(id).RectTransform.parent` is the auto host, not the stack.
- **On a direct child of `<Grid>`, or any non-`<Text>` control in a layout group, box-preserving is skipped** (the LayoutGroup owns the child's geometry). `localScale` still applies, but the group measures with the _unscaled_ `RT.rect`, so a `scale="0.5"` child still reserves its full unscaled slot (the "small text gap" footgun). Wrap in a `<Frame size="..." scale="0.5">` if you want the group to see the intended size.

**Variant-overridable**: `scale.mobile="0.5"` / `scale.portrait="2x"` follow the standard variant override shape. When a variant where `scale` is unresolved becomes active, `localScale` resets to 1 **and** the box-preserving inflation is removed (geometry returns to its plain margin-resolved baseline). Clearing under a variant is legal: `scale.mobile=""` restores identity (same convention as `size.mobile=""`); an empty BASE `scale=""` remains a parse error.

## Modal / Loading screens (XML contract)

Custom `MessageBox` / `Loading` overrides are regular `<Screen>` documents — the modal subsystem just instantiates them through the normal pipeline (anchor / margin / Variant / locale all work). Two specifics XML authors must know:

- **Dim backdrop is your responsibility.** The library does NOT auto-inject a full-screen Graphic behind the dialog. If you want clicks blocked on empty space, include something like `<Image id="backdrop" anchor="stretch" color="#000000FE"/>` as a sibling of your dialog Frame. Otherwise pointer raycasts outside the dialog box pass through to the Canvas below.
- **`MessageBox` requires specific `id=`s on built-in controls** (`text`, `title`, `ok`, `cancel`, `yes`, `no`, `close`, optional `icon`) so its `Bind` step can wire them via `screen.Get<T>(id)`. Default button labels are XML text content (`<Btn id="ok">OK</Btn>`) and become msgids extracted into your `.po` like any other `<Btn>` — translate them through your normal i18n workflow. **`Loading` only recognises `<Text id="text">`** (optional); everything else (spinner, backdrop) is up to you.

For the C# override mechanism (`MessageBox.XmlSrc = "..."`, `Loading.XmlSrc = "..."`), the full id contract, ESC behaviour, sortingOrder bands, and `UI.CanvasConfigurator` caveats, see the **scripting-promptugui-csharp** skill's "Modal dialogs" section.

## Mask & clipping

PromptUGUI never auto-enables masking — you must opt in via `mask=`. Two reasons: (1) stencil Mask isn't free (extra SetPass call, breaks batching with elements outside the mask); (2) "decorative background that lets children overflow" is a legit, common pattern.

| Want                                                          | Recipe                                                                                                                             | Component used                          |
| ------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| Pure container, no clip                                       | `<Frame/>` (current default)                                                                                                       | none                                    |
| Cheap rectangular clip (viewport-style)                       | `<Frame mask="rect"/>` or `<Image mask="rect" sprite="..."/>`                                                                      | `RectMask2D`                            |
| Sprite-shape clip + sprite drawn (rounded card)               | `<Image sprite="round" mask="self"/>`                                                                                              | stencil `Mask`, `showMaskGraphic=true`  |
| Sprite-shape clip + sprite hidden (viewport with shaped mask) | `<Image sprite="round-mask" mask="self" showMask="false"/>`                                                                        | stencil `Mask`, `showMaskGraphic=false` |
| Decorated outer frame + different inner clip shape            | Nest two `<Image>` — outer has `sprite=` only; inner has `mask="self" sprite=` (different shape) + `margin=` to control inner size | none on outer, stencil on inner         |

**Aspect-fit (`type="cover"` / `"contain"`) clipping**: the library never auto-clips a `cover` Image — its `AspectRatioFitter` sizes the Image to *envelop* its parent, so the overflow is clipped by a `mask="rect"` (`RectMask2D`) you put on the **parent** frame:

```xml
<Frame size="320x180" mask="rect">          <!-- box + clip both live on the parent -->
  <Image type="cover" sprite="ui:banner"/>  <!-- no anchor/size; the parent is the box -->
</Frame>
```

`contain` needs no mask (it fits *inside* the parent, letterboxing against the parent's background).

**Variant overrides** on `mask` / `showMask` / `maskPadding` are rejected in v1 (`PUI-MASK-VARIANT`) — switching mask mode means `AddComponent`/`Destroy` at runtime, which we don't support. If you need per-variant clipping, split into two Screens or use `<Add into=…>`.

## Progress

`<Progress>` 显示型线性进度条（只读，无 `OnValueChanged`）；一行打包 frame / mask / bg / fill / mode / direction / value。Radial 冷却环不在范围内。属性见上方 **Built-in primitives** 目录的 `<Progress>` 行。**写多图层 / mask×bg 组合 / 查 lint 规则前，先读 [`reference/controls-progress.md`](reference/controls-progress.md)。**

## Tabs

`<TabBar>` + `<Tab>`：互斥选项卡容器，`bind=` 声明式显隐兄弟 `<Frame>`，`selectedSprite` 单图换 `overrideSprite`，`isOn` 运行期独占，支持 `itemTemplate` + `BindItems`。属性见上方 **Built-in primitives** 目录的 `<TabBar>` / `<Tab>` 行；状态色见 [`reference/states.md`](reference/states.md)。**写自定义 Tab 布局 / 共享 Template / 查 lint 规则前，先读 [`reference/controls-tabs.md`](reference/controls-tabs.md)。**

## Common mistakes (XML)

| Symptom                                                                             | Cause                                                                                                                                                                                                                        | Fix                                                                                                                                                             |
| ----------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `cannot specify width/size on a horizontally-stretched axis`                        | `<X anchor="top-stretch" width="200"/>`                                                                                                                                                                                      | Either change anchor, or drop `width`. The stretched axis takes its size from `margin`.                                                                         |
| HStack/VStack 子节点全挤在一起 / 重叠 / 被压扁                                      | Stack 自己没写 `width=` / `height=`（free-positioning 下 `sizeDelta=(0,0)`），LayoutGroup 把 children 压进 0 宽/0 高 rect                                                                                                    | 给 stack 显式 `width=` / `height=`；或者改成 `anchor="X-stretch"` 让 stack 横跨整轴 + `childAlign=` 控制 children 靠哪边。见 "Layout group 放置配方"            |
| `<Text>` 宽度 0 / 渲染一字一行（free-positioning）                                  | `<Text/>` 在 `<Frame>` 等自由定位父级里 **XML 没写 text、运行时才 `text.TextValue = "..."`**。Initial ApplyCommon 看到 `_tmp.text == ""` → `GetNativeSize` 返回 null → `sizeDelta=(0,0)`；之后 C# 改文字不会重跑 ApplyCommon | 三选一：XML 给个非空占位（`<Text>...</Text>`，运行时再覆盖）让 native fallback 算出尺寸；或显式 `width=`/`size=`；或 `anchor="stretch"` + `margin`              |
| Ghost element on variant toggle                                                     | `<Add>` instantiated and never deactivated                                                                                                                                                                                   | This is by design (Strategy C). Use `hidden.variant` if you need a node to disappear.                                                                           |
| Parser silently merges children                                                     | Wrote `<Btn>开始 <Image/> </Btn>` (text + element mix)                                                                                                                                                                       | Pick one: text shorthand OR child elements. Mixed content is rejected.                                                                                          |
| Variant changes one attribute but not another                                       | `attr.variant` declared before `attr` (base) in the SAME element                                                                                                                                                             | Fine — declaration order is per-attribute. Just verify the right `.variant` exists.                                                                             |
| `'stretch' on width/height is only valid inside <VStack>/<HStack>`                  | `<Btn width="stretch"/>` under a `<Frame>` (or other non-LayoutGroup parent)                                                                                                                                                 | Either wrap the Btn in a stack, or switch to free-positioning: `anchor="X-stretch"` + `margin`                                                                  |
| `size 'stretchx72' is numeric-only...`                                              | Trying to put `stretch` or `%` keyword inside compact `size=`                                                                                                                                                                | `size=` is numeric-only. Use per-axis: `width="stretch" height="72"` or `width="50%"`                                                                           |
| `'%' (fractional) ... cannot be used inside <VStack>/<HStack>/<Grid>`               | `<Btn width="50%"/>` inside a VStack/HStack/Grid                                                                                                                                                                             | LayoutGroup is weight-based: use `stretch*N` + spacer siblings (e.g. spacer/stretch\*2/spacer = 25/50/25), or move the child to a `<Frame>` parent              |
| `stretch*0` / `stretch*-1` / `stretch*` rejected                                    | Invalid weight after `stretch*`                                                                                                                                                                                              | Weight must be a positive number, e.g. `stretch*2` / `stretch*0.5`                                                                                              |
| `'150%' must be in (0%, 100%]`                                                      | Percentage out of range                                                                                                                                                                                                      | Allowed range is `(0%, 100%]`. For "wider than parent", redesign the layout (likely a typo)                                                                     |
| UI 在不同屏上视觉大小不一（4K 上变邮票、手机上变巨人）                              | `<Screen>` 没设 `reference=`，走默认 `ConstantPixelSize, scaleFactor=1`，XML 数字直接 = 设备像素                                                                                                                             | 在 `<Screen>` 上加 `reference="1920x1080"`（或你的设计分辨率），切到 `ScaleWithScreenSize`                                                                      |
| `<Image sprite="ns:name"/>` 显示白图,控制台报 "UI.SpriteResolver is not registered" | 启动期未注册 SpriteResolver,`ns:name` 路径走 UI.SpriteResolver 找不到 atlas                                                                                                                                                  | 在 `UI.LoadDocumentAsync` / `UI.Open` 之前调一次 `SpriteResolverHelpers.UseSpriteSetResolver(spriteSets)`(或 `UseAddressableSpriteSetResolver` 走 Addressables) |

## Carousel（轮播卡）

`<Carousel>` 水平翻页轮播：自动播放 + 拖动吸附 + 无限循环 + 状态化指示点；卡片用 `itemTemplate` + C# `BindItems` 或静态子卡；当前页 `current` 是运行期独占状态（resize / Variant / Theme 不重置）。属性见上方 **Built-in primitives** 目录的 `<Carousel>` 行。**写动态/静态卡、配置 dots、查卡片布局约束与 lint 规则前，先读 [`reference/controls-carousel.md`](reference/controls-carousel.md)。**

## Markdown

`<Markdown>` Markdown document → scrollable subtree (headings/paragraphs/lists/blockquote/code/table/HR/image/links). Primary use case is dynamic load: leave the element empty in XML and set content from C# (`md.Text = …` / `md.BindText(stream)`). Static documents can be inlined via CDATA. Soft-depends on Markdig (`PROMPTUGUI_HAS_MARKDIG`); without it the raw text is shown as plain `<Text>`. **For dynamic/static usage patterns, attribute table, supported Markdown subset, block→control mapping, lossy notes, lint rules, and Markdig install + the PROMPTUGUI_HAS_MARKDIG gate, see [`reference/controls-markdown.md`](reference/controls-markdown.md).**

## Quick reference (cheatsheet)

```
MCP FEEDBACK  every .ui.xml write  →  refresh_unity + read_console (error,warning)
              MCP missing          →  ask user to open Unity + connect MCP for Unity
.NET LINT     every .ui.xml write  →  dotnet run --project Library/PackageCache/com.promptugui.core@<hash>/.lint/UIXmlLint -- <path/to/your.ui.xml>

ROOT          <PromptUGUI version="1"> ... </PromptUGUI>
TOP LEVEL     <Import src="" [as=""]/>  <Screen name="" [canvas="overlay|camera|world"]>  <Template name="">

BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
              <Toggle> <Slider> <Dropdown> <ScrollList> <InputField>
              <Progress value="0.6" fill="ui:bar"/>  最简；mask= + 不设 bg → mask sprite 自动可见兼当底；radial 进度环不在 <Progress> 范围
              <TabBar><Tab text="A" sprite="..." selectedSprite="..." bind="frame_a" isOn="true"/>...</TabBar>  互斥 + Tab 自管 sprite/selectedSprite + bind 自动 toggle Frame
              <Carousel itemTemplate="Card" interval="5" dots="bottom-center" dotSprite="ui:dot" dotColor="#888" dotSelectedColor="#fff"/>  翻页 + 自动播放 + 拖动 + 状态化指示点；卡片走 C# BindItems；当前页 resize 不重置
              <Show on="state-pressed">...</Show>  visible-while-state wrapper; siblings mutex; unclaimed states → state-normal fallback
TEXT SHORT    <Text>Hi</Text> ≡ <Text text="Hi"/>     (also <Btn>, <Toggle>, <InputField>)

BTN STATE     *Color (hoverColor/pressedColor/disabledColor)         absolute per-state bg colour (targetGraphic only, no fan-out)
              *Modulate (hoverModulate/pressedModulate/disabledModulate)  relative multiplier, fanned out to bg + all descendants, fade ~0.1s
              displayed = (absolute ?? color) × (modulate ?? white)
TAB/TOGGLE    + selectedColor (selection-aware bg base while active/isOn) / selectedModulate; <Tab selectedSprite>=overrideSprite swap on isOn (no overlay)
STATE         stateReact="false"  opt node+subtree out of *Modulate fan-out (no effect on *Color — absolute is bg-only)
              on="state-normal|hover|pressed|selected|disabled[@id]"  on <Trigger>/<Animation>/<Show>; resolves UPWARD to nearest <Btn>/<Tab>/<Toggle>; fires on enter
              state-selected is meaningful only on <Tab>/<Toggle> source; <Btn> never emits it
              详见 reference/states.md（状态化视觉）· reference/animations.md（Trigger/Animation）

COMMON ATTRS  id  anchor  size|width|height  margin  pivot  hidden  interactable  stateReact
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
              scale-mode="auto|pixel"          pixel = ConstantPixelSize + integer factor
                                               requires reference; project default via UI.DefaultScaleMode

I18N XML      <Text>...</Text>                 extract + translate
              <Text tr="false">...</Text>      skip
              <Text font="title">...</Text>    font type
              <Text ctx="door">Open</Text>     msgctxt disambiguation
```

## Triggers and Animations

`<Trigger>` 声明式事件钩子（`open` / `click` / `hover-*` / `press` / `state-*` / `manual` → C# `OnFire`）；`<Animation>` 在其上叠加 LitMotion 动效（preset 预设 / 低层 transform / 文字效果，三族互斥）。**用 `<Trigger>` / `<Animation>` 前，先读 [`reference/animations.md`](reference/animations.md)**（含完整 `on=` 表、easing、parse errors、patterns）。

## State visuals (Btn / Tab / Toggle)

`<Btn>` / `<Tab>` / `<Toggle>` 广播 uGUI 交互状态，三种响应（递进）：① `*Color`（绝对 bg 基色）/ `*Modulate`（相对乘子，向子树扩散，`stateReact="false"` 退出）；② `<Show on="state-*">` 按状态换整块美术（`<Btn pressedSprite>` / `<Btn disabledSprite>` / `<Tab selectedSprite>` 是单图简写）；③ `<Trigger>` / `<Animation on="state-*">` 状态触发动效。**用任何 `*Color` / `*Modulate` / `selectedColor` / `<Show on=state-*>` / `pressedSprite` / `selectedSprite` 前，先读 [`reference/states.md`](reference/states.md)。**

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
