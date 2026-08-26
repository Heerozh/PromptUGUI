using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// Source positions on lint findings. <see cref="LintOriginTests"/> pins WHICH FILE; this pins
    /// where in it, and which Template instance when one template is invoked more than once.
    ///
    /// <para>Together those are what turns a finding into somewhere to go. A file name alone is not
    /// enough for a 300-line document, and a declaration site alone is not enough when ten instances
    /// of the same template produce ten identical-looking findings.</para>
    /// </summary>
    public class LintSourcePositionTests
    {
        private static UIDocument Parse(string body, string src = "main.ui") =>
            UIDocumentParser.Parse(body, src);

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
        public void Parse_RecordsTheLineEachNodeStartsOn()
        {
            var doc = Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='first'/>
    <Frame id='second'/>
  </Screen>
</PromptUGUI>");

            var root = doc.Screens.Single().Root;
            Assert.AreEqual(4, FindById(root, "first").Line);
            Assert.AreEqual(5, FindById(root, "second").Line);
        }

        [Test]
        public void ParseWithoutLineInfoIsStillFine_LineIsZero()
        {
            // A synthesised node — nothing read it out of a file, so there is no line to give.
            Assert.AreEqual(0, new ElementNode("Frame").Line);
        }

        [Test]
        public void Expansion_CarriesTheDeclarationLine_ThroughTemplateInlining()
        {
            var doc = Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Card'>
    <Frame id='card' mask='self'/>
  </Template>
  <Screen name='S'>
    <Card id='a'/>
  </Screen>
</PromptUGUI>");

            var issue = DocumentLinter.Walk(doc, "main.ui")
                .Single(i => i.Code == MaskAttributeRules.FrameSelfCode && i.Id == "a");

            Assert.AreEqual(4, issue.Line,
                "the fix goes where the markup was written, not where the template was invoked");
        }

        [Test]
        public void TwoInvocationsOfOneTemplate_AreToldApartByTheirInvocationSite()
        {
            var doc = Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Card'>
    <Frame id='card' mask='self'/>
  </Template>
  <Screen name='S'>
    <Card id='a'/>
    <Card id='b'/>
  </Screen>
</PromptUGUI>");

            var issues = DocumentLinter.Walk(doc, "main.ui")
                .Where(i => i.Code == MaskAttributeRules.FrameSelfCode)
                .ToList();

            Assert.AreEqual("main.ui:7", issues.Single(i => i.Id == "a").Via);
            Assert.AreEqual("main.ui:8", issues.Single(i => i.Id == "b").Via);
        }

        [Test]
        public void NodeNotProducedByAnInvocation_HasNoVia()
        {
            var doc = Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='plain' mask='self'/>
  </Screen>
</PromptUGUI>");

            var issue = DocumentLinter.Walk(doc, "main.ui")
                .First(i => i.Code == MaskAttributeRules.FrameSelfCode);

            Assert.IsNull(issue.Via, "nothing invoked it; saying 'via' anything would be noise");
        }

        // A template invoked from inside another template: the OUTER site is the one that can tell
        // two instances apart, so that is the one recorded.
        [Test]
        public void NestedInvocation_RecordsTheOutermostSite()
        {
            var doc = Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Inner'>
    <Frame id='deep' mask='self'/>
  </Template>
  <Template name='Outer'>
    <Inner/>
  </Template>
  <Screen name='S'>
    <Outer id='x'/>
  </Screen>
</PromptUGUI>");

            var issue = DocumentLinter.Walk(doc, "main.ui")
                .Single(i => i.Code == MaskAttributeRules.FrameSelfCode && i.Id == "x");

            Assert.AreEqual("main.ui:10", issue.Via,
                "line 7's <Inner/> is identical for every <Outer>, so it cannot distinguish instances");
        }
    }
}
