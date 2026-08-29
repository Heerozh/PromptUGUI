using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>on="expand"</c> / <c>on="collapse"</c> — the hook for animating a menu's rows in, since
    /// the panel itself is an internal node an author cannot wrap (spec §7.10).
    ///
    /// <para>They are not called <c>open</c> / <c>close</c> because <c>on="open"</c> already means
    /// "the Screen opened".</para>
    /// </summary>
    public class TabMenuTriggerTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        // ── Parsing ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Parses_bare_and_targeted_forms()
        {
            Assert.AreEqual(TriggerKind.Expand, TriggerSpec.Parse("expand").Kind);
            Assert.AreEqual(TriggerKind.Collapse, TriggerSpec.Parse("collapse").Kind);

            var targeted = TriggerSpec.Parse("expand@channel");
            Assert.AreEqual(TriggerKind.Expand, targeted.Kind);
            Assert.AreEqual("channel", targeted.SourceId);

            Assert.AreEqual("channel", TriggerSpec.Parse("collapse@channel").SourceId);
        }

        [Test]
        public void An_empty_id_is_rejected_like_every_other_targeted_form()
        {
            Assert.Throws<ArgumentException>(() => TriggerSpec.Parse("expand@"));
        }

        // ── Firing ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Expand_fires_on_open_only()
        {
            var s = Open(@"
              <TabMenu id='m' transition='0'>
                <Trigger id='t' on='expand'><Tab id='a' text='A'/></Trigger>
              </TabMenu>");
            var m = s.Get<TabMenu>("m");

            int fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            m.Expand();
            Assert.AreEqual(1, fired);

            m.Collapse();
            Assert.AreEqual(1, fired, "collapsing is a different event");
        }

        [Test]
        public void Collapse_fires_on_close_only()
        {
            var s = Open(@"
              <TabMenu id='m' transition='0'>
                <Trigger id='t' on='collapse'><Tab id='a' text='A'/></Trigger>
              </TabMenu>");
            var m = s.Get<TabMenu>("m");

            int fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            m.Expand();
            Assert.AreEqual(0, fired);

            m.Collapse();
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void A_bare_trigger_resolves_upward_through_the_collapsed_popup()
        {
            // The trigger lives inside a popup that is switched off at this point — the ancestor
            // walk has to include inactive objects or it finds nothing.
            var s = Open(@"
              <TabMenu id='m' transition='0'>
                <Trigger id='t' on='expand'><Tab id='a' text='A'/></Trigger>
              </TabMenu>");

            int fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);
            s.Get<TabMenu>("m").Expand();

            Assert.AreEqual(1, fired);
        }

        // Same scope rule as state-*@<id>: the target is looked up in the trigger's own subtree,
        // so a targeted form wraps the menu rather than sitting beside it.
        [Test]
        public void A_targeted_trigger_names_a_menu_in_its_subtree()
        {
            var s = Open(@"
              <Trigger id='t' on='expand@m'>
                <TabMenu id='m' transition='0'><Tab id='a' text='A'/></TabMenu>
              </Trigger>");

            int fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);
            s.Get<TabMenu>("m").Expand();

            Assert.AreEqual(1, fired);
        }

        // ── Misuse ────────────────────────────────────────────────────────────────────────

        // The apply pass wraps whatever a setter throws in a ParseException, exactly as it does for
        // a bad state-* source; what matters is that the message names the fix.
        private static string MessageFromOpening(string innerXml)
        {
            var ex = Assert.Throws<PromptUGUI.Parser.ParseException>(() => Open(innerXml));
            return ex.Message;
        }

        [Test]
        public void A_bare_trigger_with_no_menu_ancestor_throws()
        {
            var message = MessageFromOpening("<Frame><Trigger id='t' on='expand'><Frame/></Trigger></Frame>");
            StringAssert.Contains("no <TabMenu> ancestor found", message);
            StringAssert.Contains("expand@<id>", message, "…and points at the way out");
        }

        [Test]
        public void Targeting_something_that_is_not_a_menu_throws()
        {
            var message = MessageFromOpening(
                "<Frame><Trigger id='t' on='expand@btn'><Btn id='btn'>x</Btn></Trigger></Frame>");
            StringAssert.Contains("not a <TabMenu>", message);
        }

        [Test]
        public void Show_still_refuses_anything_that_is_not_a_state()
        {
            var message = MessageFromOpening(
                "<TabMenu id='m'><Show on='expand'><Tab id='a' text='A'/></Show></TabMenu>");
            StringAssert.Contains("only accepts state-", message);
            StringAssert.Contains("expand", message, "the message echoes what the author wrote");
        }
    }
}
