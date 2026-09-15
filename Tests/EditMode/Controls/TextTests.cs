using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using R3;
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

        // --- overflow: maps onto TMP's overflowMode (the "what to do once text still doesn't fit"
        // layer). Orthogonal to wrap (line-wrapping) and autosize (the "try to squeeze it in" layer).
        [TestCase("overflow", TextOverflowModes.Overflow)]
        [TestCase("ellipsis", TextOverflowModes.Ellipsis)]
        [TestCase("truncate", TextOverflowModes.Truncate)]
        public void Overflow_MapsToTmpOverflowMode(string token, TextOverflowModes mode)
        {
            var t = OpenText($"overflow='{token}'");
            Assert.AreEqual(mode, t.TmpComponent.overflowMode);
        }

        // Tokens are case-insensitive, mirroring align.
        [Test]
        public void Overflow_IsCaseInsensitive()
        {
            var t = OpenText("overflow='Ellipsis'");
            Assert.AreEqual(TextOverflowModes.Ellipsis, t.TmpComponent.overflowMode);
        }

        [Test]
        public void Overflow_UnknownToken_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => OpenText("overflow='clip'"));
            StringAssert.Contains("clip", ex.Message);
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

        // fontSize is a float on every text-bearing control — TMP_Text.fontSize is a float, and
        // <Markdown> already forwards a fractional BodySize. A "6.5" used to hit int.Parse and
        // kill the whole Screen with a ParseException.
        [Test]
        public void FontSize_accepts_fractional_value()
        {
            var t = OpenText("fontSize='6.5'");
            Assert.AreEqual(6.5f, t.TmpComponent.fontSize);
        }

        // --- deferred Awake. TMP's Awake runs LoadDefaultSettings() on a component whose fontSize
        // is still -99, and that block rewrites raycastTarget, textWrappingMode, fontSize, font
        // features and a 100x100 sizeDelta from TMP_Settings. Awake is not "at creation": a node
        // built under an inactive parent (a ScrollList row bound while its page is hidden) gets it
        // only when the parent is shown — after every attribute has been applied. Re-writing in
        // OnAfterApply does not help there; the block has to be defused at creation.
        //
        // The Text is anchor="stretch" so nothing measures it: an un-Awake'd TMP cannot be measured
        // at all (GetPreferredValues throws inside TMP), which is a separate, pre-existing limit.

        private static TMP_Text BindOneRowUnderAHiddenPage(string textAttrs, out Frame page)
        {
            UI.LoadDocument("test", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Template name='Row'><Frame width='stretch' height='40'><Text id='t' anchor='stretch' {textAttrs}>x</Text></Frame></Template>" +
                "<Screen name='S'><Frame id='page' anchor='stretch'>" +
                "<ScrollList id='list' anchor='center' size='300x40' itemTemplate='Row' sprite='none' color='#0000'/>" +
                "</Frame></Screen></PromptUGUI>");
            var s = UI.Open("S");
            page = s.Get<Frame>("page");
            page.Hidden = true;   // off BEFORE the rows exist, so their TMP Awake is deferred
            TMP_Text tmp = null;
            s.Get<ScrollList>("list").BindItems(
                Observable.Return<IReadOnlyList<int>>(new[] { 1 }),
                (IControl row, int _) => tmp = row.Get<Text>("t").TmpComponent);
            Assume.That(tmp, Is.Not.Null, "the row was bound");
            Assume.That(tmp.gameObject.activeInHierarchy, Is.False, "precondition: built under an inactive page");
            return tmp;
        }

        [Test]
        public void Text_BoundIntoAHiddenList_StaysClickThroughOnceShown()
        {
            var tmp = BindOneRowUnderAHiddenPage("", out var page);
            Assume.That(tmp.raycastTarget, Is.False);

            page.Hidden = false;   // the deferred Awake runs here

            Assert.IsFalse(tmp.raycastTarget,
                "TMP's LoadDefaultSettings would put TMP_Settings.enableRaycastTarget back");
        }

        [Test]
        public void Text_BoundIntoAHiddenList_KeepsWrapAndOverflowOnceShown()
        {
            // The documented single-line-ellipsis idiom, on a Text with no fontSize of its own.
            var tmp = BindOneRowUnderAHiddenPage("wrap='false' overflow='ellipsis'", out var page);
            Assume.That(tmp.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));

            page.Hidden = false;

            Assert.AreEqual(TextWrappingModes.NoWrap, tmp.textWrappingMode,
                "LoadDefaultSettings would reset the wrapping mode from TMP_Settings");
            Assert.AreEqual(TextOverflowModes.Ellipsis, tmp.overflowMode);
        }

        [Test]
        public void Text_WithNoFontSize_StillGetsTheProjectDefaultSize()
        {
            // Defusing LoadDefaultSettings must not change what an unsized Text looks like.
            var t = OpenText("");
            Assert.AreEqual(TMP_Settings.defaultFontSize, t.TmpComponent.fontSize);
        }
    }
}
