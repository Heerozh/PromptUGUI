# PromptUGUI Best Practices

[English](BEST_PRACTICES.md) | [中文](BEST_PRACTICES.zh.md)

## 1. Initialization Best Practices

Use `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` to configure the resolver, scaling, theme, and locale all at once:

```csharp
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

public static class UIBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // ① Resolver: route .ui.xml / .po / SpriteSet all through Addressables
        UI.UseAddressableResolver();
        UI.Locale.UseAddressableResolver();
        _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(
            new[] { "SpriteSets-Common", $"SpriteSets-{UserConfig.Language}" });

        // ② For a pixel-art game: integer-multiple pixel-aligned scaling + a scale floor
        UI.DefaultScaleMode = ScaleMode.Pixel;
        UI.MinPixelScale = 1.0f;

        // ③ Load the global template/theme library (includes <Theme>), then set the theme
        _ = UI.LoadCommonLibraryAsync("UI/Templates/DefaultTheme.ui.xml");
        UI.Theme.Set("dark");

        // ④ Override the built-in MessageBox with the project's custom dialog
        MessageBox.XmlSrc = "UI/Modals/MessageBox.ui.xml";

        // ⑤ Apply the locale: returns synchronously, .po loads in the background
        UI.Locale.Set(UserConfig.Language);
    }
}
```
Whether single-player or online, Addressables (AA) is the recommended route — it keeps loading and future maintenance easy.

`Theme.Set` / `Locale.Set` / the SpriteSet resolver are all **order-independent**: you can kick off loading fire-and-forget (`_ =`) and call `Set` right after; once the assets finish loading, **every open screen is refreshed automatically**. So there's no need to `await` in boot, and order doesn't matter.

**Optional (`ScaleMode.Pixel` pixel-art mode)**: scales the Canvas by integer multiples so sprites are always aligned to whole pixels. You can also set this per-screen in XML on the `<Screen>` tag with `scale-mode="auto"` or `"pixel"`.

---

## 2. Loading & Opening Screens + C# Wiring

**Use an `AssetReferenceT<TextAsset>` slot for screens**: drag the asset in the Inspector instead of hand-typing a string key.

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private AssetReferenceT<TextAsset> _xml;   // drag the .ui.xml into the Inspector

    private async void Start()
    {
        await UI.LoadDocumentAsync(_xml);     // parse + expand templates + register; hot-reloads automatically in the Editor
        var screen = UI.Open("MainMenu");     // instantiate the GameObject, returns IScreen

        // Wiring: every subscription must .AddTo(screen)
        screen.Get<Btn>("play").OnClick
              .Subscribe(_ => Game.Start())
              .AddTo(screen);

        screen.Get<Toggle>("mute").OnValueChanged
              .Subscribe(on => Audio.Mute = on)
              .AddTo(screen);
    }
}
```

**`.AddTo(screen)` is a hard rule.** R3 subscriptions must be bound to the Screen lifecycle. Miss it → the subscription outlives Close, holds onto a destroyed GameObject, and fires ghost callbacks the next time you Open.

**Use `BindItems` / `BindOptions` for dynamic lists** (data-driven; don't hand-`new` child nodes):

```csharp
screen.Get<Dropdown>("quality")
      .BindOptions(Observable.Return(new[] { "Low", "Medium", "High" }))
      .AddTo(screen);

screen.Get<ScrollList>("inv").BindItems(player.Inventory, (slot, item) =>
{
    slot.Get<Text>("label").TextValue = item.Name;
}).AddTo(screen);
```

> For anything beyond one or two screens, drive opening / closing through `UI.Router` (§3) instead of calling `UI.Open` directly all over your code — the `Get` / `AddTo` / `BindItems` wiring above is identical either way.

---

## 3. Router / Deep Link Navigation

**Manage every screen through `UI.Router` — don't scatter raw `UI.Open` / `UI.Close` calls around your code.** Register each navigable destination **once at boot** with a stable opaque `name` and a canonical `parent`; from then on you navigate by name. The router reconciles the live screen chain on each call, so opening via a button and opening via a deep-link run the *same* code path and always land in the identical state.

```csharp
// Boot: declare the whole navigation graph once
UI.Router.Scheme = "myapp";                                    // optional deep-link scheme

UI.Router.Map("home",     src: "screens/Home");                // root page
UI.Router.Map("details",  src: "screens/Details", parent: "home",
    onEnter: (s, q) => s.Get<Text>("title").TextValue = q.Get("name", "—"));
UI.Router.Map("settings", src: "screens/Settings",
    present: RoutePresent.Modal, parent: "home");              // overlay panel, ESC→Back
UI.Router.MapTab("deals", parent: "home", tabId: "bar/deals"); // selects a <Tab> inside home
UI.Router.MapPrompt("rename", parent: "home", run: async (q, ct) =>
{                                                              // async flow, no screen of its own
    var name = await InputBox.Open("New name", initial: PlayerName, ct: ct);
    if (name != null) await Api.Rename(name);
});
```

```csharp
// Navigate by name — a button and a deep-link give the identical result
await UI.Router.Open("details");
await UI.Router.Navigate("myapp://details?id=42");             // deep-link / URL form
await UI.Router.Back();                                        // to parent; no-op at root
await UI.Router.Reset();                                       // close the whole chain

UI.Router.Current;   // top-of-chain name (null when empty)
UI.Router.Chain;     // root→top, e.g. ["home","details"]
UI.Router.Changed += () => Persist(UI.Router.Chain);           // fires after every reconcile
```

**Four presentations:** `Page` (full-screen), `Modal` (overlay, ESC→`Back()`), `Tab` (selects a `<Tab>` in its host Page/Modal), `Prompt` (an `async` flow such as `InputBox` / `MessageBox` — self-pops when it returns).

**Coexistence rules (hard):**

- **Open a router-managed screen only via the router.** A direct `UI.Open(...)` on it bypasses reconciliation and corrupts the chain. §2's wiring (`Get<T>` / `.AddTo` / `BindItems`) is unchanged — do it in `onEnter` or right after `Open` returns.
- **A Modal route's close button calls `UI.Router.Back()`**, not `UI.Close(...)` (the ESC listener already does this).
- **Ad-hoc overlays** (`MessageBox` / `InputBox` / `Loading` / `Toast`) stay outside the router; a reconcile auto-closes them, so a deep-link can't slip underneath them.
- **One destination, two entry points.** Register a flow once (the `rename` Prompt above) and fire it from both `OnClick` and `Navigate(...)` — never duplicate the open logic.

**Guards** veto navigation synchronously — e.g. block leaving a screen with unsaved edits:

```csharp
Func<string, bool> guard = target => !HasUnsavedChanges;       // false → NavigationRejectedException
UI.Router.AddGuard(guard);                                     // RemoveGuard(guard) by reference
```

---

## 4. Theme Colors

**Colors go through theme tokens — don't hard-code hex.** Define named colors in `<Theme>`; any `color=` attribute references them by name. When you switch themes, every screen re-colors automatically.

```xml
<PromptUGUI version="1">
  <Theme name="light">
    <Color name="primary"    value="#ff8800"/>
    <Color name="on-primary" value="#ffffff"/>
    <Color name="bg"         value="#f0f0f0"/>
  </Theme>
  <Theme name="dark" base="light">
    <Color name="primary" value="#cc6600"/>
    <Color name="bg"      value="#10141c"/>
    <!-- on-primary not redefined → inherited from base="light" -->
  </Theme>
</PromptUGUI>
```

```xml
<Image color="bg"/>
<Text  color="on-primary">Start</Text>
<Btn   color="primary">Buy</Btn>
```

```csharp
UI.Theme.Set("dark");   // switch at runtime; open screens refresh automatically
```

- Register the theme file via `UI.LoadCommonLibraryAsync(...)` (§1) or `<Import src="themes/main"/>`.
- **Tokens take priority over literals**: once a token named `red` is registered, `color="red"` resolves to it.
- A single-theme project can skip `Theme.Set` — the one theme is selected automatically after loading.

---

## 5. Sprites: author with `.pxl`, pack with SpriteSet

**Build a SpriteSet for shared icons and UI slices** (`Create → PromptUGUI → Sprite Set`, set `setName` + a source directory), reference them by name in XML, and **only the sprites actually referenced by XML are shipped** (package-time pruning):

```xml
<Icon name="Solar16Bold:Essentional, UI/Crown" color="primary" size="16x16"/>
<Image sprite="UI:Button-Small"/>
```

- `<Icon>` accepts only the `setName:icon-name` format.
- Controls like `<Image sprite=>`:
    - **`setName:icon-name` format** → goes through the SpriteSet atlas.
    - **`ui/dialog` format** → goes through `Resources.Load` (good for one-offs / prototyping).
- After changes, run `Tools → PromptUGUI → Sprite → Sync Atlases` to pack the referenced sprites.

**Author your sprites as `.pxl` pixel-grid text — the recommended way to make all UI art** (icons, 9-slice frames / borders, button skins, badges). A `.pxl` is plain text: a palette plus a character grid, one character per pixel. Drop it into a SpriteSet's source folder and Unity imports it as a point-filtered Sprite that **Sync Atlases packs exactly like a PNG** and XML references by the same `set:key`.

```
# Frames/panel.pxl — 8×8 rounded frame, 3px 9-slice border
palette: @ui
ppu: 16
chars:
  K: night
  H: cloud
border: 3,3,3,3
grid:
  .KKKKKK.
  KHHHHHHK
  KHHHHHHK
  KHHHHHHK
  KHHHHHHK
  KHHHHHHK
  KHHHHHHK
  .KKKKKK.
```

```xml
<Image sprite="UI:Frames/panel" mask="self"/>
```

Why `.pxl` is the default for sprite art:

- **It's text the model can write and self-check** — re-read the grid row by row, fix individual pixels; import errors carry line numbers.
- **9-slice `border:`, `ppu:`, and a `tiled: true` hint live in the file.** `tiled` auto-renders the sprite with `Image.Type.Tiled` (corners fixed, edges / center repeat) — exactly what directional borders need (vines, wood grain, chains).
- **A `.gpl` palette enforces project-wide color consistency** — edit the palette once and every `.pxl` that references it recolors.
- **It round-trips with art tools** — the `.pxl` Inspector has *Export PNG* / *Sync from PNG* (the `.pxl` text stays the source of truth for `border` / `ppu` / palette).
- **Sweet spot ≤48×48** — UI chrome, not large illustrations. Design at the smallest size that reads and let `ppu` / scaling handle display size.

> Full format, palette workflow, and pixel-art craft rules live in the **authoring-promptugui-pxl** skill.

**AA: one label gathers a whole group of SpriteSets.** Tag the SpriteSet assets with an Addressables label, and Addressables automatically pulls in the SpriteAtlases they depend on:

```csharp
// Multiple labels default to Union: the common atlas + the current-language atlas
await SpriteResolverHelpers.UseAddressableSpriteSetResolver(
    new[] { "SpriteSets-Common", $"SpriteSets-{lang}" });
```

> One label can map to multiple SpriteSets. You can `await` it (no empty-sprite flicker) or fire-and-forget (loading `<Icon>`s stay silently blank, then refresh automatically once downloaded).

---

## 6. Localization & Fonts

Always set up localization — PromptUGUI supports automatic translation, so localization is free.

Project right-click → Create → PromptUGUI → Settings, then configure which languages exist and their corresponding font types.

**Source text is the key — zero key names.** Whatever you write in XML is the msgid; in code, wrap it with `UI.Tr(...)`:

```xml
<Text>Start Game</Text>                  <!-- the text itself is the msgid, auto-extracted -->
<Text tr="false">{{playerName}}</Text>   <!-- player names etc. — not translated -->
<Btn ctx="door">Open</Btn>               <!-- ctx disambiguates "same word, different meaning" -->
```

```csharp
var label = string.Format(c, UI.Tr("Total: {0:C}"), price);   // strings in code go into the .po too
```

**Fonts go through the font types registered in Settings, not file paths.** When switching languages, font switch to its corresponding `TMP_FontAsset` automatically:

```xml
<Text font="title">Settings</Text>
```

**Routing `.po` through AA is the best practice**: run `Tools → PromptUGUI → I18n → Setup Addressables for Locale PO Files` once — it automatically tags the `.po` files with a `Locale:<locale>` label, after which the whole directory can be moved out of the Resources folder. At runtime:

```csharp
UI.Locale.Set("en");              // synchronous; shows the msgid while downloading, refreshes when done
await UI.Locale.SetAsync("en");   // waits for download + refresh to finish (use this when you need to read UI.Tr immediately after)
```

> **SpriteSets that contain text get split into per-language labels** (`SpriteSets-zh-Hans` / `SpriteSets-en`); at startup mount only the current-language one — i.e. the `$"SpriteSets-{lang}"` from §1.

---

## 7. XML Authoring Best Practices

**`<Screen>`: use `reference` + `reference.portrait` so one XML serves both landscape and portrait.** `reference` is the design resolution; the CanvasScaler switches to scale-with-screen and auto-locks the edge by orientation (lock width when W≥H, lock height when H>W). `portrait` / `landscape` are orientation variants the library **tracks automatically** (see Variant below).

```xml
<Screen name="MainMenu" reference="640x360" reference.portrait="360x640">
```

**Always lead regular content with `<SafeArea>`;** full-screen content like a background image can sit outside the SafeArea.

Wrap content in a single `<SafeArea>` and give it a `margin`; notched screens absorb that margin: actual margin = max(margin, non-safe-area space). For example: on a device with no safe area such as a PC, the margin you wrote takes effect so content isn't flush against the window edge; on a notched screen, part of the margin is absorbed by the notch automatically so the notch doesn't look oversized — decided automatically per screen orientation.

```xml
<Screen name="MainMenu" reference="640x360">
  <Image anchor="stretch" color="bg"/>      <!-- bleed background: sibling of SafeArea -->
  <SafeArea margin="6,_,6,_">
    ...content...
  </SafeArea>
</Screen>
```

**Layout tips**:

1. **Need a background? Use `<Image>` directly as the container** (it can hold children — one less layer).
2. **For toolbars / a variable number of buttons, use `anchor="top-stretch"` + `childAlign` + `spacing`** — it spans the full row, childAlign pushes everything to one side, and adding/removing buttons needs no layout changes. **Don't** write `anchor="top-right"` without a `width` (the rect collapses to 0 width and the buttons all pile up together).
3. **Use stretch for equal splits**: inside a LayoutGroup, `width="stretch"` (`stretch*2` to weight it); for free positioning use `anchor="X-stretch"` + margin, or `width="50%"`.

```xml
<HStack anchor="top-stretch" height="24" margin="_,6,_,_"
        spacing="4" childAlign="middle-right">
  <Btn size="22x22" sprite="UI:Button-Small">
    <Icon anchor="center" name="Solar16Bold:Settings, Fine Tuning/Settings" size="16x16"/>
  </Btn>
</HStack>
```

**Reuse = `<Template>`.** Pull repeated structure into a template: `<Param>` takes parameters, `{{var}}` does string substitution, `<Slot/>` receives children. Expansion happens at parse time (template invocations are invisible at runtime — you use them just like a built-in tag).

```xml
<Template name="IconTab">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="isOn" default="false"/>
  <Tab text="{{text}}" icon="{{icon}}" isOn="{{isOn}}"/>
</Template>

<TabBar id="topbar" itemTemplate="IconTab">
  <IconTab text="Power" icon="Solar16Bold:Security/Shield Minimalistic" isOn="true"/>
  <IconTab text="Win Rate" icon="Solar16Bold:Business"/>
</TabBar>
```

**Need "behavior" = a Control subclass (C#).** Templates only reuse visuals/layout (no code); when you need a new component or new interaction, subclass `Control`, override `OnAttached`, and expose properties with `[UIAttr]`.
**Hard rule: `[UIAttr]` / `[Bind]` must be paired with `[Preserve]`** — otherwise under IL2CPP (Medium+ stripping) the property silently fails in the Player build, with no error.

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Registry;

public sealed class Badge : Control
{
    private UnityEngine.UI.Image _img;
    public override void OnAttached()
        => _img = GameObject.GetComponent<UnityEngine.UI.Image>()
               ?? GameObject.AddComponent<UnityEngine.UI.Image>();

    [UIAttr(IsColor = true), Preserve]      // ← both attributes are required
    public string Color { set => _img.color = UI.Theme.Resolve(value); }
}
// UI.Registry.Register<Badge>("Badge", optionalPrefab: null);
```

**Variant: switch layout at runtime without rebuilding GameObjects.** On the C# side, `UI.Variants.Set("mobile", true)` toggles it; suffix any attribute with `.variantName` to override it. Switching only re-applies attribute values (subscriptions and references all survive unchanged).

```xml
<VStack anchor="center" size="480x320"
        anchor.mobile="bottom-stretch"
        size.mobile="" height.mobile="400" margin.mobile="_,16,80,16">
```

- To **insert elements** per variant, use `<Variant when="mobile"><Add into="#id">...</Add></Variant>` (there's no Remove/Replace; to hide something write `hidden.mobile="true"`).
- **Reserved variant names**: `portrait` / `landscape` (orientation, auto-tracked) and `<locale>` (e.g. `sprite.zh-Hans`) are reserved variants the library sets True/False automatically.

**`tint="linear"`: full-range tinting for pixel art.** Draw the sprite you want to tint **in grayscale up front** (128 gray is neutral) and blend with Linear Light at runtime — this can both darken and brighten, turning one grayscale sprite into a whole palette. The default `multiply` can only darken.

```xml
<Image sprite="UI:TabBar-Frame" color="primary-light" tint="linear"/>
```

**For rounded corners / avatar clipping, use `mask="self"`** — the Image's own sprite becomes the clip shape, keeping content from spilling past the rounded border:

```xml
<Image sprite="UI:Frame-Mask" anchor="stretch" mask="self">
  <Image id="avatar" anchor="stretch" margin="3" color="primary"/>
</Image>
```

> 💡 After writing any `.ui.xml`, run the lint CLI (`dotnet run --project .lint/UIXmlLint -- <file>`): it escalates illegal `anchor`/`margin` on layout-group children into errors, which are harder to miss than Unity's warnings.

---

## 8. Modal Dialogs

**MessageBox is async and blocking — `await` the result directly:**

```csharp
using PromptUGUI.Application.Modals;

var r = await MessageBox.Open(UI.Tr("Save changes?"), MsgBtn.Yes | MsgBtn.No | MsgBtn.Cancel);
if (r == MsgBtn.Yes) await game.SaveAsync();
```

**Custom appearance: `MessageBox.XmlSrc = "..."` (set once in §1).** A modal is fundamentally just a regular `<Screen>` — anchor / margin / Variant / locale all work as usual. **Required** preconditions (otherwise it throws at runtime):

- The **`<Screen name="...">` `name` in the file must be byte-for-byte equal to `XmlSrc`**.

Custom XML must carry these fixed ids: `text` / `title` / `ok` / `cancel` / `yes` / `no` / `close` (`icon` is optional).

**Loading overlay**: non-interactive, closed by your own code, idempotent:

```csharp
var loading = Loading.Open(UI.Tr("Loading..."));
try { await DoWorkAsync(); }
finally { loading.Close(); }
```

**Queued vs stacked** (the `mode` parameter):

- `ModalMode.Popup` (default) — stacks immediately on top of the current dialog; use it for "open another confirm box from inside a popup".
- `ModalMode.Queued` — blocks until the entire dialog stack is empty (no other modal open) before showing; multiple Queued modals pop in FIFO order, avoiding mutual overlap. **Note: calling this mode twice with `await` will deadlock.**

---

## 9. Animation (Optional)

**Entrance animation: wrap an element in `<Animation>`; with `on="open"` it plays automatically.**

```xml
<Animation type="fadein" duration="0.3s">
  <Text>Welcome</Text>
</Animation>
```

Stagger menu items (v1 has no stagger sugar — write multiple siblings with increasing `delay`):

```xml
<VStack>
  <Animation type="slidein-left" delay="0.0s"><Btn>Start</Btn></Animation>
  <Animation type="slidein-left" delay="0.05s"><Btn>Settings</Btn></Animation>
  <Animation type="slidein-left" delay="0.10s"><Btn>Quit</Btn></Animation>
</VStack>
```

**Button feel**: use the preset `type="pulse"` with `on="click@<id>"` for click feedback (`<Animation>` also supports low-level `translate`/`scale`/`rotate`/`fade` combos + various easings):

```xml
<Animation type="pulse" on="click@buy">
  <Btn id="buy">Buy</Btn>
</Animation>
```

> On the C# side you can subscribe to `Get<Trigger>("x").OnFire`, or call `Get<Animation>("x").Fire()` to trigger manually (`on="manual"` / replay the entrance animation).
