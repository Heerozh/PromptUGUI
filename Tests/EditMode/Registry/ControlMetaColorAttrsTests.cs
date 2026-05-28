using NUnit.Framework;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Registry
{
    public class ControlMetaColorAttrsTests
    {
        private class FakeControl
        {
            [UIAttr(IsColor = true)] public string Color { get; set; }
            [UIAttr] public string Label { get; set; }
            [UIAttr(IsSprite = true)] public string Bg { get; set; }
        }

        [Test]
        public void ColorAttrs_Contains_Only_IsColor_Marked()
        {
            var meta = ControlMeta.Build(typeof(FakeControl));
            CollectionAssert.AreEquivalent(new[] { "color" }, meta.ColorAttrs);
            CollectionAssert.AreEquivalent(new[] { "bg" }, meta.SpriteAttrs);
        }
    }
}
