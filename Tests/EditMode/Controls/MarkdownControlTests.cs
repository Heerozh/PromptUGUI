using NUnit.Framework;
using PromptUGUI;
using PromptUGUI.Application;

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
}
