using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using R3;
using UnityEngine;
using UnityEngine.UI;

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

    public class UIMarkdownFacadeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void DefaultStyle_is_non_null_after_reset()
        {
            UI.Markdown.DefaultStyle = null;     // dirty it, then reset must restore a default
            UI.ResetForTests();
            Assert.IsNotNull(UI.Markdown.DefaultStyle);
        }

        [Test]
        public void Reset_clears_ImageResolver_and_Renderer()
        {
            UI.Markdown.ImageResolver = _ => null;
            UI.Markdown.Renderer = new StubRenderer();   // non-null so reset->null is observable
            UI.ResetForTests();
            Assert.IsNull(UI.Markdown.ImageResolver);
            Assert.IsNull(UI.Markdown.Renderer);
        }

        private sealed class StubRenderer : IMarkdownRenderer
        {
            public MarkdownRenderResult Render(string markdown, MarkdownStyle style) => new();
        }
    }

    public class MarkdownControlScaffoldTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
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

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
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

        [Test]
        public void Re_render_replaces_and_disposes_old_content()
        {
            UI.Markdown.Renderer = new FakeMarkdownRenderer(); // returns a fresh root each Render()
            var md = Open();
            md.Text = "first";
            var first = md.GameObject.GetComponent<UnityEngine.UI.ScrollRect>().content;
            md.Text = "second";
            var second = md.GameObject.GetComponent<UnityEngine.UI.ScrollRect>().content;
            Assert.AreNotSame(first, second);          // new content instantiated
            Assert.IsTrue(first == null);              // old root GameObject destroyed (Unity null)
        }
    }
}

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class MarkdownBindAndLinkTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); UI.Markdown.Renderer = new FakeMarkdownRenderer(); }
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
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
