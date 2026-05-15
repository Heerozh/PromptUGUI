using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Lint
{
    public class IRWalkerTests
    {
        [Test]
        public void DirectChildOfVStack_WithAnchor_ReportsIssue()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack>
      <Text id='title' anchor='stretch'/>
    </VStack>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.AnchorCode, issues[0].Code);
            Assert.AreEqual("title", issues[0].Id);
        }

        [Test]
        public void GrandChildOfVStack_WithAnchor_NotReported()
        {
            // <Text anchor> is inside a <Frame>, not directly under <VStack> — should be fine.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack>
      <Frame>
        <Text id='inner' anchor='stretch'/>
      </Frame>
    </VStack>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void ChildOfFrame_WithAnchor_NotReported()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame>
      <Text anchor='stretch'/>
    </Frame>
  </Screen>
</PromptUGUI>");
            Assert.IsEmpty(IRWalker.Walk(doc));
        }

        [Test]
        public void HStackChild_WithMargin_ReportsMarginIssue()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <HStack>
      <Btn id='ok' margin='4'>OK</Btn>
    </HStack>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.MarginCode, issues[0].Code);
        }

        [Test]
        public void GridChild_WithAnchorAndMargin_ReportsTwoIssues()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Grid columns='2'>
      <Text id='cell' anchor='stretch' margin='4'/>
    </Grid>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.AreEqual(2, issues.Count);
        }

        [Test]
        public void NestedLayoutGroups_BothLayersChecked()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack>
      <HStack anchor='stretch'>
        <Text id='label' anchor='center'/>
      </HStack>
    </VStack>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            // outer VStack's child <HStack anchor=...> → 1 issue
            // inner HStack's child <Text anchor=...> → 1 issue
            Assert.AreEqual(2, issues.Count);
        }

        [Test]
        public void TemplateBody_LayoutGroupViolation_Reported()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <VStack>
      <Text id='label' anchor='stretch'/>
    </VStack>
  </Template>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("label", issues[0].Id);
        }

        [Test]
        public void VariantAddChildren_LayoutGroupViolation_Reported()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='root'/>
    <Variant when='portrait'>
      <Add into='#root' at='end'>
        <VStack>
          <Text id='extra' anchor='stretch'/>
        </VStack>
      </Add>
    </Variant>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("extra", issues[0].Id);
        }

        [Test]
        public void MultipleScreens_EachWalked()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='A'>
    <VStack><Text anchor='stretch'/></VStack>
  </Screen>
  <Screen name='B'>
    <HStack><Text margin='8'/></HStack>
  </Screen>
</PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.Any(i => i.Code == LayoutGroupChildRules.AnchorCode));
            Assert.IsTrue(issues.Any(i => i.Code == LayoutGroupChildRules.MarginCode));
        }
    }
}
