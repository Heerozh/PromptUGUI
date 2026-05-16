using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerMaskTests
    {
        [Test]
        public void Walk_DispatchesFrameMaskRulesOnRootAndDescendants()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='root' mask='self'>
      <Frame id='inner' mask='circle'/>
    </Frame>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            // root: mask=self → FRAME-SELF; inner: mask=circle → VALUE
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.FrameSelfCode && i.Id == "root"));
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.ValueCode && i.Id == "inner"));
        }

        [Test]
        public void Walk_DispatchesImageMaskRules()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image id='bad' mask='self'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.SelfNoSpriteCode && i.Id == "bad"));
        }

        [Test]
        public void Walk_NonFrameNonImageTags_NoMaskIssue()
        {
            // <VStack mask="rect"> 不该触发 mask rule（mask 只 Frame/Image 暴露）
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack id='v' mask='rect'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).Where(i =>
                i.Code.StartsWith("PUI-MASK-")).ToList();
            Assert.IsEmpty(issues);
        }
    }
}
