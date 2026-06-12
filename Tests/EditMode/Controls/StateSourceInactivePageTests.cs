using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Regression: a <c>state-*</c> source (<c>&lt;Show&gt;</c> / <c>&lt;Trigger&gt;</c> /
    /// <c>&lt;Animation&gt;</c>, or a <c>*Modulate</c> Btn) whose <c>&lt;Btn&gt;</c>/<c>&lt;Tab&gt;</c>/
    /// <c>&lt;Toggle&gt;</c> ancestor lives on a TabBar-bound page that is NOT the initially-selected
    /// tab must still resolve at Open. That page is <c>SetActive(false)</c> at Open, and the source
    /// resolution's <c>GetComponentInParent&lt;IStateSource&gt;</c> walk must include inactive parents
    /// (otherwise it threw "no &lt;Btn&gt;/&lt;Tab&gt;/&lt;Toggle&gt; ancestor found").
    /// </summary>
    public class StateSourceInactivePageTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        // *Modulate fan-out tweens via LitMotion (no player loop in EditMode) — snap instead.
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { StateTintReactor.TestForceInstant = false; UI.ResetForTests(); }

        // The Btn on the initially-active bound page (t1/p1) resolves fine — baseline.
        [Test]
        public void State_show_on_active_bound_page_opens()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<TabBar id='tabs' anchor='top-stretch' height='32'>" +
                "  <Tab id='t1' width='80' bind='p1' isOn='true'/>" +
                "  <Tab id='t2' width='80' bind='p2'/>" +
                "</TabBar>" +
                "<Frame id='p1' anchor='stretch' margin='32,0,0,0'>" +
                "  <Btn id='b' color='#3B82F6' hoverColor='#5B9CF8'>" +
                "    <Show on='state-hover'><Image raycastTarget='false'/></Show>" +
                "  </Btn>" +
                "</Frame>" +
                "<Frame id='p2' anchor='stretch' margin='32,0,0,0'><Text>p2</Text></Frame>" +
                $"{Footer}");
            Assert.DoesNotThrow(() => UI.Open("S"));
        }

        // The Btn on the initially-INACTIVE bound page (t2/p2, t2 not selected) must also resolve.
        // This is the case that previously threw a ParseException at Open.
        [Test]
        public void State_show_on_inactive_bound_page_opens()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<TabBar id='tabs' anchor='top-stretch' height='32'>" +
                "  <Tab id='t1' width='80' bind='p1' isOn='true'/>" +
                "  <Tab id='t2' width='80' bind='p2'/>" +
                "</TabBar>" +
                "<Frame id='p1' anchor='stretch' margin='32,0,0,0'><Text>p1</Text></Frame>" +
                "<Frame id='p2' anchor='stretch' margin='32,0,0,0'>" +
                "  <Btn id='b' color='#3B82F6' hoverColor='#5B9CF8'>" +
                "    <Show on='state-hover'><Image raycastTarget='false'/></Show>" +
                "  </Btn>" +
                "</Frame>" +
                $"{Footer}");
            Assert.DoesNotThrow(() => UI.Open("S"));
        }

        // A *Modulate Btn (StateTintReactor fan-out) on an initially-inactive bound page — exercises
        // the second GetComponentInParent<IStateSource> walk (StateTintReactor.EnsureInit).
        [Test]
        public void Modulate_btn_with_nested_state_show_on_inactive_page_opens()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<TabBar id='tabs' anchor='top-stretch' height='32'>" +
                "  <Tab id='t1' width='80' bind='p1' isOn='true'/>" +
                "  <Tab id='t2' width='80' bind='p2'/>" +
                "</TabBar>" +
                "<Frame id='p1' anchor='stretch' margin='32,0,0,0'><Text>p1</Text></Frame>" +
                "<Frame id='p2' anchor='stretch' margin='32,0,0,0'>" +
                "  <Btn id='b' color='#3B82F6' hoverColor='#5B9CF8' pressedModulate='#BBBBBB'>" +
                "    <Text raycastTarget='false'>x</Text>" +
                "    <Show on='state-hover'><Image raycastTarget='false'/></Show>" +
                "  </Btn>" +
                "</Frame>" +
                $"{Footer}");
            Assert.DoesNotThrow(() => UI.Open("S"));
        }
    }
}
