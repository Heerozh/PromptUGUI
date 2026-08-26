using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// <c>&lt;Theme&gt;</c> gains <c>&lt;Style&gt;</c> children — the declaration half of theme-driven
    /// styles (2026-08-26 spec §3, §4.1, §6.3). Because <c>class=</c> is an attribute macro rather
    /// than a style engine, letting a theme carry one lifts theming from "swap colours" to "swap the
    /// whole skin": sprite, radius, font size, padding, glass parameters.
    /// </summary>
    public class ThemeStyleParsingTests
    {
        private static UIDocument Parse(string body) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>");

        private static ParseException ParseFails(string body) =>
            Assert.Throws<ParseException>(() => Parse(body));

        private static ThemeBlock Theme(string body, string name = "pixel") =>
            Parse(body).Themes.Single(t => t.Name == name);

        [Test]
        public void ThemeStyle_LandsInThemeBlock_NotTheGlobalPool()
        {
            var doc = Parse(@"
                <Style name='card' sprite='ui:panel' radius='16'/>
                <Theme name='pixel'>
                  <Color name='surface' value='#e8d8b0'/>
                  <Style name='card' sprite='px:panel' radius='0'/>
                </Theme>");

            Assert.AreEqual("ui:panel", doc.Styles["card"].Attributes["sprite"],
                "the global <Style> must be untouched — a theme overrides, it does not replace in place");

            var themed = doc.Themes.Single().Styles["card"];
            Assert.AreEqual("px:panel", themed.Attributes["sprite"]);
            Assert.AreEqual("0", themed.Attributes["radius"]);
            Assert.AreEqual(1, doc.Themes.Single().Colors.Count, "<Color> siblings still parse");
        }

        [Test]
        public void ThemeStyle_AcceptsVariantSuffixes()
        {
            var style = Theme("<Theme name='pixel'><Style name='card' radius='8' radius.mobile='4'/></Theme>")
                .Styles["card"];

            Assert.AreEqual("8", style.Attributes["radius"]);
            CollectionAssert.AreEqual(
                new[] { ("mobile", "4") }, style.VariantOverrides["radius"]);
        }

        // §6.3: the same four names a top-level <Style> rejects.
        [TestCase("id")]
        [TestCase("if")]
        [TestCase("class")]
        [TestCase("bind")]
        public void ThemeStyle_RejectsNodeIdentityAttributes(string attr)
        {
            var ex = ParseFails($"<Theme name='pixel'><Style name='card' {attr}='x'/></Theme>");
            StringAssert.Contains("is not a style attribute", ex.Message);
        }

        // §6.3: runtime-owned state. ControlAttributeApplier skips re-applying these once code has
        // taken them over, so a theme's value would be silently swallowed some of the time — worse
        // than never applying at all.
        [TestCase("text")]
        [TestCase("isOn")]
        [TestCase("value")]
        [TestCase("current")]
        public void ThemeStyle_RejectsRuntimeOwnedState(string attr)
        {
            var ex = ParseFails($"<Theme name='pixel'><Style name='card' {attr}='x'/></Theme>");
            StringAssert.Contains("runtime", ex.Message.ToLowerInvariant());
        }

        // §6.3: PUI-MASK-VARIANT already declares per-state mask switching unsupported in v1.
        // A theme must not become a second door into the same unsupported operation.
        [TestCase("mask")]
        [TestCase("showMask")]
        [TestCase("maskPadding")]
        public void ThemeStyle_RejectsMaskFamily_PointingAtTheExistingRule(string attr)
        {
            var ex = ParseFails($"<Theme name='pixel'><Style name='card' {attr}='rect'/></Theme>");
            StringAssert.Contains("PUI-MASK-VARIANT", ex.Message);
        }

        // The blacklist is theme-scoped. A global <Style> may still carry these — nothing about
        // them is newly broken there.
        [TestCase("text")]
        [TestCase("mask")]
        public void GlobalStyle_StillAcceptsWhatOnlyThemeStylesReject(string attr)
        {
            var doc = Parse($"<Style name='card' {attr}='x'/>");
            Assert.IsTrue(doc.Styles["card"].Attributes.ContainsKey(attr));
        }

        [Test]
        public void ThemeStyle_DuplicateNameWithinOneTheme_IsAnError()
        {
            var ex = ParseFails(
                "<Theme name='pixel'><Style name='card' radius='0'/><Style name='card' radius='4'/></Theme>");
            StringAssert.Contains("card", ex.Message);
        }

        [Test]
        public void SameStyleName_InTwoDifferentThemes_IsFine()
        {
            var doc = Parse(@"
                <Theme name='modern'><Style name='card' radius='16'/></Theme>
                <Theme name='pixel'><Style name='card' radius='0'/></Theme>");

            Assert.AreEqual("16", doc.Themes.Single(t => t.Name == "modern").Styles["card"].Attributes["radius"]);
            Assert.AreEqual("0", doc.Themes.Single(t => t.Name == "pixel").Styles["card"].Attributes["radius"]);
        }

        [Test]
        public void ThemeStyle_WithChildren_IsAnError()
        {
            var ex = ParseFails(
                "<Theme name='pixel'><Style name='card' radius='0'><Frame/></Style></Theme>");
            StringAssert.Contains("attribute pack", ex.Message);
        }

        // A style's identity includes its namespace, so a theme must be able to spell one — an
        // imported skin library is exactly what a theme most wants to re-skin.
        [Test]
        public void ThemeStyle_MayNameAnImportedPackWithItsNamespace()
        {
            var theme = Theme("<Theme name='pixel'><Style name='ui:card' radius='0'/></Theme>");
            Assert.IsTrue(theme.Styles.ContainsKey("ui:card"),
                "kept as written — the resolver parses it the way class= parses a reference");
        }

        // …but a top-level <Style> may not: its namespace comes from the as= of whoever imports the
        // document. A colon there would produce a pack nothing can address.
        [Test]
        public void GlobalStyle_WithANamespacedName_IsAnError()
        {
            var ex = ParseFails("<Style name='ui:card' radius='0'/>");
            StringAssert.Contains("as=", ex.Message,
                "the message has to say where a namespace actually comes from");
        }

        [Test]
        public void ThemeStyle_WithAMalformedNamespace_IsAnError()
        {
            var ex = ParseFails("<Theme name='pixel'><Style name='UI:card' radius='0'/></Theme>");
            StringAssert.Contains("kebab-case", ex.Message);
        }

        [Test]
        public void ThemeStyle_NonKebabName_IsAnError()
        {
            var ex = ParseFails("<Theme name='pixel'><Style name='Card' radius='0'/></Theme>");
            StringAssert.Contains("kebab-case", ex.Message);
        }

        [Test]
        public void ThemeStyle_WithNoAttributes_IsAnError()
        {
            var ex = ParseFails("<Theme name='pixel'><Style name='card'/></Theme>");
            StringAssert.Contains("declares no attributes", ex.Message);
        }

        [Test]
        public void Theme_UnknownChild_NamesBothAllowedElements()
        {
            var ex = ParseFails("<Theme name='pixel'><Frame/></Theme>");
            StringAssert.Contains("Color", ex.Message);
            StringAssert.Contains("Style", ex.Message);
        }

        [Test]
        public void ThemeStyle_CarriesOriginSrc_LikeAGlobalOne()
        {
            var doc = UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                + "<Theme name='pixel'><Style name='card' radius='0'/></Theme></PromptUGUI>",
                "skin.ui");

            Assert.AreEqual("skin.ui", doc.Themes.Single().Styles["card"].OriginSrc,
                "lint attributes a bad value in a theme style to the file it was written in");
        }
    }
}
