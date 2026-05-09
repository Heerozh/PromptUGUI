using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor {
    public class TmpRichTextDetectorTests {
        [TestCase("<sprite name=\"coin\"/>")]
        [TestCase("<color=#ff0>x</color>")]
        [TestCase("<b>bold</b>")]
        [TestCase("<size=20>x</size>")]
        [TestCase("<link=\"foo\">x</link>")]
        public void Detect_KnownTags_ReturnsTrue(string s) {
            Assert.IsTrue(TmpRichTextDetector.HasTmpTags(s));
        }

        [TestCase("plain")]
        [TestCase("price: {0}")]
        [TestCase("price: {{n}}")]
        [TestCase("a < b > c")]
        public void Detect_NoTmpTag_ReturnsFalse(string s) {
            Assert.IsFalse(TmpRichTextDetector.HasTmpTags(s));
        }
    }
}
