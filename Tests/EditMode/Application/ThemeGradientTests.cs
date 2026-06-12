using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class ThemeGradientTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private const string Footer = "</PromptUGUI>";

        private static void SeedSolid(string name, string baseName, params (string k, string v)[] entries)
        {
            var d = new Dictionary<string, ColorSpec>();
            foreach (var (k, v) in entries)
            {
                ColorUtility.TryParseHtmlString(v, out var c);
                d[k] = ColorSpec.Solid(c);
            }
            ThemeStore.Instance.Register(name, baseName, d, src: "test");
            ThemeStore.Instance.ResolveBases();
        }

        private static void SeedGradient(string name, string baseName, string tokenName, string topHex, string bottomHex)
        {
            ColorUtility.TryParseHtmlString(topHex, out var top);
            ColorUtility.TryParseHtmlString(bottomHex, out var bottom);
            var d = new Dictionary<string, ColorSpec>
            {
                [tokenName] = ColorSpec.Gradient(top, bottom),
            };
            ThemeStore.Instance.Register(name, baseName, d, src: "test");
            ThemeStore.Instance.ResolveBases();
        }

        // ── load-path tests (via XML round-trip through UI infrastructure) ──

        [Test]
        public void LoadDocumentAsync_RegistersGradient_EndToEnd()
        {
            // Round-trips through the real pipeline: SourceResolver → LoadDocumentAsync
            // → RegisterThemesAndAutoSet → ParseThemeColor → ThemeStore.
            var xml = Header +
                "<Theme name='t'><Color name='grad' value='#ffffff,#000000'/></Theme>" +
                "<Screen name='s'><Frame/></Screen>" +
                Footer;
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            var spec = ThemeStore.Instance.LookupChained("t", "grad");
            Assert.IsTrue(spec.HasValue, "gradient token must be registered after LoadDocumentAsync");
            Assert.IsTrue(spec.Value.IsGradient);
            Assert.AreEqual(Color.white, spec.Value.Top);
            Assert.AreEqual(Color.black, spec.Value.Bottom);
        }

        [Test]
        public void Load_GradientToken_IsGradient_Top_White_Bottom_Black()
        {
            var xml = Header +
                "<Theme name='t'><Color name='grad' value='#ffffff,#000000'/></Theme>" +
                Footer;
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual(1, doc.Themes.Count);
            Assert.AreEqual("#ffffff,#000000", doc.Themes[0].Colors[0].Value);

            // Seed via ThemeStore directly to mirror RegisterThemesAndAutoSet behavior
            ColorParser.TrySplitGradient("#ffffff,#000000", out var topRaw, out var bottomRaw, out _);
            ColorUtility.TryParseHtmlString(topRaw, out var topC);
            ColorUtility.TryParseHtmlString(bottomRaw, out var bottomC);
            var d = new Dictionary<string, ColorSpec>
            {
                ["grad"] = ColorSpec.Gradient(topC, bottomC),
            };
            ThemeStore.Instance.Register("t", null, d, "test");
            ThemeStore.Instance.ResolveBases();

            var spec = ThemeStore.Instance.LookupChained("t", "grad");
            Assert.IsTrue(spec.HasValue);
            Assert.IsTrue(spec.Value.IsGradient);
            Assert.AreEqual(Color.white, spec.Value.Top);
            Assert.AreEqual(Color.black, spec.Value.Bottom);
        }

        [Test]
        public void GradientToken_Inherited_By_Derived_Theme()
        {
            // base has gradient token; derived overrides a different token
            ColorUtility.TryParseHtmlString("#ffffff", out var white);
            ColorUtility.TryParseHtmlString("#000000", out var black);
            var baseColors = new Dictionary<string, ColorSpec>
            {
                ["grad"] = ColorSpec.Gradient(white, black),
                ["bg"] = ColorSpec.Solid(white),
            };
            ThemeStore.Instance.Register("base", null, baseColors, "test");

            ColorUtility.TryParseHtmlString("#ff0000", out var red);
            var derivedColors = new Dictionary<string, ColorSpec>
            {
                ["bg"] = ColorSpec.Solid(red),
            };
            ThemeStore.Instance.Register("derived", "base", derivedColors, "test");
            ThemeStore.Instance.ResolveBases();

            var spec = ThemeStore.Instance.LookupChained("derived", "grad");
            Assert.IsTrue(spec.HasValue, "gradient token should be inherited from base");
            Assert.IsTrue(spec.Value.IsGradient);
            Assert.AreEqual(Color.white, spec.Value.Top);
            Assert.AreEqual(Color.black, spec.Value.Bottom);
        }

        [Test]
        public void SolidToken_Still_Resolves_IsGradient_False()
        {
            SeedSolid("light", null, ("primary", "#ff8800"));

            var spec = ThemeStore.Instance.LookupChained("light", "primary");
            Assert.IsTrue(spec.HasValue);
            Assert.IsFalse(spec.Value.IsGradient);
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff), (Color32)spec.Value.Top);
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff), (Color32)spec.Value.Bottom,
                "solid: Bottom should equal Top");
        }

        [Test]
        public void Lookup_Returns_Top_Stop_For_Gradient()
        {
            SeedGradient("t", null, "grad", "#ffffff", "#000000");
            UI.Theme.Set("t");
            var color = UI.Theme.Lookup("grad");
            Assert.IsTrue(color.HasValue);
            Assert.AreEqual(Color.white, color.Value, "public Lookup returns the Top stop");
        }

        [Test]
        public void ColorSpec_Solid_Top_Equals_Bottom()
        {
            ColorUtility.TryParseHtmlString("#ff8800", out var orange);
            var spec = ColorSpec.Solid(orange);
            Assert.IsFalse(spec.IsGradient);
            Assert.AreEqual(spec.Top, spec.Bottom);
        }

        [Test]
        public void ColorSpec_Gradient_Preserves_Stops()
        {
            var spec = ColorSpec.Gradient(Color.white, Color.black);
            Assert.IsTrue(spec.IsGradient);
            Assert.AreEqual(Color.white, spec.Top);
            Assert.AreEqual(Color.black, spec.Bottom);
        }

        [Test]
        public void ColorSpec_Multiply_Scales_Both_Stops()
        {
            var half = new Color(0.5f, 0.5f, 0.5f, 1f);
            var spec = ColorSpec.Gradient(Color.white, Color.white).Multiply(half);
            Assert.IsTrue(spec.IsGradient, "Multiply must preserve IsGradient");
            Assert.AreEqual(half, spec.Top);
            Assert.AreEqual(half, spec.Bottom);
        }
    }
}
