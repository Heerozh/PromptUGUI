using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// IRWalker integration for <see cref="MarginAnchorRules"/>: dispatch on free-positioned nodes,
    /// and suppression inside a layout group (where <c>PUI-LAYOUT-MARGIN</c> already owns the message
    /// — margin is wholly ignored there, so a second inert-side error would be noise).
    /// </summary>
    public class IRWalkerMarginAnchorTests
    {
        [Test]
        public void Walk_DispatchesMarginAnchorRule_OnInertSide()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame>
      <Text id='val' anchor='bottom-right' margin='60,_,_,_'>x</Text>
    </Frame>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc)
                .Where(i => i.Code == MarginAnchorRules.InertSideCode).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("val", issues[0].Id);
        }

        [Test]
        public void Walk_NoIssue_WhenMarginOnConsumedSide()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame>
      <Text id='ok' anchor='bottom-right' margin='_,_,60,_'>x</Text>
    </Frame>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc)
                .Where(i => i.Code == MarginAnchorRules.InertSideCode).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void Walk_SuppressedUnderLayoutGroup()
        {
            // VStack 子节点：margin 被 layout group 接管，PUI-LAYOUT-MARGIN 已经报了；
            // inert-side 不该再报第二次。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack>
      <Text id='row' anchor='bottom-right' margin='60,_,_,_'>x</Text>
    </VStack>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var all = IRWalker.Walk(doc).ToList();
            Assert.IsFalse(all.Any(i => i.Code == MarginAnchorRules.InertSideCode),
                "inert-side rule must defer to PUI-LAYOUT-MARGIN inside a layout group");
            Assert.IsTrue(all.Any(i => i.Code == LayoutGroupChildRules.MarginCode));
        }
    }
}
