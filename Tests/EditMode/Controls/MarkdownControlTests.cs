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
