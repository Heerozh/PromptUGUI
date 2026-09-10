using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ScrollListContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ScrollList_vertical_GetNativeSize_returns_vertical_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <ScrollList id='l'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var native = list.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(160f, native.Value.x, "vertical ScrollList: cross axis x = 160");
            Assert.AreEqual(200f, native.Value.y, "vertical ScrollList: main axis y = 200");
        }

        [Test]
        public void ScrollList_horizontal_GetNativeSize_returns_horizontal_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <ScrollList id='l' direction='horizontal'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var native = list.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(200f, native.Value.x, "horizontal ScrollList: main axis x = 200");
            Assert.AreEqual(160f, native.Value.y, "horizontal ScrollList: cross axis y = 160");
        }

        [Test]
        public void ScrollList_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x400'>
    <ScrollList id='l'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            Assert.AreEqual(160f, list.RectTransform.sizeDelta.x, 0.5f);
            Assert.AreEqual(200f, list.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void ScrollList_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <ScrollList id='l'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var le = list.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D4: ScrollList under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(200f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void ScrollList_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x400'>
    <ScrollList id='l' size='300x250'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            Assert.AreEqual(new Vector2(300f, 250f), list.RectTransform.sizeDelta);
        }

        // ───── grid mode: the cells are authoritative on the cross axis (SGS-D2) ─────

        private static ScrollList OpenLoose(string listAttrs)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>"
                    + "<Frame id='f' size='400x400'>"
                    + $"<ScrollList id='l' {listAttrs}/>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<ScrollList>("l");
        }

        [Test]
        public void Grid_native_width_is_the_columns_times_the_cells_plus_the_gaps()
        {
            // A 160-wide default would clip the 4th of four 66-wide columns before the author ever
            // sees the list, so grid mode derives its cross axis from what it is asked to hold.
            var native = OpenLoose("columns='4' cellSize='66x100' spacing='4' padding='0'").GetNativeSize();

            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(66f * 4 + 4f * 3, native.Value.x, 0.001f, "4 cells + 3 gaps");
            Assert.AreEqual(200f, native.Value.y, 0.001f, "how many rows are visible stays a viewport choice");
        }

        [Test]
        public void Grid_native_width_includes_the_horizontal_padding()
        {
            var native = OpenLoose("columns='4' cellSize='66x100' spacing='4' padding='0,8,0,8'").GetNativeSize();

            Assert.AreEqual(66f * 4 + 4f * 3 + 16f, native.Value.x, 0.001f);
        }

        [Test]
        public void Grid_native_width_uses_the_horizontal_half_of_a_two_part_spacing()
        {
            // Deliberately NOT a total of 160 — that is the non-grid default, and a coincidence
            // there would make this test pass without the feature.
            var native = OpenLoose("columns='3' cellSize='50x40' spacing='10,20' padding='0'").GetNativeSize();

            Assert.AreEqual(50f * 3 + 20f * 2, native.Value.x, 0.001f, "the H gap separates columns");
        }

        [Test]
        public void Columns_without_cellSize_keeps_the_plain_vertical_default()
        {
            // Guessing uGUI's 100x100 fallback would bake a number the author never wrote.
            var native = OpenLoose("columns='4'").GetNativeSize();

            Assert.AreEqual(new Vector2(160f, 200f), native.Value);
        }

        [Test]
        public void Columns_zero_keeps_the_plain_vertical_default()
        {
            var native = OpenLoose("columns='0' cellSize='66x100'").GetNativeSize();

            Assert.AreEqual(new Vector2(160f, 200f), native.Value);
        }

        [Test]
        public void Horizontal_direction_keeps_the_horizontal_default_even_with_columns()
        {
            var native = OpenLoose("columns='4' cellSize='66x100' direction='horizontal'").GetNativeSize();

            Assert.AreEqual(new Vector2(200f, 160f), native.Value);
        }

        [Test]
        public void Grid_with_no_size_sizes_its_rect_to_the_grid_native()
        {
            var list = OpenLoose("columns='4' cellSize='66x100' spacing='4' padding='0'");

            Assert.AreEqual(66f * 4 + 4f * 3, list.RectTransform.sizeDelta.x, 0.5f);
            Assert.AreEqual(200f, list.RectTransform.sizeDelta.y, 0.5f);
        }
    }
}
