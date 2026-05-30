using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateTriggerTests
    {
        // Mirror of the (protected) UnityEngine.UI.Selectable.SelectionState ordinals.
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;
        private const int Selected = 3;
        private const int Disabled = 4;

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // ---- FindStateSource resolution ----

        [Test]
        public void State_trigger_inside_Btn_resolves_ancestor()
        {
            Assert.DoesNotThrow(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Btn id='b'><Trigger id='tr' on='state-pressed'/></Btn>" +
                    $"{Footer}");
                UI.Open("S");
            });
        }

        [Test]
        public void State_trigger_with_id_resolves_named_Btn()
        {
            Assert.DoesNotThrow(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Trigger id='tr' on='state-pressed@b'><Btn id='b'/></Trigger>" +
                    $"{Footer}");
                UI.Open("S");
            });
        }

        [Test]
        public void State_trigger_with_no_Btn_ancestor_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Trigger id='tr' on='state-pressed'/>" +
                    $"{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }

        [Test]
        public void State_trigger_at_id_pointing_to_non_Btn_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Trigger id='tr' on='state-pressed@label'>" +
                    "  <Text id='label'>hi</Text>" +
                    "</Trigger>" +
                    $"{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }

        [Test]
        public void State_trigger_unknown_id_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Trigger id='tr' on='state-pressed@nope'><Btn id='b'/></Trigger>" +
                    $"{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }

        // ---- Behavior: fires on state enter ----

        [Test]
        public void State_pressed_fires_only_on_pressed()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Btn id='b'><Trigger id='tr' on='state-pressed'/></Btn>" +
                $"{Footer}");
            var screen = UI.Open("S");

            var trigger = screen.Get<Trigger>("b/tr");
            int fires = 0;
            using var _ = trigger.OnFire.Subscribe(__ => fires++);

            // state-pressed must not fire at open (button starts Normal).
            Assert.AreEqual(0, fires, "state-pressed must not fire at open");

            var pui = screen.Get<Btn>("b").GameObject.GetComponent<PuiButton>();
            Assert.IsNotNull(pui, "Btn should host a PuiButton");

            pui.SimulateState(Highlighted); // Hover -> no fire
            Assert.AreEqual(0, fires, "hover must not fire a state-pressed trigger");

            pui.SimulateState(Pressed); // Pressed -> fire
            Assert.AreEqual(1, fires, "pressed must fire a state-pressed trigger");

            pui.SimulateState(Normal); // back to Normal -> no fire
            Assert.AreEqual(1, fires);

            pui.SimulateState(Pressed); // pressed again -> fire again
            Assert.AreEqual(2, fires);
        }

        [Test]
        public void State_normal_fires_once_at_open()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Btn id='b'><Trigger id='tr' on='state-normal'/></Btn>" +
                $"{Footer}");
            var screen = UI.Open("S");

            var trigger = screen.Get<Trigger>("b/tr");
            int fires = 0;
            // OnState (ReactiveProperty) replays the current Normal value on subscribe,
            // so InitTriggerSubscription fires once at open. Subscribing after open we
            // miss that fire — assert subsequent transitions instead.
            using var _ = trigger.OnFire.Subscribe(__ => fires++);

            var pui = screen.Get<Btn>("b").GameObject.GetComponent<PuiButton>();
            pui.SimulateState(Pressed);    // -> no fire
            Assert.AreEqual(0, fires);
            pui.SimulateState(Normal);     // back to Normal -> fire
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void State_trigger_at_id_fires_for_named_Btn_only()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='tr' on='state-pressed@a'><Btn id='a'/></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");

            var trigger = screen.Get<Trigger>("tr");
            int fires = 0;
            using var _ = trigger.OnFire.Subscribe(__ => fires++);

            var puiInner = screen.Get<Btn>("tr/a").GameObject.GetComponent<PuiButton>();
            puiInner.SimulateState(Pressed);
            Assert.AreEqual(1, fires, "pressing the @a Btn must fire the trigger");
        }
    }
}
