# `<Markdown>` Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a built-in `<Markdown>` control that renders Markdown text into a scrollable uGUI subtree built from existing primitives, with Markdig as a symbol-gated soft dependency.

**Architecture:** Block-level rendering. A Runtime `Markdown : Control` always compiles; it owns a vertical `ScrollRect` and delegates parsing to an injectable `IMarkdownRenderer` that turns Markdown into an `ElementNode` IR tree, which is instantiated via `UI.GetInstantiator().InstantiateNode` into the scroll content. The Markdig-backed renderer lives in a separate `PromptUGUI.Markdown` asmdef gated by `PROMPTUGUI_HAS_MARKDIG` (set by an editor auto-detector); without it the control degrades to raw text. Images are delegated to an injectable `Func<string, Awaitable<Texture2D>>` resolver (built-in `UnityWebRequestTexture` helper). Links fire `OnLinkClicked`.

**Tech Stack:** Unity 6 uGUI + TextMeshPro, R3 (Cysharp), Markdig (NuGet, BSD-2), Unity `Awaitable`.

**Spec:** `docs~/superpowers/specs/2026-06-09-markdown-control-design.md` (decisions MD-D1..MD-D24).

**Branch:** `feat/markdown-control` (already created; never commit to `main`).

**Testing:** Always via UnityMCP (CLAUDE.md). After creating/editing source, run:
`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` then
`mcp__UnityMCP__read_console(action="get", types=["error"])` before running tests. Filter a class with `filter="ClassName"`.

---

## File Structure

**Runtime (always compiles, no Markdig reference):**
- `Runtime/Markdown/MarkdownStyle.cs` — style POCO + `CreateDefault()` + `Clone()`.
- `Runtime/Markdown/IMarkdownRenderer.cs` — `IMarkdownRenderer`, `MarkdownRenderResult`, `ImageRequest`.
- `Runtime/Application/UI.Markdown.cs` — `UI.Markdown` facade (Renderer / DefaultStyle / ImageResolver / UseWebImageResolver / reset).
- `Runtime/Controls/Markdown.cs` — the control.
- `Runtime/Controls/Internal/MarkdownLinkClicker.cs` — per-TMP click → link.
- `Runtime/Core/Lint/MarkdownRules.cs` — `PUI-MARKDOWN-NO-CHILDREN`.
- Edits: `Runtime/Application/BuiltinPrimitives.cs` (register), `Runtime/Application/UI.cs` (`OnReset` public + reset wiring), `Runtime/Core/Lint/IRWalker.cs` (dispatch), `Runtime/AssemblyInfo.cs` (InternalsVisibleTo for the gated test asmdef).

**Markdig backend (gated by `PROMPTUGUI_HAS_MARKDIG`):**
- `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef` — defineConstraint + `Markdig.dll` precompiled ref.
- `Runtime/MarkdigBackend/MarkdigRenderer.cs` — AST → IR.
- `Runtime/MarkdigBackend/MarkdigBootstrap.cs` — self-register + OnReset re-inject.

**Editor:**
- `Editor/MarkdigDetector.cs` — auto-define the symbol when Markdig is present.

**Tests:**
- `Tests/EditMode/Controls/MarkdownControlTests.cs` — Phase A (main `PromptUGUI.Tests.EditMode` asmdef; uses a fake renderer).
- `Tests/EditMode/Markdown/PromptUGUI.Tests.EditMode.Markdown.asmdef` — gated test asmdef.
- `Tests/EditMode/Markdown/MarkdigRendererTests.cs`, `MarkdownIntegrationTests.cs` — Phase B/C.
- `Tests/PlayMode/MarkdownWebImageTests.cs` — web resolver fetch.

**Docs:** `.claude/skills/authoring-promptugui-xml/{SKILL.md,reference/controls-markdown.md}`, `.claude/skills/scripting-promptugui-csharp/SKILL.md`, `.claude/skills/using-promptugui-markdown/SKILL.md`, main spec `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`, XSD.

---

# Phase A — Runtime control (no Markdig; testable with a fake renderer)

## Task 1: Style POCO + renderer interface

**Files:**
- Create: `Runtime/Markdown/MarkdownStyle.cs`
- Create: `Runtime/Markdown/IMarkdownRenderer.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Controls/MarkdownControlTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownStyleTests
    {
        [Test]
        public void CreateDefault_has_six_heading_sizes_descending()
        {
            var s = MarkdownStyle.CreateDefault();
            Assert.AreEqual(6, s.HeadingSizes.Length);
            Assert.Greater(s.HeadingSizes[0], s.HeadingSizes[5]);
            Assert.IsTrue(s.ParagraphWrap);
        }

        [Test]
        public void Clone_is_deep_for_heading_array()
        {
            var a = MarkdownStyle.CreateDefault();
            var b = a.Clone();
            b.HeadingSizes[0] = 999f;
            Assert.AreNotEqual(999f, a.HeadingSizes[0]);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `mcp__UnityMCP__refresh_unity(...)` then `mcp__UnityMCP__read_console(action="get", types=["error"])`
Expected: compile error — `MarkdownStyle` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/Markdown/MarkdownStyle.cs`:

```csharp
namespace PromptUGUI
{
    /// <summary>Visual config for &lt;Markdown&gt; rendering. Plain POCO; colors are theme tokens / hex
    /// resolved via UI.Theme.Resolve, fonts are font-type names resolved via FontApplier.</summary>
    public sealed class MarkdownStyle
    {
        public float[] HeadingSizes = { 32f, 28f, 24f, 20f, 18f, 16f };
        public float BodySize = 16f;
        public string BodyFont = "default";
        public string CodeFont = "default";
        public string LinkColor = "#4EA1FF";
        public string CodeBackground = "#00000020";
        public string QuoteBarColor = "#888888";
        public float BlockSpacing = 8f;
        public float ListIndent = 24f;
        public string BulletGlyph = "•";     // •
        public string CheckedGlyph = "☑";    // ☑
        public string UncheckedGlyph = "☐";  // ☐
        public string HrColor = "#888888";
        public float HrThickness = 2f;
        public bool ParagraphWrap = true;

        public static MarkdownStyle CreateDefault() => new();

        public MarkdownStyle Clone()
        {
            var c = (MarkdownStyle)MemberwiseClone();
            c.HeadingSizes = (float[])HeadingSizes.Clone();
            return c;
        }
    }
}
```

Create `Runtime/Markdown/IMarkdownRenderer.cs`:

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI
{
    /// <summary>Turns Markdown source into a PromptUGUI IR tree. Implemented by the Markdig backend
    /// (gated asmdef) and injected into <see cref="PromptUGUI.Application.UI.Markdown.Renderer"/>.</summary>
    public interface IMarkdownRenderer
    {
        MarkdownRenderResult Render(string markdown, MarkdownStyle style);
    }

    public sealed class MarkdownRenderResult
    {
        /// <summary>Root block container (a VStack node) holding all rendered blocks.</summary>
        public ElementNode Root;
        /// <summary>Block-level images to load asynchronously after instantiation.</summary>
        public IReadOnlyList<ImageRequest> Images;
    }

    public readonly struct ImageRequest
    {
        public readonly string NodeId;  // id of the RawImage node in the tree (control resolves via Get)
        public readonly string Url;     // passed to the image resolver
        public readonly string Alt;     // alt / placeholder text

        public ImageRequest(string nodeId, string url, string alt)
        {
            NodeId = nodeId;
            Url = url;
            Alt = alt;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `mcp__UnityMCP__refresh_unity(...)`, `read_console`, then
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MarkdownStyleTests")`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Markdown/ Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): style POCO + IMarkdownRenderer interface"
```

---

## Task 2: `UI.Markdown` facade + reset wiring + public `OnReset`

**Files:**
- Create: `Runtime/Application/UI.Markdown.cs`
- Modify: `Runtime/Application/UI.cs` (make `OnReset` public; call `Markdown.ResetForTestsInternal()`)
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MarkdownControlTests.cs` (new class):

```csharp
using PromptUGUI.Application;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class UIMarkdownFacadeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void DefaultStyle_is_non_null_after_reset()
        {
            Assert.IsNotNull(UI.Markdown.DefaultStyle);
        }

        [Test]
        public void Reset_clears_ImageResolver_and_Renderer()
        {
            UI.Markdown.ImageResolver = _ => null;
            UI.Markdown.Renderer = null; // simulate absence
            UI.ResetForTests();
            Assert.IsNull(UI.Markdown.ImageResolver);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: compile error — `UI.Markdown` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/Application/UI.Markdown.cs`:

```csharp
using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>Markdown rendering facade. The Markdig backend (gated asmdef) injects
        /// <see cref="Renderer"/> at domain load and re-injects on every <see cref="UI.OnReset"/>.</summary>
        public static class Markdown
        {
            public static IMarkdownRenderer Renderer { get; set; }
            public static MarkdownStyle DefaultStyle { get; set; } = MarkdownStyle.CreateDefault();
            public static Func<string, Awaitable<Texture2D>> ImageResolver { get; set; }

            internal static void ResetForTestsInternal()
            {
                Renderer = null;            // re-injected by the gated asmdef via UI.OnReset (end of reset)
                DefaultStyle = MarkdownStyle.CreateDefault();
                ImageResolver = null;
            }
        }
    }
}
```

In `Runtime/Application/UI.cs`: change `internal static event System.Action OnReset;` to `public static event System.Action OnReset;`. In `UI.ResetForTests()`, after the line `Theme.ResetForTestsInternal();` add:

```csharp
            Markdown.ResetForTestsInternal();
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="UIMarkdownFacadeTests")`. Expected: 2 PASS. Also run `filter="MarkdownStyleTests"` to confirm no regression.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.Markdown.cs Runtime/Application/UI.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): UI.Markdown facade + public OnReset + reset wiring"
```

---

## Task 3: `Markdown` control scaffold + registration

**Files:**
- Create: `Runtime/Controls/Markdown.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

- [ ] **Step 1: Write the failing test**

Append (new class):

```csharp
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownControlScaffoldTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md' anchor='stretch'/></Screen></PromptUGUI>";

        [Test]
        public void Markdown_tag_is_registered()
        {
            Assert.IsTrue(UI.Registry.Has("Markdown"));
        }

        [Test]
        public void Markdown_builds_scrollrect_with_viewport()
        {
            UI.LoadDocument("t", Xml);
            var screen = UI.Open("S");
            var md = screen.Get<Markdown>("md");
            var scroll = md.GameObject.GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll);
            Assert.IsFalse(scroll.horizontal);
            Assert.IsTrue(scroll.vertical);
            Assert.IsNotNull(md.GameObject.transform.Find("Viewport"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: compile error — `Markdown` control type does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/Controls/Markdown.cs` (scaffold only; rendering added in Task 4):

```csharp
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Markdown : Control
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;

        public override void OnAttached()
        {
            _viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            _viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = GameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.viewport = _viewport;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;
        }

        protected internal override Transform ChildHostTransform => _viewport;
    }
}
```

In `Runtime/Application/BuiltinPrimitives.cs`, after the `Carousel` registration line add:

```csharp
            reg.Register<Markdown>("Markdown", null, defaultTextAttr: "text");
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdownControlScaffoldTests")`. Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Markdown.cs Runtime/Application/BuiltinPrimitives.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): control scaffold (ScrollRect+Viewport) + register builtin"
```

---

## Task 4: Render dispatch + degrade + `text` lock + style attrs

**Files:**
- Modify: `Runtime/Controls/Markdown.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

This task adds the fake-renderer test helper reused by later tasks.

- [ ] **Step 1: Write the failing test**

Append (helper + new class):

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Shared fake renderer + tree builders for Phase A control tests.
    internal sealed class FakeMarkdownRenderer : IMarkdownRenderer
    {
        public MarkdownRenderResult Result;
        public string LastMarkdown;
        public MarkdownStyle LastStyle;
        public MarkdownRenderResult Render(string md, MarkdownStyle style)
        {
            LastMarkdown = md; LastStyle = style;
            return Result ?? new MarkdownRenderResult { Root = Vs(), Images = new List<ImageRequest>() };
        }
        public static ElementNode Vs()
        {
            var n = new ElementNode("VStack");
            n.Attributes["anchor"] = "top-stretch";
            return n;
        }
        public static ElementNode Text(string id, string text)
        {
            var n = new ElementNode("Text");
            n.Id = id;
            n.Attributes["wrap"] = "true";
            n.TextContent = text;
            return n;
        }
    }

    public class MarkdownRenderDispatchTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md' anchor='stretch'/></Screen></PromptUGUI>";

        private Markdown Open()
        {
            UI.LoadDocument("t", Xml);
            return UI.Open("S").Get<Markdown>("md");
        }

        [Test]
        public void Setting_Text_with_renderer_instantiates_tree_as_scroll_content()
        {
            var fake = new FakeMarkdownRenderer();
            var root = FakeMarkdownRenderer.Vs();
            root.Children.Add(FakeMarkdownRenderer.Text("p", "hello"));
            fake.Result = new MarkdownRenderResult { Root = root, Images = new List<ImageRequest>() };
            UI.Markdown.Renderer = fake;

            var md = Open();
            md.Text = "# hello";

            Assert.AreEqual("# hello", fake.LastMarkdown);
            var scroll = md.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.IsNotNull(scroll.content, "content should be the rendered root");
            Assert.IsNotNull(scroll.content.GetComponent<UnityEngine.UI.ContentSizeFitter>());
            // dynamic ids live on the rendered root's ScopedIds (not the control's); assert via the TMP tree
            var tmp = md.GameObject.GetComponentInChildren<TMPro.TMP_Text>();
            Assert.AreEqual("hello", tmp.text);
        }

        [Test]
        public void Degrade_to_raw_text_when_no_renderer()
        {
            UI.Markdown.Renderer = null;
            var md = Open();
            md.Text = "# raw <b>";
            // one Text child under Viewport holding the raw string
            var tmp = md.GameObject.GetComponentInChildren<TMPro.TMP_Text>();
            Assert.IsNotNull(tmp);
            StringAssert.Contains("raw", tmp.text);
        }

        [Test]
        public void PeekDefaultText_reflects_runtime_text_for_lock()
        {
            // ControlAttributeApplier compares PeekDefaultText() to the last applied value on ReSolve;
            // when it differs (runtime-set), the XML-declared text is NOT re-applied (same lock as <Text>).
            // PeekDefaultText is internal — visible because Runtime exposes internals to PromptUGUI.Tests.EditMode.
            UI.Markdown.Renderer = new FakeMarkdownRenderer();
            var md = Open();
            md.Text = "runtime value";
            Assert.AreEqual("runtime value", md.PeekDefaultText());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: compile error — `Markdown.Text` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `class Markdown` (new fields + members). Add `using` directives at the top:

```csharp
using System;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using TMPro;
```

Add members:

```csharp
        private IControl _renderedRoot;
        private int _renderGen;
        private bool _applied;
        private bool _dirty;
        private string _source = "";
        private MarkdownStyle _style;

        public MarkdownStyle Style
        {
            get => _style ??= UI.Markdown.DefaultStyle.Clone();
            set { _style = value; MarkDirty(); }
        }

        [UIAttr("text"), Preserve]
        public string Text
        {
            get => _source;
            set { _source = value ?? ""; MarkDirty(); }
        }

        internal override string PeekDefaultText() => _source;

        [UIAttr, Preserve]
        public string BodyFont { set { Style.BodyFont = string.IsNullOrEmpty(value) ? "default" : value; MarkDirty(); } }

        [UIAttr, Preserve]
        public string CodeFont { set { Style.CodeFont = string.IsNullOrEmpty(value) ? "default" : value; MarkDirty(); } }

        [UIAttr(IsColor = true), Preserve]
        public string LinkColor { set { if (!string.IsNullOrEmpty(value)) Style.LinkColor = value; MarkDirty(); } }

        [UIAttr, Preserve]
        public float Spacing { set { Style.BlockSpacing = value; MarkDirty(); } }

        [UIAttr, Preserve]
        public bool Wrap { set { Style.ParagraphWrap = value; MarkDirty(); } }

        internal override void OnAfterApply()
        {
            _applied = true;
            if (_dirty) Render();
        }

        private void MarkDirty()
        {
            _dirty = true;
            if (_applied) Render();
        }

        private void Render()
        {
            _dirty = false;
            _renderGen++;
            if (_renderedRoot != null) { _renderedRoot.Dispose(); _renderedRoot = null; }
            if (string.IsNullOrEmpty(_source)) return;

            var inst = UI.GetInstantiator();
            var owner = UI.OwnerScreenOf(this);
            var renderer = UI.Markdown.Renderer;

            if (renderer == null)
            {
                Debug.LogWarning("<Markdown> needs Markdig. Install it (NuGetForUnity / DLL); the editor " +
                    "auto-defines PROMPTUGUI_HAS_MARKDIG when found. Showing raw text.");
                var raw = new ElementNode("Text");
                raw.Attributes["wrap"] = "true";
                raw.Attributes["anchor"] = "top-stretch";
                raw.Attributes["tr"] = "false";
                raw.TextContent = _source;
                _renderedRoot = inst.InstantiateNode(raw, _viewport, owner);
                SetAsContent(_renderedRoot);
                return;
            }

            var result = renderer.Render(_source, Style);
            _renderedRoot = inst.InstantiateNode(result.Root, _viewport, owner);
            SetAsContent(_renderedRoot);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_renderedRoot.RectTransform);
        }

        private void SetAsContent(IControl root)
        {
            _scroll.content = root.RectTransform;
            var csf = root.GameObject.GetComponent<ContentSizeFitter>()
                      ?? root.GameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.verticalNormalizedPosition = 1f;
        }
```

> Note: `Text`/`Btn` etc. use `[UIAttr("text")]` and `defaultTextAttr:"text"`; the DefaultText runtime lock (Control.PeekDefaultText / ControlAttributeApplier) keeps a runtime-set `Text` from being overwritten by the XML-declared value on ReSolve — that is what `PeekDefaultText` overriding `_source` enables.

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdownRenderDispatchTests")`. Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Markdown.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): render dispatch, raw-text degrade, text runtime-lock, style attrs"
```

---

## Task 5: `BindText` + `OnLinkClicked` + link clicker + `Dispose`

**Files:**
- Create: `Runtime/Controls/Internal/MarkdownLinkClicker.cs`
- Modify: `Runtime/Controls/Markdown.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

- [ ] **Step 1: Write the failing test**

Append (new class):

```csharp
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownBindAndLinkTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); UI.Markdown.Renderer = new FakeMarkdownRenderer(); }
        [TearDown] public void TearDown() => UI.ResetForTests();

        const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md' anchor='stretch'/></Screen></PromptUGUI>";

        private Markdown Open() { UI.LoadDocument("t", Xml); return UI.Open("S").Get<Markdown>("md"); }

        [Test]
        public void BindText_pushes_value_into_Text()
        {
            var md = Open();
            var subject = new Subject<string>();
            md.BindText(subject);
            subject.OnNext("from stream");
            Assert.AreEqual("from stream", md.Text);
        }

        [Test]
        public void OnLinkClicked_fires_via_test_seam()
        {
            var md = Open();
            string got = null;
            md.OnLinkClicked.Subscribe(u => got = u);
            md.RaiseLinkClickedForTests("https://x.test");
            Assert.AreEqual("https://x.test", got);
        }

        [Test]
        public void Link_clicker_attached_to_rendered_texts()
        {
            var fake = (FakeMarkdownRenderer)UI.Markdown.Renderer;
            var root = FakeMarkdownRenderer.Vs();
            root.Children.Add(FakeMarkdownRenderer.Text("p", "<link=\"u\">x</link>"));
            fake.Result = new MarkdownRenderResult { Root = root, Images = new System.Collections.Generic.List<ImageRequest>() };

            var md = Open();
            md.Text = "x";
            var tmp = md.GameObject.GetComponentInChildren<TMPro.TMP_Text>();
            Assert.IsNotNull(tmp.GetComponent<PromptUGUI.Controls.Internal.MarkdownLinkClicker>());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: compile error — `BindText`/`OnLinkClicked`/`MarkdownLinkClicker` not found.

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/Controls/Internal/MarkdownLinkClicker.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>Added to each rendered TMP_Text; on click resolves the intersecting TMP &lt;link&gt;
    /// and reports its id (the URL) to the owning Markdown control.</summary>
    internal sealed class MarkdownLinkClicker : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text _tmp;
        private Action<string> _onLink;

        public void Init(TMP_Text tmp, Action<string> onLink)
        {
            _tmp = tmp;
            _onLink = onLink;
            _tmp.raycastTarget = true;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (_tmp == null) return;
            int idx = TMP_TextUtilities.FindIntersectingLink(_tmp, e.position, e.pressEventCamera);
            if (idx < 0) return;
            _onLink?.Invoke(_tmp.textInfo.linkInfo[idx].GetLinkID());
        }
    }
}
```

Add to `class Markdown` — a Subject, the observable, BindText, the test seam, link installation in `Render`, and Dispose. Add `using R3;` and `using PromptUGUI.Controls.Internal;` (already present). Insert fields/members:

```csharp
        private readonly Subject<string> _linkClicked = new();
        public Observable<string> OnLinkClicked => _linkClicked;
        public Func<string, Awaitable<Texture2D>> ImageResolver { get; set; }

        public IDisposable BindText(Observable<string> source) => source.Subscribe(s => Text = s);

        internal void RaiseLinkClickedForTests(string url) => _linkClicked.OnNext(url);

        private void InstallLinkClickers(IControl root)
        {
            foreach (var tmp in root.GameObject.GetComponentsInChildren<TMP_Text>(true))
            {
                var clicker = tmp.gameObject.GetComponent<MarkdownLinkClicker>()
                              ?? tmp.gameObject.AddComponent<MarkdownLinkClicker>();
                clicker.Init(tmp, url => _linkClicked.OnNext(url));
            }
        }

        public override void Dispose()
        {
            _renderGen++;
            _linkClicked.Dispose();
            if (_renderedRoot != null) { _renderedRoot.Dispose(); _renderedRoot = null; }
            base.Dispose();
        }
```

In `Render()`, after the renderer (non-degrade) branch's `SetAsContent(_renderedRoot);` line, add:

```csharp
            InstallLinkClickers(_renderedRoot);
```

(Place it before the `LayoutRebuilder.ForceRebuildLayoutImmediate` call.)

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdownBindAndLinkTests")`. Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Internal/MarkdownLinkClicker.cs Runtime/Controls/Markdown.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): BindText, OnLinkClicked + per-TMP link clicker, Dispose"
```

---

## Task 6: Async image loading (`LoadImageAsync` + `_renderGen` guard)

**Files:**
- Modify: `Runtime/Controls/Markdown.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

- [ ] **Step 1: Write the failing test**

Append (new class):

```csharp
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownImageTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md' anchor='stretch'/></Screen></PromptUGUI>";

        [Test]
        public void Image_request_resolves_texture_into_rawimage()
        {
            var fake = new FakeMarkdownRenderer();
            var root = FakeMarkdownRenderer.Vs();
            var img = new ElementNode("RawImage");
            img.Id = "img0";
            img.Attributes["type"] = "contain";
            img.Attributes["width"] = "stretch";
            img.Attributes["height"] = "100";
            root.Children.Add(img);
            fake.Result = new MarkdownRenderResult
            {
                Root = root,
                Images = new System.Collections.Generic.List<ImageRequest> { new ImageRequest("img0", "u", "alt") }
            };
            UI.Markdown.Renderer = fake;

            var tex = new Texture2D(2, 2);
            UI.Markdown.ImageResolver = _ => Completed(tex);

            UI.LoadDocument("t", Xml);
            var md = UI.Open("S").Get<Markdown>("md");
            md.Text = "![alt](u)";

            // resolver is synchronously completed -> texture applied (assert on the uGUI component)
            var raw = md.GameObject.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            Assert.AreEqual(tex, raw.texture);
        }

        private static Awaitable<Texture2D> Completed(Texture2D t)
        {
            var s = new AwaitableCompletionSource<Texture2D>();
            s.SetResult(t);
            return s.Awaitable;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: FAIL — texture not applied (no LoadImageAsync yet). (Compiles because `ImageResolver` already exists from Task 5.)

- [ ] **Step 3: Write minimal implementation**

Add to `class Markdown`:

```csharp
        private async Awaitable LoadImageAsync(int gen, ImageRequest req)
        {
            var resolver = ImageResolver ?? UI.Markdown.ImageResolver;
            if (resolver == null) return;   // alt placeholder stays
            Texture2D tex;
            try { tex = await resolver(req.Url); }
            catch (Exception e)
            {
                Debug.LogWarning($"<Markdown> image '{req.Url}' failed: {e.Message}");
                return;
            }
            if (gen != _renderGen || tex == null || _renderedRoot == null) return;   // stale / failed
            RawImage img;
            try { img = _renderedRoot.Get<RawImage>(req.NodeId); }
            catch { return; }
            img.Texture = tex;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_renderedRoot.RectTransform);
        }
```

In `Render()` (non-degrade branch), after `InstallLinkClickers(_renderedRoot);` add:

```csharp
            if (result.Images != null)
                foreach (var req in result.Images)
                    _ = LoadImageAsync(_renderGen, req);
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdownImageTests")`. Expected: 1 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Markdown.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): async image loading with render-generation guard"
```

---

## Task 7: Lint `PUI-MARKDOWN-NO-CHILDREN`

**Files:**
- Create: `Runtime/Core/Lint/MarkdownRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs`

- [ ] **Step 1: Write the failing test**

Append (new class):

```csharp
using System.Linq;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownLintTests
    {
        [Test]
        public void Child_elements_under_markdown_warn()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md'><Text>x</Text></Markdown></Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == MarkdownRules.NoChildrenCode));
        }

        [Test]
        public void Text_only_markdown_is_clean()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md'>hello</Markdown></Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == MarkdownRules.NoChildrenCode));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: compile error — `MarkdownRules` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/Core/Lint/MarkdownRules.cs`:

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>&lt;Markdown&gt; content comes from text= (or inline CDATA); child elements are ignored.</summary>
    public static class MarkdownRules
    {
        public const string NoChildrenCode = "PUI-MARKDOWN-NO-CHILDREN";

        public static IEnumerable<LintIssue> CheckMarkdown(ElementNode n)
        {
            if (n.Children.Count > 0)
                yield return new LintIssue(
                    NoChildrenCode, n.Tag, n.Id,
                    $"<Markdown id='{n.Id}'>: content comes from text= (or inline CDATA); " +
                    "child elements are ignored. Remove them.");
        }
    }
}
```

In `Runtime/Core/Lint/IRWalker.cs`, in the per-tag self-check chain (after the `else if (node.Tag == "Carousel")` block), add:

```csharp
            else if (node.Tag == "Markdown")
                foreach (var issue in MarkdownRules.CheckMarkdown(node))
                    yield return issue;
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdownLintTests")`. Expected: 2 PASS.

Then run the **whole Phase A suite** to confirm no regressions:
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Markdown")`
Expected: all Markdown* classes green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Lint/MarkdownRules.cs Runtime/Core/Lint/IRWalker.cs Tests/EditMode/Controls/MarkdownControlTests.cs
git commit -m "feat(markdown): lint PUI-MARKDOWN-NO-CHILDREN (child elements ignored)"
```

---

# Phase B — Markdig backend (requires Markdig installed in the host project)

## Task 8: Install Markdig + gated asmdef + editor auto-detector + IVT

**Files:**
- **Manual:** install Markdig into the host project `C:\xsoft\PromptUGUIDev`.
- Create: `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef`
- Create: `Tests/EditMode/Markdown/PromptUGUI.Tests.EditMode.Markdown.asmdef`
- Create: `Editor/MarkdigDetector.cs`
- Modify: `Runtime/AssemblyInfo.cs` (InternalsVisibleTo the gated test asmdef)

- [ ] **Step 1: Install Markdig (manual, in the host Unity project)**

In `C:\xsoft\PromptUGUIDev`: open **NuGet → Manage NuGet Packages**, search **Markdig**, install (puts `Assets/Packages/Markdig.*/lib/.../Markdig.dll`). Alternatively drop a `Markdig.dll` (netstandard2.0) under `Assets/`. This is a human step — the MCP cannot run NuGetForUnity.

- [ ] **Step 2: Create the editor auto-detector**

Create `Editor/MarkdigDetector.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace PromptUGUI.Editor
{
    /// <summary>Defines PROMPTUGUI_HAS_MARKDIG for the active build target group whenever a "Markdig"
    /// assembly is loaded (NuGetForUnity / DLL), and removes it when absent. Markdig is not a UPM
    /// package, so asmdef versionDefines can't detect it — we scan the AppDomain instead.</summary>
    [InitializeOnLoad]
    internal static class MarkdigDetector
    {
        private const string Symbol = "PROMPTUGUI_HAS_MARKDIG";

        static MarkdigDetector()
        {
            var present = HasMarkdig();
            var group = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            var defines = PlayerSettings.GetScriptingDefineSymbols(group);
            var list = new List<string>(defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
            var has = list.Contains(Symbol);

            if (present && !has) { list.Add(Symbol); PlayerSettings.SetScriptingDefineSymbols(group, string.Join(";", list)); }
            else if (!present && has) { list.Remove(Symbol); PlayerSettings.SetScriptingDefineSymbols(group, string.Join(";", list)); }
        }

        private static bool HasMarkdig()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Markdig") return true;
            return false;
        }
    }
}
```

- [ ] **Step 3: Create the gated runtime asmdef**

Create `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef`:

```json
{
    "name": "PromptUGUI.Markdown",
    "rootNamespace": "PromptUGUI.MarkdigBackend",
    "references": ["PromptUGUI.Runtime"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["Markdig.dll"],
    "autoReferenced": true,
    "defineConstraints": ["PROMPTUGUI_HAS_MARKDIG"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Create the gated test asmdef**

Create `Tests/EditMode/Markdown/PromptUGUI.Tests.EditMode.Markdown.asmdef`:

```json
{
    "name": "PromptUGUI.Tests.EditMode.Markdown",
    "rootNamespace": "PromptUGUI.Tests.Markdown",
    "references": [
        "PromptUGUI.Runtime",
        "PromptUGUI.Markdown",
        "PromptUGUI.Editor",
        "Unity.TextMeshPro",
        "UnityEngine.UI",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS", "PROMPTUGUI_HAS_MARKDIG"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 5: Expose internals to the gated test asmdef**

In `Runtime/AssemblyInfo.cs`, add next to the existing `InternalsVisibleTo` lines:

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PromptUGUI.Tests.EditMode.Markdown")]
```

- [ ] **Step 6: Verify compile + symbol**

Run: `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` then `mcp__UnityMCP__read_console(action="get", types=["error"])`.
Expected: no errors. Confirm the symbol via:
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Markdown")` still green.
If `Markdig` is not installed, `read_console` shows the gated asmdefs skipped (no error); install Markdig (Step 1) before continuing to Task 9.

- [ ] **Step 7: Commit**

```bash
git add Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef Tests/EditMode/Markdown/PromptUGUI.Tests.EditMode.Markdown.asmdef Editor/MarkdigDetector.cs Runtime/AssemblyInfo.cs
git commit -m "feat(markdown): gated PromptUGUI.Markdown asmdef + Markdig editor auto-detector"
```

---

## Task 9: `MarkdigRenderer` core (pipeline, inline engine, headings, paragraphs) + bootstrap

**Files:**
- Create: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Create: `Runtime/MarkdigBackend/MarkdigBootstrap.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Markdown/MarkdigRendererTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI;
using PromptUGUI.IR;
using PromptUGUI.MarkdigBackend;

namespace PromptUGUI.Tests.Markdown
{
    public class MarkdigRendererTests
    {
        private static ElementNode Render(string md)
            => new MarkdigRenderer().Render(md, MarkdownStyle.CreateDefault()).Root;

        // depth-first find first node with tag whose TextContent contains `needle`
        private static ElementNode Find(ElementNode n, string tag, string needle)
        {
            if (n.Tag == tag && n.TextContent != null && n.TextContent.Contains(needle)) return n;
            foreach (var c in n.Children) { var r = Find(c, tag, needle); if (r != null) return r; }
            return null;
        }

        [Test]
        public void Empty_returns_vstack_root_no_children()
        {
            var root = Render("");
            Assert.AreEqual("VStack", root.Tag);
            Assert.AreEqual(0, root.Children.Count);
        }

        [Test]
        public void Heading_becomes_bold_text()
        {
            var root = Render("# Title");
            var t = Find(root, "Text", "Title");
            Assert.IsNotNull(t);
            StringAssert.Contains("<b>", t.TextContent);
        }

        [Test]
        public void Paragraph_inline_maps_to_tmp_tags_and_escapes()
        {
            var root = Render("a **b** _c_ ~~d~~ `e` 1<2 & 3");
            var t = Find(root, "Text", "b</b>");
            Assert.IsNotNull(t);
            StringAssert.Contains("<i>c</i>", t.TextContent);
            StringAssert.Contains("<s>d</s>", t.TextContent);
            StringAssert.Contains("<mark=", t.TextContent);    // inline code background
            StringAssert.Contains("&lt;", t.TextContent);       // escaped '<'
            StringAssert.Contains("&amp;", t.TextContent);      // escaped '&'
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console. Expected: compile error — `MarkdigRenderer` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Runtime/MarkdigBackend/MarkdigRenderer.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.MarkdigBackend
{
    public sealed class MarkdigRenderer : IMarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        private MarkdownStyle _style;
        private List<ImageRequest> _images;
        private int _imageSeq;

        public MarkdownRenderResult Render(string markdown, MarkdownStyle style)
        {
            _style = style ?? MarkdownStyle.CreateDefault();
            _images = new List<ImageRequest>();
            _imageSeq = 0;

            var root = NewVStack(_style.BlockSpacing);
            root.Attributes["anchor"] = "top-stretch";
            root.Attributes["pivot"] = "0.5,1";

            var doc = Markdig.Markdown.Parse(markdown ?? "", Pipeline);
            foreach (var block in doc)
            {
                var node = RenderBlock(block);
                if (node != null) root.Children.Add(node);
            }
            return new MarkdownRenderResult { Root = root, Images = _images };
        }

        // ---- block dispatch (extended in later tasks) ----
        private ElementNode RenderBlock(Block block)
        {
            switch (block)
            {
                case HeadingBlock h:
                    return NewText(RenderInline(h.Inline),
                        _style.HeadingSizes[Mathf.Clamp(h.Level, 1, 6) - 1], bold: true);
                case ParagraphBlock p:
                    return NewText(RenderInline(p.Inline), _style.BodySize);
                default:
                    return null; // HtmlBlock etc dropped (MD-D17)
            }
        }

        // ---- inline engine ----
        private string RenderInline(ContainerInline container)
        {
            if (container == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in container) AppendInline(sb, inline);
            return sb.ToString();
        }

        private void AppendInline(StringBuilder sb, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(Escape(lit.Content.ToString()));
                    break;
                case CodeInline code:
                    sb.Append("<mark=").Append(ToHex(_style.CodeBackground)).Append("><font=\"")
                      .Append(_style.CodeFont).Append("\">").Append(Escape(code.Content))
                      .Append("</font></mark>");
                    break;
                case EmphasisInline em:
                {
                    string tag = em.DelimiterChar == '~' ? "s" : (em.DelimiterCount >= 2 ? "b" : "i");
                    sb.Append('<').Append(tag).Append('>');
                    foreach (var child in em) AppendInline(sb, child);
                    sb.Append("</").Append(tag).Append('>');
                    break;
                }
                case LineBreakInline _:
                    sb.Append('\n');
                    break;
                case ContainerInline cont:   // unknown container -> recurse
                    foreach (var child in cont) AppendInline(sb, child);
                    break;
            }
        }

        // ---- shared builders ----
        private ElementNode NewVStack(float spacing)
        {
            var n = new ElementNode("VStack");
            n.Attributes["spacing"] = spacing.ToString(CultureInfo.InvariantCulture);
            n.Attributes["childAlign"] = "upper-left";
            return n;
        }

        private ElementNode NewText(string richText, float size, bool bold = false)
        {
            var n = new ElementNode("Text");
            n.Attributes["width"] = "stretch";
            n.Attributes["fontSize"] = ((int)size).ToString(CultureInfo.InvariantCulture);
            n.Attributes["font"] = _style.BodyFont;
            n.Attributes["wrap"] = _style.ParagraphWrap ? "true" : "false";
            n.Attributes["align"] = "top-left";
            n.Attributes["tr"] = "false";
            n.TextContent = bold ? $"<b>{richText}</b>" : richText;
            return n;
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string ToHex(string colorToken)
        {
            var c = PromptUGUI.Application.UI.Theme.Resolve(colorToken);
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }
    }
}
```

Create `Runtime/MarkdigBackend/MarkdigBootstrap.cs`:

```csharp
using UnityEngine;

namespace PromptUGUI.MarkdigBackend
{
    internal static class MarkdigBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void Install()
        {
            Inject();
            PromptUGUI.Application.UI.OnReset += Inject;   // re-inject after every ResetForTests (test isolation)
        }

        private static void Inject() =>
            PromptUGUI.Application.UI.Markdown.Renderer ??= new MarkdigRenderer();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, then
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], filter="MarkdigRendererTests")`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Runtime/MarkdigBackend/MarkdigBootstrap.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat(markdown): MarkdigRenderer core (inline engine, headings, paragraphs) + bootstrap"
```

---

## Task 10: Links + lists (ordered / unordered / nested / task)

**Files:**
- Modify: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MarkdigRendererTests` class:

```csharp
        // count nodes matching a predicate
        private static int Count(ElementNode n, System.Func<ElementNode, bool> pred)
        {
            int c = pred(n) ? 1 : 0;
            foreach (var ch in n.Children) c += Count(ch, pred);
            return c;
        }

        [Test]
        public void Link_emits_link_tag()
        {
            var root = Render("see [docs](https://x.test)");
            var t = Find(root, "Text", "<link=\"https://x.test\">");
            Assert.IsNotNull(t);
        }

        [Test]
        public void Unordered_list_makes_a_row_per_item_with_bullets()
        {
            var root = Render("- one\n- two\n- three");
            var bullets = Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("•"));
            Assert.AreEqual(3, bullets);
        }

        [Test]
        public void Ordered_list_numbers_items()
        {
            var root = Render("1. a\n2. b");
            Assert.IsNotNull(Find(root, "Text", "1."));
            Assert.IsNotNull(Find(root, "Text", "2."));
        }

        [Test]
        public void Task_list_uses_check_glyphs()
        {
            var root = Render("- [x] done\n- [ ] todo");
            Assert.IsNotNull(Find(root, "Text", "☑")); // ☑
            Assert.IsNotNull(Find(root, "Text", "☐")); // ☐
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console + `run_tests(... assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], filter="MarkdigRendererTests")`.
Expected: the 4 new tests FAIL (links/lists not handled).

- [ ] **Step 3: Write minimal implementation**

Add `using Markdig.Extensions.TaskLists;` at the top of `MarkdigRenderer.cs`. In `AppendInline`, add a link case **before** the `case ContainerInline cont:` fallback (so links don't get swallowed by it):

```csharp
                case TaskList _:
                    break;  // checkbox shown as the list marker, not inline
                case LinkInline link when !link.IsImage:
                    sb.Append("<link=\"").Append(link.Url).Append("\"><color=")
                      .Append(ToHex(_style.LinkColor)).Append("><u>");
                    foreach (var child in link) AppendInline(sb, child);
                    sb.Append("</u></color></link>");
                    break;
```

In `RenderBlock`, add a case before `default:`:

```csharp
                case ListBlock list:
                    return RenderList(list, 0);
```

Add these methods to the class:

```csharp
        private ElementNode RenderList(ListBlock list, int depth)
        {
            var v = NewVStack(_style.BlockSpacing * 0.5f);
            int number = ParseStart(list.OrderedStart);
            foreach (var item in list)
            {
                if (item is not ListItemBlock li) continue;

                string marker;
                var task = GetTaskState(li);
                if (task.HasValue) marker = task.Value ? _style.CheckedGlyph : _style.UncheckedGlyph;
                else if (list.IsOrdered) { marker = number + "."; number++; }
                else marker = _style.BulletGlyph;

                var row = new ElementNode("HStack");
                row.Attributes["width"] = "stretch";
                row.Attributes["spacing"] = "6";
                row.Attributes["childAlign"] = "upper-left";

                if (depth > 0)
                {
                    var spacer = new ElementNode("Frame");
                    spacer.Attributes["width"] = (_style.ListIndent * depth).ToString(CultureInfo.InvariantCulture);
                    spacer.Attributes["height"] = "1";
                    row.Children.Add(spacer);
                }

                var bullet = NewText(Escape(marker), _style.BodySize);
                bullet.Attributes["width"] = "24";
                row.Children.Add(bullet);

                var content = NewVStack(_style.BlockSpacing * 0.5f);
                content.Attributes["width"] = "stretch";
                foreach (var child in li)
                {
                    if (child is ListBlock nested) content.Children.Add(RenderList(nested, depth + 1));
                    else { var n = RenderBlock(child); if (n != null) content.Children.Add(n); }
                }
                row.Children.Add(content);
                v.Children.Add(row);
            }
            return v;
        }

        private static int ParseStart(string s) => int.TryParse(s, out var n) ? n : 1;

        private static bool? GetTaskState(ListItemBlock li)
        {
            if (li.Count > 0 && li[0] is ParagraphBlock p && p.Inline != null)
                foreach (var inline in p.Inline)
                    if (inline is TaskList tl) return tl.Checked;
                    else break;   // task marker is the first inline only
            return null;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdigRendererTests")`. Expected: all (7) PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat(markdown): render links + ordered/unordered/nested/task lists"
```

---

## Task 11: Blockquote + fenced code + thematic break

**Files:**
- Modify: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing test**

Append:

```csharp
namespace PromptUGUI.Tests.Markdown
{
    public partial class MarkdigRendererBlockTests
    {
        private static ElementNode Render(string md)
            => new PromptUGUI.MarkdigBackend.MarkdigRenderer().Render(md, MarkdownStyle.CreateDefault()).Root;
        private static ElementNode Find(ElementNode n, string tag, string needle)
        {
            if (n.Tag == tag && ((needle == null) || (n.TextContent != null && n.TextContent.Contains(needle)))) return n;
            foreach (var c in n.Children) { var r = Find(c, tag, needle); if (r != null) return r; }
            return null;
        }

        [Test]
        public void Code_fence_uses_mark_and_code_font()
        {
            var root = Render("```\nint x = 1;\n```");
            var t = Find(root, "Text", "int x = 1;");
            Assert.IsNotNull(t);
            StringAssert.Contains("<mark=", t.TextContent);
        }

        [Test]
        public void Blockquote_has_bar_image()
        {
            var root = Render("> quoted");
            Assert.IsNotNull(Find(root, "Image", null)); // the quote bar
            Assert.IsNotNull(Find(root, "Text", "quoted"));
        }

        [Test]
        public void Thematic_break_is_thin_image()
        {
            var root = Render("a\n\n---\n\nb");
            Assert.IsNotNull(Find(root, "Image", null));
        }
    }
}
```

> The `Find(..., null)` overload matches by tag only — note the updated guard handles `needle == null`.

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console + `run_tests(... filter="MarkdigRendererBlockTests")`. Expected: FAIL (blocks not handled).

- [ ] **Step 3: Write minimal implementation**

In `RenderBlock`, add cases before `default:`:

```csharp
                case QuoteBlock q:
                    return RenderQuote(q);
                case FencedCodeBlock fc:
                    return RenderCode(fc);
                case CodeBlock cb:
                    return RenderCode(cb);
                case ThematicBreakBlock _:
                    return RenderHr();
```

Add methods:

```csharp
        private ElementNode RenderQuote(QuoteBlock q)
        {
            var row = new ElementNode("HStack");
            row.Attributes["width"] = "stretch";
            row.Attributes["spacing"] = "8";
            row.Attributes["childAlign"] = "upper-left";

            var bar = new ElementNode("Image");
            bar.Attributes["width"] = _style.HrThickness.ToString(CultureInfo.InvariantCulture);
            bar.Attributes["height"] = "stretch";
            bar.Attributes["color"] = _style.QuoteBarColor;
            row.Children.Add(bar);

            var content = NewVStack(_style.BlockSpacing);
            content.Attributes["width"] = "stretch";
            foreach (var child in q) { var n = RenderBlock(child); if (n != null) content.Children.Add(n); }
            row.Children.Add(content);
            return row;
        }

        private ElementNode RenderCode(LeafBlock code)
        {
            var n = NewText("", _style.BodySize);
            n.Attributes["font"] = _style.CodeFont;
            n.Attributes["wrap"] = "false";
            n.TextContent = "<mark=" + ToHex(_style.CodeBackground) + ">" + Escape(GetCodeText(code)) + "</mark>";
            return n;
        }

        private ElementNode RenderHr()
        {
            var img = new ElementNode("Image");
            img.Attributes["width"] = "stretch";
            img.Attributes["height"] = _style.HrThickness.ToString(CultureInfo.InvariantCulture);
            img.Attributes["color"] = _style.HrColor;
            return img;
        }

        private static string GetCodeText(LeafBlock leaf)
        {
            var lines = leaf.Lines;
            if (lines.Lines == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(lines.Lines[i].Slice.ToString());
                if (i < lines.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdigRendererBlockTests")`. Expected: 3 PASS. Re-run `filter="MarkdigRendererTests"` (7) green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat(markdown): render blockquote, fenced code (mark bg), thematic break"
```

---

## Task 12: Tables (VStack of HStack rows, equal stretch columns)

**Files:**
- Modify: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MarkdigRendererBlockTests`:

```csharp
        private static int Count(ElementNode n, System.Func<ElementNode, bool> pred)
        {
            int c = pred(n) ? 1 : 0;
            foreach (var ch in n.Children) c += Count(ch, pred);
            return c;
        }

        [Test]
        public void Table_renders_rows_of_stretch_cells_with_bold_header()
        {
            var root = Render("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |");
            // header cells bold
            Assert.IsNotNull(Find(root, "Text", "<b>A</b>"));
            Assert.IsNotNull(Find(root, "Text", "<b>B</b>"));
            // body cells present
            Assert.IsNotNull(Find(root, "Text", "1"));
            Assert.IsNotNull(Find(root, "Text", "4"));
            // 3 rows -> 3 HStacks
            Assert.AreEqual(3, Count(root, n => n.Tag == "HStack"));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console + `run_tests(... filter="MarkdigRendererBlockTests")`. Expected: FAIL (tables not handled).

- [ ] **Step 3: Write minimal implementation**

Add `using Markdig.Extensions.Tables;` at the top. In `RenderBlock`, add before `default:`:

```csharp
                case Table table:
                    return RenderTable(table);
```

Add method:

```csharp
        private ElementNode RenderTable(Table table)
        {
            int cols = 0;
            foreach (var rowObj in table)
                if (rowObj is TableRow r) cols = Mathf.Max(cols, r.Count);

            var grid = NewVStack(2f);
            grid.Attributes["width"] = "stretch";

            foreach (var rowObj in table)
            {
                if (rowObj is not TableRow row) continue;
                var hstack = new ElementNode("HStack");
                hstack.Attributes["width"] = "stretch";
                hstack.Attributes["spacing"] = "4";
                hstack.Attributes["childAlign"] = "upper-left";

                for (int c = 0; c < cols; c++)
                {
                    string text = "";
                    if (c < row.Count && row[c] is TableCell cell)
                        text = RenderCell(cell);
                    var t = NewText(text, _style.BodySize, bold: row.IsHeader);
                    t.Attributes["width"] = "stretch";   // equal columns
                    hstack.Children.Add(t);
                }
                grid.Children.Add(hstack);
            }
            return grid;
        }

        private string RenderCell(TableCell cell)
        {
            var sb = new StringBuilder();
            foreach (var block in cell)
                if (block is ParagraphBlock p) sb.Append(RenderInline(p.Inline));
            return sb.ToString();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdigRendererBlockTests")`. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat(markdown): render GFM tables as stretch-column HStack rows"
```

---

## Task 13: Images — block (RawImage + ImageRequest) + inline (sprite / alt)

**Files:**
- Modify: `Runtime/MarkdigBackend/MarkdigRenderer.cs`
- Test: `Tests/EditMode/Markdown/MarkdigRendererTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MarkdigRendererBlockTests`:

```csharp
        [Test]
        public void Block_image_emits_rawimage_and_image_request()
        {
            var renderer = new PromptUGUI.MarkdigBackend.MarkdigRenderer();
            var result = renderer.Render("![alt text](https://x.test/a.png)", MarkdownStyle.CreateDefault());
            var raw = Find(result.Root, "RawImage", null);
            Assert.IsNotNull(raw);
            Assert.AreEqual(1, result.Images.Count);
            Assert.AreEqual("https://x.test/a.png", result.Images[0].Url);
            Assert.AreEqual(raw.Id, result.Images[0].NodeId);
        }

        [Test]
        public void Inline_image_bare_name_becomes_sprite_tag()
        {
            var root = Render("coin ![c](coin) here");
            Assert.IsNotNull(Find(root, "Text", "<sprite name=\"coin\">"));
        }

        [Test]
        public void Inline_image_url_falls_back_to_alt_text()
        {
            var root = Render("x ![pic](https://x.test/p.png) y");
            var t = Find(root, "Text", "pic");
            Assert.IsNotNull(t);
            Assert.IsFalse(t.TextContent.Contains("<sprite"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: refresh + read_console + `run_tests(... filter="MarkdigRendererBlockTests")`. Expected: FAIL (images not handled).

- [ ] **Step 3: Write minimal implementation**

In `RenderBlock`, replace the `case ParagraphBlock p:` line with a lone-image check:

```csharp
                case ParagraphBlock p:
                    if (IsLoneImage(p, out var blockImg)) return RenderBlockImage(blockImg);
                    return NewText(RenderInline(p.Inline), _style.BodySize);
```

In `AppendInline`, add an inline-image case (before the link case):

```csharp
                case LinkInline img when img.IsImage:
                    AppendInlineImage(sb, img);
                    break;
```

Add methods:

```csharp
        private static bool IsLoneImage(ParagraphBlock p, out LinkInline img)
        {
            img = null;
            if (p.Inline == null) return false;
            LinkInline found = null;
            int count = 0;
            foreach (var inline in p.Inline)
            {
                if (inline is LiteralInline lit && string.IsNullOrWhiteSpace(lit.Content.ToString())) continue;
                if (inline is LinkInline l && l.IsImage) { found = l; count++; }
                else { count = 99; break; }
            }
            img = found;
            return count == 1 && found != null;
        }

        private ElementNode RenderBlockImage(LinkInline image)
        {
            string id = "mdimg" + (_imageSeq++);
            var n = new ElementNode("RawImage");
            n.Id = id;
            n.Attributes["type"] = "contain";
            n.Attributes["width"] = "stretch";
            n.Attributes["height"] = "120";   // placeholder until texture sets aspect
            _images.Add(new ImageRequest(id, image.Url ?? "", GetText(image)));
            return n;
        }

        private void AppendInlineImage(StringBuilder sb, LinkInline img)
        {
            string url = img.Url ?? "";
            if (url.Length > 0 && url.IndexOf('/') < 0 && url.IndexOf(':') < 0)
                sb.Append("<sprite name=\"").Append(url).Append("\">");  // bare name -> TMP sprite (MD-D16)
            else
                sb.Append(Escape(GetText(img)));                          // web/path -> alt text (lossy)
        }

        private static string GetText(ContainerInline c)
        {
            if (c == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in c)
                if (inline is LiteralInline lit) sb.Append(lit.Content.ToString());
                else if (inline is ContainerInline cc) sb.Append(GetText(cc));
            return sb.ToString();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: refresh, read_console, `run_tests(... filter="MarkdigRendererBlockTests")`. Expected: all PASS. Re-run `filter="MarkdigRendererTests"` green.

- [ ] **Step 5: Commit**

```bash
git add Runtime/MarkdigBackend/MarkdigRenderer.cs Tests/EditMode/Markdown/MarkdigRendererTests.cs
git commit -m "feat(markdown): block images (RawImage+request) + inline sprite/alt images"
```

---

# Phase C — web resolver + end-to-end integration

## Task 14: `UseWebImageResolver` (UnityWebRequestTexture + cache)

**Files:**
- Modify: `Runtime/Application/UI.Markdown.cs`
- Test: `Tests/EditMode/Controls/MarkdownControlTests.cs` (set check), `Tests/PlayMode/MarkdownWebImageTests.cs` (fetch)

- [ ] **Step 1: Write the failing tests**

Append to `MarkdownControlTests.cs`:

```csharp
namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownWebResolverTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void UseWebImageResolver_sets_resolver()
        {
            Assert.IsNull(UI.Markdown.ImageResolver);
            UI.Markdown.UseWebImageResolver();
            Assert.IsNotNull(UI.Markdown.ImageResolver);
        }
    }
}
```

Create `Tests/PlayMode/MarkdownWebImageTests.cs`:

```csharp
using System.Collections;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    public class MarkdownWebImageTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator WebResolver_loads_local_file_url_and_caches()
        {
            // write a tiny PNG to a temp file and load it via file:// (no network)
            var path = Path.Combine(Application.temporaryCachePath, "md_test.png");
            var src = new Texture2D(4, 4);
            File.WriteAllBytes(path, src.EncodeToPNG());
            Object.DestroyImmediate(src);
            var url = "file://" + path.Replace('\\', '/');

            UI.Markdown.UseWebImageResolver();

            var op = UI.Markdown.ImageResolver(url);
            while (!op.IsCompleted) yield return null;
            var tex = op.GetAwaiter().GetResult();
            Assert.IsNotNull(tex);

            // second call returns the cached instance
            var op2 = UI.Markdown.ImageResolver(url);
            while (!op2.IsCompleted) yield return null;
            Assert.AreSame(tex, op2.GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: refresh + read_console. Expected: compile error — `UseWebImageResolver` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `Runtime/Application/UI.Markdown.cs`, add `using System.Collections.Generic;` and `using UnityEngine.Networking;`. Inside `class Markdown`, add the cache + helper and clear the cache on reset:

```csharp
            private static readonly Dictionary<string, Texture2D> WebCache = new();

            public static void UseWebImageResolver()
            {
                ImageResolver = LoadWebTextureAsync;
            }

            private static async Awaitable<Texture2D> LoadWebTextureAsync(string url)
            {
                if (string.IsNullOrEmpty(url)) return null;
                if (WebCache.TryGetValue(url, out var cached) && cached != null) return cached;

                using var req = UnityWebRequestTexture.GetTexture(url);
                var op = req.SendWebRequest();
                var acs = new AwaitableCompletionSource<bool>();
                op.completed += _ => acs.SetResult(true);
                if (!op.isDone) await acs.Awaitable;

                if (req.result != UnityWebRequest.Result.Success) return null;
                var tex = DownloadHandlerTexture.GetContent(req);
                WebCache[url] = tex;
                return tex;
            }
```

Update `ResetForTestsInternal()` to clear the cache (and destroy cached textures):

```csharp
            internal static void ResetForTestsInternal()
            {
                Renderer = null;
                DefaultStyle = MarkdownStyle.CreateDefault();
                ImageResolver = null;
                foreach (var t in WebCache.Values)
                    if (t != null)
                    {
                        if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(t);
                        else UnityEngine.Object.DestroyImmediate(t);
                    }
                WebCache.Clear();
            }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: refresh, read_console, then:
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MarkdownWebResolverTests")` → 1 PASS.
`run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="MarkdownWebImageTests")` → 1 PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.Markdown.cs Tests/EditMode/Controls/MarkdownControlTests.cs Tests/PlayMode/MarkdownWebImageTests.cs
git commit -m "feat(markdown): UseWebImageResolver (UnityWebRequestTexture + texture cache)"
```

---

## Task 15: End-to-end integration test (real renderer)

**Files:**
- Create: `Tests/EditMode/Markdown/MarkdownIntegrationTests.cs` (gated asmdef)

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Markdown/MarkdownIntegrationTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Markdown
{
    public class MarkdownIntegrationTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();   // real MarkdigRenderer re-injected via OnReset
        [TearDown] public void TearDown() => UI.ResetForTests();

        const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Markdown id='md' anchor='stretch'/></Screen></PromptUGUI>";

        private Controls.Markdown Open()
        {
            UI.LoadDocument("t", Xml);
            return UI.Open("S").Get<Controls.Markdown>("md");
        }

        [Test]
        public void Real_renderer_is_injected_after_reset()
        {
            Assert.IsNotNull(UI.Markdown.Renderer, "Markdig backend should re-inject on OnReset");
        }

        [Test]
        public void Full_document_builds_scroll_content_with_headings_and_lists()
        {
            var md = Open();
            md.Text = "# Title\n\nA paragraph with **bold**.\n\n- one\n- two\n\n| A | B |\n|---|---|\n| 1 | 2 |";

            var scroll = md.GameObject.GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll.content);
            Assert.IsNotNull(scroll.content.GetComponent<ContentSizeFitter>());

            // at least one TMP carries the bold title
            var tmps = md.GameObject.GetComponentsInChildren<TMPro.TMP_Text>(true);
            bool hasTitle = false;
            foreach (var t in tmps) if (t.text.Contains("<b>Title</b>")) hasTitle = true;
            Assert.IsTrue(hasTitle);
        }

        [Test]
        public void Block_image_placeholder_then_swap_with_fake_resolver()
        {
            var tex = new Texture2D(2, 2);
            UI.Markdown.ImageResolver = _ => Completed(tex);
            var md = Open();
            md.Text = "![pic](https://x.test/a.png)";

            // the rendered RawImage got the texture (resolver synchronously completed)
            var raw = md.GameObject.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            Assert.IsNotNull(raw);
            Assert.AreEqual(tex, raw.texture);
        }

        private static Awaitable<Texture2D> Completed(Texture2D t)
        {
            var s = new AwaitableCompletionSource<Texture2D>();
            s.SetResult(t);
            return s.Awaitable;
        }
    }
}
```

> Note: `RawImage` control wraps a uGUI `UnityEngine.UI.RawImage`; the assertion reads `.texture` off that component (the control's `Texture` setter writes it).

- [ ] **Step 2: Run test to verify it fails / passes**

Run: refresh, read_console, `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"], filter="MarkdownIntegrationTests")`.
Expected: PASS (all backend pieces exist by now). If `Real_renderer_is_injected_after_reset` fails, verify `MarkdigBootstrap` subscribes to `UI.OnReset` (Task 9) and `OnReset` is `public` (Task 2).

- [ ] **Step 3: (only if a test fails) fix and re-run**

Address any failure per systematic-debugging, then re-run.

- [ ] **Step 4: Full-suite regression**

Run all three suites:
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"])`
`run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add Tests/EditMode/Markdown/MarkdownIntegrationTests.cs
git commit -m "test(markdown): end-to-end integration (real renderer, scroll content, image swap)"
```

---

# Phase D — documentation

## Task 16: SKILL updates + main spec row + XSD

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Create: `.claude/skills/authoring-promptugui-xml/reference/controls-markdown.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Create: `.claude/skills/using-promptugui-markdown/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`
- Regenerate XSD

- [ ] **Step 1: XML SKILL — catalog row + reference stub pointer**

In `authoring-promptugui-xml/SKILL.md`, add a built-in primitives table row:

```
| `<Markdown>` | Renders a Markdown document into a scrollable subtree (headings/paragraphs/lists/quote/code/table/hr/image/links). Content via `text=` (CDATA) or C# `Text`. Soft-depends on Markdig. | text, bodyFont, codeFont, linkColor, spacing, wrap → see reference/controls-markdown.md |
```

Add a stub pointer line where other reference pointers live:

```
- `<Markdown>` Markdown rendering, supported subset, lossy notes, lint → reference/controls-markdown.md
```

- [ ] **Step 2: Create `reference/controls-markdown.md`**

Write a deep-dive covering: dynamic (`md.Text`) and static-CDATA usage; the attribute table (text/bodyFont/codeFont/linkColor/spacing/wrap); the supported Markdown subset and the **block→control** mapping table (copy from spec §7.1); lossy items (HTML stripped, inline web images → alt text, no syntax highlighting, no cell borders, vertical-scroll-only); lint `PUI-MARKDOWN-NO-CHILDREN`; "content comes from `text`, child elements ignored"; and a pointer: "requires Markdig — see the `using-promptugui-markdown` skill."

```markdown
# `<Markdown>` — Markdown rendering

`<Markdown>` parses a Markdown string and renders it block-by-block into a built-in
vertical `ScrollRect`, reusing `Text` / `RawImage` / `VStack` / `HStack` primitives.

## Content
- Dynamic (primary): leave it empty / placeholder in XML, set from C#: `screen.Get<Markdown>("doc").Text = md;`
- Static: inline with CDATA (protects `<`, `&`):
  ```xml
  <Markdown id="about" anchor="stretch" margin="16"><![CDATA[
  # Title
  **bold** and a [link](https://example.com)
  ]]></Markdown>
  ```

## Attributes
| attr | default | effect |
|---|---|---|
| `text` | "" | Markdown source (CDATA for inline). Runtime-set value survives ReSolve. |
| `bodyFont` | default | body/heading font type |
| `codeFont` | default | code font type (use a monospace font type) |
| `linkColor` | theme | link text color |
| `spacing` | style | block spacing |
| `wrap` | true | paragraph wrapping |

Full styling (heading sizes, quote bar, code background, list indent, glyphs, hr) is in
C# `MarkdownStyle` (`md.Style` / `UI.Markdown.DefaultStyle`).

## Supported subset & mapping
(headings, paragraphs, **bold**/*italic*/~~strike~~/`code`, links, ordered/unordered/nested
lists, task lists `[x]`, blockquotes, fenced code, `---`, images, GFM tables.)

## Lossy / out of scope (v1)
- HTML stripped; inline web images → alt text (bare-name → TMP `<sprite>`); no syntax highlighting;
  tables have no cell borders; scroll is vertical only (wide tables / no-wrap code clip horizontally).

## Lint
- `PUI-MARKDOWN-NO-CHILDREN` — content comes from `text=`; child elements are ignored.

## Requires Markdig
Rendering needs the Markdig package — see the `using-promptugui-markdown` skill. Without it the
control shows the raw Markdown text.
```

- [ ] **Step 3: C# SKILL — Markdown section**

In `scripting-promptugui-csharp/SKILL.md`, add a `<Markdown>` section:

```markdown
### Markdown

```csharp
var md = screen.Get<Markdown>("doc");
md.Text = await Http.GetString(url);            // set re-renders; get returns the source
md.BindText(localizedStream).AddTo(screen);     // reactive (i18n / live)
md.OnLinkClicked.Subscribe(Application.OpenURL).AddTo(screen);
md.Style = MarkdownStyle.CreateDefault();       // per-control; UI.Markdown.DefaultStyle is global
md.ImageResolver = MyResolver;                   // per-control; UI.Markdown.ImageResolver is global

UI.Markdown.UseWebImageResolver();               // built-in http(s)/file image loader (cached)
```

`text` is runtime content: a value set from C# survives resize / Variant / Theme ReSolve
(same DefaultText lock as `<Text>`). Images load asynchronously (text shows immediately).
```

- [ ] **Step 4: Create `using-promptugui-markdown/SKILL.md`**

Mirror `using-promptugui-addressables`: how to install Markdig (NuGetForUnity / DLL); the editor
auto-detector defines `PROMPTUGUI_HAS_MARKDIG`; the gated `PromptUGUI.Markdown` asmdef lights up
`MarkdigRenderer`; without Markdig `<Markdown>` shows raw text. Include IL2CPP note (add a `link.xml`
preserving `Markdig` if stripping removes it).

```markdown
---
name: using-promptugui-markdown
description: Use when enabling PromptUGUI's <Markdown> control — installing Markdig and the PROMPTUGUI_HAS_MARKDIG gate. Without Markdig the control shows raw text.
---

# Markdown support (Markdig)

`<Markdown>` renders via Markdig, a symbol-gated soft dependency.

## Enable
1. Install **Markdig** (NuGetForUnity: NuGet → Manage Packages → Markdig → Install; or drop
   `Markdig.dll` (netstandard2.0) under `Assets/`).
2. The editor auto-detector (`PromptUGUI.Editor.MarkdigDetector`) finds the `Markdig` assembly and
   defines `PROMPTUGUI_HAS_MARKDIG` for the active build target, triggering a recompile that lights up
   the gated `PromptUGUI.Markdown` asmdef and its `MarkdigRenderer`.
3. Done — `<Markdown>` now renders. Without Markdig it shows the raw Markdown text + a warning.

## IL2CPP
Markdig is pure managed; if managed stripping removes it, add a `link.xml` preserving the `Markdig`
assembly.
```

- [ ] **Step 5: Main spec control table row**

In `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` §5 (controls), add:

```
| `<Markdown>` | Markdown document → scrollable subtree of existing primitives; dynamic `text`, resize-safe; soft-depends Markdig (PROMPTUGUI_HAS_MARKDIG) | see 2026-06-09-markdown-control-design.md |
```

- [ ] **Step 6: Regenerate XSD**

In Unity: run the menu **Tools → PromptUGUI → Schema → Generate XSD** (the project's XSD generator). Confirm the output contains `Markdown` and its attributes. If there is an XSD generator test, add a substring assertion for `Markdown` (matching the existing convention).

- [ ] **Step 7: Lint the docs you touched + commit**

If any `.ui.xml` examples were added, run the UIXmlLint CLI on them (CLAUDE.md). Then:

```bash
git add .claude/skills/ "docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md"
git commit -m "docs(markdown): XML + C# + addressables-style skills, main spec row, XSD"
```

---

## Final verification (after all tasks)

- [ ] `mcp__UnityMCP__refresh_unity(...)` + `read_console(types=["error"])` → no errors.
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` → green (existing suite + Phase A).
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Markdown"])` → green (renderer + integration).
- [ ] `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])` → green.
- [ ] Lint: `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` → clean.
- [ ] Visual QA (user): a real Markdown doc renders, scrolls, images stream in, links fire.
- [ ] Open PR from `feat/markdown-control` (do NOT merge to `main` without review).
