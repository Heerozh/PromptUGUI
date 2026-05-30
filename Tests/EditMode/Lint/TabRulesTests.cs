using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class TabRulesTests
    {
        private static ElementNode Parse(string xml)
            => UIDocumentParser.Parse(xml).Screens[0].Root;

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

        [Test]
        public void TabBar_With_Template_Wrapper_Child_Does_Not_Trigger_TabBarChild()
        {
            // After TemplateExpander, the Frame containing a Tab is a TabBar child.
            // Build the IR manually since TemplateExpander runs in the loader, not parser.
            var root = new ElementNode("TabBar");
            var frame = new ElementNode("Frame");
            frame.Children.Add(new ElementNode("Tab"));
            root.Children.Add(frame);
            var issues = TabRules.CheckTabBar(root).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == TabRules.TabBarChildCode),
                "Template-wrapper Frame containing a Tab should not trigger TABBAR-CHILD warning");
        }

        [Test]
        public void TabBar_With_NonTab_Child_Without_Tab_Descendant_Still_Triggers_TabBarChild()
        {
            // Sanity: the relaxation should ONLY exempt subtrees containing a Tab.
            var root = new ElementNode("TabBar");
            var frame = new ElementNode("Frame");   // no Tab anywhere inside
            frame.Children.Add(new ElementNode("Text"));
            root.Children.Add(frame);
            var issues = TabRules.CheckTabBar(root).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabBarChildCode),
                "Frame without a Tab descendant should still trigger TABBAR-CHILD");
        }

        [Test]
        public void IRWalker_Does_Not_Warn_Tab_Parent_When_Wrapped_In_Template_Instance_Root()
        {
            // After expansion: <TabBar><Frame IsTemplateInstanceRoot=true><Tab/></Frame></TabBar>
            // The Frame wrapper has IsTemplateInstanceRoot=true; Tab inside should not warn PARENT.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Template name='FT'><Frame><Tab/></Frame></Template>
  <Screen name='S'><TabBar><FT/></TabBar></Screen>
</PromptUGUI>");
            var expanded = TemplateExpander.Expand(doc);
            var issues = IRWalker.Walk(expanded).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == TabRules.TabParentCode),
                "Tab wrapped in IsTemplateInstanceRoot wrapper should not warn PARENT");
        }

        [Test]
        public void IRWalker_Still_Warns_Tab_Parent_When_Wrapped_In_Plain_Frame()
        {
            // Sanity: non-Template Frame containing a Tab should still warn PARENT,
            // since IsTemplateInstanceRoot is only set by TemplateExpander.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Frame><Tab/></Frame></TabBar>
</Screen></PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabParentCode),
                "Plain Frame wrapper (no Template) should still warn PARENT");
        }

        [Test]
        public void IRWalker_Does_Not_Flag_Tab_With_Children()
        {
            // Container model: <Tab> may now hold children. PUI-TAB-CHILDREN is retired.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Tab id='t'><Icon name='ui:gear'/></Tab></TabBar>
</Screen></PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == "PUI-TAB-CHILDREN"),
                "Tab with children must not produce PUI-TAB-CHILDREN anymore");
        }
    }
}
