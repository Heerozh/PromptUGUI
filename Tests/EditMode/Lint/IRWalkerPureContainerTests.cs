using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerPureContainerTests
    {
        [Test]
        public void Walk_DispatchesPureContainerRule_OnFrameSprite()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='bg' sprite='ui:card'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i =>
                i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "bg"));
        }

        [TestCase("VStack")]
        [TestCase("HStack")]
        [TestCase("Grid")]
        [TestCase("SafeArea")]
        public void Walk_DispatchesPureContainerRule_OnOtherContainers(string tag)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <{tag} id='c' sprite='ui:card'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i =>
                i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "c"));
        }

        [Test]
        public void Walk_DoesNotFireOnImage()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image id='legit' sprite='ui:card' color='#fff'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc)
                .Where(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode).ToList();
            Assert.IsEmpty(issues);
        }
    }
}
