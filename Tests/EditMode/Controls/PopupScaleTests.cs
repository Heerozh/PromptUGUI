using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A panel hung off its host's corner (<c>&lt;Suggestions&gt;</c>, <c>&lt;TabMenu&gt;</c>) under a scaled ancestor:
    /// the anchor rect and the bounds are in root-canvas units, the panel's size, gap and position in its
    /// own. Mixing them put a <c>-end</c> list 38.4 units left of its field at <c>scale="0.8"</c>, pushed it back
    /// at the wrong edge, and capped its height short of the room.
    /// </summary>
    public class PopupScaleTests
    {
        private const string Row = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private Screen _s;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private (InputField, Suggestions) Open(string listAttrs, string scale = "0.8")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + $"<Screen name='S'><Frame id='box' anchor='center' size='600x400' scale='{scale}'>"
                                 + "<InputField id='f' anchor='center' width='240' height='44'>"
                                 + $"<Suggestions id='s' itemTemplate='Row' transition='0' {listAttrs}/></InputField>"
                                 + "</Frame></Screen></PromptUGUI>");
            _s = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return (_s.Get<InputField>("f"), _s.Get<Suggestions>("s"));
        }

        private static void Show(InputField field, Suggestions list, int rows)
        {
            field.SetEditingForTests(true);
            list.NotifyUserInput();
            var items = new string[rows];
            for (var i = 0; i < rows; i++) items[i] = "r" + i;
            list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => { });
            Canvas.ForceUpdateCanvases();
            Assume.That(list.IsOpen);
        }

        private static Canvas Root(IControl c) => c.GameObject.GetComponentInParent<Canvas>().rootCanvas;

        private static Rect InCanvas(IControl c) => PopupCore.RectInCanvas(c.RectTransform, Root(c));

        private static void Bounds(Rect r) => PopupBounds.BoundsOverride = () => r;

        // ── The report's three cases ───────────────────────────────────────────────────────

        [Test]
        public void below_end_lines_up_the_right_edges_under_a_scaled_parent()
        {
            Bounds(new Rect(-1000f, -1000f, 2000f, 2000f));
            var (field, list) = Open("placement='below-end'");
            Show(field, list, 2);

            Assert.AreEqual(InCanvas(field).xMax, InCanvas(list).xMax, 0.5f);
        }

        [Test]
        public void below_end_with_popupWidth_lines_up_the_right_edges_under_a_scaled_parent()
        {
            Bounds(new Rect(-1000f, -1000f, 2000f, 2000f));
            var (field, list) = Open("placement='below-end' popupWidth='360'");
            Show(field, list, 2);

            Assert.AreEqual(InCanvas(field).xMax, InCanvas(list).xMax, 0.5f);
            Assert.AreEqual(360f * 0.8f, InCanvas(list).width, 0.5f, "popupWidth is in the list's own units");
        }

        [Test]
        public void below_end_in_an_over_end_popover_under_a_scaled_parent()
        {
            Bounds(new Rect(-1000f, -1000f, 2000f, 2000f));
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + "<Screen name='S'><Frame id='box' anchor='center' size='600x400' scale='0.8'>"
                                 + "<Btn id='a' anchor='center' size='44x44'>A</Btn>"
                                 + "<Popover id='pop' on='click@a' placement='over-end' width='200' transition='0'>"
                                 + "<InputField id='pf' width='stretch' height='44'>"
                                 + "<Suggestions id='ps' itemTemplate='Row' transition='0' placement='below-end'/></InputField>"
                                 + "</Popover></Frame></Screen></PromptUGUI>");
            _s = UI.Open("S");
            var pop = _s.Get<Popover>("pop");
            pop.Open();
            Canvas.ForceUpdateCanvases();
            var field = _s.Get<InputField>("pf");
            var list = _s.Get<Suggestions>("ps");
            Show(field, list, 2);

            Assert.AreEqual(InCanvas(field).xMax, InCanvas(list).xMax, 0.5f);
        }

        // ── The same mix elsewhere on the path ─────────────────────────────────────────────

        [Test]
        public void A_wider_list_is_pushed_back_exactly_to_the_right_bound_under_a_scaled_parent()
        {
            var (field, list) = Open("popupWidth='360'");
            var f = InCanvas(field);
            var bounds = new Rect(f.xMin - 500f, -1000f, (f.xMax + 10f) - (f.xMin - 500f), 2000f);
            Bounds(bounds);
            Show(field, list, 2);

            Assert.AreEqual(bounds.xMax, InCanvas(list).xMax, 0.5f);
        }

        [Test]
        public void The_height_reaches_the_room_on_its_side_under_a_scaled_parent()
        {
            var (field, list) = Open("gap='4'");
            var f = InCanvas(field);
            Bounds(Rect.MinMaxRect(-1000f, f.yMin - 80f, 1000f, f.yMax + 40f));
            Show(field, list, 5);   // 150 units of rows, 120 canvas units: more than either side holds

            Assert.AreEqual(1f, list.RectTransform.pivot.y, "below: it was preferred and the other side is not roomier");
            Assert.AreEqual(80f - 4f * 0.8f, InCanvas(list).height, 0.5f, "80 canvas units below, less the scaled gap");
        }

        [Test]
        public void A_list_that_fits_its_side_does_not_flip_under_a_scaled_parent()
        {
            var (field, list) = Open("gap='0'");
            var f = InCanvas(field);
            Bounds(Rect.MinMaxRect(-1000f, f.yMin - 100f, 1000f, f.yMax + 1000f));
            Show(field, list, 4);   // 120 units of rows = 96 canvas units: fits in the 100 below

            Assert.AreEqual(1f, list.RectTransform.pivot.y, "still below");
            Assert.AreEqual(120f, list.RectTransform.rect.height, 0.5f, "whole: not capped");
        }

        // ── Unchanged ──────────────────────────────────────────────────────────────────────

        [Test]
        public void start_still_lines_up_the_left_edges_under_a_scaled_parent()
        {
            Bounds(new Rect(-1000f, -1000f, 2000f, 2000f));
            var (field, list) = Open("popupWidth='360'");
            Show(field, list, 2);

            Assert.AreEqual(InCanvas(field).xMin, InCanvas(list).xMin, 0.5f);
        }

        [Test]
        public void Unscaled_is_unchanged()
        {
            Bounds(new Rect(-1000f, -1000f, 2000f, 2000f));
            var (field, list) = Open("placement='below-end' popupWidth='360' gap='4'", scale: "1");
            Show(field, list, 2);

            Assert.AreEqual(InCanvas(field).xMax, InCanvas(list).xMax, 0.5f);
            Assert.AreEqual(4f, InCanvas(field).yMin - InCanvas(list).yMax, 0.5f);
        }
    }
}
