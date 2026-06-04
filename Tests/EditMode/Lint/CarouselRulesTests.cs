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
    }
}
