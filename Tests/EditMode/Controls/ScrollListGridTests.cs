using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;ScrollList columns&gt;</c> grid mode (requirements §R1): the Content layout group is
    /// swapped between Grid / Horizontal / Vertical, and cellSize / spacing / padding survive the
    /// swap whatever order they arrive in — <c>ControlAttributeApplier</c> walks a HashSet, so
    /// attribute arrival order is genuinely unspecified.
    /// </summary>
    public class ScrollListGridTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static ScrollList OpenList(string attrs, string children = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>"
                    + "<Template name='Slot'><Frame/></Template>"
                    + $"<Screen name='S'><ScrollList id='sl' itemTemplate='Slot' {attrs}>{children}</ScrollList></Screen>"
                    + "</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");
            Canvas.ForceUpdateCanvases();
            return list;
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        // ───── spacing: single value and the two-part "V,H" form (SGS-D1) ─────

        [Test]
        public void Vertical_list_takes_the_vertical_half_of_a_two_part_spacing()
        {
            var content = ContentOf(OpenList("direction='vertical' spacing='10,20'"));

            Assert.AreEqual(10f, content.GetComponent<VerticalLayoutGroup>().spacing, 0.001f,
                "parts[0] is the vertical gap — the only one a single column has");
        }

        [Test]
        public void Horizontal_list_takes_the_horizontal_half_of_a_two_part_spacing()
        {
            var content = ContentOf(OpenList("direction='horizontal' spacing='10,20'"));

            Assert.AreEqual(20f, content.GetComponent<HorizontalLayoutGroup>().spacing, 0.001f,
                "parts[1] is the horizontal gap — the only one a single row has");
        }

        [Test]
        public void Single_value_spacing_still_applies_to_the_single_axis()
        {
            var content = ContentOf(OpenList("spacing='4'"));

            Assert.AreEqual(4f, content.GetComponent<VerticalLayoutGroup>().spacing, 0.001f);
        }
    }
}
