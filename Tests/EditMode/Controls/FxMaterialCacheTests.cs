using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// One shared material per distinct parameter set (spec 2026-09-02 §5.2) — the same bargain
    /// <c>ProceduralMaterialCache</c> makes, and for the same reason: <c>CanvasRenderer</c> ignores
    /// <c>MaterialPropertyBlock</c>, so per-instance parameters can only live in materials, and every
    /// distinct material is a batch break. Twenty icons wearing one <c>class="rare"</c> therefore
    /// come out as one material; a tweened radius walks the spare stack instead of allocating.
    /// </summary>
    public class FxMaterialCacheTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static FxParams Glow(float radius) =>
            new FxParams(0f, radius, Color.white, true, false, false);

        [Test]
        public void The_same_parameters_hand_out_the_same_material()
        {
            var p = Glow(6f);

            var a = FxMaterialCache.Acquire(p);
            var b = FxMaterialCache.Acquire(p);

            Assert.AreSame(a, b);
            Assert.AreEqual(1, FxMaterialCache.LiveMaterialCount);
            Assert.AreEqual("UI/ImageFx", a.shader.name);
            Assert.AreEqual(6f, a.GetFloat("_Glow"), 1e-4f);
        }

        [Test]
        public void Different_parameters_hand_out_different_materials()
        {
            var a = FxMaterialCache.Acquire(Glow(6f));
            var b = FxMaterialCache.Acquire(Glow(8f));

            Assert.AreNotSame(a, b);
            Assert.AreEqual(2, FxMaterialCache.LiveMaterialCount);
        }

        [Test]
        public void The_last_release_parks_the_material_for_reuse()
        {
            var p = Glow(6f);
            FxMaterialCache.Acquire(p);
            var mat = FxMaterialCache.Acquire(p);

            FxMaterialCache.Release(p);
            Assert.AreEqual(1, FxMaterialCache.LiveMaterialCount, "still one reference outstanding");
            Assert.AreEqual(0, FxMaterialCache.SpareCount);

            FxMaterialCache.Release(p);
            Assert.AreEqual(0, FxMaterialCache.LiveMaterialCount);
            Assert.AreEqual(1, FxMaterialCache.SpareCount, "parked, not destroyed");

            var next = FxMaterialCache.Acquire(Glow(9f));
            Assert.AreSame(mat, next, "the parked material is reconfigured, not a new one");
            Assert.AreEqual(9f, next.GetFloat("_Glow"), 1e-4f);
            Assert.AreEqual(0, FxMaterialCache.SpareCount);
        }

        [Test]
        public void Tweening_a_radius_allocates_nothing()
        {
            // What LMotion does to Glow every frame: a fresh key each time, the old one released.
            var previous = Glow(0.1f);
            FxMaterialCache.Acquire(previous);

            for (var i = 2; i <= 100; i++)
            {
                var next = Glow(i * 0.1f);
                FxMaterialCache.Acquire(next);
                FxMaterialCache.Release(previous);
                previous = next;
            }

            Assert.AreEqual(1, FxMaterialCache.LiveMaterialCount);
            Assert.LessOrEqual(FxMaterialCache.SpareCount, 1);
            Assert.LessOrEqual(CountFxMaterials(), 2,
                "a tween must not leave a trail of Material objects behind");
        }

        [Test]
        public void Parameters_that_cannot_show_are_normalised_away()
        {
            // Two keys that draw the identical pixels must BE one key, or the cache splits into
            // entries that render the same — two materials, two draw calls, no visible difference
            // (PanelParams zeroes an opaque panel's glass block for exactly this reason).
            Assert.AreEqual(new FxParams(0f, 6f, Color.red, true, false, false),
                            new FxParams(0f, 6f, Color.green, true, false, false),
                            "an explicit colour is meaningless while the glow takes its own");

            // glowColor="self/0.5": the rgb still cannot show, the strength can.
            Assert.AreEqual(new FxParams(0f, 6f, new Color(1f, 0f, 0f, 0.5f), true, false, false),
                            new FxParams(0f, 6f, new Color(0f, 1f, 0f, 0.5f), true, false, false),
                            "a self glow's rgb is normalised away");
            Assert.AreNotEqual(new FxParams(0f, 6f, new Color(1f, 1f, 1f, 0.5f), true, false, false),
                               new FxParams(0f, 6f, Color.white, true, false, false),
                               "but its strength is a real parameter");

            Assert.AreEqual(new FxParams(4f, 0f, Color.red, false, false, false),
                            new FxParams(4f, 0f, Color.green, true, false, false),
                            "with no glow at all, neither the colour nor its source can show");
        }

        [Test]
        public void ResetForTests_empties_both_the_live_set_and_the_spares()
        {
            FxMaterialCache.Acquire(Glow(6f));
            var parked = Glow(8f);
            FxMaterialCache.Acquire(parked);
            FxMaterialCache.Release(parked);
            Assert.AreEqual(1, FxMaterialCache.SpareCount);

            UI.ResetForTests();

            Assert.AreEqual(0, FxMaterialCache.LiveMaterialCount);
            Assert.AreEqual(0, FxMaterialCache.SpareCount);
            Assert.AreEqual(0, CountFxMaterials(), "EditMode runs must not accumulate HideAndDontSave objects");
        }

        private static int CountFxMaterials()
        {
            var n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name == "PromptUGUI/ImageFx") n++;
            return n;
        }
    }
}
