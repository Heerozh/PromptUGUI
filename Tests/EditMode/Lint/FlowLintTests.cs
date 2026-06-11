using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// flow="false" 的 lint 面：
    /// - layout-group 子节点声明 flow=false 后，anchor / margin 恢复合法 → PUI-LAYOUT-ANCHOR /
    ///   PUI-LAYOUT-MARGIN 放行，PUI-MARGIN-INERT-SIDE（自由定位语义的检查）恢复生效；
    /// - 非 layout-group 父级下写 flow 是 inert 属性 → PUI-FLOW-OUTSIDE-GROUP。
    /// </summary>
    public class FlowLintTests
    {
        // ---- LayoutGroupChildRules.CheckChild：flow=false 放行 anchor/margin ----

        [Test]
        public void CheckChild_AnchorAndMargin_Suppressed_WhenFlowFalse()
        {
            var child = new ElementNode("Image") { Id = "bg" };
            child.Attributes["flow"] = "false";
            child.Attributes["anchor"] = "stretch";
            child.Attributes["margin"] = "8";
            Assert.IsEmpty(LayoutGroupChildRules.CheckChild(child),
                "flow=false: anchor/margin are meaningful again — no PUI-LAYOUT-* issue");
        }

        [Test]
        public void CheckChild_Anchor_StillFlagged_WhenFlowTrue()
        {
            var child = new ElementNode("Image") { Id = "bg" };
            child.Attributes["flow"] = "true";
            child.Attributes["anchor"] = "stretch";
            var issues = LayoutGroupChildRules.CheckChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.AnchorCode, issues[0].Code);
        }

        [Test]
        public void CheckChild_Suppressed_WhenFlowHasVariantOverride()
        {
            // flow 只在某 variant 下为 false：作者明确接管了流内/流外切换，
            // anchor 是为流外形态准备的 —— 静态检查放行（保守不误报）。
            var child = new ElementNode("Image") { Id = "bg" };
            child.VariantOverrides["flow"] =
                new List<(string Variant, string Value)> { ("deco", "false") };
            child.Attributes["anchor"] = "stretch";
            Assert.IsEmpty(LayoutGroupChildRules.CheckChild(child));
        }

        // ---- 非 layout-group 父级下的 flow：inert → PUI-FLOW-OUTSIDE-GROUP ----

        [Test]
        public void CheckNonLayoutChild_Flow_Flagged()
        {
            var child = new ElementNode("Image") { Id = "bg" };
            child.Attributes["flow"] = "false";
            var issues = LayoutGroupChildRules.CheckNonLayoutChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.FlowOutsideCode, issues[0].Code);
            StringAssert.Contains("'flow'", issues[0].Message);
        }

        [Test]
        public void CheckNonLayoutChild_NoFlow_NoIssue()
        {
            var child = new ElementNode("Image") { Id = "bg" };
            child.Attributes["anchor"] = "stretch";
            Assert.IsEmpty(LayoutGroupChildRules.CheckNonLayoutChild(child));
        }

        // ---- IRWalker 集成 ----

        [Test]
        public void Walk_FlowUnderFrame_Flagged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame>
      <Image id='bg' flow='false' anchor='stretch'/>
    </Frame>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc)
                .Where(i => i.Code == LayoutGroupChildRules.FlowOutsideCode).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("bg", issues[0].Id);
        }

        [Test]
        public void Walk_FlowInsideHStack_NotFlagged_AndLayoutAnchorSuppressed()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <HStack>
      <Image id='bg' flow='false' anchor='stretch'/>
      <Btn id='b' size='64x16'/>
    </HStack>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var all = IRWalker.Walk(doc).ToList();
            Assert.IsFalse(all.Any(i => i.Code == LayoutGroupChildRules.FlowOutsideCode));
            Assert.IsFalse(all.Any(i => i.Code == LayoutGroupChildRules.AnchorCode));
        }

        [Test]
        public void Walk_InertSideMargin_Restored_ForFlowFalseChild()
        {
            // 流内子节点的 inert-side 检查让位给 PUI-LAYOUT-MARGIN；流外恢复自由定位
            // 语义后 margin 重新有意义，写在 anchor 不消费的边上要重新报。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <HStack>
      <Text id='val' flow='false' anchor='bottom-right' margin='60,_,_,_'>x</Text>
    </HStack>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc)
                .Where(i => i.Code == MarginAnchorRules.InertSideCode).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("val", issues[0].Id);
        }

        [Test]
        public void Walk_TemplateBody_FlowNotFlagged()
        {
            // Template body 的根节点活在声明空间，真实父级要到 invocation 处才知道
            // （可能就是个 HStack）—— FLOW-OUTSIDE 只在父 tag 已知的 child loop 派发，
            // body 根（以及 <Add> 根、Screen 根）自动豁免，宁缺毋滥。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Deco'>
    <Image flow='false' anchor='stretch'/>
  </Template>
  <Screen name='S'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.IsFalse(IRWalker.Walk(doc)
                .Any(i => i.Code == LayoutGroupChildRules.FlowOutsideCode));
        }
    }
}
