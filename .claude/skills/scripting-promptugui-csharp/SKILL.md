---
name: scripting-promptugui-csharp
description: Use when writing C# that drives PromptUGUI — `UI.LoadDocumentAsync` / `UI.Open`, `Screen.Get<T>`, R3 event subscriptions (`OnClick` / `OnValueChanged` / `OnSelected`), `BindItems` / `BindOptions`, runtime `UI.Variants.Set` / `UI.Locale.Set` / `UI.Orientation` switching, `UI.CanvasConfigurator`, modal dialogs (`MessageBox.Open` / `Loading.Open` / `UI.Modal.OpenAsync` / `ModalRequest<T>` / `MsgBtn` / `ModalMode`) and overriding `MessageBox.XmlSrc` / `Loading.XmlSrc`, or custom `[UIAttr]` / `[Bind]` controls. For the XML markup itself, see authoring-promptugui-xml; for Addressables-backed loaders (`.ui.xml` / `.po` / icon atlases), see using-promptugui-addressables.
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

**像素美术整数缩放**：`UI.DefaultScaleMode = ScaleMode.Pixel`（启动期一次性设置）让所有 `<Screen>` 默认走 `ConstantPixelSize` + 整数倍 `scaleFactor`。每个 Screen 必须配 `reference="WxH"` 作为设计分辨率。具体某个 Screen 想 opt-out 写 XML `scale-mode="auto"`。详见 [authoring-promptugui-xml](../authoring-promptugui-xml/SKILL.md) 的 Canvas 段。

**Pixel 模式下限 `UI.MinPixelScale`**：默认 `0f` = 不限制（小屏算到 `0.5 / 0.25 / 0.125 ...` 自由下落）。设为 `0.5f` / `1f` 等限制 factor 下限——小屏不再缩小内容，而是让内容溢出（你 `anchor="stretch"` 的元素会被物理屏幕吃边距）。建议值在算法台阶上 `{0.5, 1, 2, ...}`；off-ladder 值（如 `0.7f`）会被原样使用但破坏整数像素对齐。只对 Pixel 模式生效，Auto 模式忽略。

## Sprite resolver (Resources-backed)

Needed if your XML uses `<Icon>` or any `sprite="ns:name"` form:

```csharp
// Default helper: enumerate Resources/SpriteSets/ folder
SpriteResolverHelpers.UseSpriteSetResolver();
// Or pass an explicit list of SpriteSet ScriptableObjects:
SpriteResolverHelpers.UseSpriteSetResolver(new[] { uiSpriteSet, artSpriteSet });
```

The helper builds a `(set:name) → Sprite` lookup from each SpriteSet's SpriteAtlas.

**Source formats**: SpriteSet's source folder accepts any Unity-recognized texture
format (PNG, JPG, JPEG, TGA, PSD, TIFF, BMP, EXR, HDR, GIF) plus Aseprite
(`.ase` / `.aseprite`, requires `com.unity.2d.aseprite ≥ 1.0`). For Aseprite,
each file must produce exactly **one sprite** — set the AsepriteImporter Import
Mode to single-frame output or use one file per icon. Multi-sprite Aseprite
files are logged as errors and skipped during sync.

For Addressables-backed atlases, see **using-promptugui-addressables**.

To use a fully custom backend, set `UI.SpriteResolver` directly with your own `(key → Sprite)` lookup.

## `sprite=` dual-syntax (built-in controls + subclasses)

Built-in controls (`<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>`) route their `sprite=` attribute through `UI.ResolveSprite(string)`:

- Values containing `:` (e.g. `sprite="ui:dialog"`) go through `UI.SpriteResolver` → SpriteSet/atlas path (`SpriteAtlasSyncer` includes them in package-time pruning).
- Bare paths (`sprite="ui/dialog"`) fall back to `Resources.Load<Sprite>(value)` — handy for one-off sprites and prototype work that doesn't justify a SpriteSet yet.
- Bare paths may add a `#sliceName` suffix to pick a named sub-sprite out of a multi-sprite (sliced) texture, e.g. `sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"`. The path before `#` goes through `Resources.LoadAll<Sprite>`, then the slice with matching `.name` is returned. Any file extension on the path before the `#` is stripped, so `foo.png#bar`, `foo.aseprite#bar`, and `foo#bar` are all equivalent.

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

Error handling: when a `ns:name` value is used and `UI.SpriteResolver` is unset or returns null, `UI.ResolveSprite` logs `Debug.LogError` (pointing to `SpriteResolverHelpers.UseSpriteSetResolver` or the Sync menu) and returns null. **Exception:** while `UI.IsSpriteResolverLoadInFlight` is `true` (an async resolver loader like `UseAddressableSpriteSetResolver` is mid-download), both `UI.ResolveSprite` and `<Icon>` stay silent and return null — open Screens automatically re-resolve via a Variant broadcast once the loader completes. Bare-path failures stay silent — same behavior as `Resources.Load` returning null — except for the `#sliceName` form: a missing texture is silent, but a present texture with no matching slice name logs `Debug.LogError` listing the available slice names (typos in an explicit slice should not fail silently).

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

**Progress** — `screen.Get<Progress>("hp").Value = 0.42f;` Progress 是只读显示控件，无 `OnValueChanged`，用 `Bind`-属性或直接 setter 推值。`Value` 被 `Mathf.Clamp01` 钳位。

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

    [UIAttr, Preserve] public string Color { set { /* parse hex, apply */ } }
    [UIAttr("backgroundSprite"), Preserve] public string Sprite { set { /* ... */ } }
}

UI.Registry.Register<MyControl>("MyControl", optionalPrefab: null);
```

- `[UIAttr]` (no name) maps to the camelCase of the property name (`Color` → `color`). `[UIAttr("foo")]` overrides.
- Supported types: `string` / `int` / `float` / `bool`. Use string + parse internally for everything else.
- `[Bind]` on a field auto-wires a child component from a Prefab by child name. Useful when the control has a non-trivial Prefab structure.
- `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` are reference implementations — for project-specific differentiation (pixel border, press feedback, custom popup chrome), subclass and override `OnAttached`; don't modify the base controls.
- **IL2CPP Managed Stripping (Medium+)**: setter-only `[UIAttr]` properties get their `PropertyInfo` metadata stripped (`Type.GetProperties()` returns nothing for them), reflection misses the property, attribute silently reverts to default in Player builds with no error log. **Pair every `[UIAttr]` and `[Bind]` with `[Preserve]`**: `[UIAttr, Preserve] public string Color { set { ... } }`. `PromptUGUI.Registry.PreserveAttribute` is name-matched by Mono.Linker (any class named exactly `PreserveAttribute`, inheritance does **not** count). All built-in controls already do this; custom controls must too.

## Common mistakes (C#)

| Symptom                                   | Cause                                                                               | Fix                                                                                               |
| ----------------------------------------- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| Element not found at runtime              | `id` only declared inside a `<Template>`, accessed by flat name                     | Use path: `screen.Get("templateInstanceId/innerId")`                                              |
| Subscription survives Close → null refs   | Forgot `.AddTo(screen)`                                                             | Always tie R3 subscriptions to Screen lifetime                                                    |
| Custom control's `[UIAttr]` ignored       | Property type other than string/int/float/bool                                      | Take a string param and parse internally (see `Btn.Color` for a hex example)                      |
| Attrs silently default in IL2CPP build    | Forgot `[Preserve]` next to `[UIAttr]` — Medium+ stripping drops PropertyInfo metadata, reflection misses the property | Always write `[UIAttr, Preserve]` (both from `PromptUGUI.Registry`)                                |
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
               Progress                display-only; .Value = 0.42f (Clamp01); no event

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
               MessageBox.Open(text, [(label,key),...], icon, title, mode)  custom labels
               MessageBox.Open(text, ..., mode: ModalMode.Queued)  排队,不叠加
               ESC priority   Cancel > No > Close   (OK-only → no-op)
               override XML   MessageBox.XmlSrc = "MyUI/Modals/Foo.ui"   (keep .ui suffix)
                              prereq #1  resolver registered + (Addressables) address pre-registered
                              prereq #2  <Screen name="..."> byte-equal to MessageBox.XmlSrc
                              else: InvalidKeyException / "Modal screen 'X' not loaded; call LoadDocument first"
                              (do NOT call LoadDocument manually — auto via ModalDocCache.EnsureLoaded)
               required ids   text  title  ok  cancel  yes  no  close   (icon optional)
               backdrop       author writes <Image anchor="stretch"/> — NOT auto-injected
               UI.Modal.OpenAsync(new MyRequest(), ModalMode.Popup) custom ModalRequest<T>
                              override TryEscape(out T) to map ESC → result
               UI.Modal.CloseAll()                          cancel all (OperationCanceledException)
               UI.Modal.SortingOrderBase = 1000             default; configurator can't pin sortingOrder
LOADING        var h = Loading.Open(text); h.Close()        idempotent; h.IsClosed
               Loading.XmlSrc = "MyUI/Modals/Foo.ui"        override; only <Text id="text"> recognised
               Loading.SortingOrder = 500                   overlay 层带,低于 dialog
               concurrent Open() → independent overlays at the same band (no ref-count)
```

## Modal dialogs

PromptUGUI ships a generic modal stack in `PromptUGUI.Application.Modals` plus two
builtin overlays: a `MessageBox` dialog and a `Loading` spinner. **Every modal IS a real
`Screen` instantiated from `.ui.xml`** — anchor / margin / Variant / locale / `<Icon>`
all work normally. The modal subsystem only adds: stack management, ESC handling, and a
sortingOrder band above regular Screens.

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

// Optional icon and title; ModalMode.Queued waits behind any current dialog
await MessageBox.Open("Saved.", MsgBtn.OK,
    icon: "ui:check", title: "Done", mode: ModalMode.Queued);
```

### API surface (`PromptUGUI.Application.Modals`)

```csharp
public static class MessageBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox.ui";

    public static Awaitable<MsgBtn> Open(
        string text, MsgBtn buttons = MsgBtn.OK,
        string icon = null, string title = null,
        ModalMode mode = ModalMode.Popup);

    public static Awaitable<MsgBtn> Open(
        string text,
        IEnumerable<(string label, MsgBtn key)> buttons,   // also sets the .Buttons mask
        string icon = null, string title = null,
        ModalMode mode = ModalMode.Popup);
}

[Flags] public enum MsgBtn { None=0, OK=1, Cancel=2, Yes=4, No=8, Close=16 }
public enum ModalMode { Popup = 0, Queued = 1 }

public static class Loading {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";
    public static int SortingOrder { get; set; } = 500;   // keep < SortingOrderBase
    public static LoadingHandle Open(string text = null);
}

public sealed class LoadingHandle {
    public bool IsClosed { get; }
    public void Close();                                  // idempotent
}

// nested under UI:
public static class UI.Modal {
    public static int SortingOrderBase { get; set; } = 1000;
    public static int QueuedCount { get; }
    public static bool IsAnyOpen { get; }

    public static Awaitable<TResult> OpenAsync<TResult>(
        ModalRequest<TResult> request, ModalMode mode = ModalMode.Popup);

    public static void CloseAll();                        // cancels every pending await
}
```

### Behavior

- **Stacking (`ModalMode`)**: **`ModalMode.Popup`** (default) shows the dialog immediately,
  stacked on top of any current dialog — use it for nested dialogs (e.g. a confirm dialog
  opened from inside another modal). **`ModalMode.Queued`** waits until the whole dialog
  stack is empty, then shows as the new base; multiple `Queued` dialogs show FIFO.
  Closing the top dialog reveals the one below.
- **ESC / Android Back**: only the top dialog responds; maps to `Cancel > No > Close` in
  that priority (whichever flag is set in the requested `buttons` mask wins). ESC on an
  `OK`-only dialog does nothing. The listener (`ModalEscapeListener`) is auto-attached —
  **no XML markup is required**. It uses `UnityEngine.InputSystem` when
  `ENABLE_INPUT_SYSTEM` is defined (bindings: `<Keyboard>/escape` + `<Gamepad>/start`),
  else legacy `Input.GetKeyDown(KeyCode.Escape)`. `Loading` overlays do NOT have this
  listener — they're not dismissible by input.
- **Raycast / sortingOrder**: each dialog's Canvas overrides `sortingOrder` to
  `UI.Modal.SortingOrderBase + depth` (depth 0 = bottom of dialog stack). Loading
  overlays sit at `Loading.SortingOrder` (default 500). Keep `Loading.SortingOrder <
  UI.Modal.SortingOrderBase` so dialogs opened during a Loading appear above it.
- **Dim backdrop is part of the XML, not auto-injected.** If you want clicks blocked on
  empty space outside your dialog box, include a stretched Graphic in your override XML
  (the builtin uses `<Image id="backdrop" anchor="stretch" color="#000000FE"/>`).
  Without a full-screen Graphic, pointer raycasts outside the dialog pass through to
  the Canvas underneath. The `id="backdrop"` itself has no special meaning to Bind — any
  id (or none) works as long as the Graphic exists.
- **Locale / Variant**: a dialog is a regular `Screen` — `UI.Locale.Set(...)` and
  `UI.Variants.Set(...)` ReSolve open modals in place, no rebuild. Fonts swap on locale
  switch like in any other Screen (`<Text font="title">` etc.).

### Cancelling

```csharp
UI.Modal.CloseAll();   // every pending await throws OperationCanceledException
```

`UI.UnloadAll()` and `UI.ResetForTests()` also cancel all pending modals AND close all
active Loading overlays.

### Custom modal types

Subclass `ModalRequest<TResult>` and pass it to `UI.Modal.OpenAsync(...)`. `Bind(screen,
close)` wires events; calling `close(result)` resolves the awaiter. Optionally override
`TryEscape(out TResult)` to map ESC to a result (return `false` to suppress ESC
dismissal — the default).

```csharp
public sealed class NamePickerRequest : ModalRequest<string> {
    public override string XmlSrc => "MyUI/Modals/NamePicker.ui";
    public override void Bind(IScreen screen, Action<string> close) {
        screen.Get<Btn>("ok").OnClick.Subscribe(_ =>
            close(screen.Get<InputField>("input").Text)).AddTo(screen);
        screen.Get<Btn>("cancel").OnClick.Subscribe(_ => close(null)).AddTo(screen);
    }
    public override bool TryEscape(out string r) { r = null; return true; }  // ESC → null
}

var name = await UI.Modal.OpenAsync(new NamePickerRequest());
```

Custom modal `XmlSrc` keys go through the caller's `UI.SourceResolver` like any other
Screen (the `PromptUGUI/` prefix is reserved — those keys load synchronously from the
package's bundled Resources via `Resources.Load`, no resolver involved). **The same
"Setup prerequisites" rules from "Overriding the builtin MessageBox layout" below apply
verbatim**: a matching resolver must be registered (Addressables needs the address
pre-registered, not just the file on disk), and your XML's `<Screen name="...">` must
equal `XmlSrc` byte-for-byte — otherwise you get the same `InvalidKeyException` /
`Modal screen '...' not loaded` errors listed in the override section's mistakes table.

### Overriding the builtin MessageBox layout

Set `MessageBox.XmlSrc` once at boot to point at your own XML file. **Caveat**: Unity
strips only the final `.xml` from multi-dot filenames, so for `MyMessageBox.ui.xml` the
lookup key is `MyMessageBox.ui` (keep the `.ui` suffix). Default:
`"PromptUGUI/Modals/MessageBox.ui"`.

```csharp
MessageBox.XmlSrc = "MyUI/Modals/PixelMessageBox.ui";   // your SourceResolver resolves this
```

There is no per-call `template:` override; `MessageBox.XmlSrc` is the global swap point.

#### Setup prerequisites (read BEFORE swapping `XmlSrc`)

Two non-obvious requirements have to be satisfied or `MessageBox.Open` will throw at
runtime. Walking through both BEFORE you change `MessageBox.XmlSrc` saves a round of
"file exists on disk, why doesn't it load":

1. **A `SourceResolver` matching the key prefix must be registered.** `MessageBox.XmlSrc`
   values starting with `PromptUGUI/` load synchronously from the package's bundled
   Resources (no resolver involved — the `Resources/PromptUGUI/...` tree shipped with the
   package). **Every other key** flows through `UI.SourceResolver`:
   - Resources resolver: `UI.UseResourcesResolver(rootPath)` — `XmlSrc` is the path under
     `Resources/{rootPath}/...` (no `.ui.xml` extension).
   - Addressables resolver: `UI.UseAddressableResolver()` — `XmlSrc` is an Addressables
     **Address**, not a filesystem path. **The asset MUST be added to an Addressables
     group with its Address set to exactly your `XmlSrc` string.** "The .ui.xml file
     exists in `Assets/`" is not enough; resolvers do NOT do filesystem fallback.
     Missing registration → `InvalidKeyException: No Location found for Key=<XmlSrc>`
     from `AddressableResolverHelper`.
   - Custom resolver: whatever `(string src) → Awaitable<string>` you assigned to
     `UI.SourceResolver`.

2. **Your XML's `<Screen name="...">` must equal `MessageBox.XmlSrc` byte-for-byte.**
   `UI.LoadDocument` keys the internal `_docs` table by the XML's `<Screen name>`, NOT
   by the load key — so the resolver successfully fetching the XML is only step one.
   `OpenModalScreen(XmlSrc)` then looks up `_docs[XmlSrc]`; if `<Screen name>` was
   anything else, you get `InvalidOperationException: Modal screen '<XmlSrc>' not
   loaded; call LoadDocument first`. **You do NOT need to call `LoadDocument` manually**
   — `ModalDocCache.EnsureLoaded` runs it on first `Open`. The error wording is
   misleading; the real fix is to align the two strings.

#### Worked example

`MessageBox.XmlSrc = "Modals/MessageBox.ui"` with an Addressables resolver:

```csharp
// boot
UI.UseAddressableResolver();
MessageBox.XmlSrc = "Modals/MessageBox.ui";
```

```xml
<!-- Assets/UI/Modals/MessageBox.ui.xml
     Addressables Groups → Address: "Modals/MessageBox.ui"   ← prerequisite #1 -->
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="Modals/MessageBox.ui" reference="1920x1080">
                  <!-- ↑ must match MessageBox.XmlSrc byte-for-byte — prerequisite #2 -->
    <Image id="backdrop" anchor="stretch" color="#000000FE"/>
    <Frame id="dialog" anchor="center" size="640x300">
      ... (id table below) ...
    </Frame>
  </Screen>
</PromptUGUI>
```

Common copy-paste mistake — taking the package default XML and changing only `XmlSrc`:

```csharp
MessageBox.XmlSrc = "Modals/MessageBox.ui";
```

```xml
<Screen name="PromptUGUI/Modals/MessageBox.ui">   <!-- ❌ still the default — doesn't match XmlSrc -->
```

→ Addressables resolves fine, `LoadDocument` registers `_docs["PromptUGUI/Modals/MessageBox.ui"]`,
then `OpenModalScreen("Modals/MessageBox.ui")` looks up `_docs["Modals/MessageBox.ui"]` →
miss → `"Modal screen 'Modals/MessageBox.ui' not loaded; call LoadDocument first"`.

#### Custom `MessageBox.ui.xml` contract

Inside the `<Screen>`, your override XML must declare these `id`s:

| Id        | Required | Bind behavior                                                                                                                                                                                                                                  |
| --------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `text`    | yes      | `<Text>`. Receives the `text` argument verbatim (no auto-translation — pass `UI.Tr(...)` yourself if needed). Always shown.                                                                                                                    |
| `title`   | yes      | `<Text>`. Receives the `title` argument. **`GameObject.SetActive(false)`** when the argument is null/empty — don't depend on it always being visible. Reserve layout space if you want a fixed dialog height regardless of title.              |
| `ok`      | yes      | `<Btn>`. `SetActive(false)` unless `MsgBtn.OK` is in the requested `buttons` mask. Default label = XML text content (e.g. `<Btn id="ok">OK</Btn>`).                                                                                            |
| `cancel`  | yes      | `<Btn>`. Same rule for `MsgBtn.Cancel`.                                                                                                                                                                                                        |
| `yes`     | yes      | `<Btn>`. Same rule for `MsgBtn.Yes`.                                                                                                                                                                                                           |
| `no`      | yes      | `<Btn>`. Same rule for `MsgBtn.No`.                                                                                                                                                                                                            |
| `close`   | yes      | `<Btn>`. Same rule for `MsgBtn.Close`.                                                                                                                                                                                                         |
| `icon`    | no       | `<Icon>`. `Bind` swallows `KeyNotFoundException`, so omitting the id is fine. If you include it, PromptUGUI's parser still requires a `name=` attribute (use any placeholder — Bind overwrites `.Name` when set, `SetActive(false)` otherwise). |
| backdrop  | no       | Any full-screen Graphic if you want a dim / click-blocker. No required id. **Library does NOT auto-create one.**                                                                                                                               |

**Default button labels & i18n**: the builtin XML uses English text content (`<Btn
id="ok">OK</Btn>` etc.). Those literals become msgids during XML extraction, so they go
into your project's `.po` files alongside all other XML strings — translate them via
your normal i18n workflow. The package does NOT ship its own `.po`; the default labels
are visible English until your project supplies translations.

**Custom button labels** (the `IEnumerable<(label, key)>` overload): each `label` string
is assigned to the button via `btn.Text = label` at Bind time, replacing the XML text.
These are NOT auto-translated — wrap with `UI.Tr(...)` at the call site:
`new[] { (UI.Tr("Retry"), MsgBtn.OK) }`.

#### Common mistakes (modal override)

Same table applies to `Loading.XmlSrc` and any `ModalRequest<T>.XmlSrc` — they all share
the resolver path + `<Screen name>` contract.

| Symptom (exact runtime error)                                                                                            | Cause                                                                                                                              | Fix                                                                                                                                                                                                       |
| ------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `InvalidKeyException: No Location found for Key=<XmlSrc>` (stack: `AddressableResolverHelper.LoadFromAddressablesInternalAsync`) | Addressables resolver is active and `<XmlSrc>` isn't registered as an Address (or the Address differs by even one character).      | Window → Asset Management → Addressables → Groups; drag your `.ui.xml` in; set its Address to exactly `<XmlSrc>`. Or boot a Resources/custom resolver instead. "File exists in `Assets/`" doesn't matter. |
| `InvalidOperationException: Modal screen '<XmlSrc>' not loaded; call LoadDocument first` (stack: `UI.OpenModalScreen`)   | XML's `<Screen name="...">` ≠ `<XmlSrc>`. `_docs` got registered under the wrong key.                                              | Edit your XML: `<Screen name="<XmlSrc>" ...>` — byte-equal. **Do NOT** call `LoadDocument` manually; the error wording is misleading.                                                                     |
| Dialog opens but clicks on empty space still hit the UI below it                                                         | No full-screen Graphic in your override XML; pointer raycasts pass through where there's no drawn surface.                         | Add `<Image id="backdrop" anchor="stretch" color="#000000FE"/>` as a sibling of your dialog Frame inside the `<Screen>`.                                                                                  |
| `Screen 'X' already loaded` on second `Open`                                                                             | You ALSO called `UI.LoadDocumentAsync("X")` manually (or two modal `XmlSrc`s point at XML files whose `<Screen name>` collides).   | Pick one path — either let `ModalDocCache` auto-load (recommended), or load yourself and don't touch `XmlSrc`. Distinct modals need distinct `<Screen name>`s.                                            |

### Loading overlay

A non-interactive overlay that blocks the screen while async work runs, then your code
closes it. **Not a modal/dialog** — separate subsystem that sits *below* the dialog
stack, so a MessageBox opened during a Loading appears on top.

```csharp
var loading = Loading.Open(UI.Tr("Loading..."));
try { await DoWorkAsync(); }
finally { loading.Close(); }   // idempotent; loading.IsClosed == true afterwards
```

- `Loading.Open(text)` returns a `LoadingHandle` synchronously; close from code via
  `.Close()` (idempotent). Query `.IsClosed` if you need a status check.
- **No ESC dismissal** — `LoadingOverlay` does not attach a `ModalEscapeListener`. Cancel
  by closing the handle.
- Coexists with dialogs — a MessageBox opened while a Loading is showing stacks above
  it; opening one no longer deadlocks against the other.
- **Concurrent `Loading.Open()` calls each get their own overlay Screen instance**, all
  sharing the same `Loading.SortingOrder` band. Each call returns an independent
  `LoadingHandle`; close them independently. There's no built-in ref-counting — if you
  want "show once, close after N tasks", track that with your own counter.
- `Loading.SortingOrder` (default 500) is the overlay band; keep it below
  `UI.Modal.SortingOrderBase` (default 1000) so dialogs render above overlays.
- `text` is optional (`null`/`""` → spinner only — the `<Text id="text">` node is
  `SetActive(false)`'d). Custom XML: only `<Text id="text">` is recognised by Bind, and
  it is optional (KeyNotFoundException tolerated).

#### Overriding the builtin Loading layout

```csharp
Loading.XmlSrc = "MyUI/Modals/PixelLoading.ui";   // resolved by UI.SourceResolver
```

Same key / resolver / `.ui` suffix rules as `MessageBox.XmlSrc`. Default:
`"PromptUGUI/Modals/Loading.ui"`. Custom XML need only include `<Text id="text">`
(optional) — everything else (spinner animation, backdrop) is up to you.

**Same setup prerequisites apply** — see "Setup prerequisites" and "Common mistakes
(modal override)" under "Overriding the builtin MessageBox layout" above. The resolver
registration and `<Screen name>` ↔ `XmlSrc` byte-equal rules are identical; the
`InvalidKeyException` / "Modal screen 'X' not loaded" errors will surface here too if
either is wrong.

### Modal Canvas + `UI.CanvasConfigurator`

Modal and Loading Screens go through the same `UI.CanvasConfigurator` callback as
regular Screens:

```csharp
UI.CanvasConfigurator = (canvas, screenName) => {
    if (screenName == MessageBox.XmlSrc) {            // "PromptUGUI/Modals/MessageBox.ui"
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
    }
};
```

Two caveats specific to modals/overlays:

1. **`screenName` is the XML `<Screen name="...">` value**, not the internal modal
   instance key (`"{name}#m{N}"`). Branch on the XML name — stable across `Open` calls
   and across multiple concurrent instances of the same dialog.
2. **The modal subsystem overrides `canvas.sortingOrder` AFTER your configurator runs**
   — modals to `UI.Modal.SortingOrderBase + depth`, Loading to `Loading.SortingOrder`.
   Don't try to pin `sortingOrder` from the configurator for modal/loading XML keys;
   tune `UI.Modal.SortingOrderBase` / `Loading.SortingOrder` instead. `renderMode` /
   `worldCamera` / `planeDistance` / `pixelPerfect` etc. are still honored.

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
