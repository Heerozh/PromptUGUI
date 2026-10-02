using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Internal;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    [TestFixture]
    public class OrientationTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();
        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Set_true_activates_portrait_and_deactivates_landscape()
        {
            UI.Variants.Set("landscape", true);

            UI.Orientation.Set(true);

            Assert.IsTrue(UI.Variants.IsActive("portrait"));
            Assert.IsFalse(UI.Variants.IsActive("landscape"));
        }

        [Test]
        public void Set_false_activates_landscape_and_deactivates_portrait()
        {
            UI.Variants.Set("portrait", true);

            UI.Orientation.Set(false);

            Assert.IsFalse(UI.Variants.IsActive("portrait"));
            Assert.IsTrue(UI.Variants.IsActive("landscape"));
        }

        [Test]
        public void IsPortrait_reflects_portrait_variant()
        {
            UI.Orientation.Set(true);
            Assert.IsTrue(UI.Orientation.IsPortrait);

            UI.Orientation.Set(false);
            Assert.IsFalse(UI.Orientation.IsPortrait);
        }

        [Test]
        public void AutoTrack_defaults_to_true()
        {
            Assert.IsTrue(UI.Orientation.AutoTrack);
        }

        [Test]
        public void ResetForTests_clears_both_variants_and_restores_AutoTrack()
        {
            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(true);

            UI.ResetForTests();

            Assert.IsTrue(UI.Orientation.AutoTrack);
            Assert.IsFalse(UI.Variants.IsActive("portrait"));
            Assert.IsFalse(UI.Variants.IsActive("landscape"));
        }

        [Test]
        public void Tracker_Apply_with_portrait_size_activates_portrait()
        {
            OrientationTracker.ScreenSizeOverride = () => new Vector2(1080, 1920);

            OrientationTracker.Apply();

            Assert.IsTrue(UI.Variants.IsActive("portrait"));
            Assert.IsFalse(UI.Variants.IsActive("landscape"));
        }

        [Test]
        public void Tracker_Apply_with_landscape_size_activates_landscape()
        {
            OrientationTracker.ScreenSizeOverride = () => new Vector2(1920, 1080);

            OrientationTracker.Apply();

            Assert.IsTrue(UI.Variants.IsActive("landscape"));
            Assert.IsFalse(UI.Variants.IsActive("portrait"));
        }

        [Test]
        public void Tracker_Apply_with_square_size_treats_as_landscape()
        {
            // 等宽高 → 习惯 landscape（与 ApplyCanvasScaler 里 W>=H 锁宽逻辑一致）
            OrientationTracker.ScreenSizeOverride = () => new Vector2(1080, 1080);

            OrientationTracker.Apply();

            Assert.IsTrue(UI.Variants.IsActive("landscape"));
            Assert.IsFalse(UI.Variants.IsActive("portrait"));
        }

        [Test]
        public void Tracker_Apply_skips_when_AutoTrack_disabled()
        {
            UI.Orientation.AutoTrack = false;
            OrientationTracker.ScreenSizeOverride = () => new Vector2(1080, 1920);

            OrientationTracker.Apply();

            Assert.IsFalse(UI.Variants.IsActive("portrait"));
            Assert.IsFalse(UI.Variants.IsActive("landscape"));
        }

        [Test]
        public void Tracker_Apply_skips_when_screen_size_invalid()
        {
            OrientationTracker.ScreenSizeOverride = () => new Vector2(0, 0);

            OrientationTracker.Apply();

            Assert.IsFalse(UI.Variants.IsActive("portrait"));
            Assert.IsFalse(UI.Variants.IsActive("landscape"));
        }

        private static string Pair() =>
            $"portrait={UI.Variants.IsActive("portrait")}, landscape={UI.Variants.IsActive("landscape")}";

        [Test]
        public void Set_flips_the_pair_with_one_Changed()
        {
            UI.Orientation.Set(false);
            var seen = new List<string>();
            using var sub = UI.VariantStore.Changed.Subscribe(_ => seen.Add(Pair()));

            UI.Orientation.Set(true);
            CollectionAssert.AreEqual(new[] { "portrait=True, landscape=False" }, seen,
                "landscape → portrait: one Changed, never both active");

            seen.Clear();
            UI.Orientation.Set(false);
            CollectionAssert.AreEqual(new[] { "portrait=False, landscape=True" }, seen,
                "portrait → landscape: one Changed, never neither active");

            seen.Clear();
            UI.Orientation.Set(false);
            Assert.IsEmpty(seen, "an unchanged orientation (the tracker calls Set every frame) fires nothing");
        }

        // The stretched axis swaps with the orientation (sidebar ↔ bottom sheet). Each orientation is valid
        // alone; a mix of the two — portrait's anchor with landscape's width — puts a size on a stretched
        // axis, which ApplyCommon rejects.
        [Test]
        public void Flip_re_solves_an_axis_swapping_node_without_error()
        {
            using var r3 = new R3ExceptionCapture();
            UI.Orientation.Set(false);
            UI.LoadDocument("test", @"<PromptUGUI version='1'><Screen name='S'>
  <Frame id='sheet' anchor.landscape='stretch-left' width.landscape='320'
                    anchor.portrait='bottom-stretch' height.portrait='400'/>
</Screen></PromptUGUI>");
            var rt = UI.Open("S").Get<Frame>("sheet").RectTransform;

            UI.Orientation.Set(true);
            Assert.IsEmpty(r3.Caught, "landscape → portrait");
            Assert.AreEqual(new Vector2(0, 0), rt.anchorMin);
            Assert.AreEqual(new Vector2(1, 0), rt.anchorMax);
            Assert.AreEqual(400f, rt.sizeDelta.y, 0.01f);

            UI.Orientation.Set(false);
            Assert.IsEmpty(r3.Caught, "portrait → landscape");
            Assert.AreEqual(new Vector2(0, 0), rt.anchorMin);
            Assert.AreEqual(new Vector2(0, 1), rt.anchorMax);
            Assert.AreEqual(320f, rt.sizeDelta.x, 0.01f);
        }

        // A pass aborted mid-flip had already written Slider.value but not its baseline, so the next pass
        // read the value as a runtime takeover and never applied the declared one again.
        [Test]
        public void Flip_keeps_a_variant_runtime_state_reversible()
        {
            using var r3 = new R3ExceptionCapture();
            UI.Orientation.Set(false);
            UI.LoadDocument("test", @"<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' value='0.5' value.portrait='0.2'
          anchor.landscape='stretch-left' width.landscape='40'
          anchor.portrait='bottom-stretch' height.portrait='40'/>
</Screen></PromptUGUI>");
            var slider = UI.Open("S").Get<Slider>("s");
            Assert.AreEqual(0.5f, slider.Value, 1e-4f);

            UI.Orientation.Set(true);
            Assert.AreEqual(0.2f, slider.Value, 1e-4f);

            UI.Orientation.Set(false);
            Assert.AreEqual(0.5f, slider.Value, 1e-4f, "back to the base value");
        }

        // ReSolve runs inside the Screen's Variants.Changed subscription, so an exception there never reaches
        // Orientation.Set: R3 hands it to the handler snapshotted at Subscribe time (Debug.LogException with
        // R3 for Unity, Console.WriteLine with core R3 alone). Installed before UI.Open, this sees it either way.
        private sealed class R3ExceptionCapture : IDisposable
        {
            private readonly Action<Exception> _previous = ObservableSystem.GetUnhandledExceptionHandler();
            public readonly List<Exception> Caught = new();

            public R3ExceptionCapture() => ObservableSystem.RegisterUnhandledExceptionHandler(Caught.Add);

            public void Dispose() => ObservableSystem.RegisterUnhandledExceptionHandler(_previous);
        }
    }
}
