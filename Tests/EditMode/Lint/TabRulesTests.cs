using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class TabRulesTests
    {
        private static ElementNode Parse(string xml)
            => UIDocumentParser.Parse(xml).Screens[0].Root;

        [Test]
        public void Tab_With_Children_Triggers_TabChildren()
        {
            var root = Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Tab id='t'><Frame/></Tab></TabBar>
</Screen></PromptUGUI>");
            var tab = root.Children[0].Children[0];
            var issues = TabRules.CheckTab(tab).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabChildrenCode));
        }

        [Test]
        public void Tab_Bind_Empty_String_Triggers_TabBindEmpty()
        {
            var root = Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Tab bind=''/></TabBar>
</Screen></PromptUGUI>");
            var tab = root.Children[0].Children[0];
            var issues = TabRules.CheckTab(tab).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.BindEmptyCode));
        }

        [Test]
        public void TabBar_Direction_Invalid_Triggers_TabBarDirection()
        {
            var root = Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar direction='diagonal'/>
</Screen></PromptUGUI>");
            var bar = root.Children[0];
            var issues = TabRules.CheckTabBar(bar).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.DirectionCode));
        }

        [Test]
        public void TabBar_With_NonTab_Child_Triggers_TabBarChild()
        {
            var root = Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Tab/><Btn/></TabBar>
</Screen></PromptUGUI>");
            var bar = root.Children[0];
            var issues = TabRules.CheckTabBar(bar).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabBarChildCode));
        }

        [Test]
        public void IRWalker_Dispatches_Tab_Children_Rule()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Tab><Frame/></Tab></TabBar>
</Screen></PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabChildrenCode));
        }

        [Test]
        public void IRWalker_Dispatches_TabBar_Direction_Rule()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar direction='nope'/>
</Screen></PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.DirectionCode));
        }

        [Test]
        public void IRWalker_Inline_Tab_Parent_Rule_When_Tab_Outside_TabBar()
        {
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack><Tab/></HStack>
</Screen></PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabParentCode));
        }
    }
}
