using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class CarouselRulesTests
    {
        private static PromptUGUI.IR.UIDocument Doc(string inner)
            => UIDocumentParser.Parse($@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>{inner}</Screen></PromptUGUI>");

        [Test]
        public void Card_With_Size_Triggers_CardSize()
        {
            var issues = IRWalker.Walk(Doc("<Carousel><Image width='50'/></Carousel>")).ToList();
            Assert.That(issues.Any(i => i.Code == CarouselRules.CardSizeCode));
        }

        [Test]
        public void Card_With_Anchor_Triggers_LayoutAnchor()
        {
            var issues = IRWalker.Walk(Doc("<Carousel><Image anchor='center'/></Carousel>")).ToList();
            Assert.That(issues.Any(i => i.Code == LayoutGroupChildRules.AnchorCode),
                "anchor on card handled by existing layout-group rule");
        }

        [Test]
        public void Bad_Dots_Anchor_Triggers_DotsAnchor()
        {
            var issues = IRWalker.Walk(Doc("<Carousel dots='diagonal'><Image/></Carousel>")).ToList();
            Assert.That(issues.Any(i => i.Code == CarouselRules.DotsAnchorCode));
        }

        [Test]
        public void Empty_Dots_Does_Not_Trigger_DotsAnchor()
        {
            var issues = IRWalker.Walk(Doc("<Carousel dots=''><Image/></Carousel>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.DotsAnchorCode));
        }

        [Test]
        public void Card_Size_And_Anchor_Not_Double_Reported_On_Same_Attribute()
        {
            // anchor 只由 PUI-LAYOUT-ANCHOR 报；size 只由 PUI-CAROUSEL-CARD-SIZE 报。
            var issues = IRWalker.Walk(Doc("<Carousel><Image anchor='center'/></Carousel>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.CardSizeCode),
                "anchor must NOT trigger the size rule");
        }

        [Test]
        public void Peek_Card_With_Size_Does_Not_Trigger_CardSize()
        {
            var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Frame width='120'/></Carousel>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.CardSizeCode),
                "fill=false allows a card to declare its own size");
        }

        [Test]
        public void Peek_Bare_Container_Card_Triggers_PeekNoSize()
        {
            var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Frame/></Carousel>")).ToList();
            Assert.That(issues.Any(i => i.Code == CarouselRules.PeekNoSizeCode),
                "bare Frame in peek mode has no resolvable size -> warning");
        }

        [Test]
        public void Peek_Image_Card_Without_Size_Does_Not_Trigger_PeekNoSize()
        {
            var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Image/></Carousel>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.PeekNoSizeCode),
                "Image carries a native (sprite) size -> not flagged");
        }

        // 守护整套 no-native-size 容器集合：只测 Frame 时，集合里漏掉 VStack/HStack/Grid 的回归
        // 不会被前面的测试抓到。
        [TestCase("VStack")]
        [TestCase("HStack")]
        [TestCase("Grid")]
        public void Peek_Bare_NoNativeSize_Container_Triggers_PeekNoSize(string tag)
        {
            var issues = IRWalker.Walk(Doc($"<Carousel fill='false'><{tag}/></Carousel>")).ToList();
            Assert.That(issues.Any(i => i.Code == CarouselRules.PeekNoSizeCode),
                $"<{tag}> in peek mode has no native size -> warning");
        }

        [Test]
        public void Peek_Card_With_Only_Height_Does_Not_Trigger_PeekNoSize()
        {
            var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Frame height='80'/></Carousel>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.PeekNoSizeCode),
                "height alone counts as a declared size");
        }

        [Test]
        public void Fill_Mode_Card_With_Size_Still_Triggers_CardSize()
        {
            var issues = IRWalker.Walk(Doc("<Carousel><Image width='50'/></Carousel>")).ToList();
            Assert.That(issues.Any(i => i.Code == CarouselRules.CardSizeCode),
                "default fill=true keeps the v1 card-size error");
        }
    }
}
