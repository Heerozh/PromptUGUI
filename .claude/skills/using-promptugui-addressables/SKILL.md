---
name: using-promptugui-addressables
description: Use when integrating PromptUGUI with Unity Addressables — loading `.ui.xml` via `UI.UseAddressableResolver` / `AssetReferenceT<TextAsset>`, `.po` translations via `UI.Locale.UseAddressableResolver` and `Locale:<locale>` labels, or icon SpriteAtlases via `SpriteResolverHelpers.UseAddressableSpriteSetResolver`. Requires `com.unity.addressables` ≥ 1.0 in the project (gated by the `PROMPTUGUI_HAS_ADDRESSABLES` compile symbol).
---

# Using PromptUGUI with Addressables

When your project ships content via Unity Addressables instead of Resources, PromptUGUI exposes parallel APIs for the three asset categories it touches: `.ui.xml` documents, `.po` translation tables, and icon SpriteAtlases.

All APIs in this skill are gated by the `PROMPTUGUI_HAS_ADDRESSABLES` compile symbol — they only exist when `com.unity.addressables` ≥ 1.0 is installed in the project. If the package is missing, the methods below won't compile; either install the package or use the Resources-backed equivalents documented in **scripting-promptugui-csharp**.

For the XML markup and Resources-backed C# wire-up, see **authoring-promptugui-xml** and **scripting-promptugui-csharp** respectively. This skill only covers the Addressables-specific deltas.

## `.ui.xml` via Addressables

Prefer a serialized `AssetReferenceT<TextAsset>` field (so authors drag the asset in the Inspector instead of typing a key):

```csharp
[SerializeField] AssetReferenceT<TextAsset> mainMenuXml;
// ...
UI.UseAddressableResolver();
await UI.LoadDocumentAsync(mainMenuXml);                   // forwards AssetGUID to the string pipeline
UI.Open("MainMenu");
```

Or load via key:

```csharp
UI.UseAddressableResolver();
await UI.LoadDocumentAsync("UI/screens/MainMenu.ui.xml");   // src = Addressable key; enables hot-reload
UI.Open("MainMenu");
```

In Editor, saving a `.ui.xml` that's registered with Addressables auto-triggers hot-reload (same as the Resources path). Player builds load via the Addressables catalog.

Both `UI.UseAddressableResolver` and the `LoadDocumentAsync(AssetReferenceT<TextAsset>)` overload only exist when `PROMPTUGUI_HAS_ADDRESSABLES` is defined.

## `.po` (i18n) via Addressables

Call `UI.Locale.UseAddressableResolver()` at boot. The resolver loads every TextAsset whose Addressables **label is `Locale:<locale>`** — so `UI.Locale.Set("zh-Hans")` loads every asset labelled `Locale:zh-Hans`. Files can live anywhere in the project.

```csharp
UI.Locale.UseAddressableResolver();
UI.Locale.Set("zh-Hans");                  // sync; UI briefly shows msgid during download
// or:
await UI.Locale.SetAsync("zh-Hans");       // awaits download + parse + ReSolve
```

`Locale.Set` returns immediately after issuing the load. While the download is in flight, open Screens briefly fall back to msgid text; when the load completes the locale variant flips on and all open Screens re-resolve to the translated strings. **`SetAsync` returns only after that re-resolve completes** — use it when you need to read `UI.Tr(...)` immediately after switching locales.

### One-shot label setup

Run `Tools → PromptUGUI → I18n → Setup Addressables for Locale PO Files`. The menu:

1. Scans every `.po` in the project.
2. For each one whose parent folder matches a `PromptUGUISettings.locales[].locale` entry (e.g. `Assets/Localization/zh-Hans/main.po`), applies the `Locale:<locale>` label.
3. Scrubs any stale `Locale:*` label left over from a previous folder location.

Non-`Locale` labels you've set yourself (e.g. `UI`, `Stage:1-1`) are preserved.

## Icon atlases via Addressables

Tag your SpriteSet assets in Addressables with a label (default: `SpriteSets`). Addressables auto-pulls each referenced SpriteAtlas as a dependency.

```csharp
// Default label "SpriteSets"
await SpriteResolverHelpers.UseAddressableSpriteSetResolver();

// Custom label
await SpriteResolverHelpers.UseAddressableSpriteSetResolver("MyIcons");

// Multiple labels — OR (Union, default): every SpriteSet tagged with "core" OR "mobile"
await SpriteResolverHelpers.UseAddressableSpriteSetResolver(
    new[] { "core", "mobile" });

// AND (Intersection): only SpriteSets tagged with BOTH "core" AND "mobile"
await SpriteResolverHelpers.UseAddressableSpriteSetResolver(
    new[] { "core", "mobile" },
    UnityEngine.AddressableAssets.Addressables.MergeMode.Intersection);
```

Returns `Awaitable`. You can either `await` it (no flash of empty icons) or fire-and-forget (`_ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(); UI.Open("MainMenu");`) — between the call and the await continuation `UI.IsSpriteResolverLoadInFlight` is `true`, so any `<Icon>` rendered in that window stays empty silently (no `LogError`) and is re-resolved automatically via a `VariantStore` broadcast once the download completes. Use the awaited form if a one-frame empty icon would be visible in your golden path (e.g. boot splash → main menu with no intermediate loader).

### Sprite handle lifecycle

The loaded handle is held static and **released on a second `UseAddressableSpriteSetResolver` call** (label swap, reset). `Sprite` references returned from `UI.SpriteResolver` are only valid while the current handle is held — releasing the handle unloads the underlying `SpriteAtlas`.

**Do not cache** the returned `Sprite` in your own fields across such calls; resolve via `UI.SpriteResolver` (or rely on `<Icon name>` re-resolving on Variant changes) each time you need it.

## Common mistakes

| Symptom                                                | Cause                                                                                | Fix                                                                                          |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------- |
| `UseAddressableResolver` doesn't exist                 | `com.unity.addressables` not installed (no `PROMPTUGUI_HAS_ADDRESSABLES`)            | Install the Addressables package, or use `UI.UseResourcesResolver(...)` instead              |
| `<Icon>` shows pink right after `Locale.Set` swap      | Old SpriteSet handle was released, new one still downloading                           | Fire-and-forget is safe — `UI.IsSpriteResolverLoadInFlight` keeps Icons silent + auto re-resolve on completion. `await` only if a one-frame empty Icon would be visible. |
| `UI.SpriteResolver is not registered` LogError spam    | A Screen was opened before any `UseAddressableSpriteSetResolver` / `UseSpriteSetResolver` call (no in-flight load to silence the warning either) | Call `UseAddressableSpriteSetResolver(...)` (sync prefix sets in-flight) BEFORE `UI.Open`; you do not need to `await` it.   |
| Translated text doesn't appear until next frame        | `Locale.Set` returned before `.po` finished loading                                  | Use `await UI.Locale.SetAsync(...)` when you need to read `UI.Tr(...)` synchronously after   |
| `.po` files not picked up                              | Files don't carry the `Locale:<locale>` label, or the label points at the wrong locale | Run `Tools → PromptUGUI → I18n → Setup Addressables for Locale PO Files`, or set labels manually |
| Cached `Sprite` field becomes invalid after label swap | Sprite was captured in a user field across a `UseAddressable...Resolver` call        | Don't cache — go through `UI.SpriteResolver` each time, or re-resolve on `UI.Variants.Changed` |

## Quick reference (cheatsheet)

```
PREREQ        com.unity.addressables ≥ 1.0   (defines PROMPTUGUI_HAS_ADDRESSABLES)

.ui.xml       UI.UseAddressableResolver()
              await UI.LoadDocumentAsync(assetRef)        AssetReferenceT<TextAsset>
              await UI.LoadDocumentAsync("UI/screens/X.ui.xml")    key form
              hot-reload supported in Editor

.po (i18n)    UI.Locale.UseAddressableResolver()
              UI.Locale.Set("zh-Hans")                    sync; msgid fallback briefly
              await UI.Locale.SetAsync("zh-Hans")         awaits download + ReSolve
              label convention: Locale:<locale>
              one-shot setup: Tools → PromptUGUI → I18n → Setup Addressables for Locale PO Files

Icons         SpriteResolverHelpers.UseAddressableSpriteSetResolver()      fire-and-forget OK
              SpriteResolverHelpers.UseAddressableSpriteSetResolver("MyLabel")
              SpriteResolverHelpers.UseAddressableSpriteSetResolver(
                    labels, MergeMode.Union | Intersection)
              must be called BEFORE UI.Open (synchronous prefix flips
              UI.IsSpriteResolverLoadInFlight so in-flight Icons stay silent);
              await only if a one-frame empty Icon would be visible
              handle released on next call → invalidates returned Sprite refs
```
