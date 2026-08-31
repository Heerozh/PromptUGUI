using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The accordion: <c>group=</c> makes opening one panel close its siblings, and closing them all
    /// is legal (spec 2026-08-31-collapsible-design §4.6).
    /// </summary>
    public class CollapsibleGroupTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Three = @"
            <VStack id='v' anchor='top-left' width='240' spacing='4'>
              <Collapsible id='a' group='settings' text='画面' transition='0'><Btn id='r1' height='30'/></Collapsible>
              <Collapsible id='b' group='settings' text='音频' expanded='false' transition='0'><Btn id='r2' height='30'/></Collapsible>
              <Collapsible id='c' group='settings' text='操作' expanded='false' transition='0'><Btn id='r3' height='30'/></Collapsible>
            </VStack>";

        [Test]
        public void Opening_one_closes_the_others()
        {
            var s = CollapsibleTests.Open(Three);
            Assert.IsTrue(s.Get<Collapsible>("a").IsExpanded);

            s.Get<Collapsible>("b").Expand();

            Assert.IsTrue(s.Get<Collapsible>("b").IsExpanded);
            Assert.IsFalse(s.Get<Collapsible>("a").IsExpanded, "one at a time");
            Assert.IsFalse(s.Get<Collapsible>("c").IsExpanded);
        }

        [Test]
        public void All_closed_is_a_legal_state()
        {
            var s = CollapsibleTests.Open(Three);

            s.Get<Collapsible>("a").Collapse();

            Assert.IsFalse(s.Get<Collapsible>("a").IsExpanded,
                           "unlike a ToggleGroup, folding the open one away is allowed");
            Assert.IsFalse(s.Get<Collapsible>("b").IsExpanded);
            Assert.IsFalse(s.Get<Collapsible>("c").IsExpanded);
        }

        [Test]
        public void Groups_do_not_reach_each_other()
        {
            var s = CollapsibleTests.Open(@"
                <VStack id='v' anchor='top-left' width='240'>
                  <Collapsible id='a' group='g1' text='A' transition='0'><Btn id='r1'/></Collapsible>
                  <Collapsible id='b' group='g2' text='B' transition='0'><Btn id='r2'/></Collapsible>
                </VStack>");

            s.Get<Collapsible>("b").Collapse();
            s.Get<Collapsible>("b").Expand();

            Assert.IsTrue(s.Get<Collapsible>("a").IsExpanded, "a different group is a different accordion");
        }

        [Test]
        public void Ungrouped_panels_never_close_each_other()
        {
            var s = CollapsibleTests.Open(@"
                <VStack id='v' anchor='top-left' width='240'>
                  <Collapsible id='a' text='A' transition='0'><Btn id='r1'/></Collapsible>
                  <Collapsible id='b' text='B' expanded='false' transition='0'><Btn id='r2'/></Collapsible>
                </VStack>");

            s.Get<Collapsible>("b").Expand();

            Assert.IsTrue(s.Get<Collapsible>("a").IsExpanded);
            Assert.IsTrue(s.Get<Collapsible>("b").IsExpanded);
        }

        [Test]
        public void Several_authored_open_keep_the_first_in_document_order()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("group='settings'"));

            var s = CollapsibleTests.Open(@"
                <VStack id='v' anchor='top-left' width='240'>
                  <Collapsible id='a' group='settings' text='画面' transition='0'><Btn id='r1'/></Collapsible>
                  <Collapsible id='b' group='settings' text='音频' transition='0'><Btn id='r2'/></Collapsible>
                  <Collapsible id='c' group='settings' text='操作' transition='0'><Btn id='r3'/></Collapsible>
                </VStack>");

            Assert.IsTrue(s.Get<Collapsible>("a").IsExpanded);
            Assert.IsFalse(s.Get<Collapsible>("b").IsExpanded, "the rest open closed, and are told so");
            Assert.IsFalse(s.Get<Collapsible>("c").IsExpanded);
            Assert.IsFalse(CollapsibleTests.Content(s.Get<Collapsible>("b")).gameObject.activeSelf);
        }

        [Test]
        public void A_variant_that_opens_another_member_closes_the_first()
        {
            var s = CollapsibleTests.Open(@"
                <VStack id='v' anchor='top-left' width='240'>
                  <Collapsible id='a' group='settings' text='画面' transition='0'><Btn id='r1'/></Collapsible>
                  <Collapsible id='b' group='settings' text='音频' expanded='false' expanded.portrait='true' transition='0'>
                    <Btn id='r2'/>
                  </Collapsible>
                </VStack>");
            Assert.IsTrue(s.Get<Collapsible>("a").IsExpanded);

            UI.Variants.Set("portrait", true);

            Assert.IsTrue(s.Get<Collapsible>("b").IsExpanded);
            Assert.IsFalse(s.Get<Collapsible>("a").IsExpanded);
        }

        [Test]
        public void Closing_the_screen_empties_the_pool()
        {
            var s = CollapsibleTests.Open(Three);
            var groups = s.CollapsibleGroups;
            Assert.AreEqual(3, groups.Members("settings").Count);

            s.Close();

            Assert.AreEqual(0, groups.Members("settings").Count);
        }

        [Test]
        public void Changing_a_panels_group_at_runtime_moves_it()
        {
            var s = CollapsibleTests.Open(Three);
            var c = s.Get<Collapsible>("c");

            c.Group = "other";

            Assert.AreEqual(2, s.CollapsibleGroups.Members("settings").Count);
            c.Expand();
            Assert.IsTrue(s.Get<Collapsible>("a").IsExpanded, "it no longer speaks for the old group");
        }
    }
}
