---
name: scripting-promptugui-csharp
description: Use when writing C# that drives PromptUGUI — `UI.LoadDocumentAsync` / `UI.Open`, `Screen.Get<T>`, R3 event subscriptions (`OnClick` / `OnValueChanged` / `OnSelected` / `OnState`). For the XML markup itself, see authoring-promptugui-xml; for Addressables-backed loaders (`.ui.xml` / `.po` / icon atlases), see using-promptugui-addressables.
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

**Power-of-two-only scaling `UI.PixelScalePowerOfTwo`** (`bool`, default `false`): when `true`, the Pixel-mode `scaleFactor` is constrained to a power-of-two ladder `…0.25, 0.5, 1, 2, 4, 8…`. The magnify segment snaps **down** to the largest power of two ≤ the fit-inside ratio, so a 3×-capable screen renders at 2×, 5× at 4× — content still fits inside (rounds down, never overflows; the slack is absorbed by `anchor="stretch"` / letterboxing). The sub-1× fallback is already `1/2^n`, so it is unchanged; only the integer magnify steps `3, 5, 6, 7, …` are removed. Applied **before** `MinPixelScale`, which still floors the snapped result — pair it with a power-of-two `MinPixelScale` to stay on-ladder. Pixel-mode only; Auto mode ignores it.

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

Note: inside a template body always write a **literal** id (e.g. `id="close"`) — never parameterize it as `id="{{param}}"`. The `{{...}}` substitution only touches attribute values and text content; it **never** applies to `id`, so a parameterized id stays the literal string. You don't need it: each invocation has its **own id scope**, so the same `id="close"` across many instances never collides — disambiguate from C# with the `instanceId/close` path above.

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

**Progress** — `screen.Get<Progress>("hp").Value = 0.42f;` 或 R3 推送 `healthStream.Subscribe(v => p.Value = v).AddTo(screen)`。Progress 是只读显示控件，无 `OnValueChanged`。`Value` 被 `Mathf.Clamp01` 钳位。注意：`[Bind]` 在本项目里是把 child control 字段注入到 parent（见 `Runtime/Registry/BindAttribute.cs`），不是数据流绑定 —— 用直接 setter / R3 推。

**RawImage** — 显示运行时加载的 `Texture`（不是 sprite）：`screen.Get<RawImage>("avatar").Texture = tex;`（`Texture2D` / `RenderTexture` 均可）。`Texture` 是普通 C# 属性（get/set），**不是** XML 属性 —— XML 端只声明位置 / `color` / `type=contain|cover` / `mask`。重新赋值会自动按新 texture 重算 `type=contain/cover` 的纵横比。典型用法是把异步加载结果推进去：`LoadAsync(url).Subscribe(t => raw.Texture = t).AddTo(screen)`。

**`Btn.OnState` / `Tab.OnState` / `Toggle.OnState`** — each control broadcasts its uGUI interaction state as `Observable<InteractState>` (`InteractState { Normal, Hover, Pressed, Selected, Disabled }`). `Selected` = the active/`isOn` control at rest; transient Hover/Pressed/Disabled override it and revert on release; a momentary `<Btn>` never emits `Selected`. The observable replays the current value to new subscribers, so you can react in C# to press / hover / select / disable — complementing the XML-side `*Color` / `*Modulate` state colours and `<Show>` artwork swap (see authoring-promptugui-xml → "Btn state visuals"):

```csharp
screen.Get<Btn>("buy").OnState
      .Where(s => s == InteractState.Pressed)
      .Subscribe(_ => sfx.PlayClick()).AddTo(screen);

screen.Get<Tab>("edit").OnState
      .Where(s => s == InteractState.Selected)
      .Subscribe(_ => ShowEditPanel()).AddTo(screen);
```

State is driven by the Selectable machine and is disabled-aware (a disabled control only emits `Disabled`). `interactable="false"` (XML) puts the control in `Disabled`.

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

### TabBar

```csharp
var bar = screen.Get<TabBar>("topbar");

// Static XML tabs already exist; subscribe to selection changes:
bar.OnSelectionChanged
   .Subscribe(tab => Debug.Log($"selected: {tab?.Id}"))
   .AddTo(screen);

// Or per-Tab:
screen.Get<Tab>("edit").OnSelected
      .Subscribe(_ => OpenEditor()).AddTo(screen);

// Dynamic: clear and rebuild from a data source (same shape as ScrollList.BindItems):
bar.BindItems(
    items,
    (Tab tab, MyModel m) => { tab.Text = m.Name; tab.Bind = m.FrameId; })
   .AddTo(screen);

// Custom Template (when Tab needs to be wrapped — e.g. <HStack><Icon/><Tab id="tab"/></HStack>):
bar.BindItems<MyModel, IControl>(
    items,
    (slot, m) => slot.Get<Tab>("tab").Text = m.Name)
   .AddTo(screen);

// Query API:
bar.Count;          // current tab count
bar.SelectedIndex;  // -1 if none (only possible right after BindItems with empty list)
bar.SelectedTab;    // Tab ref or null
bar.GetAt(i);
```

Setting `tab.IsOn = true` triggers mutex (other Tabs flip to false via the TabBar's private `ToggleGroup`) AND auto-shows the `bind`-ed Frame — no manual `frame.GameObject.SetActive(...)` needed. If `BindItems` is called with an empty list, `OnSelectionChanged` fires with `null` to let subscribers clear UI state. After hot-reload, re-Bind just like ScrollList.

`bar.BindItems<T, TSlot>` lets the template root be any `IControl`; `bar.BindItems<T>` is shorthand when the template root _is_ a `<Tab>` directly. The `<Tab>` reachable inside the slot is found via `ScopedIds` first, then a recursive child walk — Templates without an id'd Tab still work as long as exactly one `<Tab>` exists in the subtree.

### Carousel

```csharp
// Dynamic cards (same shape as ScrollList.BindItems):
screen.Get<Carousel>("banner")
      .BindItems(banners, (IControl card, Banner b) => {
          card.Get<Text>("title").TextValue = b.Title;
          card.Get<Btn>("cta").OnClick.Subscribe(_ => Open(b.Link)).AddTo(screen);
      }).AddTo(screen);

// Custom template slot type:
screen.Get<Carousel>("banner")
      .BindItems<Banner, BannerCard>(banners, (BannerCard card, Banner b) => card.Bind(b))
      .AddTo(screen);
```

Control API:

```csharp
var car = screen.Get<Carousel>("banner");
car.Current;                       // 当前页（int，runtime-owned；resize 不重置）
car.Count;                         // 卡片数
car.GoTo(2, animated: true);       // 跳到指定页（默认带动画）
car.Next(); car.Previous();        // 前/后一页（默认带动画）
car.Playing = false;               // 暂停/恢复自动播放（interval > 0 才有效）
car.OnCurrentChanged
   .Subscribe(i => Analytics.BannerView(i))
   .AddTo(screen);                 // Observable<int>：任意来源的页变化，dedup 相同值
```

`current` is a **runtime-owned state** (same as Tab `isOn`): resize / Variant / Theme ReSolve does NOT reset the page. `current.<variant>` initial overrides still apply when the user has not navigated at runtime; once navigated, the user's choice wins.

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

## Theme switching

`UI.Theme.Set` is order-independent — it never blocks on the load. You can fire it before `LoadCommonLibraryAsync` resolves; when the named theme registers, `Theme.Changed` re-fires and open Screens repaint. While pending, color attribute resolution falls back to `Color.white` for token names (literal hex / CSS named values resolve as usual).

```csharp
// Inspect / switch.
UI.Theme.Current;       // string?, the active theme name (= last Set, even if not yet loaded)
UI.Theme.Available;     // IReadOnlyCollection<string> of registered themes
UI.Theme.Set("dark");   // never throws on unknown name; throws ArgumentNullException on null

// Subscribe to switches (Screens do this automatically and ReSolve themselves).
UI.Theme.Changed += newName => Debug.Log($"theme switched to {newName}");

// Programmatic lookup (returns null when no Current theme or token absent).
Color? c = UI.Theme.Lookup("primary");

// Full resolution chain.
Color resolved  = UI.Theme.Resolve("primary");   // token hit
Color resolved2 = UI.Theme.Resolve("#ff8800");   // hex literal
Color resolved3 = UI.Theme.Resolve("red");       // CSS named color
// UI.Theme.Resolve("primaru")  // throws when Current is registered;
//                                 returns Color.white when Current is still pending.
```

### AA / async load idiom

Common pattern with Addressables: fire-and-forget the load from `[RuntimeInitializeOnLoadMethod]`, then `Set` immediately. No `await` needed in the boot hook.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Boot()
{
    UI.UseAddressableResolver();
    _ = UI.LoadCommonLibraryAsync("themes/main");  // fire and forget
    UI.Theme.Set("dark");                          // queued; takes effect once "dark" registers
}
```

Sequence:

1. `Set("dark")` fires `Theme.Changed` once; any already-open Screens ReSolve → tokens soft-fail to white.
2. `LoadCommonLibraryAsync` completes, `dark` registers, `Theme.Changed` re-fires automatically → Screens ReSolve again → tokens hit real colors.
3. If `Current` still names an unregistered theme after the load (typo, missing source), the loader emits one `Debug.LogWarning` to surface it.

### Single-theme projects

If `LoadCommonLibraryAsync` registers exactly one theme and `UI.Theme.Current` is null, the loader auto-selects it. Multi-theme projects must call `UI.Theme.Set` explicitly (before or after the load, your choice).

### Hot reload

When a `<Theme>` XML file is edited at Editor time, `Theme.Changed` re-fires for the current theme so all open Screens re-color.

## Custom controls

```csharp
public sealed class MyControl : Control {
    UnityEngine.UI.Image _bg;

    public override void OnAttached() {
        _bg = GameObject.GetComponent<UnityEngine.UI.Image>()
              ?? GameObject.AddComponent<UnityEngine.UI.Image>();
    }

    [UIAttr, Preserve] public string Color { set { /* parse hex, apply */ } }
    [UIAttr("backgroundSprite", IsSprite = true), Preserve] public string Sprite { set { /* ... */ } }
}

UI.Registry.Register<MyControl>("MyControl", optionalPrefab: null);
```

- `[UIAttr]` (no name) maps to the camelCase of the property name (`Color` → `color`). `[UIAttr("foo")]` overrides.
- Supported types: `string` / `int` / `float` / `bool`. Use string + parse internally for everything else.
- `[UIAttr(IsSprite = true)]` marks an attribute whose value is a sprite reference resolved via `UI.ResolveSprite` (or `UI.SpriteResolver` for atlas-only `ns:name` lookups). The Editor "Sync Sprite Sets" feature reads this flag to discover which attribute names per tag carry sprite refs, so the atlas auto-packs sprites referenced via non-`sprite` attribute names too (e.g. `<Progress fill='ui:foo' bg='ui:bar' frame='ui:baz' mask='ui:qux'/>`). Custom controls whose setter calls `UI.ResolveSprite` should set the flag; without it, only the conventional attribute name `sprite` is scanned and you'll see "missing sprite" warnings at sync time.
- `[Bind]` on a field auto-wires a child component from a Prefab by child name. Useful when the control has a non-trivial Prefab structure.
- `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` are reference implementations — for project-specific differentiation (pixel border, press feedback, custom popup chrome), subclass and override `OnAttached`; don't modify the base controls.
- **IL2CPP Managed Stripping (Medium+)**: setter-only `[UIAttr]` properties get their `PropertyInfo` metadata stripped (`Type.GetProperties()` returns nothing for them), reflection misses the property, attribute silently reverts to default in Player builds with no error log. **Pair every `[UIAttr]` and `[Bind]` with `[Preserve]`**: `[UIAttr, Preserve] public string Color { set { ... } }`. `PromptUGUI.Registry.PreserveAttribute` is name-matched by Mono.Linker (any class named exactly `PreserveAttribute`, inheritance does **not** count). All built-in controls already do this; custom controls must too.

### Color attributes on custom controls

Mark color-bearing `[UIAttr]` properties with `IsColor = true`. The setter receives the raw `string` from XML and should call `UI.Theme.Resolve` to convert:

```csharp
[UIAttr(IsColor = true), Preserve]
public string AccentColor
{
    set => _accent.color = UI.Theme.Resolve(value);
}
```

`IsColor = true` enables static lint discovery (the lint pipeline checks hex literals). Runtime resolution is in the setter itself — there is no separate applier branch (same pattern as `IsSprite` + `UI.ResolveSprite`).

If `UI.Theme.Resolve` throws, the exception flows through the reflection setter into `ControlAttributeApplier.ApplyOne`, which wraps it with the node's path — authors see e.g. `<Image id='avatar'> attribute color="primaru": unknown color token "primaru" (no entry in theme 'light', not a valid hex/named literal)`.

## Common mistakes (C#)

| Symptom                                   | Cause                                                                                                                  | Fix                                                                                               |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| Element not found at runtime              | `id` only declared inside a `<Template>`, accessed by flat name                                                        | Use path: `screen.Get("templateInstanceId/innerId")`                                              |
| Subscription survives Close → null refs   | Forgot `.AddTo(screen)`                                                                                                | Always tie R3 subscriptions to Screen lifetime                                                    |
| Custom control's `[UIAttr]` ignored       | Property type other than string/int/float/bool                                                                         | Take a string param and parse internally (see `Btn.Color` for a hex example)                      |
| Attrs silently default in IL2CPP build    | Forgot `[Preserve]` next to `[UIAttr]` — Medium+ stripping drops PropertyInfo metadata, reflection misses the property | Always write `[UIAttr, Preserve]` (both from `PromptUGUI.Registry`)                               |
| ScrollList shows nothing after hot-reload | `BindItems` subscription disposed on close, but the ScrollList is rebuilt on reload                                    | Re-call `BindItems` on reload — the convention is to re-wire from a single `OnOpened` entry point |
| `<Icon>` shows pink/error sprite          | `UI.SpriteResolver` not set (or `SpriteSet` not in Resources/SpriteSets)                                               | Call `SpriteResolverHelpers.UseSpriteSetResolver(...)` before any Screen opens                    |

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
               .OnState                Btn/Tab/Toggle:InteractState (Normal/Hover/Pressed/Selected/Disabled; replays current)
                                        Selected = active/isOn at rest; <Btn> never emits Selected
               .OnValueChanged         Toggle:bool / Slider:float / InputField:string / Tab:bool
               .OnSelected             Dropdown:int / Tab:Unit (on=true only)
               .OnSelectionChanged     TabBar:Tab (the newly-on Tab, or null when emptied)
               .OnCurrentChanged       Carousel:int (any-source page change, deduped)
               .OnEndEdit / .OnSubmit  InputField:string
               .Subscribe(...).AddTo(screen)   tie lifetime — ALWAYS
               Progress                display-only; .Value = 0.42f (Clamp01); no event

DATA PUSH      Dropdown.BindOptions(Observable<IEnumerable<string>>)
               ScrollList.BindItems(Observable<IReadOnlyList<T>>, (slot,t)=>...)
               TabBar.BindItems(Observable<IReadOnlyList<T>>, (Tab tab,t)=>...)
                                       or BindItems<T,TSlot>(...) for wrapped templates
               Carousel.BindItems(Observable<IReadOnlyList<T>>, (IControl card,t)=>...)
                                       or BindItems<T,TSlot>(...) for typed card template
               .AddTo(screen)
               TabBar query: .Count / .SelectedIndex (-1 if empty) / .SelectedTab / .GetAt(i)
               Carousel query: .Count / .Current (get/set) / .Playing (get/set) / .GoTo(i,animated) / .Next() / .Previous()

VARIANT        UI.Variants.Set("name", true|false)            re-applies, no rebuild
ORIENTATION    UI.Orientation.IsPortrait                      auto-tracked: portrait / landscape variants
               UI.Orientation.Set(bool)                       manual override
               UI.Orientation.AutoTrack = false               disable global tracker
LOCALE         UI.Locale.Set("en")                            sync
               UI.Locale.SetToSystemDefault()
               UI.Tr("...")                                   extract + translate

THEME          UI.Theme.Set("dark")                           switch active theme; order-independent (accepts unregistered name); fires Theme.Changed
               UI.Theme.Resolve(value)                        token → base chain → literal hex/CSS-name; soft-fails to Color.white when Current is set but not yet registered
               UI.Theme.Lookup(token)                         token-only lookup; returns Color? (no literal fallback, no throw)
               UI.Theme.Changed                               event Action<string>; fires on Set, post-load registration of pre-Set theme, and hot reload

CANVAS         UI.CanvasConfigurator = (canvas, name) => { ... }
               runs AFTER XML canvas= / reference= apply

CUSTOM         class X : Control { override OnAttached() { ... } }
               [UIAttr] / [UIAttr("name")]    string/int/float/bool only
               [Bind] field                   auto-wire child by name
               UI.Registry.Register<X>("Tag", prefab)

MODAL          var r = await MessageBox.Open(text, MsgBtn.OK|MsgBtn.Cancel, icon, title)
               MessageBox.Open(text, [(label,key),...], icon, title, mode)  custom labels
               MessageBox.Open(text, ..., mode: ModalMode.Queued)  排队,不叠加
               var s = await InputBox.Open(title, message, initial, placeholder,
                                           contentType, okLabel, cancelLabel, mode)
                       // confirm → text ("" if empty); cancel/ESC → null
                       // Enter respects ok.Interactable (disable ok → Enter gated too)
               configure:     Action<IScreen> trailing arg on every Open (MessageBox/InputBox/Loading)
                              // post-bind hook → live Screen; reach any control w/o subclassing
                              // e.g. InputBox.Open(t, configure: s => s.Get<Btn>("ok").Interactable = false)
                              // base ModalRequest<T>.Configure field → custom modals get it free
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
LOADING        var h = Loading.Open(text, configure); h.Close()  idempotent; h.IsClosed
               Loading.XmlSrc = "MyUI/Modals/Foo.ui"        override; only <Text id="text"> recognised
               Loading.SortingOrder = 500                   overlay 层带,低于 dialog
               concurrent Open() → independent overlays at the same band (no ref-count)
```

## Modal dialogs

PromptUGUI ships a generic modal stack in `PromptUGUI.Application.Modals` plus three
builtin overlays: a `MessageBox` dialog, an `InputBox` text prompt, and a `Loading`
spinner. **Every modal IS a real
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

// InputBox: text prompt. Returns the text on confirm/Enter, null on cancel/ESC.
// "" (empty) is distinct from null — empty submit vs cancelled.
string name = await InputBox.Open(UI.Tr("Your name?"), placeholder: UI.Tr("e.g. Link"));
if (name != null) game.PlayerName = name;

// password prompt with a sub-message line
string pw = await InputBox.Open(UI.Tr("Enter password"),
    message: UI.Tr("at least 8 chars"), contentType: "password");
```

### API surface (`PromptUGUI.Application.Modals`)

```csharp
// Every Open helper takes a trailing `configure: Action<IScreen>` — a post-bind hook that
// hands you the live modal Screen so you can reach ANY control without subclassing (disable
// the OK Btn, wire field validation, restyle, …). It runs AFTER the builtin Bind, so it
// overrides builtin wiring. See "Customizing a builtin modal" below.
public static class MessageBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox.ui";

    public static Awaitable<MsgBtn> Open(
        string text, MsgBtn buttons = MsgBtn.OK,
        string icon = null, string title = null,
        ModalMode mode = ModalMode.Popup,
        Action<IScreen> configure = null);

    public static Awaitable<MsgBtn> Open(
        string text,
        IEnumerable<(string label, MsgBtn key)> buttons,   // also sets the .Buttons mask
        string icon = null, string title = null,
        ModalMode mode = ModalMode.Popup,
        Action<IScreen> configure = null);
}

public static class InputBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/InputBox.ui";

    // confirm/Enter → entered text ("" if empty); cancel/ESC → null
    // Enter (OnSubmit) respects the ok Btn's Interactable — disabling ok in `configure`
    // gates Enter too, so OK is the single source of truth for "can submit".
    public static Awaitable<string> Open(
        string title,
        string message     = null,   // optional line under the title
        string initial     = null,   // prefill text
        string placeholder = null,
        string contentType = null,   // InputField.contentType, e.g. "password" / "email"
        string okLabel     = null,
        string cancelLabel = null,
        ModalMode mode     = ModalMode.Popup,
        Action<IScreen> configure = null);
}

[Flags] public enum MsgBtn { None=0, OK=1, Cancel=2, Yes=4, No=8, Close=16 }
public enum ModalMode { Popup = 0, Queued = 1 }

public static class Loading {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";
    public static int SortingOrder { get; set; } = 500;   // keep < SortingOrderBase
    public static LoadingHandle Open(string text = null, Action<IScreen> configure = null);
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

### Customizing a builtin modal (the `configure` hook)

Every builtin `Open` (`MessageBox` / `InputBox` / `Loading`) takes a trailing
`configure: Action<IScreen>`. It fires **once, right after the builtin Bind**, with the
live modal `Screen` — so you reach any control via `screen.Get<T>(id)` and customize
without writing a `ModalRequest<T>` subclass. Because it runs after Bind, it overrides
builtin wiring (labels, etc.) rather than being overwritten.

```csharp
// Disable OK until the field is non-empty. Disabling the OK Btn gates BOTH click and Enter
// (InputBox's OnSubmit checks ok.Interactable), and greys the button (runtime Btn.Interactable
// drives the Selectable, not just the CanvasGroup).
string name = await InputBox.Open(UI.Tr("Your name?"), configure: screen => {
    var ok    = screen.Get<Btn>("ok");
    var field = screen.Get<InputField>("field");
    ok.Interactable = false;
    field.OnValueChanged
         .Subscribe(v => ok.Interactable = !string.IsNullOrWhiteSpace(v))
         .AddTo(screen);
});
```

The hook is declared once on the base `ModalRequest<TResult>` as a public
`Action<IScreen> Configure` field (the builtin helpers just thread their `configure`
param into it), so **custom `ModalRequest<T>` subclasses inherit it for free** — set
`new MyRequest { Configure = s => … }`. Subscriptions made in the hook should still
`.AddTo(screen)`. A throwing `configure` cancels that modal's await (logged for Loading).

> **Btn.Interactable at runtime**: setting it from code (here or anywhere) now drives the
> underlying Button — greying it + emitting `InteractState.Disabled` — and blocks raycasts
> via the CanvasGroup. Same end state as the XML `interactable="false"` attr.

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
            close(screen.Get<InputField>("input").TextValue)).AddTo(screen);
        screen.Get<Btn>("cancel").OnClick.Subscribe(_ => close(null)).AddTo(screen);
    }
    public override bool TryEscape(out string r) { r = null; return true; }  // ESC → null
}

var name = await UI.Modal.OpenAsync(new NamePickerRequest());
```

> `InputField.TextValue` is the read/write current-text property (the same one bound by
> the XML `text=` attribute). For the common "prompt the user for a string" case, prefer
> the builtin `InputBox.Open(...)` over hand-rolling a `ModalRequest<string>`.

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

| Id       | Required | Bind behavior                                                                                                                                                                                                                                   |
| -------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `text`   | yes      | `<Text>`. Receives the `text` argument verbatim (no auto-translation — pass `UI.Tr(...)` yourself if needed). Always shown.                                                                                                                     |
| `title`  | yes      | `<Text>`. Receives the `title` argument. **`GameObject.SetActive(false)`** when the argument is null/empty — don't depend on it always being visible. Reserve layout space if you want a fixed dialog height regardless of title.               |
| `ok`     | yes      | `<Btn>`. `SetActive(false)` unless `MsgBtn.OK` is in the requested `buttons` mask. Default label = XML text content (e.g. `<Btn id="ok">OK</Btn>`).                                                                                             |
| `cancel` | yes      | `<Btn>`. Same rule for `MsgBtn.Cancel`.                                                                                                                                                                                                         |
| `yes`    | yes      | `<Btn>`. Same rule for `MsgBtn.Yes`.                                                                                                                                                                                                            |
| `no`     | yes      | `<Btn>`. Same rule for `MsgBtn.No`.                                                                                                                                                                                                             |
| `close`  | yes      | `<Btn>`. Same rule for `MsgBtn.Close`.                                                                                                                                                                                                          |
| `icon`   | no       | `<Icon>`. `Bind` swallows `KeyNotFoundException`, so omitting the id is fine. If you include it, PromptUGUI's parser still requires a `name=` attribute (use any placeholder — Bind overwrites `.Name` when set, `SetActive(false)` otherwise). |
| backdrop | no       | Any full-screen Graphic if you want a dim / click-blocker. No required id. **Library does NOT auto-create one.**                                                                                                                                |

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

| Symptom (exact runtime error)                                                                                                    | Cause                                                                                                                            | Fix                                                                                                                                                                                                       |
| -------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `InvalidKeyException: No Location found for Key=<XmlSrc>` (stack: `AddressableResolverHelper.LoadFromAddressablesInternalAsync`) | Addressables resolver is active and `<XmlSrc>` isn't registered as an Address (or the Address differs by even one character).    | Window → Asset Management → Addressables → Groups; drag your `.ui.xml` in; set its Address to exactly `<XmlSrc>`. Or boot a Resources/custom resolver instead. "File exists in `Assets/`" doesn't matter. |
| `InvalidOperationException: Modal screen '<XmlSrc>' not loaded; call LoadDocument first` (stack: `UI.OpenModalScreen`)           | XML's `<Screen name="...">` ≠ `<XmlSrc>`. `_docs` got registered under the wrong key.                                            | Edit your XML: `<Screen name="<XmlSrc>" ...>` — byte-equal. **Do NOT** call `LoadDocument` manually; the error wording is misleading.                                                                     |
| Dialog opens but clicks on empty space still hit the UI below it                                                                 | No full-screen Graphic in your override XML; pointer raycasts pass through where there's no drawn surface.                       | Add `<Image id="backdrop" anchor="stretch" color="#000000FE"/>` as a sibling of your dialog Frame inside the `<Screen>`.                                                                                  |
| `Screen 'X' already loaded` on second `Open`                                                                                     | You ALSO called `UI.LoadDocumentAsync("X")` manually (or two modal `XmlSrc`s point at XML files whose `<Screen name>` collides). | Pick one path — either let `ModalDocCache` auto-load (recommended), or load yourself and don't touch `XmlSrc`. Distinct modals need distinct `<Screen name>`s.                                            |

### Loading overlay

A non-interactive overlay that blocks the screen while async work runs, then your code
closes it. **Not a modal/dialog** — separate subsystem that sits _below_ the dialog
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
