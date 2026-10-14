using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>&lt;Cooldown&gt;</c>'s lint rules (spec 2026-10-13 §6): the per-node checks, and the
    /// screen-wide nearest-source rule for bare <c>ready</c> / <c>cooling</c> triggers.
    /// </summary>
    public class CooldownRulesTests
    {
        private static ElementNode N(params (string k, string v)[] attrs)
        {
            var n = new ElementNode("Cooldown") { Id = "cd" };
            foreach (var (k, v) in attrs) n.Attributes[k] = v;
            return n;
        }

        private static List<string> Codes(ElementNode n) => CooldownRules.Check(n).Select(i => i.Code).ToList();

        private static List<LintIssue> LintDoc(string screenBody, string extra = "") =>
            DocumentLinter.Walk(UIDocumentParser.Parse(
                    "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + extra +
                    "<Screen name='S'>" + screenBody + "</Screen></PromptUGUI>", "main.ui.xml"))
                .Where(i => i.Code.StartsWith("PUI-CD-"))
                .ToList();

        [Test]
        public void ATypicalCooldown_IsClean()
        {
            CollectionAssert.IsEmpty(Codes(N(("value", "0.5"), ("radius", "pill"), ("thickness", "4"),
                                             ("cap", "round"), ("origin", "left"), ("draw", "elapsed"),
                                             ("time", "unscaled"), ("countdown", "true"), ("tenthsBelow", "3"))));
        }

        // ───── values ─────

        [TestCase("-0.1")]
        [TestCase("1.5")]
        public void AValueOutsideTheRange_IsReported(string value)
        {
            CollectionAssert.AreEqual(new[] { CooldownRules.ValueRangeCode }, Codes(N(("value", value))));
        }

        [Test]
        public void ABoundValue_IsNotJudged()
        {
            CollectionAssert.IsEmpty(Codes(N(("value", "{{cd}}"))));
        }

        [TestCase("origin", "north")]
        [TestCase("draw", "all")]
        [TestCase("cap", "square")]
        [TestCase("time", "game")]
        public void AnUnknownKeyword_IsReported(string attr, string value)
        {
            var issues = CooldownRules.Check(N((attr, value))).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(CooldownRules.EnumCode, issues[0].Code);
            StringAssert.Contains(attr, issues[0].Message);
        }

        [Test]
        public void AnUnknownKeywordInAVariant_IsReported()
        {
            var n = N();
            n.VariantOverrides["origin"] = new List<(string, string)> { ("mobile", "up") };
            CollectionAssert.AreEqual(new[] { CooldownRules.EnumCode }, Codes(n));
        }

        [TestCase("-1")]
        [TestCase("soon")]
        public void ABadTenthsBelow_IsReported(string value)
        {
            CollectionAssert.AreEqual(new[] { CooldownRules.TenthsCode }, Codes(N(("tenthsBelow", value))));
        }

        [Test]
        public void Children_AreReported()
        {
            var n = N();
            n.Children.Add(new ElementNode("Frame"));
            CollectionAssert.AreEqual(new[] { CooldownRules.ChildrenCode }, Codes(n));
        }

        // ───── shape ─────

        [Test]
        public void ARoundCap_WithoutARing_IsReported()
        {
            var issues = CooldownRules.Check(N(("cap", "round"), ("radius", "pill"))).ToList();
            Assert.AreEqual(CooldownRules.RoundCapCode, issues.Single().Code);
            StringAssert.Contains("thickness", issues[0].Message);
        }

        [Test]
        public void ARoundCap_OnANonCircularRing_IsReported()
        {
            var issues = CooldownRules.Check(N(("cap", "round"), ("thickness", "4"), ("radius", "8"))).ToList();
            Assert.AreEqual(CooldownRules.RoundCapCode, issues.Single().Code);
        }

        [Test]
        public void ARoundCap_OnANumericRadiusThatMakesACircle_IsClean()
        {
            CollectionAssert.IsEmpty(Codes(N(("cap", "round"), ("thickness", "4"), ("radius", "24"), ("size", "48x48"))));
        }

        [Test]
        public void ThicknessOnABitmapFill_IsReported_AndOutranksTheCapRule()
        {
            CollectionAssert.AreEqual(new[] { CooldownRules.BitmapShapeCode },
                Codes(N(("fill", "ui:ring"), ("thickness", "4"), ("cap", "round"))));
            CollectionAssert.IsEmpty(Codes(N(("fill", ""), ("thickness", "4"))), "fill=\"\" is no bitmap");
        }

        [Test]
        public void Glass_IsReported_EvenFromAClass()
        {
            CollectionAssert.AreEqual(new[] { CooldownRules.GlassCode }, Codes(N(("glass", "true"))));
            CollectionAssert.IsEmpty(Codes(N(("glass", "false"))));

            var issues = LintDoc("<Cooldown id='cd' class='frosted'/>", "<Style name='frosted' glass='true'/>");
            Assert.IsTrue(issues.Any(i => i.Code == CooldownRules.GlassCode), "a skin pack carries it too");
        }

        // ───── nearest source ─────

        [Test]
        public void ABareReady_BesideItsCooldown_IsClean()
        {
            CollectionAssert.IsEmpty(LintDoc(
                "<Btn id='b' size='64x64'><Cooldown id='cd' anchor='stretch'/>" +
                "<Show on='ready'><Frame id='lit'/></Show></Btn>"));
        }

        [Test]
        public void ABareReady_AroundItsCooldown_IsClean()
        {
            CollectionAssert.IsEmpty(LintDoc(
                "<Animation on='ready' type='pulse'><Btn size='64x64'><Cooldown anchor='stretch'/></Btn></Animation>"));
        }

        [Test]
        public void ABareReady_WithNoCooldownAnywhere_IsReported()
        {
            var issues = LintDoc("<Show on='ready'><Frame id='lit'/></Show>");
            Assert.AreEqual(CooldownRules.NoSourceCode, issues.Single().Code);
            Assert.AreEqual(1, issues[0].Line, "stamped with the trigger's own line");
        }

        [Test]
        public void TwoCooldownsAtTheNearestLevel_AreAmbiguous()
        {
            var issues = LintDoc(
                "<Btn size='64x64'><Cooldown id='a'/><Cooldown id='b'/><Show on='cooling'><Frame/></Show></Btn>");
            Assert.AreEqual(CooldownRules.AmbiguousCode, issues.Single().Code);
        }

        [Test]
        public void ANearerCooldown_WinsOverTwoFurtherOut()
        {
            CollectionAssert.IsEmpty(LintDoc(
                "<Frame><Cooldown id='a'/><Cooldown id='b'/>" +
                "<Frame><Cooldown id='c'/><Show on='ready'><Frame/></Show></Frame></Frame>"));
        }

        [Test]
        public void ReverseOn_IsResolvedTheSameWay()
        {
            var issues = LintDoc("<Animation on='open' reverse-on='cooling' fade='0:1'><Frame/></Animation>");
            Assert.AreEqual(CooldownRules.NoSourceCode, issues.Single().Code);
        }

        [Test]
        public void AnIdForm_IsLeftToTheRuntime()
        {
            CollectionAssert.IsEmpty(LintDoc("<Show on='ready@cd'><Frame/></Show>"));
        }

        [Test]
        public void ATemplateInvocationAtTheNearestLevel_KeepsTheRuleQuiet()
        {
            // Before expansion the invocation's body is unknown — it may hold the Cooldown.
            var root = UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                "<Btn size='64x64'><SkillIcon/><Show on='ready'><Frame/></Show></Btn>" +
                "</Screen></PromptUGUI>", "main.ui.xml").Screens[0].Root;
            CollectionAssert.IsEmpty(CooldownRules.CheckSources(root, screenHasAdds: false));
        }

        [Test]
        public void AScreenWithAddBlocks_IsNotToldItHasNoCooldown()
        {
            var root = UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                "<Show on='ready'><Frame/></Show></Screen></PromptUGUI>", "main.ui.xml").Screens[0].Root;
            CollectionAssert.IsEmpty(CooldownRules.CheckSources(root, screenHasAdds: true));
        }
    }
}
