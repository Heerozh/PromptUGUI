using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    public class ColorParserGradientTests
    {
        [Test]
        public void NoComma_ReturnsSingleSegment()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("black/0.5", out var top, out var bottom, out var err));
            Assert.AreEqual("black/0.5", top);
            Assert.IsNull(bottom);
            Assert.IsNull(err);
        }

        [Test]
        public void TwoSegments_SplitAndTrimmed()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#ffe08a, #b8860b", out var top, out var bottom, out _));
            Assert.AreEqual("#ffe08a", top);
            Assert.AreEqual("#b8860b", bottom);
        }

        [TestCase("a,b,c")]
        [TestCase("a,")]
        [TestCase(",b")]
        [TestCase(",")]
        public void Malformed_Fails(string raw)
        {
            Assert.IsFalse(ColorParser.TrySplitGradient(raw, out _, out _, out var err));
            StringAssert.Contains("gradient", err);
        }

        [Test]
        public void Empty_IsSingleNullSegment_HandledByCaller()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("", out var top, out var bottom, out _));
            Assert.AreEqual("", top);
            Assert.IsNull(bottom);
        }
    }
}
