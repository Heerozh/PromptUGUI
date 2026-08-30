using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// Stop positions through the resolver: <c>UI.Theme.ResolveSpec</c>, the
    /// <c>&lt;Color value&gt;</c> definition site, and what <see cref="ColorSpec"/> carries.
    /// </summary>
    public class GradientStopResolveTests
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

        // ── ColorSpec ────────────────────────────────────────────────────────────

        [Test]
        public void Solid_HasNoStops()
        {
            var spec = ColorSpec.Solid(Color.red);
            Assert.IsFalse(spec.HasStops);
            Assert.AreEqual(0f, spec.TopStop);
            Assert.AreEqual(1f, spec.BottomStop);
        }

        [Test]
        public void DefaultGradient_HasNoStops()
        {
            Assert.IsFalse(ColorSpec.Gradient(Color.red, Color.blue).HasStops);
        }

        [Test]
        public void MovedGradient_HasStops()
        {
            Assert.IsTrue(ColorSpec.Gradient(Color.red, Color.blue, 0.7f, 1f).HasStops);
        }

        [Test]
        public void Multiply_KeepsStops()
        {
            // A state multiplier changes the colours, never the shape of the ramp.
            var spec = ColorSpec.Gradient(Color.white, Color.white, 0.3f, 0.6f).Multiply(Color.red);
            Assert.AreEqual(0.3f, spec.TopStop, 1e-5f);
            Assert.AreEqual(0.6f, spec.BottomStop, 1e-5f);
            Assert.AreEqual(Color.red, spec.Top);
        }

        // ── ResolveSpec ──────────────────────────────────────────────────────────

        [Test]
        public void ResolveSpec_BothStops_LandNormalized()
        {
            var spec = UI.Theme.ResolveSpec("#ffffff 30%,#000000 60%");
            Assert.IsTrue(spec.IsGradient);
            Assert.AreEqual(Color.white, spec.Top);
            Assert.AreEqual(Color.black, spec.Bottom);
            Assert.AreEqual(0.3f, spec.TopStop, 1e-5f);
            Assert.AreEqual(0.6f, spec.BottomStop, 1e-5f);
        }

        [Test]
        public void ResolveSpec_TopStopOnly_BottomDefaultsToOne()
        {
            var spec = UI.Theme.ResolveSpec("#ffffff 70%,#000000");
            Assert.AreEqual(0.7f, spec.TopStop, 1e-5f);
            Assert.AreEqual(1f, spec.BottomStop, 1e-5f);
        }

        [Test]
        public void ResolveSpec_StopWithAlphaSuffix_BothSurvive()
        {
            var spec = UI.Theme.ResolveSpec("#ffffff/0.45 70%,#000000/0.45");
            Assert.AreEqual(0.45f, spec.Top.a, 1e-3f);
            Assert.AreEqual(0.45f, spec.Bottom.a, 1e-3f);
            Assert.AreEqual(0.7f, spec.TopStop, 1e-5f);
        }

        [Test]
        public void ResolveSpec_NoStops_StaysFullHeight()
        {
            var spec = UI.Theme.ResolveSpec("#ffffff,#000000");
            Assert.IsFalse(spec.HasStops);
        }

        [Test]
        public void ResolveSpec_GradientToken_CarriesItsOwnStops()
        {
            Seed("panel-grad", ColorSpec.Gradient(Color.white, Color.black, 0.7f, 1f));
            var spec = UI.Theme.ResolveSpec("panel-grad");
            Assert.AreEqual(0.7f, spec.TopStop, 1e-5f);
        }

        [Test]
        public void ResolveSpec_GradientTokenWithAlpha_KeepsStops()
        {
            Seed("panel-grad", ColorSpec.Gradient(Color.white, Color.black, 0.7f, 1f));
            var spec = UI.Theme.ResolveSpec("panel-grad/0.5");
            Assert.AreEqual(0.7f, spec.TopStop, 1e-5f);
            Assert.AreEqual(0.5f, spec.Top.a, 1e-3f);
        }

        [Test]
        public void ResolveSpec_StopOnSolid_Throws()
        {
            var ex = Assert.Throws<System.Exception>(() => UI.Theme.ResolveSpec("#ffffff 70%"));
            StringAssert.Contains("two-colour gradient", ex.Message);
        }

        [Test]
        public void ResolveSpec_InvertedStops_Throws()
        {
            var ex = Assert.Throws<System.Exception>(() => UI.Theme.ResolveSpec("#ffffff 70%,#000000 30%"));
            StringAssert.Contains("second stop position", ex.Message);
        }

        [Test]
        public void ResolveSpec_OutOfRangeStop_Throws()
        {
            var ex = Assert.Throws<System.Exception>(() => UI.Theme.ResolveSpec("#ffffff 120%,#000000"));
            StringAssert.Contains("0%..100%", ex.Message);
        }

        // ── colour hint ─────────────────────────────────────────────────────────

        [Test]
        public void ResolveSpec_Hint_BendsTheRamp()
        {
            var spec = UI.Theme.ResolveSpec("#ffffff, 70%, #000000");
            Assert.IsTrue(spec.IsGradient);
            Assert.AreEqual(Color.white, spec.Top);
            Assert.AreEqual(Color.black, spec.Bottom);
            Assert.AreEqual(0f, spec.TopStop, 1e-5f);
            Assert.AreEqual(1f, spec.BottomStop, 1e-5f);
            // Half mix at 70% is the whole point of the hint.
            Assert.AreEqual(0.5f, Mathf.Pow(0.7f, spec.Curve), 1e-3f);
            Assert.IsTrue(spec.HasStops, "a hint is just as undrawable on the vertex path as a stop");
        }

        [Test]
        public void ResolveSpec_NoHint_IsLinear()
        {
            Assert.AreEqual(1f, UI.Theme.ResolveSpec("#ffffff,#000000").Curve, 1e-6f);
            Assert.AreEqual(1f, UI.Theme.ResolveSpec("#ffffff").Curve, 1e-6f);
        }

        [Test]
        public void ResolveSpec_HintAndStops_Compose()
        {
            var spec = UI.Theme.ResolveSpec("#ffffff 20%, 60%, #000000");
            Assert.AreEqual(0.2f, spec.TopStop, 1e-5f);
            Assert.AreEqual(1f, spec.Curve, 1e-4f, "60% is the midpoint of the 20%..100% ramp");
        }

        [Test]
        public void ResolveSpec_HintOutsideStops_Throws()
        {
            var ex = Assert.Throws<System.Exception>(
                () => UI.Theme.ResolveSpec("#ffffff 40%, 20%, #000000"));
            StringAssert.Contains("between the two stop positions", ex.Message);
        }

        [Test]
        public void ResolveSpec_HintToken_KeepsCurveThroughAlpha()
        {
            Seed("panel-grad", ColorSpec.Gradient(Color.white, Color.black, 0f, 1f, 1.943f));
            var spec = UI.Theme.ResolveSpec("panel-grad/0.5");
            Assert.AreEqual(1.943f, spec.Curve, 1e-3f);
            Assert.AreEqual(0.5f, spec.Top.a, 1e-3f);
        }

        [Test]
        public void Multiply_KeepsCurve()
        {
            var spec = ColorSpec.Gradient(Color.white, Color.white, 0f, 1f, 2f).Multiply(Color.red);
            Assert.AreEqual(2f, spec.Curve, 1e-5f);
        }

        [Test]
        public void ThemeToken_MayCarryAHint()
        {
            var xml = Header +
                "<Theme name='t'><Color name='g' value='#ffffff, 70%, #000000'/></Theme>" +
                "<Screen name='s'><Frame/></Screen>" + Footer;
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            var spec = ThemeStore.Instance.LookupChained("t", "g");
            Assert.IsTrue(spec.HasValue);
            Assert.AreEqual(0.5f, Mathf.Pow(0.7f, spec.Value.Curve), 1e-3f);
        }

        // ── <Color value="…"> definition site ────────────────────────────────────

        [Test]
        public void ThemeToken_MayCarryStops()
        {
            var xml = Header +
                "<Theme name='t'><Color name='panel-grad' value='#4a6fa5 70%,#c9a227'/></Theme>" +
                "<Screen name='s'><Frame/></Screen>" + Footer;
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            var spec = ThemeStore.Instance.LookupChained("t", "panel-grad");
            Assert.IsTrue(spec.HasValue);
            Assert.IsTrue(spec.Value.IsGradient);
            Assert.AreEqual(0.7f, spec.Value.TopStop, 1e-5f);
            Assert.AreEqual(1f, spec.Value.BottomStop, 1e-5f);
        }

        [Test]
        public void ThemeToken_BadStop_IsAParseError()
        {
            var xml = Header + "<Theme name='t'><Color name='g' value='#ffffff 70,#000000'/></Theme>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("percentage", ex.Message);
        }

        [Test]
        public void ThemeToken_StopOnSolid_IsAParseError()
        {
            var xml = Header + "<Theme name='t'><Color name='g' value='#ffffff 70%'/></Theme>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("two-colour gradient", ex.Message);
        }

        [Test]
        public void ThemeToken_StopsDoNotBreakTheLiteralCheck()
        {
            // The hex validator must see "#4a6fa5", not "#4a6fa5 70%".
            var xml = Header + "<Theme name='t'><Color name='g' value='#4a6fa5 70%,#c9a227'/></Theme>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }
    }
}
