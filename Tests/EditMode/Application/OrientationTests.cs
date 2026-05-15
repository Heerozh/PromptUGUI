using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Internal;
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
    }
}
