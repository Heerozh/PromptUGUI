using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Gradient stops and hints on the vertex path (spec 2026-09-01 VGS). The plain two-colour ramp
    /// keeps its old code path untouched; a shaped ramp slices the mesh at the stops instead.
    /// </summary>
    public class GradientTintStopTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("GradientTintStopTest", typeof(Image));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Set_Spec_RoundTrips()
        {
            var fx = _go.AddComponent<GradientTint>();
            var spec = ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f, 2f);

            fx.Set(spec);

            Assert.AreEqual(Color.red, fx.Spec.Top);
            Assert.AreEqual(Color.blue, fx.Spec.Bottom);
            Assert.AreEqual(0.3f, fx.Spec.TopStop, 1e-5f);
            Assert.AreEqual(0.6f, fx.Spec.BottomStop, 1e-5f);
            Assert.AreEqual(2f, fx.Spec.Curve, 1e-5f);
            Assert.IsTrue(fx.Spec.HasStops);
        }

        [Test]
        public void Set_ColorPair_KeepsTheConvenienceOverload()
        {
            var fx = _go.AddComponent<GradientTint>();

            fx.Set(Color.red, Color.blue);

            Assert.AreEqual(Color.red, fx.Top);
            Assert.AreEqual(Color.blue, fx.Bottom);
            Assert.IsTrue(fx.Spec.IsGradient);
            Assert.IsFalse(fx.Spec.HasStops, "a plain pair must stay on the untouched code path");
        }

        [Test]
        public void Set_ReplacingAShapedRampWithAPlainOne_ForgetsTheStops()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f, 2f));

            fx.Set(Color.red, Color.blue);

            Assert.IsFalse(fx.Spec.HasStops, "stale stops would keep slicing a mesh that no longer needs it");
        }
    }
}
