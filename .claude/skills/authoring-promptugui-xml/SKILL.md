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

## Built-in primitives (8)

Pre-registered on `UI.Registry`. Use as XML tags by name:

| Tag        | Notes                                                                                                                                                                | Tag-specific attributes                                                                                                                                                                                                                                                                                             |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<Frame>`  | Empty container (RectTransform only).                                                                                                                                | —                                                                                                                                                                                                                                                                                                                   |
| `<Image>`  | uGUI Image; loads sprites from `Resources`.                                                                                                                          | `sprite` (resource path), `color` (`#RRGGBB[AA]`), `type` (`simple` / `sliced` / `tiled` / `filled`)                                                                                                                                                                                                                |
| `<Text>`   | TMP_Text. Has text-content shorthand: `<Text>Hello</Text>` ≡ `<Text text="Hello"/>`.                                                                                 | `text`, `fontSize` (int), `color`, `align` (`left` / `center` / `right`), `wrap` (bool), `raycastTarget` (bool), `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table) |
| `<VStack>` | Vertical layout group.                                                                                                                                               | `spacing` (float), `padding` (`T,R,B,L` 1/2/4 components)                                                                                                                                                                                                                                                           |
| `<HStack>` | Horizontal layout group.                                                                                                                                             | Same as VStack.                                                                                                                                                                                                                                                                                                     |
| `<Grid>`   | Grid layout group, fixed columns.                                                                                                                                    | `columns` (int), `cellSize` (`WxH`), `spacing` (single or `H,V`), `padding`                                                                                                                                                                                                                                         |
| `<Btn>`    | Image + Button + R3 `OnClick`. `<Btn>开始</Btn>` shorthand creates an internal TMP label child. Use as **template root** or registered prefab tag for any clickable. | `color`, `sprite`, `font` (string, font type from Settings; default `default`), `tr` (bool, default `true`; set `false` to skip i18n extraction), `ctx` (string, msgctxt to disambiguate same-msgid in the .po table)                                                                                               |
| `<Icon>`   | Sprite from a project-level IconSet; by-name lookup, package-time pruning.                                                                                           | `name` (required, `ns:icon-name`), `color` (`#RRGGBB[AA]`), `size` (numeric / `WxH` / `stretch` / `native`)                                                                                                                                                                                                         |

**No built-in `<Button>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>`.** Build those as `<Template>` (composing `<Btn>` + `<Image>` + `<Text>`) or register your own C# `Control` + Prefab.

### `<Icon>`

References a sprite from a project-level IconSet (shared icons, by-name lookup, package-time pruning).

```xml
<Icon name="ui:settings" color="#ffffff"/>
<Icon name="art:gold-coin" size="48"/>
<Icon name="ui:bell" color.dark="#fff"/>
```

| Attribute | Required | Default   | Notes                                                                                                                                                                |
| --------- | -------- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `name`    | yes      | —         | Format `ns:icon-name`. `ns` (set name) is strict `[A-Za-z0-9_-]+`; `icon-name` additionally allows spaces and `/` for subfolder paths, e.g. `ui:Combat/heart` (see below) |
| `color`   | no       | `#ffffff` | Multiply tint on the underlying Image. White preserves a colored PNG; non-white tints a mono-mask PNG                                                                |
| `size`    | no       | `native`  | Numeric / `WxH` / `stretch` / `native` (Icon-only). Native reads sprite pixel dimensions                                                                             |

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

**Dynamic icon names**: writing `<Icon name="ui:{{x}}"/>` (Template substitution or expression-driven name) cannot be statically analyzed — the Editor sync tool will skip it with a warning. Two ways out:

- Preferred: enumerate states explicitly via Variant overrides — `<Icon name="ui:sun" name.dark="ui:moon"/>`, the scanner sees both candidates.
- Fallback: list candidates in `IconSet.alwaysInclude` (always packed into the atlas).

## Common attributes (any tag)

| Attribute                  | Format       | Notes                                                                                  |
| -------------------------- | ------------ | -------------------------------------------------------------------------------------- |
| `id="..."`                 | string       | Unique within Screen / Template instance scope. Lift to dedicated handle for `Get<T>`. |
| `anchor="..."`             | preset       | See "Anchor system" below. Default `top-left`.                                         |
| `size="WxH"`               | `240x80`     | Both dimensions in pixels. **Forbidden on stretched axes.**                            |
| `width="W"` / `height="H"` | float        | Use when only one axis is point-anchored. **Forbidden on stretched axes.**             |
| `margin="..."`             | 1/2/4 floats | "Distance from anchor inward, positive". `"_"` = 0 placeholder.                        |
| `pivot="x,y"`              | `0..1, 0..1` | Defaults derive from `anchor`; rarely needed.                                          |
| `hidden="true"`            | bool         | Initial `SetActive(false)`.                                                            |
| `interactable="false"`     | bool         | Initial `CanvasGroup.interactable=false` + `blocksRaycasts=false`.                     |

`padding` and `spacing` are **NOT** universal — only on `<VStack>` / `<HStack>` / `<Grid>`.

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
        size.mobile="_,400"
        margin.mobile="_,16,80,16">
  <Btn size="240x64" size.mobile="stretch,72">开始</Btn>
</VStack>
```

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

- `src` is an opaque key passed to the user's `UI.SourceResolver` — could be a Resources path, an Addressables address, anything. The library never touches the filesystem itself.
- Imports merge **recursively**; cycles are detected.
- Imported files cannot contain `<Screen>` — only `<Template>`.
- Same-named templates from two imports without `as=` → conflict error. Resolve with `as="ns"` on one of them.

There's also a **commons pool**: `UI.LoadCommonLibrary("ui/common", as: null)` populates a global template pool merged into every screen automatically (no `<Import>` needed at the call site). Use this for project-wide shared widgets.

## C# code-side bridge

The XML doesn't bind data or events — it just creates handles. C# does the rest.

### Setup

```csharp
using PromptUGUI.Application;
using R3;

UI.SourceResolver = key => Resources.Load<TextAsset>($"UI/{key}").text;
UI.Registry.Register<MyCustomControl>("MyTag", myPrefab);  // optional; built-ins are pre-registered

UI.LoadCommonLibrary("common/Buttons");                    // optional
UI.LoadDocumentFromSrc("screens/MainMenu");                // resolver path
// or:
UI.LoadDocument("MainMenu", xmlString);                    // raw XML, no hot-reload
```

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

The helper builds a `(set:icon) → Sprite` lookup from each IconSet's SpriteAtlas. To use a different backend (Addressables, custom), set `UI.IconResolver` directly.

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

All events are R3 `Observable<T>` — never `event` or `Action`:

```csharp
screen.Get<Btn>("playBtn").OnClick
      .Subscribe(_ => Game.Start())
      .AddTo(screen);          // disposed when Screen closes
```

`screen.Track(disposable)` (or the `.AddTo(screen)` extension) ties a subscription to Screen lifetime. Always do this — leaked R3 subscriptions hold the GameObject alive after Close.

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

| Symptom                                                      | Cause                                                               | Fix                                                                                     |
| ------------------------------------------------------------ | ------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `cannot specify width/size on a horizontally-stretched axis` | `<X anchor="top-stretch" width="200"/>`                             | Either change anchor, or drop `width`. The stretched axis takes its size from `margin`. |
| Element not found at runtime                                 | `id` only declared inside a `<Template>`, but accessed by flat name | Use path: `screen.Get("templateId/innerId")`                                            |
| Ghost element on variant toggle                              | `<Add>` instantiated and never deactivated                          | This is by design (Strategy C). Use `hidden.variant` if you need a node to disappear.   |
| Subscription survives Close → null refs                      | Forgot `.AddTo(screen)`                                             | Always tie R3 subscriptions to Screen lifetime                                          |
| Parser silently merges children                              | Wrote `<Btn>开始 <Image/> </Btn>` (text + element mix)              | Pick one: text shorthand OR child elements. Mixed content is rejected.                  |
| Variant changes one attribute but not another                | `attr.variant` declared before `attr` (base) in the SAME element    | Fine — declaration order is per-attribute. Just verify the right `.variant` exists.     |
| Custom control's `[UIAttr]` ignored                          | Type other than string/int/float/bool                               | Take a string param and parse internally (see `Btn.Color` for a hex example).           |

## Quick reference (cheatsheet)

```
VALIDATE      every .ui.xml write  →  xmllint --noout --schema Assets/PromptUGUI.gen.xsd <file>
              schema missing       →  ask user to run Tools → PromptUGUI → Schema → Generate XSD
MCP FEEDBACK  every .ui.xml/.cs write → refresh_unity + read_console (error,warning)
              MCP missing          →  ask user to open Unity + connect MCP for Unity

ROOT          <PromptUGUI version="1"> ... </PromptUGUI>
TOP LEVEL     <Import src="" [as=""]/>  <Screen name="" [canvas="overlay|camera|world"]>  <Template name="">

BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
TEXT SHORT    <Text>Hi</Text> ≡ <Text text="Hi"/>     (only <Text>)

COMMON ATTRS  id  anchor  size|width|height  margin  pivot  hidden  interactable
STACK-ONLY    padding  spacing                                    (VStack/HStack/Grid)

ANCHOR        "<v>-<h>"     v ∈ {top, center, bottom, stretch}
                            h ∈ {left, center, right, stretch}
ALIASES       center  =  center-center
              stretch | fill  =  stretch-stretch

SIZE          size="WxH"  /  width="W"  /  height="H"
              FORBIDDEN on stretched axis (parse error)

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

C# OPEN       UI.LoadDocumentFromSrc("path"); UI.Open("ScreenName")
C# GET        screen.Get<Btn>("id")  /  "outerId/innerId"
C# EVENT      .OnClick.Subscribe(...).AddTo(screen)
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
```

## Worked end-to-end example

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">

  <Template name="MenuButton">
    <Param name="label"/>
    <Param name="color" default="#3B82F6"/>
    <Btn color="{{color}}" size="240x64" size.mobile="stretch,72">
      <Text anchor="center" fontSize="24" color="#FFFFFF">{{label}}</Text>
    </Btn>
  </Template>

  <Screen name="MainMenu">
    <Image anchor="stretch" sprite="bg/main"/>

    <VStack id="menu" anchor="center" size="280x240" spacing="12"
            anchor.mobile="bottom-stretch"
            size.mobile="_,320"
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
UI.UseResourcesResolver("UI"); // same as UI.SourceResolver = key => Resources.Load<TextAsset>($"UI/{key}").text;
IconResolverHelpers.UseSpriteAtlasIconResolver(iconSets);   // pass icon set setting
UI.LoadDocumentFromSrc("screens/main"); // FromSrc will enable hot-reload.

#if UNITY_IOS || UNITY_ANDROID
UI.Variants.Set("mobile", true);
#endif

var screen = UI.Open("MainMenu");

screen.Get<Btn>("play").OnClick               // call-site id is transferred to template body root (a <Btn>)
      .Subscribe(_ => Game.Start()).AddTo(screen);

screen.Get<Btn>("quit").OnClick
      .Subscribe(_ => Application.Quit()).AddTo(screen);
```

Note: `id="play"` on `<MenuButton id="play"/>` is automatically transferred to the template body's single root element (the `<Btn>`), so `screen.Get<Btn>("play")` resolves directly without a path. Use a path (`"play/inner"`) only when reaching into an element that has its own id **inside** the template body.
