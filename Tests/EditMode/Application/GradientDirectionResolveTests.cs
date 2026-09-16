using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// Directions and three-plus stops through the resolver (spec 2026-09-17 §5.3): inline values,
    /// tokens that carry a direction, <c>token/alpha</c> over every stop, and the definition site.
    /// </summary>
    public class GradientDirectionResolveTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private const string Footer = "</PromptUGUI>";

        private static void Seed(string tokenName, ColorSpec spec)
        {
            ThemeStore.Instance.Register("t", null, new Dictionary<string, ColorSpec> { [tokenName] = spec }, src: "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("t");
        }

        [Test]
        public void ResolveSpec_Direction_Inline()
        {
            var spec = UI.Theme.ResolveSpec("to right, #ffffff, #000000");
            Assert.IsTrue(spec.IsGradient);
            Assert.AreEqual(2, spec.Count);
            Assert.AreEqual(90f, spec.Direction.AngleDeg, 1e-5f);
            Assert.AreEqual(Color.white, spec.Start);
            Assert.AreEqual(Color.black, spec.End);
        }

        [Test]
        public void ResolveSpec_ThreeStops_WithCornerAndHint()
        {
            var spec = UI.Theme.ResolveSpec("to bottom right, #ff0000, 30%, #00ff00 50%, #0000ff");
            Assert.AreEqual(3, spec.Count);
            Assert.AreEqual(GradientDirection.Kinds.Corner, spec.Direction.Kind);
            Assert.AreEqual(Color.red, spec.ColorAt(0));
            Assert.AreEqual(Color.green, spec.ColorAt(1));
            Assert.AreEqual(Color.blue, spec.ColorAt(2));
            Assert.AreEqual(0f, spec.StopAt(0), 1e-5f);
            Assert.AreEqual(0.5f, spec.StopAt(1), 1e-5f);
            Assert.AreEqual(1f, spec.StopAt(2), 1e-5f);
            Assert.AreNotEqual(1f, spec.CurveAt(0), "first segment carries the hint");
            Assert.AreEqual(1f, spec.CurveAt(1), 1e-6f, "second segment is linear");
            Assert.IsTrue(spec.HasStops);
        }

        [Test]
        public void ResolveSpec_FourStops_DefaultsSpreadEvenly()
        {
            var spec = UI.Theme.ResolveSpec("#ff0000, #00ff00, #0000ff, #ffffff");
            Assert.AreEqual(4, spec.Count);
            Assert.AreEqual(1f / 3f, spec.StopAt(1), 1e-5f);
            Assert.AreEqual(2f / 3f, spec.StopAt(2), 1e-5f);
        }

        [Test]
        public void ResolveSpec_TokensInsideStops_AndAlphaPerStop()
        {
            Seed("accent", ColorSpec.Solid(Color.red));
            var spec = UI.Theme.ResolveSpec("45deg, accent, accent/0.5, #0000ff");
            Assert.AreEqual(3, spec.Count);
            Assert.AreEqual(45f, spec.Direction.AngleDeg, 1e-5f);
            Assert.AreEqual(1f, spec.ColorAt(0).a, 1e-6f);
            Assert.AreEqual(0.5f, spec.ColorAt(1).a, 1e-6f);
        }

        [Test]
        public void ResolveSpec_GradientTokenAsOneStop_StillThrows()
        {
            Seed("grad", ColorSpec.Gradient(Color.white, Color.black));
            var ex = Assert.Throws<System.Exception>(() => UI.Theme.ResolveSpec("to right, grad, #000000, #ffffff"));
            StringAssert.Contains("cannot nest", ex.Message);
        }

        [Test]
        public void ResolveSpec_TokenCarriesDirectionAndStops()
        {
            Seed("edge-lit", ColorSpec.Gradient(GradientDirection.Corner(1, -1),
                                                 new[] { Color.cyan, new Color(0f, 1f, 1f, 0.3f), Color.cyan },
                                                 new[] { 0f, 0.5f, 1f }, new[] { 1f, 1f }));
            var spec = UI.Theme.ResolveSpec("edge-lit");
            Assert.AreEqual(3, spec.Count);
            Assert.AreEqual(GradientDirection.Corner(1, -1), spec.Direction);
        }

        [Test]
        public void ResolveSpec_TokenAlpha_ReplacesEveryStop_KeepsDirection()
        {
            Seed("edge-lit", ColorSpec.Gradient(GradientDirection.Angle(90f),
                                                 new[] { Color.cyan, new Color(0f, 1f, 1f, 0.3f), Color.cyan },
                                                 new[] { 0f, 0.5f, 1f }, new[] { 1f, 1f }));
            var spec = UI.Theme.ResolveSpec("edge-lit/0.25");
            for (var i = 0; i < 3; i++) Assert.AreEqual(0.25f, spec.ColorAt(i).a, 1e-6f, "stop " + i);
            Assert.AreEqual(90f, spec.Direction.AngleDeg, 1e-5f);
            Assert.AreEqual(0.5f, spec.StopAt(1), 1e-6f);
        }

        [Test]
        public void Resolve_SolidSignature_StillRejectsADirectedGradient()
        {
            var ex = Assert.Throws<System.Exception>(() => UI.Theme.Resolve("to right, #fff, #000"));
            StringAssert.Contains("does not support gradient", ex.Message);
        }

        [TestCase("to right, #fff", "at least two colours")]
        [TestCase("#fff, to right, #000", "first segment")]
        [TestCase("45deg #fff, #000", "own comma-separated segment")]
        [TestCase("to up, #fff, #000", "to <side or corner>")]
        [TestCase("#fff, #000, #111, #222, #333", "2 to 4 colours")]
        [TestCase("#fff 60%, #000 30%, #111", "must not decrease")]
        public void ResolveSpec_ShapeErrors_Throw(string value, string fragment)
        {
            var ex = Assert.Throws<System.Exception>(() => UI.Theme.ResolveSpec(value));
            StringAssert.Contains(fragment, ex.Message);
        }

        // ── definition site ─────────────────────────────────────────────────────

        [Test]
        public void ThemeToken_MayCarryDirectionAndThreeStops()
        {
            var xml = Header +
                "<Theme name='t'><Color name='edge-lit' value='to bottom right, #7ef3ff, #1c6d8a80 50%, #7ef3ff'/></Theme>" +
                "<Screen name='s'><Frame/></Screen>" + Footer;
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            var spec = ThemeStore.Instance.LookupChained("t", "edge-lit");
            Assert.IsTrue(spec.HasValue);
            Assert.AreEqual(3, spec.Value.Count);
            Assert.AreEqual(GradientDirection.Corner(1, -1), spec.Value.Direction);
            Assert.AreEqual(0.5f, spec.Value.StopAt(1), 1e-5f);
            Assert.AreEqual(0x80 / 255f, spec.Value.ColorAt(1).a, 1e-3f);
        }
    }
}
