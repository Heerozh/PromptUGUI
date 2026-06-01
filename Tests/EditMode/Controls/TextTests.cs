using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TextTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Text OpenText(string attrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t' {attrs}>hi</Text>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Text>("t");
        }

        // --- align: horizontal-only values stay vertically Middle (backward compat with the
        // old left/center/right behaviour, which mapped to TextAlignmentOptions.*=horizontal|Middle).

        [TestCase("left", HorizontalAlignmentOptions.Left)]
        [TestCase("center", HorizontalAlignmentOptions.Center)]
        [TestCase("right", HorizontalAlignmentOptions.Right)]
        [TestCase("justified", HorizontalAlignmentOptions.Justified)]
        [TestCase("flush", HorizontalAlignmentOptions.Flush)]
        [TestCase("geo", HorizontalAlignmentOptions.Geometry)]
        public void Align_HorizontalOnly_KeepsVerticalMiddle(string token, HorizontalAlignmentOptions h)
        {
            var t = OpenText($"align='{token}'");
            Assert.AreEqual(h, t.TmpComponent.horizontalAlignment);
            Assert.AreEqual(VerticalAlignmentOptions.Middle, t.TmpComponent.verticalAlignment);
        }

        // --- align: vertical-only values default horizontal to Left.

        [TestCase("top", VerticalAlignmentOptions.Top)]
        [TestCase("middle", VerticalAlignmentOptions.Middle)]
        [TestCase("bottom", VerticalAlignmentOptions.Bottom)]
        [TestCase("baseline", VerticalAlignmentOptions.Baseline)]
        [TestCase("midline", VerticalAlignmentOptions.Geometry)]
        [TestCase("capline", VerticalAlignmentOptions.Capline)]
        public void Align_VerticalOnly_DefaultsHorizontalLeft(string token, VerticalAlignmentOptions v)
        {
            var t = OpenText($"align='{token}'");
            Assert.AreEqual(HorizontalAlignmentOptions.Left, t.TmpComponent.horizontalAlignment);
            Assert.AreEqual(v, t.TmpComponent.verticalAlignment);
        }

        // --- align: combined "<h>-<v>" / "<v>-<h>" sets both axes, order-independent.

        [TestCase("bottom-right", HorizontalAlignmentOptions.Right, VerticalAlignmentOptions.Bottom)]
        [TestCase("right-bottom", HorizontalAlignmentOptions.Right, VerticalAlignmentOptions.Bottom)]
        [TestCase("top-center", HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Top)]
        [TestCase("capline-flush", HorizontalAlignmentOptions.Flush, VerticalAlignmentOptions.Capline)]
        public void Align_Combined_SetsBothAxes(
            string token, HorizontalAlignmentOptions h, VerticalAlignmentOptions v)
        {
            var t = OpenText($"align='{token}'");
            Assert.AreEqual(h, t.TmpComponent.horizontalAlignment);
            Assert.AreEqual(v, t.TmpComponent.verticalAlignment);
        }

        [Test]
        public void Align_UnknownToken_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => OpenText("align='bottm'"));
            StringAssert.Contains("bottm", ex.Message);
        }

        [Test]
        public void Visual_ColorDefaultsToDarkGrey()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t'>hi</Text>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.AreEqual(ProceduralBuilders.DefaultLabelColor, text.TmpComponent.color);
        }

        [Test]
        public void Visual_ExplicitColorOverridesDefault()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t' color='#ff0000'>hi</Text>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.AreEqual(Color.red, text.TmpComponent.color);
        }
    }
}
