---
name: scripting-promptugui-csharp
description: Use when writing C# that drives PromptUGUI — `UI.LoadDocumentAsync` / `UI.Open`, `Screen.Get<T>`, R3 event subscriptions (`OnClick` / `OnValueChanged` / `OnSelected`), `BindItems` / `BindOptions`, runtime `UI.Variants.Set` / `UI.Locale.Set` / `UI.Orientation` switching, `UI.CanvasConfigurator`, or custom `[UIAttr]` / `[Bind]` controls. For the XML markup itself, see authoring-promptugui-xml; for Addressables-backed loaders (`.ui.xml` / `.po` / icon atlases), see using-promptugui-addressables.
---

# Scripting PromptUGUI in C#

PromptUGUI `.ui.xml` files describe **pure structure** — no logic, no data binding expressions. All wiring lives in C#:

1. Resolver setup (`UI.UseResourcesResolver(...)`) → tell the library where XML strings come from.
2. Document load (`await UI.LoadDocumentAsync(...)`) → parse + expand templates + register definitions.
3. Screen open (`UI.Open("Name")`) → instantiate GameObjects, return `IScreen`.
4. Handle lookup (`screen.Get<Btn>("id")`) → reach into named controls.
5. R3 wire-up (`.OnClick.Subscribe(...).AddTo(screen)`) → events & data flow.

This skill covers steps 1–5 plus custom controls. See **authoring-promptugui-xml** for the XML side; see **using-promptugui-addressables** if your project ships XML / translations / icon atlases via Addressables.

## Validation & feedback loop (run after every C# write)

Every `.cs` write that touches PromptUGUI MUST be verified via Unity MCP before reporting the work done:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error","warning"])
# Notice: this is a CoplayDev/unity-mcp, user may use official unity mcp.
```

Catches C# compile failures and runtime hot-reload errors.

**If MCP for Unity is unavailable** (call fails / no Unity instance):

- Check the user's MCP configuration files. If no Unity MCP installation is detected, issue a warning that MCP for Unity needs to be installed; treat strictly as a warning—do not halt operations.
- If an installation is detected, the user has not launched Unity or the MCP server. **STOP** and instruct the user to open the Unity Editor and ensure that the MCP server is running.

**DO NOT USE** `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")` unless the user explicitly allows it during an alignment step — pops a modal confirmation dialog in Unity that blocks every subsequent MCP call until manually dismissed.

## Setup

```csharp
using PromptUGUI.Application;
using R3;

UI.UseResourcesResolver("UI");                             // sets SourceResolver rootPath + Editor hot-reload mapping
UI.Registry.Register<MyCustomControl>("MyTag", myPrefab);  // optional; built-ins are pre-registered

async void Start() {
    await UI.LoadCommonLibraryAsync("common/Buttons");     // optional, populates the commons pool
    await UI.LoadDocumentAsync("screens/MainMenu");        // load "{rootPath}/screens/MainMenu.ui.xml"; enables hot-reload
    // or, sync raw-XML form (no resolver, no hot-reload):
    // UI.LoadDocument("MainMenu", xmlString);
    var screen = UI.Open("MainMenu");
}
```

**Commons pool**: `await UI.LoadCommonLibraryAsync("ui/common", @as: null)` populates a global template pool merged into every Screen automatically (no `<Import>` needed at call sites). Use for project-wide shared widgets.

**Hot-reload** is enabled automatically when you load via `LoadDocumentAsync` (resolver-backed). The sync `UI.LoadDocument(label, xml)` overload bypasses the resolver — handy for raw-XML tests but **cannot be hot-reloaded**.

Prefer to use (if `com.unity.addressables` package is installed) Addressables-backed `.ui.xml` loading (`UI.UseAddressableResolver()` + `AssetReferenceT<TextAsset>`), see the **using-promptugui-addressables** skill.

## Canvas configuration

Each `Screen.Open()` creates its own root Canvas (+ `CanvasScaler` + `GraphicRaycaster`). The render mode comes from the XML `canvas` attribute on `<Screen>` (`overlay` / `camera` / `world`, default `overlay`). For everything _else_ — pinning a `worldCamera`, setting `sortingOrder` / `planeDistance`, swapping render mode at runtime, etc. — register a configurator. The configurator runs **after** the XML-declared mode is applied, so it can override anything:

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

**CanvasScaler**: the `<Screen reference="WxH">` XML attribute is the recommended way to switch from `ConstantPixelSize` to `ScaleWithScreenSize`. If you need `match=0.5` or a custom `referencePixelsPerUnit`, modify `canvas.GetComponent<CanvasScaler>()` inside the configurator — but **don't fight the XML path on the same property** because Variant flips will re-apply the XML setting and overwrite your configurator change.

## Sprite resolver (Resources-backed)

Needed if your XML uses `<Icon>` or any `sprite="ns:name"` form:

```csharp
// Default helper: enumerate Resources/SpriteSets/ folder
SpriteResolverHelpers.UseSpriteSetResolver();
// Or pass an explicit list of SpriteSet ScriptableObjects:
SpriteResolverHelpers.UseSpriteSetResolver(new[] { uiSpriteSet, artSpriteSet });
```

The helper builds a `(set:name) → Sprite` lookup from each SpriteSet's SpriteAtlas.

For Addressables-backed atlases, see **using-promptugui-addressables**.

To use a fully custom backend, set `UI.SpriteResolver` directly with your own `(key → Sprite)` lookup.

## `sprite=` dual-syntax (built-in controls + subclasses)

Built-in controls (`<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>`) route their `sprite=` attribute through `UI.ResolveSprite(string)`:

- Values containing `:` (e.g. `sprite="ui:dialog"`) go through `UI.SpriteResolver` → SpriteSet/atlas path (`SpriteAtlasSyncer` includes them in package-time pruning).
- Bare paths (`sprite="ui/dialog"`) fall back to `Resources.Load<Sprite>(value)` — handy for one-off sprites and prototype work that doesn't justify a SpriteSet yet.
- Bare paths may add a `#sliceName` suffix to pick a named sub-sprite out of a multi-sprite (sliced) texture, e.g. `sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"`. The path before `#` goes through `Resources.LoadAll<Sprite>`, then the slice with matching `.name` is returned. A trailing `.png` / `.jpg` / `.jpeg` / `.tga` / `.psd` extension on the path is stripped, so `foo.png#bar` and `foo#bar` are equivalent.

`<Icon>` stays atlas-only — it requires `ns:name` and calls `UI.SpriteResolver` directly.

Custom Control subclasses that want a `sprite=` attribute should call `UI.ResolveSprite` to inherit the dual-syntax behaviour:

```csharp
public sealed class AtlasImage : PromptUGUI.Controls.Control
{
    private UnityEngine.UI.Image _img;
    public override void OnAttached()
        => _img = GameObject.GetComponent<UnityEngine.UI.Image>()
                  ?? GameObject.AddComponent<UnityEngine.UI.Image>();

    [UIAttr]
    public string Sprite
    {
        set => _img.sprite = UI.ResolveSprite(value);
    }
}
```

Error handling: when a `ns:name` value is used and `UI.SpriteResolver` is unset or returns null, `UI.ResolveSprite` logs `Debug.LogError` (pointing to `SpriteResolverHelpers.UseSpriteSetResolver` or the Sync menu) and returns null. Bare-path failures stay silent — same behavior as `Resources.Load` returning null — except for the `#sliceName` form: a missing texture is silent, but a present texture with no matching slice name logs `Debug.LogError` listing the available slice names (typos in an explicit slice should not fail silently).

## Open / Close / Get

```csharp
var screen = UI.Open("MainMenu");                          // returns IScreen

var btn = screen.Get<Btn>("playBtn");                      // throws KeyNotFoundException if missing
IControl any = screen.Get("playBtn");                      // untyped fallback

// Path syntax for nested template instances:
//   <TitledPanel id="bagPanel"> ...inside template <Btn id="close"/>... </TitledPanel>
var close = screen.Get<Btn>("bagPanel/close");

UI.Close("MainMenu");                                      // destroys GameObjects
```

Note: when a Template invocation carries `id="bagPanel"`, that id is **transferred to the template body's single root element** automatically — `screen.Get<TitledPanel>("bagPanel")` returns the root. Use the path form (`"bagPanel/close"`) only when reaching into an element that has its own id **inside** the template body.

## Events & subscriptions

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

screen.Get<InputField>("playerName").OnEndEdit
      .Subscribe(s => Player.Rename(s)).AddTo(screen);
```

`screen.Track(disposable)` (or the `.AddTo(screen)` extension) ties a subscription to Screen lifetime. **Always do this** — leaked R3 subscriptions hold the GameObject alive after Close, and the next Open will produce phantom callbacks against the old (destroyed) GameObject.

## Screen-level hooks

`screen.RectTransformDimensionsChanged` is the same as the Canvas's `screen.RootGameObject.RectTransformDimensionsChanged` — useful for re-layout reactions that span multiple controls.

## List / option push

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

- `BindOptions` takes `Observable<IEnumerable<string | DropdownOption>>`.
- `BindItems` takes `Observable<IReadOnlyList<T>>` and a per-slot binder.
- `itemTemplate=` in the XML resolves to either a `<Template name="...">` (slot root is the template body) or a registered Control class (slot is that Control). Use `slot.Get<T>("childId")` inside the binder to reach into Template bodies.
- After hot-reload, you must **re-Bind** — the underlying ScrollList is rebuilt.

## Variant switching at runtime

```csharp
UI.Variants.Set("mobile", true);    // all open Screens re-apply attribute values
UI.Variants.Set("mobile", false);
```

Variants do **not** rebuild GameObjects — `VariantStore.Changed` triggers `Screen.ReSolve` which re-applies attributes. `<Add>` blocks use a "instantiate once on first activation, only `SetActive`-toggle thereafter" strategy so references and R3 subscriptions survive variant flips.

## Orientation (auto-tracked variants)

The library boots a global `OrientationTracker` (RuntimeInitializeOnLoadMethod → `DontDestroyOnLoad`) that every frame reads `Screen.width` vs `Screen.height` and toggles two reserved, mutually-exclusive variants:

- `portrait` — active when `Screen.height > Screen.width`
- `landscape` — active otherwise (square dims count as landscape, matching `Screen.ApplyCanvasScaler`'s `W >= H → match=0` rule)

XML authors override per-orientation via `attr.portrait="..."` / `attr.landscape="..."` on any element. Typical use: `<Screen reference="1920x1080" reference.portrait="1080x1920">` so each orientation gets its own CanvasScaler reference (and therefore the auto-derived `match` is correct on both axes).

```csharp
UI.Orientation.IsPortrait;                // read current state
UI.Orientation.Set(true);                 // manual override (still subject to AutoTrack overwriting next frame)
UI.Orientation.AutoTrack = false;         // disable auto-tracking; user fully self-manages
```

Portrait-locked games can ignore the system entirely — base values apply when no `.portrait`/`.landscape` override exists, and `landscape` overrides never fire on a locked-portrait device. Don't reuse `portrait` / `landscape` as Variant names for non-orientation state.

## Locale & i18n (C# side)

Switch language at runtime:

```csharp
// Switch locale; swaps both the .po table and the font table
UI.Locale.Set("en");
UI.Locale.SetToSystemDefault();

// Strings extracted from code (these msgid land in the .po alongside XML strings)
var text = string.Format(c, UI.Tr("Total: {0:C}"), price);
```

Locale switching rides the Variant pipeline — already-open Screens auto-ReSolve. `UI.Locale.Set("zh-Hans")` internally registers `zh-Hans` as an active Variant; don't reuse that name for non-locale state.

**.po file location (Resources-backed)**: by default `.po` files live in `Assets/Resources/PromptUGUI/i18n/<locale>/` or `/PromptUGUI/i18n-custom/<locale>/`. Files anywhere under those paths are picked up by `Resources.LoadAll<TextAsset>`; subfolder names are ignored.

For Addressables-backed `.po` loading (`UI.Locale.UseAddressableResolver`, `Locale:<locale>` labels, `SetAsync`), see **using-promptugui-addressables**.

## Custom controls

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

- `[UIAttr]` (no name) maps to the camelCase of the property name (`Color` → `color`). `[UIAttr("foo")]` overrides.
- Supported types: `string` / `int` / `float` / `bool`. Use string + parse internally for everything else.
- `[Bind]` on a field auto-wires a child component from a Prefab by child name. Useful when the control has a non-trivial Prefab structure.
- `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` are reference implementations — for project-specific differentiation (pixel border, press feedback, custom popup chrome), subclass and override `OnAttached`; don't modify the base controls.

## Common mistakes (C#)

| Symptom                                   | Cause                                                                               | Fix                                                                                               |
| ----------------------------------------- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| Element not found at runtime              | `id` only declared inside a `<Template>`, accessed by flat name                     | Use path: `screen.Get("templateInstanceId/innerId")`                                              |
| Subscription survives Close → null refs   | Forgot `.AddTo(screen)`                                                             | Always tie R3 subscriptions to Screen lifetime                                                    |
| Custom control's `[UIAttr]` ignored       | Property type other than string/int/float/bool                                      | Take a string param and parse internally (see `Btn.Color` for a hex example)                      |
| ScrollList shows nothing after hot-reload | `BindItems` subscription disposed on close, but the ScrollList is rebuilt on reload | Re-call `BindItems` on reload — the convention is to re-wire from a single `OnOpened` entry point |
| `<Icon>` shows pink/error sprite          | `UI.SpriteResolver` not set (or `SpriteSet` not in Resources/SpriteSets)             | Call `SpriteResolverHelpers.UseSpriteSetResolver(...)` before any Screen opens                |

## Quick reference (cheatsheet)

```
SETUP          UI.UseResourcesResolver("UI")
               UI.Registry.Register<T>("Tag", optionalPrefab)
               SpriteResolverHelpers.UseSpriteSetResolver([spriteSets])
               await UI.LoadCommonLibraryAsync("common/Foo")
               await UI.LoadDocumentAsync("screens/Main")
               UI.LoadDocument("Label", xmlString)            sync, no hot-reload

OPEN/CLOSE     var screen = UI.Open("Name");                  returns IScreen
               UI.Close("Name");

GET            screen.Get<Btn>("id")                          typed
               screen.Get("id")                               untyped (IControl)
               screen.Get<Btn>("outerId/innerId")             path into Template body

EVENTS (R3)    .OnClick                Btn
               .OnValueChanged         Toggle:bool / Slider:float / InputField:string
               .OnSelected             Dropdown:int
               .OnEndEdit / .OnSubmit  InputField:string
               .Subscribe(...).AddTo(screen)   tie lifetime — ALWAYS

DATA PUSH      Dropdown.BindOptions(Observable<IEnumerable<string>>)
               ScrollList.BindItems(Observable<IReadOnlyList<T>>, (slot,t)=>...)
               .AddTo(screen)

VARIANT        UI.Variants.Set("name", true|false)            re-applies, no rebuild
ORIENTATION    UI.Orientation.IsPortrait                      auto-tracked: portrait / landscape variants
               UI.Orientation.Set(bool)                       manual override
               UI.Orientation.AutoTrack = false               disable global tracker
LOCALE         UI.Locale.Set("en")                            sync
               UI.Locale.SetToSystemDefault()
               UI.Tr("...")                                   extract + translate

CANVAS         UI.CanvasConfigurator = (canvas, name) => { ... }
               runs AFTER XML canvas= / reference= apply

CUSTOM         class X : Control { override OnAttached() { ... } }
               [UIAttr] / [UIAttr("name")]    string/int/float/bool only
               [Bind] field                   auto-wire child by name
               UI.Registry.Register<X>("Tag", prefab)

MODAL          var r = await MessageBox.Open(text, MsgBtn.OK|MsgBtn.Cancel, icon, title)
               UI.Modal.OpenAsync(new MyRequest())          custom ModalRequest<T>
               UI.Modal.CloseAll()                          cancel all pending
               UI.Modal.SortingOrderBase = 1000             default
```

## Modal dialogs

PromptUGUI ships a generic modal system in `PromptUGUI.Application.Modals` plus a builtin MessageBox.

### Quick usage

```csharp
using PromptUGUI.Application.Modals;

// Default messagebox
var r = await MessageBox.Open(UI.Tr("Save changes?"),
                              MsgBtn.Yes | MsgBtn.No | MsgBtn.Cancel);
if (r == MsgBtn.Yes) await game.SaveAsync();

// Custom button labels (still returns mapped MsgBtn flag)
var r2 = await MessageBox.Open(UI.Tr("File not found."),
    new[] { (UI.Tr("Retry"), MsgBtn.OK), (UI.Tr("Skip"), MsgBtn.Cancel) });

// Optional icon and title
await MessageBox.Open("Saved.", MsgBtn.OK, icon: "ui:check", title: "Done");
```

### Behavior

- **Modal stacking**: when one MessageBox is open, subsequent `Open(...)` calls queue FIFO. The next pops automatically when the active one closes.
- **ESC / Android Back**: maps to the most-negative button in the combo: `Cancel > No > Close`. ESC on an `OK`-only modal does nothing.
- **Raycast block**: the modal Screen overrides `Canvas.sortingOrder` to `UI.Modal.SortingOrderBase` (default 1000), so it sits above every regular Screen. The XML's backdrop Image fills the canvas and absorbs clicks.
- **Locale / Variant**: a modal is a regular `Screen` — locale switches translate its button labels; Variants re-apply attribute values normally.

### Cancelling

```csharp
UI.Modal.CloseAll();   // every pending await throws OperationCanceledException
```

`UI.UnloadAll()` and `UI.ResetForTests()` also cancel all pending modals.

### Custom modal types

Subclass `ModalRequest<TResult>` and pass it to `UI.Modal.OpenAsync(...)`. Your `Bind(screen, close)` wires events; `close(result)` resolves the awaiter.

```csharp
public sealed class NamePickerRequest : ModalRequest<string> {
    public override string XmlSrc => "MyUI/Modals/NamePicker";
    public override void Bind(IScreen screen, Action<string> close) {
        screen.Get<Btn>("ok").OnClick.Subscribe(_ =>
            close(screen.Get<InputField>("input").Text)).AddTo(screen);
        screen.Get<Btn>("cancel").OnClick.Subscribe(_ => close(null)).AddTo(screen);
    }
    public override bool TryEscape(out string r) { r = null; return true; }
}

var name = await UI.Modal.OpenAsync(new NamePickerRequest());
```

Custom modal `XmlSrc` keys go through the caller's `UI.SourceResolver` like any other Screen.

### Overriding the builtin MessageBox layout

Set `MessageBox.XmlSrc` once at boot to point at your own XML file. Note: Unity strips only the final `.xml` from multi-dot filenames, so for `MyMessageBox.ui.xml`, the lookup key is `MyMessageBox.ui` (with the `.ui` suffix). The builtin default is `"PromptUGUI/Modals/MessageBox.ui"`.

```csharp
MessageBox.XmlSrc = "MyUI/Modals/PixelMessageBox.ui";  // resolved by UI.SourceResolver
```

Keys starting with `PromptUGUI/` resolve to the package's bundled Resources; other keys go through `UI.SourceResolver`.

Your XML must declare a Screen with these ids: `text`, `title`, `ok`, `cancel`, `yes`, `no`, `close`. An optional `icon` id is supported but not required (Bind tolerates missing icon node). If you want icon support, your XML must include `<Icon id="icon" name="placeholder:something"/>` because PromptUGUI's parser requires the `name=` attribute on `<Icon>` elements.

## `<Trigger>` and `<Animation>` from C#

XML declares the trigger condition and effect; C# subscribes when game logic needs to react on top.

### `Trigger.OnFire` — R3 Observable

```csharp
screen.Get<Trigger>("bonus").OnFire
    .Subscribe(_ => Game.AwardBonus())
    .AddTo(screen);
```

Pattern: XML places the `<Trigger>` with `on="click@<id>"` next to the relevant UI element. C# attaches the game-side reaction. The wiring (which event triggers what) lives in XML; the action lives in C#. Decoupled — designers tweak XML, programmers tweak handlers.

### `Animation.Fire()` — manual trigger

```csharp
screen.Get<Animation>("welcome-anim").Fire();
```

Works for any `on=` mode. Useful for:
- `on="manual"` triggers (no auto-fire, fully C# driven)
- Re-firing `on="click"` triggers from code (e.g., on a non-Btn event)
- Replaying open animations (debug / preview)

### Lifecycle notes

- `Animation` registers as a Control via `BuiltinPrimitives.Register<Animation>("Animation", null)` — already wired into `UI.ResetForTests`
- `Screen.Close()` disposes all Controls (including Animations); `MotionHandle`s are `TryCancel`led at that point — no lingering callbacks after Close
- Variant ReSolve re-evaluates Animation's attributes; if `duration` / `easing` / `loop` / from-to values change, the running motion is cancelled and ready to re-fire on the next trigger. If attributes are unchanged, in-flight motion is preserved

## Worked end-to-end example (C#)

XML in the **authoring-promptugui-xml** worked example (a `MainMenu` Screen with three `<MenuButton>` Template instances + a `mobile` Variant that adds a logo). C# side:

```csharp
async void Start() {
    UI.UseResourcesResolver("UI");                                  // sets SourceResolver + Editor hot-reload mapping
    SpriteResolverHelpers.UseSpriteSetResolver(spriteSets);       // pass SpriteSet[] (asset references)
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

`id="play"` on `<MenuButton id="play"/>` is automatically transferred to the template body's single root element (the `<Btn>`), so `screen.Get<Btn>("play")` resolves directly without a path. Use a path (`"play/inner"`) only when reaching into an element that has its own id **inside** the template body.
