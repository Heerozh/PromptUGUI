using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// File attribution for lint findings. Once the CLI follows <c>&lt;Import&gt;</c> and walks the
    /// EXPANDED tree, the node a rule complains about is frequently NOT in the document that was
    /// linted — it was written in an imported library and inlined here. Reporting all of it against
    /// the entry file sends the author to the wrong place, so every node carries the src its markup
    /// came from. See the 2026-08-26 theme-driven-style spec §9.5.
    /// </summary>
    public class LintOriginTests
    {
        private static UIDocument Parse(string body, string src = null) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>",
                src);

        private static ElementNode FindById(ElementNode node, string id)
        {
            if (node == null) return null;
            if (node.Id == id) return node;
            foreach (var child in node.Children)
            {
                var hit = FindById(child, id);
                if (hit != null) return hit;
            }
            return null;
        }

        [Test]
        public void Parse_StampsEveryNode_IncludingTemplateBodiesAndAddBlocks()
        {
            var doc = Parse(@"
                <Template name='Card'><Frame id='card-root'><Text id='deep'>x</Text></Frame></Template>
                <Screen name='S'>
                  <Frame id='top'/>
                  <Variant when='mobile'><Add into='#top'><Frame id='added'/></Add></Variant>
                </Screen>", "lib.ui");

            var screen = doc.Screens.Single();
            Assert.AreEqual("lib.ui", screen.Root.OriginSrc, "screen root");
            Assert.AreEqual("lib.ui", FindById(screen.Root, "top")?.OriginSrc, "screen descendant");
            Assert.AreEqual("lib.ui", FindById(doc.Templates["Card"].Body, "deep")?.OriginSrc,
                "template body descendant");
            Assert.AreEqual("lib.ui",
                screen.Variants.Single().Adds.Single().Children.Single().OriginSrc, "<Add> child");
        }

        [Test]
        public void Parse_WithoutSrc_LeavesOriginNull()
        {
            var doc = Parse("<Screen name='S'><Frame id='f'/></Screen>");
            Assert.IsNull(FindById(doc.Screens.Single().Root, "f").OriginSrc,
                "callers that have no src must keep working; their own path is the attribution");
        }

        [Test]
        public void Expansion_CarriesOrigin_FromTheDeclaringLibrary()
        {
            var lib = Parse("<Template name='Card'><Frame id='card'/></Template>", "skin.ui");
            var entry = Parse(@"
                <Import src='skin.ui'/>
                <Screen name='S'><Card/></Screen>", "main.ui");

            var loaded = DocumentAssembler.Assemble(
                "main.ui", src => src == "main.ui" ? entry : lib, allowScreens: true);
            var expanded = TemplateExpander.Expand(loaded);

            var inlined = FindById(expanded.Screens.Single().Root, "card");
            Assert.IsNotNull(inlined, "guard: the template was inlined");
            Assert.AreEqual("skin.ui", inlined.OriginSrc,
                "the node was written in skin.ui; main.ui only invoked it");
        }

        // The bug this whole class exists for.
        [Test]
        public void ExpandedFinding_IsAttributedToTheLibrary_NotTheEntryDocument()
        {
            // mask='self' on a <Frame> has no Image to mask with -> PUI-MASK-FRAME-SELF.
            // It sits inside a Template declared in skin.ui and is invoked from main.ui, so the raw
            // walk of main.ui cannot see it at all - only the expanded pass finds it.
            var lib = Parse("<Template name='Card'><Frame id='card' mask='self'/></Template>", "skin.ui");
            var entry = Parse(@"
                <Import src='skin.ui'/>
                <Screen name='S'><Card/></Screen>", "main.ui");

            var issue = DocumentLinter
                .Walk(entry, "main.ui", src => src == "skin.ui" ? lib : null)
                .Single(i => i.Code == MaskAttributeRules.FrameSelfCode);

            Assert.AreEqual("skin.ui", issue.Origin,
                "sending the author to main.ui would be sending them to a file that does not contain "
                + "the mistake");
        }

        [Test]
        public void EntryDocumentFinding_KeepsTheEntryOrigin()
        {
            var issue = DocumentLinter
                .Walk(Parse("<Screen name='S'><Frame id='f' mask='self'/></Screen>", "main.ui"), "main.ui")
                .Single(i => i.Code == MaskAttributeRules.FrameSelfCode);

            Assert.AreEqual("main.ui", issue.Origin);
        }

        // Parent-frame rules (LayoutGroupChildRules and friends) run while walking the PARENT but
        // complain about the CHILD, so they must not inherit the parent's origin.
        [Test]
        public void ChildTargetedRule_IsAttributedToTheChild_NotItsParent()
        {
            var lib = Parse(
                "<Template name='Row'><Frame id='row-child' anchor='top-left'/></Template>", "skin.ui");
            var entry = Parse(@"
                <Import src='skin.ui'/>
                <Screen name='S'><VStack id='stack' anchor='stretch'><Row/></VStack></Screen>", "main.ui");

            var issue = DocumentLinter
                .Walk(entry, "main.ui", src => src == "skin.ui" ? lib : null)
                .Single(i => i.Id == "row-child" && i.Code == LayoutGroupChildRules.AnchorCode);

            Assert.AreEqual("skin.ui", issue.Origin,
                "the <VStack> is in main.ui but the offending anchor= was written in skin.ui");
        }
    }
}
