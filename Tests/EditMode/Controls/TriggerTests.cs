using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TriggerTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Open_trigger_fires_OnFire_on_open()
        {
            UI.LoadDocument("t", $"{Header}<Trigger id='t' on='open'/>{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            // Subscribe is after open, so we miss the initial fire — verify Fire() works
            screen.Get<Trigger>("t").Fire();
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Open_is_default_when_on_attribute_missing()
        {
            UI.LoadDocument("t", $"{Header}<Trigger id='t'/>{Footer}");
            var screen = UI.Open("S");
            Assert.IsNotNull(screen.Get<Trigger>("t"));
        }

        [Test]
        public void Manual_trigger_does_not_auto_fire()
        {
            UI.LoadDocument("t", $"{Header}<Trigger id='t' on='manual'/>{Footer}");
            int fires = 0;
            // Subscribe BEFORE open is impossible (screen doesn't exist yet); subscribe after open
            var screen = UI.Open("S");
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            Assert.AreEqual(0, fires, "manual trigger must not auto-fire");
            screen.Get<Trigger>("t").Fire();
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Invalid_on_throws_at_parse_time()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}<Trigger id='t' on='hover'/>{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Click_trigger_fires_when_unique_inner_Btn_clicked()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='click'><Btn id='b'>OK</Btn></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            var btn = screen.Get<Btn>("t/b");  // scoped path
            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Click_trigger_with_id_picks_correct_Btn()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='click@ok'>" +
                "  <Btn id='ok'>OK</Btn>" +
                "  <Btn id='cancel'>Cancel</Btn>" +
                "</Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            screen.Get<Btn>("t/cancel").GameObject
                .GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(0, fires, "cancel click must NOT fire when source is @ok");
            screen.Get<Btn>("t/ok").GameObject
                .GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Click_trigger_no_inner_Btn_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}<Trigger id='t' on='click'/>{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Click_trigger_multiple_Btn_no_id_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Trigger id='t' on='click'>" +
                    "  <Btn id='a'>A</Btn><Btn id='b'>B</Btn>" +
                    "</Trigger>" +
                    $"{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Click_trigger_unknown_id_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Trigger id='t' on='click@nope'><Btn id='ok'>OK</Btn></Trigger>" +
                    $"{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>());
        }
    }
}
