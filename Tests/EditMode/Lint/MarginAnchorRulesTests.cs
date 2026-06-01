using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// A free-positioned element's <c>margin</c> only offsets it from the edge(s) its
    /// <c>anchor</c> actually consumes (spec §6.2 / MarginResolver). For a point anchor like
    /// <c>bottom-right</c>, only the <c>bottom</c> + <c>right</c> margin slots do anything — a
    /// value in the <c>top</c> / <c>left</c> slot is silently dropped. This rule (CLI-only) catches
    /// that, e.g. the common <c>margin="60,_,_,_"</c> (top=60) under a <c>bottom</c> anchor.
    ///
    /// Margin slot order is top,right,bottom,left. Only the 4-component (per-side) form is checked;
    /// 1- / 2-component shorthands are symmetric and always land on the consumed side.
    /// </summary>
    public class MarginAnchorRulesTests
    {
        private static ElementNode Node(string anchor, string margin, string tag = "Text", string id = "n")
        {
            var n = new ElementNode(tag) { Id = id };
            if (anchor != null) n.Attributes["anchor"] = anchor;
            if (margin != null) n.Attributes["margin"] = margin;
            return n;
        }

        [Test]
        public void BottomRight_TopMargin_InertIssue()
        {
            // 用户原始 case：slot0 = top，但 anchor 在 bottom → top 边失效。
            var issues = MarginAnchorRules.Check(Node("bottom-right", "60,_,_,_")).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MarginAnchorRules.InertSideCode, issues[0].Code);
            Assert.AreEqual("Text", issues[0].Tag);
            Assert.AreEqual("n", issues[0].Id);
            StringAssert.Contains("top", issues[0].Message);
            StringAssert.Contains("anchor", issues[0].Message);
        }

        [Test]
        public void BottomRight_BottomMargin_NoIssue()
        {
            // bottom 槽 → 被 bottom 锚点消费 → 有效。
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", "_,_,60,_")));
        }

        [Test]
        public void BottomRight_RightMargin_NoIssue()
        {
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", "_,60,_,_")));
        }

        [Test]
        public void BottomRight_LeftMargin_InertIssue()
        {
            // left 槽在 right 锚点下失效。
            var issues = MarginAnchorRules.Check(Node("bottom-right", "_,_,_,60")).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("left", issues[0].Message);
        }

        [Test]
        public void TopLeft_RightAndBottom_TwoIssues()
        {
            // top-left 消费 top+left；right(slot1)+bottom(slot2) 都失效。
            var issues = MarginAnchorRules.Check(Node("top-left", "_,60,40,_")).ToList();
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.All(i => i.Code == MarginAnchorRules.InertSideCode));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("right")));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("bottom")));
        }

        [Test]
        public void Stretch_AllFourSides_NoIssue()
        {
            // 两轴都 stretch → 四条边都被消费。
            Assert.IsEmpty(MarginAnchorRules.Check(Node("stretch", "4,8,4,8")));
        }

        [Test]
        public void BottomStretch_TopMargin_InertIssue()
        {
            // V=Bottom, H=Stretch：top(slot0) 失效；left/right 被 stretch 消费。
            var issues = MarginAnchorRules.Check(Node("bottom-stretch", "60,_,_,_")).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("top", issues[0].Message);
        }

        [Test]
        public void Center_TopMargin_InertIssue()
        {
            // anchor=center：Y 轴 center 不消费任何边 → top 失效。
            var issues = MarginAnchorRules.Check(Node("center", "10,_,_,_")).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("top", issues[0].Message);
        }

        [Test]
        public void OneComponentMargin_NotChecked()
        {
            // 简写：对称，永远落在有效边上 → 不查。
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", "8")));
        }

        [Test]
        public void TwoComponentMargin_NotChecked()
        {
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", "8,16")));
        }

        [Test]
        public void ZeroOnInertSide_NoIssue()
        {
            // top 槽 = 0 → 不算"设了值"，不报。
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", "0,_,60,_")));
        }

        [Test]
        public void Underscore_AllInert_NoIssue()
        {
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", "_,_,_,_")));
        }

        [Test]
        public void NoAnchorAttribute_NotChecked()
        {
            // 没显式 anchor → 默认锚点按控件类型推导，lint 纯 C# 拿不到 → 跳过。
            Assert.IsEmpty(MarginAnchorRules.Check(Node(null, "60,_,_,_")));
        }

        [Test]
        public void NoMargin_NoIssue()
        {
            Assert.IsEmpty(MarginAnchorRules.Check(Node("bottom-right", null)));
        }

        [Test]
        public void InvalidAnchor_NotChecked()
        {
            // 非法 anchor 由别处报错；本规则不崩。
            Assert.IsEmpty(MarginAnchorRules.Check(Node("frobnicate", "60,_,_,_")));
        }

        [Test]
        public void FractionalValue_OnInertSide_InertIssue()
        {
            // 小数也算非零值。
            var issues = MarginAnchorRules.Check(Node("bottom-right", "12.5,_,_,_")).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("top", issues[0].Message);
        }
    }
}
