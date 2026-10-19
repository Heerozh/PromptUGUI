using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using InputField = PromptUGUI.Controls.InputField;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Where the list goes (spec 2026-10-18 §6.8): as wide as the field, as tall as
    /// <c>min(content, maxHeight, room)</c>, on the preferred side unless it does not fit there and
    /// the other side is roomier. Bounds = the root canvas shrunk into the safe area, minus a visible
    /// on-screen keyboard.
    /// </summary>
    public class SuggestionsPlacementTests
    {
        // An 800×800 canvas centred on the origin; the field is 240×44 with its top at y.
        private static readonly Rect Bounds = new(-400f, -400f, 800f, 800f);
        private static Rect FieldAt(float top) => new(-120f, top - 44f, 240f, 44f);

        [SetUp] public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown()
        {
            SafeAreaTracker.SafeAreaOverride = null;
            SafeAreaTracker.ScreenSizeOverride = null;
            SafeAreaTracker.ScaleFactorOverride = null;
            UI.ResetForTests();
        }

        // ── the pure rule ──────────────────────────────────────────────────────────────────

        [Test]
        public void Below_by_default()
        {
            var (h, above) = SuggestionsPlacement.Solve(FieldAt(300f), Bounds, 100f, 320f, 4f, above: false, locked: false);
            Assert.IsFalse(above);
            Assert.AreEqual(100f, h);
        }

        [Test]
        public void Above_when_preferred()
        {
            var (_, above) = SuggestionsPlacement.Solve(FieldAt(0f), Bounds, 100f, 320f, 4f, above: true, locked: false);
            Assert.IsTrue(above);
        }

        [Test]
        public void Flips_when_the_preferred_side_lacks_room()
        {
            // 40 px below the field, 400 above.
            var (h, above) = SuggestionsPlacement.Solve(FieldAt(-316f), Bounds, 100f, 320f, 4f, above: false, locked: false);
            Assert.IsTrue(above);
            Assert.AreEqual(100f, h);
        }

        [Test]
        public void Height_is_content_clamped_by_maxHeight_and_room()
        {
            Assert.AreEqual(200f, SuggestionsPlacement.Solve(FieldAt(300f), Bounds, 500f, 200f, 4f, false, false).Height);
            // Room below = 300 − 44 − (−400) − 4 = 652; a cap of 1000 leaves the room as the limit.
            Assert.AreEqual(652f, SuggestionsPlacement.Solve(FieldAt(300f), Bounds, 2000f, 1000f, 4f, false, false).Height);
        }

        [Test]
        public void maxHeight_zero_means_room_only()
            => Assert.AreEqual(652f, SuggestionsPlacement.Solve(FieldAt(300f), Bounds, 2000f, 0f, 4f, false, false).Height);

        [Test]
        public void A_locked_side_never_flips_and_only_reclamps()
        {
            var (h, above) = SuggestionsPlacement.Solve(FieldAt(-316f), Bounds, 100f, 320f, 4f, above: false, locked: true);
            Assert.IsFalse(above);
            Assert.AreEqual(36f, h, "40 px below minus the 4 px gap");
        }

        // ── bounds ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Safe_area_reduces_the_room()
        {
            // A 100 px home bar at the bottom of a 1080×1920 screen, scale factor 1.
            SafeAreaTracker.SafeAreaOverride = () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride = () => new Vector2(1080f, 1920f);
            SafeAreaTracker.ScaleFactorOverride = () => 1f;

            var safe = SafeAreaTracker.ApplySafeInset(new Rect(-540f, -960f, 1080f, 1920f), 1f);

            Assert.AreEqual(-860f, safe.yMin, 0.01f);
            Assert.AreEqual(960f, safe.yMax, 0.01f);
        }

        [Test]
        public void Keyboard_height_reduces_the_room_below()
        {
            var full = new Rect(-540f, -960f, 1080f, 1920f);

            var bounds = PopupBounds.Compute(full, full, keyboardPx: 1200f, scaleFactor: 2f);

            Assert.AreEqual(-360f, bounds.yMin, 0.01f, "a 1200 px keyboard covers 600 canvas units");
            Assert.AreEqual(full.yMax, bounds.yMax);
            Assert.AreEqual(full, PopupBounds.Compute(full, full, 0f, 2f), "no keyboard, no change");
        }

        // ── wiring ─────────────────────────────────────────────────────────────────────────

        private static (InputField, Suggestions) Open(string listAttrs, float fieldY = 0f)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                 + "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
                                 + $"<Screen name='S'><InputField id='f' anchor='center' width='240' height='44'>"
                                 + $"<Suggestions id='s' itemTemplate='Row' transition='0' {listAttrs}/></InputField>"
                                 + "</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var field = screen.Get<InputField>("f");
            field.RectTransform.anchoredPosition = new Vector2(0f, fieldY);
            Canvas.ForceUpdateCanvases();
            return (field, screen.Get<Suggestions>("s"));
        }

        private static void Show(InputField field, Suggestions list, int rows)
        {
            field.SetEditingForTests(true);
            list.NotifyUserInput();
            var items = new string[rows];
            for (var i = 0; i < rows; i++) items[i] = "r" + i;
            list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => { });
            Canvas.ForceUpdateCanvases();
        }

        [Test]
        public void The_panel_is_as_wide_as_the_field_and_as_tall_as_two_rows()
        {
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            var (field, list) = Open("");

            Show(field, list, 2);

            Assert.IsTrue(list.IsOpen);
            Assert.AreEqual(240f, list.RectTransform.rect.width, 0.5f);
            Assert.AreEqual(60f, list.RectTransform.rect.height, 0.5f);
        }

        [Test]
        public void Content_taller_than_a_small_maxHeight_is_capped()
        {
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            var (field, list) = Open("maxHeight='50'");

            Show(field, list, 5);

            Assert.AreEqual(50f, list.RectTransform.rect.height, 0.5f);
        }

        [Test]
        public void A_short_bound_caps_the_height_at_the_room_on_the_chosen_side()
        {
            // The field's bottom is 22 below the canvas origin; 80 units of bounds under it, none above.
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -102f, 2000f, 124f);
            var (field, list) = Open("gap='4'");

            Show(field, list, 5);

            Assert.AreEqual(76f, list.RectTransform.rect.height, 0.5f, "80 below minus the gap");
            Assert.AreEqual(1f, list.RectTransform.pivot.y, "hangs below the field");
        }
    }
}
