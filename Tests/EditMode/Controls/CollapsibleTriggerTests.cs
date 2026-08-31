using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using PuiAnimation = PromptUGUI.Controls.Animation;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>expand</c> / <c>collapse</c> resolve to a <c>&lt;Collapsible&gt;</c> as readily as to a
    /// <c>&lt;TabMenu&gt;</c> — one <c>IExpandable</c> source, so a row's entrance animation does not
    /// care which it lives in (spec 2026-08-31-collapsible-design §4.5).
    /// </summary>
    public class CollapsibleTriggerTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void A_bare_trigger_in_the_body_follows_the_panel()
        {
            var s = CollapsibleTests.Open(@"
                <Collapsible id='c' text='任务' transition='0'>
                  <Trigger id='opened' on='expand'><Frame/></Trigger>
                  <Trigger id='closed' on='collapse'><Frame/></Trigger>
                </Collapsible>");
            var c = s.Get<Collapsible>("c");
            var opened = 0;
            var closed = 0;
            s.Get<Trigger>("opened").OnFire.Subscribe(_ => opened++);
            s.Get<Trigger>("closed").OnFire.Subscribe(_ => closed++);

            c.Collapse();
            Assert.AreEqual(1, closed);
            Assert.AreEqual(0, opened);

            c.Expand();
            Assert.AreEqual(1, opened);
        }

        [Test]
        public void A_trigger_in_the_header_follows_the_panel_too()
        {
            var s = CollapsibleTests.Open(@"
                <Collapsible id='c' transition='0'>
                  <Header><Trigger id='closed' on='collapse'><Frame/></Trigger></Header>
                  <Btn id='r'/>
                </Collapsible>");
            var closed = 0;
            s.Get<Trigger>("closed").OnFire.Subscribe(_ => closed++);

            s.Get<Collapsible>("c").Collapse();
            Assert.AreEqual(1, closed);
        }

        [Test]
        public void Opening_at_screen_open_is_not_an_expand()
        {
            var s = CollapsibleTests.Open(@"
                <Collapsible id='c' text='任务' expanded='true' transition='0'>
                  <Trigger id='opened' on='expand'><Frame/></Trigger>
                </Collapsible>");
            var opened = 0;
            s.Get<Trigger>("opened").OnFire.Subscribe(_ => opened++);

            Assert.AreEqual(0, opened, "expand means 'it just opened', not 'it is open'");
        }

        [Test]
        public void Closing_at_screen_open_is_not_a_collapse()
        {
            var s = CollapsibleTests.Open(@"
                <Collapsible id='c' text='任务' expanded='false' transition='0'>
                  <Trigger id='closed' on='collapse'><Frame/></Trigger>
                </Collapsible>");
            var closed = 0;
            s.Get<Trigger>("closed").OnFire.Subscribe(_ => closed++);

            Assert.AreEqual(0, closed);
        }

        [Test]
        public void The_nearest_expandable_wins()
        {
            var s = CollapsibleTests.Open(@"
                <TabMenu id='m' transition='0'>
                  <Tab id='a' text='A'/>
                </TabMenu>
                <Collapsible id='c' text='任务' transition='0'>
                  <Trigger id='inPanel' on='collapse'><Frame/></Trigger>
                </Collapsible>");
            var panelClosed = 0;
            s.Get<Trigger>("inPanel").OnFire.Subscribe(_ => panelClosed++);

            s.Get<TabMenu>("m").Expand();
            s.Get<TabMenu>("m").Collapse();
            Assert.AreEqual(0, panelClosed, "the menu's fold is not this trigger's source");

            s.Get<Collapsible>("c").Collapse();
            Assert.AreEqual(1, panelClosed);
        }

        [Test]
        public void An_id_form_reaches_a_panel_from_outside_it()
        {
            var s = CollapsibleTests.Open(@"
                <VStack id='v' anchor='top-left' width='200'>
                  <Collapsible id='tasks' text='任务' transition='0'><Btn id='r'/></Collapsible>
                  <Trigger id='watcher' on='collapse@tasks'><Frame/></Trigger>
                </VStack>");
            var closed = 0;
            s.Get<Trigger>("watcher").OnFire.Subscribe(_ => closed++);

            s.Get<Collapsible>("tasks").Collapse();
            Assert.AreEqual(1, closed, "@id resolves lexically — a sibling is in scope");
        }

        [Test]
        public void Expand_fires_with_the_rows_already_alive()
        {
            var s = CollapsibleTests.Open(@"
                <Collapsible id='c' text='任务' expanded='false' transition='0'>
                  <Trigger id='opened' on='expand'><Frame id='probe'/></Trigger>
                </Collapsible>");
            var c = s.Get<Collapsible>("c");
            var activeWhenFired = false;
            s.Get<Trigger>("opened").OnFire.Subscribe(
                _ => activeWhenFired = s.Get<Frame>("probe").GameObject.activeInHierarchy);

            c.Expand();

            Assert.IsTrue(activeWhenFired,
                          "rows animate themselves in on expand, so they must be measurable by then");
        }

        [Test]
        public void A_row_animation_subscribes_to_both_directions()
        {
            var s = CollapsibleTests.Open(@"
                <Collapsible id='c' text='任务' transition='0'>
                  <Animation id='row' on='expand' reverse-on='collapse' translate='-12,0:0,0'>
                    <Btn id='r' height='32'/>
                  </Animation>
                </Collapsible>");
            var c = s.Get<Collapsible>("c");
            var anim = s.Get<PuiAnimation>("row");
            var forward = 0;
            var back = 0;
            anim.OnFire.Subscribe(_ => forward++);
            anim.OnReverse.Subscribe(_ => back++);

            c.Collapse();
            Assert.AreEqual(1, back);

            c.Expand();
            Assert.AreEqual(1, forward);
        }
    }
}
