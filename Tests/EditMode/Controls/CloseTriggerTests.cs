using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The <c>close</c> trigger kind (spec 2026-09-16-close-transition-design §4.1): a Screen
    /// event, accepted by <c>on=</c> and <c>reverse-on=</c>, no <c>@id</c> form, refused by
    /// <c>&lt;Show&gt;</c>. In EditMode a Screen never plays an exit, so a close-bound animation is
    /// still destroyed on the spot — but the event itself fires.
    /// </summary>
    public class CloseTriggerTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        [Test]
        public void Close_parses_as_a_screen_event_for_on_and_reverse_on()
        {
            Assert.AreEqual(TriggerKind.Close, TriggerSpec.Parse("close").Kind);
            Assert.IsNull(TriggerSpec.Parse("close").SourceId);
            Assert.AreEqual(TriggerKind.Close, TriggerSpec.ParseReverseOn("close").Kind);
        }

        [Test]
        public void Close_has_no_id_form()
        {
            Assert.Throws<ArgumentException>(() => TriggerSpec.Parse("close@dialog"));
        }

        [Test]
        public void Show_refuses_close()
        {
            Assert.That(() => Open("<Btn id='b'><Show on='close'><Image/></Show></Btn>"),
                Throws.InstanceOf<Exception>().With.Message.Contains("close"));
        }

        [Test]
        public void EditMode_close_with_a_close_bound_animation_is_still_immediate()
        {
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.3s'><Frame id='f'/></Animation>");
            UI.Close("S");
            Assert.IsNull(screen.RootGameObject, "no frames in EditMode — nothing to play the exit on");
            Assert.IsFalse(screen.IsClosing);
        }

        [Test]
        public void Trigger_on_close_fires_once_on_Close_even_in_EditMode()
        {
            var screen = Open("<Trigger id='t' on='close'><Frame id='f'/></Trigger>");
            var fired = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);
            UI.Close("S");
            Assert.AreEqual(1, fired);
        }
    }
}
