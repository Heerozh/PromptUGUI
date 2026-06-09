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

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
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
