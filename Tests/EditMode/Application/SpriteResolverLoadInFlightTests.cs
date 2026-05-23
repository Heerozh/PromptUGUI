using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// Covers the "resolver-load in-flight" fallback path: when no
    /// <c>UI.SpriteResolver</c> is installed yet but a loader (typically
    /// <c>SpriteResolverHelpers.UseAddressableSpriteSetResolver</c>) has
    /// announced it is loading via <c>UI.BeginSpriteResolverLoad</c>, both
    /// <c>UI.ResolveSprite</c> and the <c>Icon</c> setter must stay silent
    /// (no error log, sprite=null) and the matching <c>EndSpriteResolverLoad</c>
    /// must broadcast a Variant change so open Screens re-resolve.
    /// </summary>
    public class SpriteResolverLoadInFlightTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void ResolveSprite_silent_when_load_in_flight()
        {
            UI.BeginSpriteResolverLoad();
            try
            {
                var sprite = UI.ResolveSprite("ui:any");
                Assert.IsNull(sprite,
                    "in-flight + null resolver → ResolveSprite returns null without logging");
            }
            finally
            {
                UI.EndSpriteResolverLoad();
            }
        }

        [Test]
        public void ResolveSprite_logs_error_when_no_load_in_flight()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            var sprite = UI.ResolveSprite("ui:any");
            Assert.IsNull(sprite);
        }

        [Test]
        public void EndSpriteResolverLoad_fires_VariantChanged_when_count_reaches_zero()
        {
            var ticks = 0;
            using var sub = UI.VariantStore.Changed.Subscribe(_ => ticks++);
            UI.BeginSpriteResolverLoad();
            Assert.AreEqual(0, ticks, "Begin alone must not broadcast");
            UI.EndSpriteResolverLoad();
            Assert.AreEqual(1, ticks,
                "End that drops the counter to 0 must broadcast a Variant change " +
                "so open Screens ReSolve and re-render Icons");
        }

        [Test]
        public void EndSpriteResolverLoad_no_broadcast_until_outer_End()
        {
            var ticks = 0;
            using var sub = UI.VariantStore.Changed.Subscribe(_ => ticks++);
            UI.BeginSpriteResolverLoad();
            UI.BeginSpriteResolverLoad();
            UI.EndSpriteResolverLoad();
            Assert.AreEqual(0, ticks,
                "Inner End must not broadcast while another load is still in flight");
            UI.EndSpriteResolverLoad();
            Assert.AreEqual(1, ticks, "Outer End drops counter to 0 → broadcast");
        }

        [Test]
        public void Icon_silent_when_load_in_flight()
        {
            var go = new GameObject("icon-host");
            try
            {
                var icon = new Icon();
                icon.AttachTo(go);
                UI.BeginSpriteResolverLoad();
                try
                {
                    icon.Name = "ui:any";          // must not log
                }
                finally
                {
                    UI.EndSpriteResolverLoad();
                }
                var img = go.GetComponent<UnityEngine.UI.Image>();
                Assert.IsNotNull(img);
                Assert.IsNull(img.sprite,
                    "in-flight + null resolver → Icon stays empty silently");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Icon_logs_error_when_no_load_in_flight()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            var go = new GameObject("icon-host");
            try
            {
                var icon = new Icon();
                icon.AttachTo(go);
                icon.Name = "ui:any";
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResetForTests_clears_in_flight_count()
        {
            UI.BeginSpriteResolverLoad();
            UI.ResetForTests();
            // After reset the counter must be 0 again — verify by asserting that
            // ResolveSprite reverts to the error path.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            UI.ResolveSprite("ui:any");
        }
    }
}
