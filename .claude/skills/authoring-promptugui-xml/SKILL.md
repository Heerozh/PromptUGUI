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

## Built-in primitives (18)

**默认视觉主题**：白底 sliced + #323232 深字（同 Unity 6 标准 UI prefab）。`color=` / `sprite=` 单点 override，整体深色覆写 `ProceduralBuilders` 常量或用 Variant `color.dark="..."`。想完全去掉自带 sliced 底（只留纯色或透明）写 `sprite="none"`（等价于 `sprite=""`）——见下方"内置控件 `sprite=` 解析"说明。

Pre-registered on `UI.Registry`. Use as XML tags by name:

| Tag            | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Tag-specific attributes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<Frame>`      | Empty container; optional `RectMask2D` via `mask="rect"`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | `mask` (`rect`), `maskPadding` (`T,R,B,L`, "\_" placeholder; only with `mask="rect"`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `<SafeArea>`   | Stretches to its parent; per-edge inset = `max(designMargin, Screen.safeArea_i)`. Auto-reacts to rotation, window resize, Device Simulator, Dynamic Island. Accepts `margin` (absorbed by device inset); **rejects** `anchor` / `size` / `width` / `height` / `pivot` (incl. `.variant`); see "Safe area" section below.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | —                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `<Image>`      | uGUI Image; loads sprites from `Resources`. Optional `RectMask2D` via `mask="rect"`, or stencil `Mask` via `mask="self"` (Image's own sprite becomes the mask shape).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | `sprite`, `color` (hex / CSS named color / theme token; at runtime, theme token wins over literal; see **Color Tokens** section), `type` (`simple` / `sliced` / `tiled` / `filled`; **omit to auto-pick `sliced` when sprite has a non-zero border, else `simple`**), `mask` (`rect` / `self`), `showMask` (bool, default `true`; only with `mask="self"`), `maskPadding` (`T,R,B,L`; only with `mask="rect"`), `tint` (`multiply` / `linear`; see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `<Text>`       | TMP_Text. Has text-content shorthand: `<Text>Hello</Text>` ≡ `<Text text="Hello"/>`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | `text`, `fontSize` (int), `color` (hex / CSS named color / theme token; see **Color Tokens** section), `align` (TMP alignment; one horizontal token `left` / `center` / `right` / `justified` / `flush` / `geo`, and/or one vertical token `top` / `middle` / `bottom` / `baseline` / `midline` / `capline`, hyphen- or space-joined and order-independent, e.g. `bottom-right`, `top-center`, `capline-flush`; a lone horizontal token keeps vertical `middle`, a lone vertical token keeps horizontal `left`; `left`/`center`/`right` therefore stay vertically centered as before; unknown token = parse error), `wrap` (bool), `raycastTarget` (bool), `font` (string, font type from Settings; default `default`), `autosize` (bool, default `false`; turns on TMP auto-sizing in **WD% only** form — character width squishes up to 50%, font size stays at `fontSize`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table)         |
| `<VStack>`     | Vertical layout group. Default `childAlign="upper-center"` (cross-axis centered).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | `spacing` (float), `padding` (`T,R,B,L` 1/2/4 components; `"_"` = 0 placeholder, e.g. `padding="6,_,_,_"`), `childAlign` (`upper/middle/lower-left/center/right`; `center` alias for `middle-center`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `<HStack>`     | Horizontal layout group. Default `childAlign="middle-left"` (cross-axis centered).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Same as VStack.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `<Grid>`       | Grid layout group, fixed columns.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | `columns` (int), `cellSize` (`WxH`), `spacing` (single or `H,V`), `padding`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `<Btn>`        | Image + Button + R3 `OnClick` / `OnState`. `<Btn>开始</Btn>` shorthand creates an internal TMP label child. Use as **template root** or registered prefab tag for any clickable. **不写 size 时自动按文字宽 + 左右 16 padding、上下 max(44, 文字高+12) 自适应**；无 text（icon-only）回退到 80×44。 `interactable="false"` also drives `Button.interactable` → Btn enters Disabled state (`disabledColor` applies, `state-disabled` fires) on top of the CanvasGroup raycast block. State colours: see **Btn state visuals** section.                                                                                                                                                                                                                                                                                                                                                                                                                                                       | `color` (hex / CSS named color / theme token; see **Color Tokens** section), `sprite`, `pressedSprite` (sprite key, same forms as `sprite`; while the Btn is held it swaps the bg via `overrideSprite` and reverts on release — the authored `sprite` is untouched; setting it auto-switches the Btn off uGUI's built-in ColorTint so the pressed image isn't double-darkened; `""` / `none` = no swap; composes with `pressedColor`; see **Btn state visuals**), `hoverColor` / `pressedColor` / `disabledColor` (hex / CSS named / theme token; **colour multipliers** in the uGUI ColorTint sense — see **Btn state visuals**), `fontSize` (int, applied to the auto-label only; other Text attrs like `align` / `wrap` require an explicit `<Text>` child), `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table), `tint` (`multiply` / `linear`; see **Tint blend modes**) |
| `<Icon>`       | Sprite from a project-level SpriteSet; by-name lookup, package-time pruning.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | `name` (required, `ns:icon-name`), `color` (hex / CSS named color / theme token; see **Color Tokens** section), `size` (`WxH` / `native`; 拉伸用 `anchor="stretch"`), `tint` (`multiply` / `linear`; see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `<Toggle>`     | Image + uGUI Toggle + auto label. R3 `OnValueChanged: bool`. `<Toggle>静音</Toggle>` shorthand sets the label. Same `group=` name → mutual exclusion. **不要给单个 Toggle 写 `group=`** — uGUI ToggleGroup 默认要求至少一个 active，单成员组一旦点上就锁死。**不写 size 时按文字宽 + 23 左 checkmark 区 + 5 右 padding、上下 max(44, 文字高+12) 自适应**；无 text（checkbox-only）回退到 44×44。State colours: see **Btn state visuals** section.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | `text`, `isOn` (bool, default false), `group` (string, mutual-exclusion key), `color` (hex / CSS named color / theme token; see **Color Tokens** section), `sprite` (Resources path for checkmark sprite), `font`, `tint` (`multiply` / `linear`; see **Tint blend modes**), `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor` (hex / CSS named / theme token; **colour multipliers** in the uGUI ColorTint sense; `selectedColor` applies while the Toggle is the `isOn` one at rest; presence of any one switches the control to reactor-driven tint that fans out to bg + descendant graphics — opt a child out with `stateReact="false"` — see **Btn state visuals**)                                                                                                                                                                                                                                                                                                                                    |
| `<Slider>`     | Image + uGUI Slider. R3 `OnValueChanged: float`. **不写 size 时按方向给默认**：横向 160×44、纵向 44×160（长边视觉宽度、短边 tap target）。                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | `min` (float), `max` (float), `value` (float), `wholeNumbers` (bool), `direction` (`horizontal` / `vertical` / `reverse-horizontal` / `reverse-vertical`), `color` (hex / CSS named color / theme token; see **Color Tokens** section), `sprite`, `tint` (`multiply` / `linear`; see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `<Dropdown>`   | TMP_Dropdown. R3 `OnSelected: int`. Options pushed C#-side via `BindOptions(...)`. **不写 size 时默认 160×44**（不读 caption 文字宽，避免每选一项就改宽度）。                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | `value` (int initial index), `color` (hex / CSS named color / theme token; see **Color Tokens** section), `sprite`, `font`, `tint` (`multiply` / `linear`; see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `<ScrollList>` | ScrollRect + Mask. Items pushed C#-side via `BindItems(...)`. `itemTemplate` references a `<Template name=...>` or registered Control class. **不写 size 时按方向给视口默认**：纵向滚动 160×200、横向滚动 200×160；实际项目通常显式写 size。                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | `itemTemplate` (required tag name), `direction` (`vertical` / `horizontal`), `spacing` (float), `padding`, `color` (hex / CSS named color / theme token; see **Color Tokens** section), `sprite`, `tint` (`multiply` / `linear`; see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `<InputField>` | TMP_InputField；R3 `OnValueChanged` / `OnEndEdit` / `OnSubmit: string`。`<InputField>初始文本</InputField>` 短手设 `text=`。                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | `text`, `placeholder`, `contentType` (`standard`/`autocorrected`/`integer-number`/`decimal-number`/`alphanumeric`/`name`/`email`/`password`/`pin`/`custom`), `lineType` (`single`/`multi-newline`/`multi-submit`), `characterLimit` (int), `readOnly` (bool), `color` (hex / CSS named color / theme token; see **Color Tokens** section), `sprite`, `font`, `tr` (placeholder)/`ctx`, `tint` (`multiply` / `linear`; see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `<Progress>`   | 显示型线性进度条（只读，无 `OnValueChanged`）。一行配齐 frame / mask / bg / fill / mode / direction / value，零手糊图层。                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | `value` (float `[0..1]`, default `0`), `fill` (sprite key), `fillColor` (hex / CSS named / theme token), `bg` (sprite key), `bgColor` (hex / CSS named / theme token; 单独设也激活 bg 层), `frame` (sprite key), `frameColor` (hex / CSS named / theme token; 单独设也激活 frame 层), `mask` (sprite key), `mode` (`scale`\|`fill`, default `scale`), `direction` (`horizontal`\|`vertical`\|`reverse-horizontal`\|`reverse-vertical`, default `horizontal`), `tint` (`multiply` / `linear`; applies to fill+bg+frame together — see **Tint blend modes**)                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `<TabBar>`     | Tab container; private `ToggleGroup` (`allowSwitchOff=false`) + `Horizontal`/`VerticalLayoutGroup`. Pure layout — no own visual (wrap in `<Image>` if you need a background strip). Children may be direct `<Tab>` or Template wrappers containing a `<Tab>` (recursive collect). Supports `itemTemplate` + `BindItems` for dynamic content (same shape as `<ScrollList>`). Behaves as a layout group for children — Tabs can't declare `anchor` / `margin`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | `direction` (`horizontal` / `vertical`, default `horizontal`), `spacing` (float), `padding` (`T,R,B,L`), `itemTemplate` (tag name, default `Tab`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `<Tab>`        | Child of `<TabBar>`; uGUI `Toggle` + centered TMP label + optional left-side icon + optional selected overlay. Mutex via TabBar's `ToggleGroup` (automatic). `bind="frame_id"` declaratively shows/hides a sibling `<Frame>` on selection (lazy lookup, cached). No `bind=` → only `OnSelected`. `sprite` sets the normal bg; `selectedSprite` creates the overlay swap (bound to `UnityToggle.graphic`, instant transition); omit `selectedSprite` for "button mode" (mutex stays, no selected feedback — ColorTint on `_bg` still highlights). To share visuals across all Tabs in a bar, put them on a `<Template>` and use it as `itemTemplate` or as a static child wrapper. Accepts nested XML children (overlaid Frame-style on the Tab bg). For click-through, children must set `raycastTarget="false"` (`<Icon>` already does); a child that keeps `raycastTarget=true` (e.g. a nested `<Btn>`) handles its own clicks instead. State colours: see **Btn state visuals** section. | `text`, `isOn` (bool, default `false`), `bind` (id of sibling `<Frame>` to show/hide), `color` (hex / CSS named color / theme token; `#00000000` = transparent-but-clickable), `font`, `fontSize` (int), `icon` (sprite key, left-aligned 24×24 with 4px gap), `sprite` (normal bg; `""` / `none` 移除自带 9-slice 底), `selectedSprite` (overlay sprite when on; `""` / `none` = 无 overlay, no-op), `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor` (hex / CSS named / theme token; **colour multipliers** in the uGUI ColorTint sense; `selectedColor` applies while the Tab is the active/`isOn` one at rest; presence of any one switches the control to reactor-driven tint that fans out to bg + descendant graphics — opt a child out with `stateReact="false"` — see **Btn state visuals**), `tint` (`multiply` / `linear`; applies to the bg only — see **Tint blend modes**)                                                                                                                                                                                                       |
| `<Show>`       | No-visual wrapper (Trigger-derived). Its subtree is **visible while** the nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` is in the `on="state-*"` state, hidden otherwise (`SetActive` toggle, never destroyed — hidden subtrees + their R3 subs survive). Sibling `<Show>` blocks under one source control are **mutually exclusive**; a state with no explicit `<Show>` falls back to the `state-normal` block (so declaring only `state-normal` + `state-pressed` makes Normal artwork also cover PC `state-hover` — add an explicit `state-hover` block to override). Only `state-*` `on=` values are valid (any other, e.g. `on="click"`, is an error). Bare `state-*` needs a `<Btn>` / `<Tab>` / `<Toggle>` ancestor (`PUI-STATE-NO-SOURCE`). Use it to swap artwork per state — see **Btn state visuals**.                                                                                                                                                                         | `on` (required; one of `state-normal` / `state-hover` / `state-pressed` / `state-selected` / `state-disabled`, each also `@<id>`; `state-selected` is meaningful only with a `<Tab>` / `<Toggle>` source — a `<Btn>` never emits it)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

`<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<TabBar>` are reference implementations. For project-specific differentiation (pixel border, press feedback, custom popup chrome) subclass and override `OnAttached` — see scripting-promptugui-csharp.

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
| `<Tab>`        | `UnityImage`（bg, `targetGraphic`, `sprite`= 自身常态底）+ `UnityToggle`（`transition=ColorTint`，配 `selectedSprite` 时 `graphic=Overlay`、`toggleTransition=None`）；Toggle 的 `group` 在 `OnAttached` 用 transform-ancestor walk 找 TabBar 的 `ToggleGroup` | 可选 `Label`(`TMP_Text`, stretch fill, `Center` 对齐, raycast off, 懒建—写了 `text`/`fontSize`/`font` 才有)；可选 `Icon`(`Image`, 左 16px + 24×24, 懒建)；写了 `selectedSprite` 才有 `Overlay`(`Image`, stretch fill, 绑到 `Toggle.graphic`)；外加任意作者子节点(Frame 式叠放在 bg 上) | `OnValueChanged: bool` / `OnSelected: Unit`（只在 isOn=true 时 fire）                                                       |
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
- 内置 `<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>` 的 `sprite=` 走 `UI.ResolveSprite(value)` 双语法分流:含 `:` 的值(`sprite="ui:dialog"`)走 `UI.SpriteResolver` → SpriteSet/atlas 通道(包时按 XML scan 剪枝);无 `:` 的值(`sprite="ui/dialog"`)走 `Resources.Load<Sprite>(value)`(适合一次性 / 原型期 sprite)。bare path 支持 `#sliceName` 后缀,从多 sprite 切片纹理里按名取子 sprite,例如 `sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"` 走 `Resources.LoadAll<Sprite>(path)` 找 `name==sliceName`;`#` 之前的 `.png`/`.jpg`/`.jpeg`/`.tga`/`.psd` 扩展名会被剥掉,写不写后缀都行。`<Icon>` 仍强制 `ns:name` 形式,只走 SpriteResolver 通道。自定义 Control subclass 用同一 `UI.ResolveSprite(value)` 入口即可。**空值 `sprite=""` 与关键字 `sprite="none"` 等价**——都不解析任何贴图、把该 `sprite=` 所驱动的图清成 `null`,对所有内置控件及走同一入口的自定义 Control 一致生效。对自带默认 9-slice 底的 `<Btn>` / `<Tab>`,这会移除默认底、让控件退化为纯色实心矩形(颜色仍由 `color=` 控制;要透明就把 `color` 的 alpha 设 0,如 `color="#00000000"`);`<Toggle sprite="none">` 清的是 checkmark 图(box 底另算),`<Image sprite="none">` 清的是 Image 自身的图。`<Tab>` 的 `selectedSprite="none"` / `selectedSprite=""` 则表示"无 selected overlay"(无默认 overlay,纯 no-op)。

## Common attributes (any tag)

| Attribute                                   | Format                                                                                                    | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ------------------------------------------- | --------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id="..."`                                  | string                                                                                                    | Unique within Screen / Template instance scope. Lift to dedicated handle for `Get<T>`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `anchor="..."`                              | preset                                                                                                    | See "Anchor system" below. Default `top-left`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `size="WxH"`                                | `240x80`                                                                                                  | Both dimensions in pixels (numeric only — keywords `stretch` / `N%` are **not** accepted here, use per-axis attrs). **Forbidden on stretched axes.**                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `width="W"` / `height="H"`                  | float / `stretch[*N]` / `N%`                                                                              | Numeric is base. `stretch` / `stretch*N` is LayoutGroup-only — see "Stretch keyword". `N%` is free-positioning-only — see "Fractional %". **Numeric forbidden on stretched anchor axes.**                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `margin="..."`                              | 1/2/4 floats                                                                                              | "Distance from anchor inward, positive". `"_"` = 0 placeholder. **4-component order is `top,right,bottom,left`** (1 component = all sides; 2 = `vertical,horizontal`). A margin only offsets from a side the `anchor` **consumes**: a **stretched** axis reads both its slots, a **point** anchor (`top`/`bottom`/`left`/`right`) reads only its own side, a **centered** axis reads neither. So `anchor="bottom-right" margin="60,_,_,_"` puts 60 in the **top** slot → silently dropped (a `bottom` anchor reads only the bottom slot; write `margin="_,_,60,_"` to push it up). The lint CLI flags a non-zero value on an unconsumed side as **`PUI-MARGIN-INERT-SIDE`** (CLI-only; 4-component + explicit-`anchor` form only — symmetric 1-/2-component shorthands always land on the consumed side and are not flagged).                                                                                                                                                                               |
| `pivot="x,y"`                               | `0..1, 0..1`                                                                                              | Defaults derive from `anchor`; rarely needed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `hidden="true"`                             | bool                                                                                                      | Initial `SetActive(false)`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `interactable="false"`                      | bool                                                                                                      | Initial `CanvasGroup.interactable=false` + `blocksRaycasts=false`. On `<Btn>` it **also** sets `Button.interactable=false` → the Btn enters its Disabled state (see **Btn state visuals**).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `stateReact="false"`                        | bool (default `true`)                                                                                     | Opts this node **and its whole subtree** out of an ancestor `<Btn>` / `<Tab>` / `<Toggle>`'s state-colour tint fan-out (`hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`). See **Btn state visuals**.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `scale="N"` / `scale="Nx"` / `scale="<r>r"` | positive float `N`; **or** `Nx` (N a positive integer); **or** `<r>r` (r a positive float, lowercase `r`) | `scale="N"` (float): `localScale=(N,N,1)`, **box-preserving** — declared `anchor`/`size`/`margin` stays the visual box, `N` only changes render density (`N<1` finer/crisper, `N>1` coarser), not on-screen size. `scale="Nx"` (device-density): `localScale = N / canvasFactor` → locks to **N physical pixels per design-unit** (constant size across factors, does **not** grow with the window). `scale="<r>r"` (canvas-relative snapped): `localScale = max(1, round(canvasFactor × r)) / canvasFactor` → scales to `r×` the canvas factor but **snaps the net to an integer**, so it **grows with the window yet stays pixel-aligned at any factor** (e.g. `0.5r` is net 1 px/unit at factor 2, net 2 at factor 3 _and_ 4, net 3 at factor 6). All three recompute on factor change; `Nx`/`<r>r` are crisp under a `scale-mode='pixel'` integer factor. `scale="2"` (coarse 2×) and `scale="2x"` (net 2 device-px) differ. See "Relative scale" / "Device-density" / "Canvas-relative snapped" below. |

`padding` and `spacing` are **NOT** universal — only on `<VStack>` / `<HStack>` / `<Grid>`.

`anchor` and `margin` are **NOT** available on `<VStack>` / `<HStack>` / `<Grid>`.

**Inside `<VStack>` / `<HStack>`**, a child's `size` / `width` / `height` is written to `LayoutElement.preferredX` with `flexibleX=0` (not to `sizeDelta`). So `<Btn size="64x64"/>` inside a VStack is **strictly 64×64** — the layout group will not stretch it. **Per-axis native fallback** (CSS `inline-block` 直觉): each axis the author omits is independently filled from the control's intrinsic content size, so `<Btn width="100"/>` keeps `preferredWidth=100` and gets `preferredHeight=44` (Btn's default). Controls that report a native size: `<Btn>`、`<Toggle>`、`<Icon>`、`<Dropdown>`、`<Slider>`、`<ScrollList>`；`<Text>` 当 text 非空、`<Image>` 当 sprite 非空时 (e.g. `<Btn>OK</Btn>` widens to fit text + padding, default height 44; `<Toggle>静音</Toggle>` widens to fit text + 28 padding, default height 44; `<Dropdown/>` defaults 160×44; `<Slider/>` defaults 160×44 horizontal or 44×160 vertical; `<ScrollList/>` defaults 160×200 vertical or 200×160 horizontal; `<Text>Hello</Text>` widens to TMP `preferredWidth/Height`; `<Image sprite="..."/>` defaults to `sprite.rect.size / pixelsPerUnit`); the native-filled axis keeps `flexibleX=-1` (the LE "no opinion" sentinel) so an intrinsic `ILayoutElement` can still contribute. 空文本 `<Text/>` / 无 sprite 的 `<Image/>` 拿不到 native → 那一轴回到 `preferredX=-1` 哨兵，看其他 `ILayoutElement`（TMP / Image 自带）报告，空状态多半 0，要可见自己写 size。

**Inside `<Frame>` / `<Screen>` / `<SafeArea>` (free-positioning)**, a child's `size` / `width` / `height` is written to `RectTransform.sizeDelta`. **Per-axis native fallback**：`anchor` 该轴不 stretch + 该轴没写 size + 控件有 intrinsic content size（`<Btn>`、`<Toggle>`、`<Icon>`、`<Dropdown>`、`<Slider>`、`<ScrollList>`；`<Text>` 当 text 非空时取 TMP `preferredWidth/Height`；`<Image>` 当 sprite 非空时取 `sprite.rect.size / pixelsPerUnit`）→ 该轴 `sizeDelta` 用 native 兜底（避免 0 不可见）；写了的那一轴保留作者值。例：`<Text height="12">Lv. 45</Text>` 在 Frame 里 → 高 12 固定、宽按 TMP `preferredWidth` 自适应。空文本 `<Text/>` / 无 sprite 的 `<Image/>` 整体保持 `sizeDelta=(0,0)`，得自己写 `size` 或 `anchor="stretch"` + `margin`。

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

### Error codes

- `<Theme>: missing required attribute 'name'`
- `<Color name="X" value="Y">: invalid color literal` — value didn't parse as hex and isn't a named color
- `<Color name="X">: token name must be kebab-case [a-z0-9-]`
- `<Theme name="X"> declares 'Y' twice`
- `duplicate <Theme name="X"> in 'src1' and 'src2'` — same theme name declared in two source files
- `<Theme name="X" base="Y">: base theme 'Y' not found`
- `<Theme> base cycle starting at 'X': ...`
- (Runtime, when value can't resolve) `<Image id='X'> attribute color="Y": unknown color token "Y" (no entry in theme 'Z', not a valid hex/named literal)`
- (Lint) `PUI-COLOR-LITERAL-INVALID` — static check: `color="#..."` literal that doesn't parse

## Tint blend modes

`tint=` chooses how a control's `color` combines with its sprite. Available on these controls: `<Image>`, `<Icon>`, `<Btn>`, `<Toggle>`, `<Slider>`, `<Dropdown>`, `<ScrollList>`, `<InputField>`, `<Progress>`, `<Tab>`. On `<Tab>` it applies to the bg only (where `color` lands) — the `selectedSprite` overlay is not tinted. Not supported on `<Text>` (TMP uses its own shader, not the UI Image shader).

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
- Box-preserving behavior and the LayoutGroup-skip caveat below apply identically to `Nx` (the inflation uses `1 / localScale`).

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
- Box-preserving behavior and the LayoutGroup-skip caveat below apply identically (the inflation uses `1 / localScale`).

**Where to put `scale`**:

- Directly on a `<Text>` / `<Image>` (single-element use) — works under any free-positioning parent (`<Frame>` / `<Screen>` / `<SafeArea>` / `<Tab>` / `<Toggle>`); anchor / margin / wrapping all behave against the visual box.
- Container `<Frame>` (for multi-element groups) — the whole subtree renders at density `N` inside the Frame's declared box.
- **On a direct child of `<VStack>` / `<HStack>` / `<Grid>`, box-preserving is skipped** (the LayoutGroup owns the child's geometry). `localScale` still applies, but the group measures with the _unscaled_ `RT.rect`, so a `scale="0.5"` child still reserves its full unscaled slot (the "small text gap" footgun). Wrap in a `<Frame size="..." scale="0.5">` if you want the group to see the intended size.

**Variant-overridable**: `scale.mobile="0.5"` / `scale.portrait="2x"` follow the standard variant override shape. When a variant where `scale` is unresolved becomes active, `localScale` resets to 1 **and** the box-preserving inflation is removed (geometry returns to its plain margin-resolved baseline).

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

**Variant overrides** on `mask` / `showMask` / `maskPadding` are rejected in v1 (`PUI-MASK-VARIANT`) — switching mask mode means `AddComponent`/`Destroy` at runtime, which we don't support. If you need per-variant clipping, split into two Screens or use `<Add into=…>`.

## Progress

`<Progress>` 是显示型线性进度条，把 frame / mask / bg / fill / mode / direction / value 打包进一行 XML。**只读** — C# 侧直接 setter，无 `OnValueChanged` Observable。

Radial fill（冷却环）不在 `<Progress>` 范围；以后用单独的 `<Cooldown>` 控件。

### 六个典型用例

```xml
<!-- 1. 最简：纯色 bg + 单色 fill；scale 横向 -->
<Progress value="0.6" bgColor="#222" fillColor="#3cf"/>

<!-- 2. 单 sprite 填充；scale 横向 -->
<Progress value="0.6" fill="ui:bar_red"/>

<!-- 3. 圆角胶囊：mask sprite 兼当底 (PB-D9) -->
<Progress value="0.4" mask="ui:pill" fill="ui:bar_blue"/>

<!-- 4. 全套装饰：frame + mask + bg + fill；frameColor 给金边换色 -->
<Progress value="0.6" frame="ui:gold_border" frameColor="#ffd56b" mask="ui:pill" bg="ui:track" fill="ui:bar_red"/>

<!-- 5. Unity Image.Type.Filled, 反向纵向（液体从顶部往下空） -->
<Progress value="0.3" fill="ui:liquid" mode="fill" direction="reverse-vertical"/>

<!-- 6. 在 Variant 中切换 value / colors (frame / bg / fill sprite 允许；mask 完全禁止 — PUI-PROG-MASK-VARIANT) -->
<Progress id="hp"
          value="1.0" value.low="0.2"
          fill="ui:bar" fillColor.low="#f44"
          bgColor="#000"/>
```

### mask × bg 四种组合

| 条件                   | MaskWrapper.UnityImage | MaskWrapper.Mask | MaskWrapper.showMaskGraphic | Bg.SetActive | Frame.SetActive |
| ---------------------- | ---------------------- | ---------------- | --------------------------- | ------------ | --------------- |
| 无 mask、无 bg/bgColor | 不挂                   | 不挂             | —                           | false        | (按 frame)      |
| 无 mask、有 bg/bgColor | 不挂                   | 不挂             | —                           | true         | (按 frame)      |
| 有 mask、无 bg/bgColor | 挂（sprite=mask）      | 挂               | true                        | false        | (按 frame)      |
| 有 mask、有 bg/bgColor | 挂（sprite=mask）      | 挂               | false                       | true         | (按 frame)      |

`有 mask、无 bg/bgColor` 时 `showMaskGraphic=true` — mask sprite 兼任可见底，一个 sprite 干两件事（圆角胶囊最常见路径）。

### Lint 规则

| Code                    | 触发条件                                                                      | 级别    |
| ----------------------- | ----------------------------------------------------------------------------- | ------- |
| `PUI-PROG-VALUE-RANGE`  | `value` 字面量超出 `[0..1]`                                                   | warning |
| `PUI-PROG-MODE`         | `mode` 不在 `scale\|fill`                                                     | error   |
| `PUI-PROG-DIRECTION`    | `direction` 不在 `horizontal\|vertical\|reverse-horizontal\|reverse-vertical` | error   |
| `PUI-PROG-CHILDREN`     | `<Progress>` 包含子元素                                                       | error   |
| `PUI-PROG-MASK-VARIANT` | `mask` 出现在 Variant 覆盖里                                                  | error   |
| `PUI-PROG-NO-FILL`      | `value` 有值但 `fill`/`fillColor` 均未设                                      | warning |

## Tabs

`<TabBar>` 是 Tab 的容器；私有 `ToggleGroup`（`allowSwitchOff=false`）保证互斥，私有 `HorizontalLayoutGroup` / `VerticalLayoutGroup`（看 `direction=`）排布。**TabBar 本身没视觉**，只是布局容器；每个 `<Tab>` 自带 `sprite`（常态底）和 `selectedSprite`（选中时 overlay，绑到 `UnityToggle.graphic`，瞬切无淡入）。不写 `selectedSprite` Tab 退化成"按钮"视觉 —— 互斥仍在，没有 overlay，但 `UnityToggle` 的 `ColorTint` 还会给背景图染色作为兜底高亮。`selectedSprite=""`（空字符串）按"未声明"处理，不会创建 overlay。

```xml
<TabBar id="topbar" anchor="top-stretch" height="40">
  <Tab text="Edit"     sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="editor_panel" isOn="true"/>
  <Tab text="Help"     sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="help_panel"/>
  <Tab text="Settings" sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="settings_panel"/>
</TabBar>

<Frame id="editor_panel"   margin="40,0,0,0">...</Frame>
<Frame id="help_panel"     margin="40,0,0,0">...</Frame>
<Frame id="settings_panel" margin="40,0,0,0">...</Frame>
```

要把所有 Tab 的视觉抽出来共享,定义一个 `<Template>` 让 Tab 在里面声明 sprite,然后当作 TabBar 子元素或者 `itemTemplate=` 使用（见下面 "Custom Tab layout via Template"）。

要给 TabBar 本身加一条背景条,直接把 TabBar 套进一个 `<Image>` —— `<Image>` 接受子节点,一层搞定。注意 Image / TabBar 的默认 anchor 是 top-left,需要显式写 `anchor=` 把它们撑开:

```xml
<Image sprite="ui:tabbar_bg" anchor="top-stretch" height="40">
  <TabBar id="topbar" anchor="stretch">...</TabBar>
</Image>
```

`bind="frame_id"` 让 Tab 选中时显示、未选时隐藏命名 Frame。lookup 是 lazy 的 —— 首次切换才解析并缓存。Tab `isOn="true"` 在 XML 里指定初始选中；都没写时 TabBar 自动选第一个。`bind=` 省略时只 fire `OnSelected`（C# 端自己处理）。

用自定义 `itemTemplate` 时（`<TabBar itemTemplate="MyTabTemplate"/>`），Template body 必须在树里某处包含恰好一个 `<Tab>`（通过 `ScopedIds` 或递归 `Control.Children` walk 在 `BindItems` 时定位）。

Tab 是 TabBar 的 layout group child —— 不能写 `anchor=` / `margin=`（`HorizontalLayoutGroup` 接管排布；TabBar 在 `selfIsLayoutGroup` 名单里）。

### Custom Tab layout

#### Via Template (for shared styling across instances / dynamic BindItems)

When several tabs share one rich face, or tabs are generated at runtime from data, make the `<Tab>` the
Template root and put its content **inside** the Tab (children overlay the bg, Frame-style). `sprite` /
`selectedSprite` live on the `<Tab>` so every instance shares them without restating:

```xml
<Template name="FileTab">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="bind"/>
  <Param name="isOn" default="false"/>
  <Tab id="tab" width="80" height="96"
       sprite="ui:cell_normal" selectedSprite="ui:cell_selected"
       isOn="{{isOn}}" bind="{{bind}}">
    <Icon id="icon" name="ui:icon_{{icon}}"
          anchor="top-center" width="48" height="48"
          margin="8,0,0,0"/>
    <Text id="name" anchor="top-stretch" margin="60,4,0,4"
          fontSize="12" align="center" raycastTarget="false">{{text}}</Text>
  </Tab>
</Template>

<TabBar id="files">
  <FileTab text="111.png" icon="png" isOn="true" bind="panel1"/>
  <FileTab text="222.jpg" icon="jpg" bind="panel2"/>
</TabBar>
```

TabBar collects the Tab whether it is the Template root (as here) or nested inside a wrapper; auto-select and `OnSelectionChanged` work the same either way. Lint rules `PUI-TABBAR-CHILD` and `PUI-TAB-PARENT` are suppressed for Template-instance roots. The Tab's `width`/`height` is its layout-group cell size; its children use their own `anchor` / `margin` (Tab is not a layout group). Keep decorative children `raycastTarget=false` (`<Icon>` already is; add it on `<Text>`) so clicks fall through to the containing Tab.

For dynamic data, use `BindItems` with `itemTemplate="FileTab"` (the same Template works for both patterns).

### Lint 规则

| Code                   | 触发条件                                                                              | 级别    |
| ---------------------- | ------------------------------------------------------------------------------------- | ------- |
| `PUI-TAB-PARENT`       | `<Tab>` 不在 `<TabBar>` 直接父节点下（Template-instance root 内的 Tab 已豁免）        | warning |
| `PUI-TABBAR-CHILD`     | `<TabBar>` 的直接子节点不是 `<Tab>` 且子树里也没有 `<Tab>`（Template wrapper 已豁免） | warning |
| `PUI-TABBAR-DIRECTION` | `direction` 不是 `horizontal` / `vertical`                                            | error   |

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
              <Show on="state-pressed">...</Show>  visible-while-state wrapper; siblings mutex; unclaimed states → state-normal fallback
TEXT SHORT    <Text>Hi</Text> ≡ <Text text="Hi"/>     (also <Btn>, <Toggle>, <InputField>)

BTN STATE     hoverColor/pressedColor/disabledColor  colour multipliers (base*state), fan out to bg + descendants, fade ~0.1s
TAB/TOGGLE    + selectedColor  (applies while the control is the active/isOn one at rest; <Btn> has no selected state)
STATE         stateReact="false"  opt node+subtree out of fan-out
              on="state-normal|hover|pressed|selected|disabled[@id]"  on <Trigger>/<Animation>/<Show>; resolves UPWARD to nearest <Btn>/<Tab>/<Toggle>; fires on enter
              state-selected is meaningful only on <Tab>/<Toggle> source; <Btn> never emits it

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

`<Trigger>` is the base — it subscribes to an event (open / loop / click / manual) and exposes an `OnFire` stream to C#. `<Animation>` extends Trigger by also playing a LitMotion animation on fire.

### `<Trigger>` — declarative event hook

```xml
<Trigger id="bonus" on="click@bonus-btn">
  <Frame><Btn id="bonus-btn">领取</Btn></Frame>
</Trigger>
```

`on=` values:

| Value              | Fires when                                                                                                                                                                                                        |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `open`             | Once when Screen opens (default if `on=` is omitted)                                                                                                                                                              |
| `loop`             | (Animation only) Fires once on open and enables looping (default yoyo)                                                                                                                                            |
| `click`            | The unique `<Btn>` inside this Trigger's subtree is clicked (uses Unity `Button.onClick`)                                                                                                                         |
| `click@<id>`       | The `<Btn>` matching `<id>` inside the subtree is clicked                                                                                                                                                         |
| `hover-enter`      | Pointer enters the unique `<Btn>` or `<Image>` in this Trigger's subtree (uGUI `IPointerEnterHandler`)                                                                                                            |
| `hover-enter@<id>` | Pointer enters the `<Btn>` or `<Image>` with `<id>` inside the subtree                                                                                                                                            |
| `hover-exit`       | Pointer leaves the unique `<Btn>` or `<Image>` (`IPointerExitHandler`)                                                                                                                                            |
| `hover-exit@<id>`  | Pointer leaves the `<Btn>` or `<Image>` with `<id>`                                                                                                                                                               |
| `press`            | Pointer pressed down on the unique `<Btn>` or `<Image>` (`IPointerDownHandler`). Instantaneous — release / long-press are v2                                                                                      |
| `press@<id>`       | Pointer pressed down on the `<Btn>` or `<Image>` with `<id>`                                                                                                                                                      |
| `state-normal`     | The nearest **ancestor** `<Btn>` / `<Tab>` / `<Toggle>` enters its Normal state (also fires once at open, since the control starts Normal)                                                                        |
| `state-hover`      | The nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` enters Hover                                                                                                                                                  |
| `state-pressed`    | The nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` enters Pressed                                                                                                                                                |
| `state-selected`   | The nearest ancestor `<Tab>` / `<Toggle>` is the active/`isOn` one at rest; fires on selection and once at open if already on. **Meaningful only with a `<Tab>` / `<Toggle>` source — a `<Btn>` never emits it.** |
| `state-disabled`   | The nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` enters Disabled                                                                                                                                               |
| `state-...@<id>`   | Same, but the source is the `<Btn>` / `<Tab>` / `<Toggle>` with `<id>` (any of the five `state-*` values)                                                                                                         |
| `manual`           | Does not auto-fire; C# must call `Fire()`                                                                                                                                                                         |

**`state-*` source resolution is UPWARD**: unlike `click` / `hover-enter` / `press` (which search this Trigger's **subtree downward** for a `<Btn>` / `<Image>` source), `state-*` resolves to the nearest `<Btn>` / `<Tab>` / `<Toggle>` **ancestor** (`state-...@<id>` targets a specific source control by id). A bare `state-*` with no `<Btn>` / `<Tab>` / `<Toggle>` ancestor is a runtime error (and `PUI-STATE-NO-SOURCE` in the lint CLI; `@id` forms and Template bodies are exempt). They **fire on entering** the state, so `state-normal` fires once at open and `<Animation on="state-pressed">` plays on press with `<Animation on="state-normal">` as its natural revert.

**`hover-enter` vs `state-hover`**: `hover-enter` / `press` are **raw pointer events** (`PointerEventRelay`, `IPointer*Handler`, downward source) — they fire on any pointer enter / down regardless of interactable state. `state-hover` / `state-pressed` come from the control's **Selectable state machine** (disabled-aware, drag-cancel-aware, upward source); a disabled `<Btn>` / `<Tab>` / `<Toggle>` never emits `state-hover` / `state-pressed`, only `state-disabled`.

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

<!-- Per-character color wave (hex or theme token) -->
<Animation char-color="#ffffff:#ff4400" char-stagger="0.05s" duration="0.4s">
  <Text>VICTORY</Text>
</Animation>
<!-- Or with theme tokens -->
<Animation char-color="primary:secondary" char-stagger="0.05s" duration="0.4s">
  <Text>VICTORY</Text>
</Animation>
```

| Attribute                                       | Notes                                                                                                                                                                                          |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `count="from:to"` + `format="{0:N0}"`           | Animates a number; writes formatted string into `<Text>` (LitMotion `BindToText`)                                                                                                              |
| `char-color="from:to"` + `char-stagger="0.05s"` | Per-char color wave (`BindToTMPCharColor`); `from:to` is hex literal / CSS named / theme token (e.g. `#ffffff:#ff0000` or `primary:secondary`); each char's motion is delayed by `i * stagger` |
| `target="@id"`                                  | Resolves a `<Text id="id">` in screen-global scope when the target is outside the wrapper subtree                                                                                              |

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

## Btn state visuals

`<Btn>`, `<Tab>`, and `<Toggle>` all broadcast their uGUI interaction state. `<Btn>` emits `Normal` / `Hover` / `Pressed` / `Disabled` (Selectable's `Selected` is folded into `Normal`). `<Tab>` and `<Toggle>` also emit `Selected` (= the active/`isOn` control at rest; transient Hover/Pressed/Disabled override it and it reverts on release). Three ways to react, in increasing power:

### 1. State colour multipliers — `hoverColor` / `pressedColor` / `selectedColor` / `disabledColor`

These are **colour multipliers** with uGUI ColorTint semantics: the graphic shows `baseColor * stateColor`, and Normal is the identity (white). They accept the same value forms as `color` (hex / CSS named / theme token). When **any** is set, the control fans the tint out to its bg **and every descendant Graphic** (label, icons, nested images) — switching the bg off uGUI's built-in ColorTint so these reactors become the single source of truth — and the tint **fades** over ~0.1s. A control with **none** of them set is unchanged (plain ColorTint on its bg only). `selectedColor` applies while the control is the active/`isOn` one at rest — it is meaningful only on `<Tab>` / `<Toggle>`; a `<Btn>` has no selected state and ignores it.

```xml
<Btn pressedColor="#cccccc" disabledColor="#888888">
  <Text anchor="center">Buy</Text>
</Btn>
```

- Distinct from `tint` (which picks the multiply-vs-linear-light **material**) and `color` (the base bg colour). All three compose.
- `interactable="false"` on the Btn now also sets `Button.interactable=false`, so it enters the Disabled state — `disabledColor` applies and `state-disabled` fires (on top of the existing CanvasGroup raycast block; the two compose).
- Variant-overridable like any `[UIAttr]` colour (the reactor re-resolves on ReSolve; the captured base colour is never re-captured).

**Opting out — `stateReact="false"`**: a **common attribute** (any element, default `true`) that opts a node **and its whole subtree** out of an ancestor Btn's tint fan-out. The installer prunes that subtree, so those graphics keep their authored colour through hover / press / disable. (A nested `<Btn>` is auto-pruned — it owns its own graphics.)

```xml
<Btn pressedColor="#aaaaaa">
  <Image sprite="ui:badge" stateReact="false"/>  <!-- stays full-colour on press -->
  <Text anchor="center">Claim</Text>             <!-- tints with the Btn -->
</Btn>
```

### 2. Artwork swap — `<Show on="state-...">`

`<Show>` is a no-visual wrapper whose subtree is visible **only while** the nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` is in that state (hidden otherwise, via `SetActive` — never destroyed). Sibling `<Show>` blocks under one source control are mutually exclusive; an unclaimed state falls back to the `state-normal` block. Only `state-*` `on=` values are valid (any other, e.g. `on="click"`, is an error). Wrap two `<Image>` siblings to swap artwork per state:

```xml
<Btn id="play">
  <Show on="state-normal"><Image anchor="stretch" sprite="ui:play-normal"/></Show>
  <Show on="state-pressed"><Image anchor="stretch" sprite="ui:play-pressed"/></Show>
  <Text anchor="center">Play</Text>
</Btn>
```

Here PC `state-hover` has no explicit block, so the `state-normal` artwork covers it too; add a `<Show on="state-hover">` to give hover its own art.

**Single-bg shorthand — `pressedSprite`.** When the only per-state change is the button's own bg image, `<Btn pressedSprite="ui:play-pressed">` is the one-attribute form of a `state-normal`/`state-pressed` `<Show>` pair: it swaps the bg's `overrideSprite` while Pressed and reverts on release (the authored `sprite` is never touched). Setting it auto-switches the Btn off uGUI's built-in ColorTint (so the pressed art isn't additionally darkened), and it composes with `pressedColor` (swap + tint stack). `""` / `none` = no swap. For swapping whole child subtrees (icon + label together, or more than two states), use `<Show>` instead.

```xml
<Btn sprite="ui:play-normal" pressedSprite="ui:play-pressed">Play</Btn>
```

### 3. State-triggered animation — `<Trigger>` / `<Animation on="state-...">`

`state-normal` / `state-hover` / `state-pressed` / `state-selected` / `state-disabled` (each also `@<id>`) are `on=` values on `<Trigger>` / `<Animation>` / `<Show>` — see the Triggers `on=` table. Source resolution is **upward** to the nearest `<Btn>` / `<Tab>` / `<Toggle>` ancestor (opposite of `click` / `press`), and they fire **on entering** the state. `state-selected` is meaningful only with a `<Tab>` / `<Toggle>` source. Pair a press animation with its revert:

```xml
<Btn>
  <Animation scale="1:0.95" duration="0.08s" on="state-pressed"><Frame anchor="stretch"/></Animation>
  <Animation scale="0.95:1" duration="0.08s" on="state-normal"><Frame anchor="stretch"/></Animation>
  <Text anchor="center">Tap</Text>
</Btn>
```

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
