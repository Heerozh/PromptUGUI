---
name: using-promptugui-markdown
description: Use when enabling PromptUGUI's <Markdown> control — installing Markdig and the PROMPTUGUI_HAS_MARKDIG gate. Without Markdig the control shows raw text.
---

# Markdown support (Markdig)

`<Markdown>` renders Markdown documents via Markdig, a soft dependency gated by the `PROMPTUGUI_HAS_MARKDIG` compile symbol. The control class is always compiled and the XML tag is always valid; Markdig is only required for actual Markdown rendering. Without it, the control displays the raw Markdown source as plain text and emits a one-time `Debug.LogWarning`.

For the XML markup, see **authoring-promptugui-xml** → `reference/controls-markdown.md`. For the C# API (`Text`, `BindText`, `OnLinkClicked`, `Style`, `ImageResolver`), see **scripting-promptugui-csharp → Markdown**.

---

## Why a separate install step?

Markdig is not a UPM package, so it cannot be declared in `Packages/manifest.json` and Unity's `versionDefines` mechanism cannot auto-detect it. Instead:

1. You install Markdig as a DLL (via NuGetForUnity or by dropping the file manually).
2. The editor auto-detector (`PromptUGUI.Editor.MarkdigDetector`) finds the Markdig assembly in the loaded `AppDomain` and automatically sets the `PROMPTUGUI_HAS_MARKDIG` scripting define symbol.
3. That symbol gates the `PromptUGUI.Markdown` asmdef, which compiles `MarkdigRenderer` and auto-registers it with `UI.Markdown.Renderer`. The `<Markdown>` control immediately starts rendering.

The process is fully automatic once Markdig is in the project — no manual symbol editing is required.

---

## Installation

### Option A: NuGetForUnity (recommended)

1. Install NuGetForUnity in your Unity project (available on GitHub or the Asset Store).
2. In Unity: **NuGet → Manage NuGet Packages → search "Markdig" → Install**.
   - Both **Markdig** and **Markdig.Signed** work. The host project here uses **Markdig.Signed**; the namespace is `Markdig` in both cases, so the code is identical.
3. NuGetForUnity places `Markdig.dll` (netstandard2.0) under `Assets/Packages/Markdig/` (or similar).
4. Unity compiles, the editor auto-detector runs, `PROMPTUGUI_HAS_MARKDIG` is added, and `MarkdigRenderer` is registered.

### Option B: Manual DLL drop

1. Obtain `Markdig.dll` for the **netstandard2.0** target framework from the [Markdig NuGet page](https://www.nuget.org/packages/Markdig) or [GitHub releases](https://github.com/xoofx/markdig/releases).
2. Drop the file anywhere under `Assets/` (e.g. `Assets/Plugins/Markdig.dll`).
3. Unity imports it; the editor auto-detector finds it on the next domain reload and defines `PROMPTUGUI_HAS_MARKDIG`.

### Manual symbol (fallback / CI)

If the auto-detector does not fire (e.g., unusual CI environment), define `PROMPTUGUI_HAS_MARKDIG` manually in **Project Settings → Player → Scripting Define Symbols** for each build target where Markdig is present.

---

## What the auto-detector does

`PromptUGUI.Editor.MarkdigDetector` runs as an `[InitializeOnLoadMethod]` in the Editor. It scans `AppDomain.CurrentDomain.GetAssemblies()` for an assembly named `Markdig` (or `Markdig.Signed`):

- **Found**: adds `PROMPTUGUI_HAS_MARKDIG` to `PlayerSettings` Scripting Define Symbols for the active build target → triggers a recompile.
- **Not found**: removes `PROMPTUGUI_HAS_MARKDIG` if present → triggers a recompile.

The detector runs on every domain reload (domain load after script compile, entering Play mode, etc.), so installing or removing Markdig is picked up automatically without any manual action.

---

## What lights up when the symbol is defined

| Component | Location | Behavior |
|---|---|---|
| `MarkdigRenderer` | `Runtime/MarkdigBackend/` (gated asmdef `PromptUGUI.Markdown`) | Walks Markdig AST → `ElementNode` IR subtree + `ImageRequest[]`. Registered with `UI.Markdown.Renderer` at domain load and after each `UI.ResetForTests()`. |
| `PromptUGUI.Markdown` asmdef | `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef` | `defineConstraints: ["PROMPTUGUI_HAS_MARKDIG"]`, `precompiledReferences: ["Markdig.dll"]`, references `PromptUGUI.Runtime`. Compiled only when the symbol is defined. |
| `PromptUGUI.Tests.EditMode.Markdown` asmdef | `Tests/EditMode/Markdown/` | Same `defineConstraints`. Contains renderer tree-shape tests and control integration tests. |

---

## IL2CPP / managed stripping

Markdig is pure managed code with no unsafe blocks or P/Invoke. IL2CPP compiles it normally. However, the Unity linker's managed stripping may remove Markdig types that are only referenced through reflection if stripping is set to Medium or High.

**If you observe `<Markdown>` falling back to plain-text mode in an IL2CPP build** (the `Debug.LogWarning` fires because `UI.Markdown.Renderer` is null), add a `link.xml` to preserve the Markdig assembly:

```xml
<!-- Assets/link.xml -->
<linker>
  <assembly fullname="Markdig" preserve="all"/>
  <!-- If using the signed variant: -->
  <assembly fullname="Markdig.Signed" preserve="all"/>
</linker>
```

Place this file anywhere under `Assets/`. Unity's linker picks up every `link.xml` in the project automatically. `preserve="all"` keeps every type and member in the assembly regardless of reference analysis.

---

## Quick reference

```
INSTALL       NuGetForUnity: NuGet → Manage Packages → Markdig → Install
              or: drop Markdig.dll (netstandard2.0) under Assets/

SYMBOL        PROMPTUGUI_HAS_MARKDIG
              auto-defined by Editor/MarkdigDetector.cs when Markdig DLL is in the domain
              manual fallback: Project Settings → Player → Scripting Define Symbols

RENDERER      UI.Markdown.Renderer                 auto-set by MarkdigBootstrap
              (domain load + UI.OnReset; null without Markdig → plain-text fallback)

FALLBACK      UI.Markdown.Renderer == null
              → raw text in a <Text wrap> node + one-time Debug.LogWarning

IL2CPP        Add link.xml preserving "Markdig" (or "Markdig.Signed") if stripping
              causes the renderer to become null in IL2CPP builds
```
