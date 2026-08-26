using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// The two static constraints theme-driven styling rests on (2026-08-26 spec §6.1, §7). Both
    /// guard against the same runtime symptom — an attribute silently keeping the previous theme's
    /// value — which is why they have to be caught at write time rather than documented.
    /// </summary>
    public class ThemeStyleRulesTests
    {
        private static UIDocument Parse(string body) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>");

        private static System.Collections.Generic.List<LintIssue> Lint(string body) =>
            DocumentLinter.Walk(Parse(body)).ToList();

        // --- PUI-THEME-STYLE-SHAPE (§6.1) ---

        [Test]
        public void ThemesAgreeingOnTheAttributeSet_AreClean()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233' radius='16'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000' radius='0'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "both resolve to {color, radius} — the global pack supplies whatever a theme omits");
        }

        [Test]
        public void AttributeOnlyOneThemeSets_IsReported()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000' glow='8'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.ShapeCode);
            StringAssert.Contains("glow", issue.Message);
            StringAssert.Contains("card", issue.Message);
        }

        [Test]
        public void StyleOnlyOneThemeDeclares_IsReported()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Style name='chip' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/><Style name='chip' glow='4'/></Theme>
                <Theme name='dark'><Style name='card' color='#000'/></Theme>
                <Screen name='S'><Image id='c' class='chip'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.ShapeCode);
            StringAssert.Contains("chip", issue.Message);
            StringAssert.Contains("glow", issue.Message);
            Assert.IsFalse(issue.Message.Contains("card"),
                "'card' resolves to {color} under both themes — the global baseline covers 'dark'");
        }

        // --- PUI-THEME-STYLE-NO-BASELINE (§4.2) ---

        // Expansion resolves class= against the GLOBAL pool and the theme layer only re-derives
        // values afterwards, so a theme-only style is unreferenceable. The bare runtime error says
        // "unknown style 'card'" and cannot mention the theme it was really written in.
        [Test]
        public void ThemeStyleWithNoGlobalCounterpart_IsReported_BeforeExpansionEvenRuns()
        {
            var issues = Lint(@"
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.NoBaselineCode);
            StringAssert.Contains("card", issue.Message);
            StringAssert.Contains("global <Style>", issue.Message);

            Assert.IsNotEmpty(issues.Where(i => i.Code == DocumentLinter.ExpansionCode),
                "the reference genuinely cannot resolve, so the expansion failure is reported too — "
                + "but no longer as the only clue");
        }

        [Test]
        public void ThemeStyleWithAGlobalCounterpart_IsNotReported()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.NoBaselineCode));
        }

        [Test]
        public void SingleTheme_IsNeverReported()
        {
            var issues = Lint(@"
                <Theme name='only'><Style name='card' color='#fff' glow='8'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "with one theme there is no switch that could go wrong");
        }

        [Test]
        public void VariantSuffix_CountsAsTheSameName()
        {
            var issues = Lint(@"
                <Style name='card' radius='16'/>
                <Theme name='light'><Style name='card' radius='8'/></Theme>
                <Theme name='dark'><Style name='card' radius.mobile='0'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "both claim the whole 'radius' slot, so both resolve to the same name set");
        }

        // --- PUI-THEME-STYLE-ON-INVOCATION (§7) ---

        [Test]
        public void ThemedClassOnTemplateInvocation_IsReported()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000'/></Theme>
                <Template name='Panel'><Frame id='body'/></Template>
                <Screen name='S'><Panel id='p' class='card'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.OnInvocationCode);
            StringAssert.Contains("Panel", issue.Message);
            StringAssert.Contains("will NOT follow a theme switch", issue.Message);
        }

        [Test]
        public void ThemedClassInsideTemplateBody_IsFine()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000'/></Theme>
                <Template name='Panel'><Frame id='body' class='card'/></Template>
                <Screen name='S'><Panel id='p'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.OnInvocationCode),
                "inside the body it is an ordinary node attribute and re-merges like any other — "
                + "this is exactly the fix the rule tells authors to make");
        }

        [Test]
        public void UnthemedClassOnTemplateInvocation_IsFine()
        {
            var issues = Lint(@"
                <Style name='plain' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000'/></Theme>
                <Template name='Panel'><Param name='color' default='#000'/><Frame id='body' color='{{color}}'/></Template>
                <Screen name='S'><Panel id='p' class='plain'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.OnInvocationCode),
                "no theme overrides 'plain', so baking it in at expansion loses nothing");
        }

        [Test]
        public void NoThemeStylesAtAll_ReportsNeitherRule()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Color name='ink' value='#000'/></Theme>
                <Theme name='dark'><Color name='ink' value='#fff'/></Theme>
                <Template name='Panel'><Frame id='body'/></Template>
                <Screen name='S'><Panel id='p' class='card'/></Screen>");

            Assert.IsEmpty(issues.Where(i =>
                i.Code == ThemeStyleRules.ShapeCode || i.Code == ThemeStyleRules.OnInvocationCode),
                "colour-only themes are every project written before this feature");
        }

        // Rules that need the resolved configuration can only answer for one skin at a time, so the
        // linter re-derives the tree under each declared theme and walks again.
        [Test]
        public void ProblemVisibleOnlyUnderOneTheme_IsStillFound()
        {
            var issues = Lint(@"
                <Style name='boxed' color='#112233'/>
                <Theme name='plain'><Style name='boxed' color='#fff'/></Theme>
                <Theme name='sprited'><Style name='boxed' color='#fff' sprite='ui:panel'/></Theme>
                <Screen name='S'><VStack id='v' class='boxed' anchor='stretch'/></Screen>");

            Assert.IsNotEmpty(
                issues.Where(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "v"),
                "'sprite' on a pure container only exists under the 'sprited' skin; walking just one "
                + "resolved state would miss it entirely");
        }
    }
}
