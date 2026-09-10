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

        // ───── columns: the Content group becomes a grid ─────

        [Test]
        public void Columns_replaces_the_content_group_with_a_configured_GridLayoutGroup()
        {
            var content = ContentOf(OpenList("columns='4' cellSize='66x100'"));

            Assert.AreEqual(1, content.GetComponents<LayoutGroup>().Length,
                "LayoutGroup is [DisallowMultipleComponent] — the old group must be gone, not merely shadowed");
            var grid = content.GetComponent<GridLayoutGroup>();
            Assert.IsNotNull(grid);
            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
            Assert.AreEqual(4, grid.constraintCount);
            Assert.AreEqual(GridLayoutGroup.Corner.UpperLeft, grid.startCorner);
            Assert.AreEqual(GridLayoutGroup.Axis.Horizontal, grid.startAxis);
            Assert.AreEqual(TextAnchor.UpperLeft, grid.childAlignment);
            Assert.AreEqual(new Vector2(66f, 100f), grid.cellSize);
        }

        [Test]
        public void Grid_mode_scrolls_vertically_like_the_vertical_branch()
        {
            var list = OpenList("columns='4' cellSize='66x100'");
            var content = ContentOf(list);
            var scroll = list.GameObject.GetComponent<ScrollRect>();

            Assert.IsTrue(scroll.vertical, "grid mode grows downwards, so it scrolls vertically");
            Assert.IsFalse(scroll.horizontal);
            var fitter = content.GetComponent<ContentSizeFitter>();
            Assert.AreEqual(ContentSizeFitter.FitMode.Unconstrained, fitter.horizontalFit);
            Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit);
            Assert.AreEqual(new Vector2(0f, 1f), content.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), content.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 1f), content.pivot);
        }

        [Test]
        public void Grid_configuration_is_independent_of_attribute_arrival_order()
        {
            // ControlAttributeApplier walks a HashSet, so `columns` may arrive after `cellSize` —
            // the setters must only stash values and let one replay push them into the live group.
            var a = ContentOf(OpenList("columns='4' cellSize='66x100' spacing='4,8' padding='2,3,4,5'"))
                .GetComponent<GridLayoutGroup>();
            UI.ResetForTests();
            var b = ContentOf(OpenList("padding='2,3,4,5' spacing='4,8' cellSize='66x100' columns='4'"))
                .GetComponent<GridLayoutGroup>();

            Assert.AreEqual(a.constraintCount, b.constraintCount);
            Assert.AreEqual(a.cellSize, b.cellSize);
            Assert.AreEqual(a.spacing, b.spacing);
            // RectOffset is a class with no value-semantic Equals — compare the sides.
            Assert.AreEqual(
                (a.padding.left, a.padding.right, a.padding.top, a.padding.bottom),
                (b.padding.left, b.padding.right, b.padding.top, b.padding.bottom));
        }

        [Test]
        public void Grid_mode_spacing_and_padding_reach_the_GridLayoutGroup()
        {
            var grid = ContentOf(OpenList("columns='4' cellSize='40x40' spacing='4,8' padding='2,3,4,5'"))
                .GetComponent<GridLayoutGroup>();

            Assert.AreEqual(new Vector2(8f, 4f), grid.spacing, "\"V,H\" → Vector2(x=H, y=V)");
            Assert.AreEqual(5, grid.padding.left);
            Assert.AreEqual(3, grid.padding.right);
            Assert.AreEqual(2, grid.padding.top);
            Assert.AreEqual(4, grid.padding.bottom);
        }

        [Test]
        public void Single_value_spacing_fills_both_grid_axes()
        {
            var grid = ContentOf(OpenList("columns='3' cellSize='40x40' spacing='4'"))
                .GetComponent<GridLayoutGroup>();

            Assert.AreEqual(new Vector2(4f, 4f), grid.spacing);
        }

        [Test]
        public void Horizontal_direction_wins_over_columns_at_runtime()
        {
            // The combination is a lint error (PUI-SCROLL-COLUMNS-DIRECTION); v1 has no row-major
            // grid, so the runtime keeps the single row rather than inventing one.
            var content = ContentOf(OpenList("columns='4' cellSize='40x40' direction='horizontal'"));

            Assert.IsNotNull(content.GetComponent<HorizontalLayoutGroup>());
            Assert.IsNull(content.GetComponent<GridLayoutGroup>());
        }

        // ───── swapping the group type ─────

        [Test]
        public void Columns_zero_falls_back_to_the_direction_group()
        {
            var content = ContentOf(OpenList("columns='0' cellSize='40x40'"));

            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>());
            Assert.IsNull(content.GetComponent<GridLayoutGroup>());
        }

        [Test]
        public void A_variant_can_leave_and_re_enter_grid_mode_without_stacking_groups()
        {
            // `columns` resolving to null is SKIPPED, not reverted (ControlAttributeApplier does
            // `if (v == null) continue;`) — so "0" is how a variant spells "no grid here".
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>"
                    + "<Screen name='S'>"
                    + "<ScrollList id='sl' columns='4' columns.portrait='0' cellSize='40x40'/>"
                    + "</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var content = ContentOf(UI.Open("S").Get<ScrollList>("sl"));

            Assert.IsNotNull(content.GetComponent<GridLayoutGroup>(), "landscape base is the grid");

            UI.Variants.Set("portrait", true);
            Assert.AreEqual(1, content.GetComponents<LayoutGroup>().Length, "no stranded group");
            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>());
            Assert.IsNull(content.GetComponent<GridLayoutGroup>());

            UI.Variants.Set("portrait", false);
            Assert.AreEqual(1, content.GetComponents<LayoutGroup>().Length);
            Assert.IsNotNull(content.GetComponent<GridLayoutGroup>());
        }

        [Test]
        public void A_ReSolve_that_changes_nothing_keeps_the_same_group_instance()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>"
                    + "<ScrollList id='sl' columns='4' cellSize='40x40'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var before = ContentOf(screen.Get<ScrollList>("sl")).GetComponent<GridLayoutGroup>();

            screen.ReSolve();

            var after = ContentOf(screen.Get<ScrollList>("sl")).GetComponent<GridLayoutGroup>();
            Assert.AreSame(before, after,
                "the group is only swapped on a real type change — a no-op ReSolve must not churn it");
        }

        [Test]
        public void CellSize_variant_reaches_the_live_GridLayoutGroup()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>"
                    + "<Screen name='S'>"
                    + "<ScrollList id='sl' columns='4' cellSize='66x100' cellSize.portrait='67x100'/>"
                    + "</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var grid = ContentOf(UI.Open("S").Get<ScrollList>("sl")).GetComponent<GridLayoutGroup>();

            Assert.AreEqual(66f, grid.cellSize.x, 0.001f);

            UI.Variants.Set("portrait", true);
            Assert.AreEqual(67f, grid.cellSize.x, 0.001f);

            UI.Variants.Set("portrait", false);
            Assert.AreEqual(66f, grid.cellSize.x, 0.001f, "base value reverts when the variant clears");
        }
    }
}
