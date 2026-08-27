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

        // Deliberately NOT a procedural attribute: those toggle the whole surface wholesale and
        // revert on their own, which is why they are exempt below
        // (ProceduralShapeOnlyOneThemeSets_IsExempt). `fontSize` is an ordinary setter with no
        // per-pass reconcile — the residue §6.1 describes is real for it.
        [Test]
        public void AttributeOnlyOneThemeSets_IsReported()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/></Theme>
                <Theme name='dark'><Style name='card' color='#000' fontSize='18'/></Theme>
                <Screen name='S'><Text id='c' class='card'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.ShapeCode);
            StringAssert.Contains("fontSize", issue.Message);
            StringAssert.Contains("card", issue.Message);
        }

        [Test]
        public void StyleOnlyOneThemeDeclares_IsReported()
        {
            var issues = Lint(@"
                <Style name='card' color='#112233'/>
                <Style name='chip' color='#112233'/>
                <Theme name='light'><Style name='card' color='#fff'/><Style name='chip' fontSize='14'/></Theme>
                <Theme name='dark'><Style name='card' color='#000'/></Theme>
                <Screen name='S'><Text id='c' class='chip'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.ShapeCode);
            StringAssert.Contains("chip", issue.Message);
            StringAssert.Contains("fontSize", issue.Message);
            Assert.IsFalse(issue.Message.Contains("card"),
                "'card' resolves to {color} under both themes — the global baseline covers 'dark'");
        }

        // --- PUI-THEME-STYLE-SHAPE: procedural self-heal exemption (2026-08-27 spec §3) ---
        //
        // The twin rule on the variant side (VariantBaseRules.cs) has exempted this shape since
        // procedural surfaces shipped: a control whose surface toggles WHOLESALE reverts on its own,
        // because ProceduralSurface recomputes the mode every pass from "was the setter called at
        // all" and Reconcile puts the retired Image back. A theme that simply omits the shape
        // attributes produces exactly that signal — StyleMerger.ReMerge drops the names outright, so
        // the setter is never called. §6.1's "give it a baseline in the global <Style>" advice is
        // actively WRONG here: writing radius="" is what ATTACHES the surface (presence, not value),
        // which retires the sprite the other theme needs and trips PUI-PROC-SPRITE-CONFLICT.

        [Test]
        public void ProceduralShapeOnlyOneThemeSets_IsExempt()
        {
            var issues = Lint(@"
                <Style name='btn' sprite='ui:wood' color='#E8D2A8'/>
                <Theme name='farm'><Style name='btn' color='#E8D2A8'/></Theme>
                <Theme name='glass'><Style name='btn' sprite='none' color='#FFFFFF38'
                       radius='10' glass='true' borderWidth='1' borderColor='#FFFFFF8C'/></Theme>
                <Screen name='S'><Btn id='b' class='btn'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "'farm' declares no procedural attribute at all, so the surface turns off wholesale "
                + "and the Image comes back — nothing is left stuck at the glass values");
        }

        [Test]
        public void InnerLayerRadius_IsExempt()
        {
            var issues = Lint(@"
                <Style name='slider' sprite='ui:wood' fill='ui:wood' handle='ui:knob'/>
                <Theme name='farm'><Style name='slider' sprite='ui:wood'/></Theme>
                <Theme name='glass'><Style name='slider' sprite='none' fill='none' handle='none'
                       radius='pill' glass='true' fillRadius='pill' handleRadius='pill'/></Theme>
                <Screen name='S'><Slider id='s' class='slider'/></Screen>");

            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "one attribute per inner surface, so a base-less <layer>Radius always means "
                + "'this surface toggles wholesale' — the same reason VariantBaseRules exempts it");
        }

        /// <summary>
        /// Green today and must STAY green: the exemption is all-or-nothing across themes, so a
        /// theme holding HALF the procedural set pins the mode on and its missing half really does
        /// stick. Also the order-independence guard — 'a-plain' sorts first and is the theme
        /// CheckShape compares everything against, so an exemption applied pairwise-against-the-
        /// reference would skip both comparisons and report nothing at all.
        /// </summary>
        [Test]
        public void PartialProceduralSet_IsStillReported()
        {
            var issues = Lint(@"
                <Style name='btn' color='#E8D2A8'/>
                <Theme name='a-plain'><Style name='btn' color='#E8D2A8'/></Theme>
                <Theme name='b-full'><Style name='btn' color='#fff' radius='10' glass='true'/></Theme>
                <Theme name='c-partial'><Style name='btn' color='#fff' radius='10'/></Theme>
                <Screen name='S'><Btn id='b' class='btn'/></Screen>");

            Assert.IsNotEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "'c-partial' declares radius, which pins the surface on — so the 'glass' it omits "
                + "is never reset and the button keeps b-full's frosted fill");
        }

        /// <summary>
        /// The report itself stays (fontSize is a genuine residue), but the MESSAGE is RED today: it
        /// currently names radius too, which sends the author straight at the one baseline that
        /// breaks the other skin. The exemption must eat names in the procedural family without
        /// swallowing the ordinary setters sitting beside them.
        /// </summary>
        [Test]
        public void NonProceduralAttributeAlongsideProcedural_IsStillReported()
        {
            var issues = Lint(@"
                <Style name='btn' color='#E8D2A8'/>
                <Theme name='farm'><Style name='btn' color='#E8D2A8'/></Theme>
                <Theme name='glass'><Style name='btn' color='#fff' radius='10' fontSize='18'/></Theme>
                <Screen name='S'><Btn id='b' class='btn'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.ShapeCode);
            StringAssert.Contains("fontSize", issue.Message);
            Assert.IsFalse(issue.Message.Contains("radius"),
                "radius reverts on its own; naming it in the message would send the author to add "
                + "the one baseline that breaks the other skin");
        }

        /// <summary>
        /// Green today and must stay green. `sprite` is NOT in the exempt family, and that is what
        /// closes the one hole ProceduralSurface.Restore cannot: it snapshots the host Image at the
        /// first retire, so a theme that clears the sprite while another declares none at all would
        /// have nothing to come back to. Reported statically instead.
        /// </summary>
        [Test]
        public void SpriteOnlyOneThemeSets_IsStillReported()
        {
            var issues = Lint(@"
                <Style name='btn' color='#E8D2A8'/>
                <Theme name='farm'><Style name='btn' color='#E8D2A8'/></Theme>
                <Theme name='glass'><Style name='btn' color='#fff' sprite='none'
                       radius='10' glass='true'/></Theme>
                <Screen name='S'><Btn id='b' class='btn'/></Screen>");

            var issue = issues.Single(i => i.Code == ThemeStyleRules.ShapeCode);
            StringAssert.Contains("sprite", issue.Message);
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
