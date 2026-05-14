---
name: scripting-promptugui-csharp
description: Use when writing C# that drives PromptUGUI — `UI.LoadDocumentAsync` / `UI.Open`, `Screen.Get<T>`, R3 event subscriptions (`OnClick` / `OnValueChanged` / `OnSelected`), `BindItems` / `BindOptions`, runtime `UI.Variants.Set` / `UI.Locale.Set` switching, `UI.CanvasConfigurator`, or custom `[UIAttr]` / `[Bind]` controls. For the XML markup itself, see authoring-promptugui-xml; for Addressables-backed loaders (`.ui.xml` / `.po` / icon atlases), see using-promptugui-addressables.
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

## Icon resolver (Resources-backed)

Only needed if your XML uses `<Icon>`:

```csharp
// Default helper: enumerate Resources/IconSets/ folder
IconResolverHelpers.UseSpriteAtlasIconResolver();
// Or pass an explicit list of IconSet ScriptableObjects:
IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { uiIconSet, artIconSet });
```

The helper builds a `(set:icon) → Sprite` lookup from each IconSet's SpriteAtlas.

For Addressables-backed icon atlases, see **using-promptugui-addressables**.

To use a fully custom backend, set `UI.IconResolver` directly with your own `(key → Sprite)` lookup.

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
| `<Icon>` shows pink/error sprite          | `UI.IconResolver` not set (or `IconSet` not in Resources/IconSets)                  | Call `IconResolverHelpers.UseSpriteAtlasIconResolver(...)` before any Screen opens                |

## Quick reference (cheatsheet)

```
SETUP          UI.UseResourcesResolver("UI")
               UI.Registry.Register<T>("Tag", optionalPrefab)
               IconResolverHelpers.UseSpriteAtlasIconResolver([iconSets])
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
LOCALE         UI.Locale.Set("en")                            sync
               UI.Locale.SetToSystemDefault()
               UI.Tr("...")                                   extract + translate

CANVAS         UI.CanvasConfigurator = (canvas, name) => { ... }
               runs AFTER XML canvas= / reference= apply

CUSTOM         class X : Control { override OnAttached() { ... } }
               [UIAttr] / [UIAttr("name")]    string/int/float/bool only
               [Bind] field                   auto-wire child by name
               UI.Registry.Register<X>("Tag", prefab)
```

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
    IconResolverHelpers.UseSpriteAtlasIconResolver(iconSets);       // pass icon set settings
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
