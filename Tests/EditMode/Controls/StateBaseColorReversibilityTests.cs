using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using PuiToggle = PromptUGUI.Controls.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A control that declares a state colour must still follow its own <c>color=</c> when a Variant
    /// or a theme changes it.
    ///
    /// <para><see cref="StateTintReactor"/> captures the authored base by peeking the graphic on first
    /// init and never re-peeks — deliberately, because on a re-apply the graphic may be showing a TINT
    /// (the control is hovered), and promoting that would bake the hover colour in for good. The
    /// consequence was that a later <c>color=</c> never landed at all: <c>ControlAttributeApplier</c>
    /// writes it, then <c>Configure</c>'s repaint paints the stale captured base straight back over
    /// it. Every <c>&lt;Btn hoverColor&gt;</c> / <c>&lt;Tab selectedColor&gt;</c> / <c>&lt;Toggle&gt;</c>
    /// was frozen at its first colour for the life of the Screen — a responsive <c>color.mobile</c>
    /// and a theme switch were both silently ignored.</para>
    ///
    /// <para>The fix is the §8 shape: the base comes from the declaration the control is holding, not
    /// from a snapshot of the pixels. The hover guard at the bottom is what makes that a fix rather
    /// than a swap of one bug for another.</para>
    /// </summary>
    public class StateBaseColorReversibilityTests
    {
        private const int Normal = 0;
        private const int Highlighted = 1;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = true;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                + $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        // The bg the state machine drives, wherever the control keeps it (Toggle's lives on a child).
        private static string BgOf(IControl control)
        {
            var selectable = ((Control)control).GameObject.GetComponent<Selectable>();
            Assert.IsNotNull(selectable, "guard: the control has a Selectable");
            Assert.IsNotNull(selectable.targetGraphic, "guard: the Selectable has a target graphic");
            return "#" + ColorUtility.ToHtmlStringRGB(selectable.targetGraphic.color);
        }

        [Test]
        public void Btn_WithAStateColour_FollowsAVariantThatChangesItsBase()
        {
            UI.Variants.Set("alt", false);
            var screen = Open(
                "<Btn id='b' width='40' height='20' color='#E8D2A8' color.alt='#112233' "
                + "hoverColor='#F5E6C8'>x</Btn>");
            var btn = screen.Get<Btn>("b");
            Assume.That(BgOf(btn), Is.EqualTo("#E8D2A8"), "guard: the base colour landed");

            UI.Variants.Set("alt", true);

            Assert.AreEqual("#112233", BgOf(btn),
                "declaring hoverColor must not freeze the base colour for the life of the Screen");
        }

        [Test]
        public void Tab_WithSelectedColour_FollowsAVariantOnItsUnselectedBase()
        {
            UI.Variants.Set("alt", false);
            var screen = Open(
                "<TabBar id='tb' width='200' height='20'>"
                + "  <Tab id='t' width='stretch' color='#E8D2A8' color.alt='#112233' "
                + "       selectedColor='#CDEBA8'/>"
                + "  <Tab id='other' width='stretch' isOn='true'/>"
                + "</TabBar>");
            var tab = screen.Get<Tab>("t");
            Assume.That(BgOf(tab), Is.EqualTo("#E8D2A8"), "guard: unselected tab shows its base");

            UI.Variants.Set("alt", true);

            Assert.AreEqual("#112233", BgOf(tab),
                "selectedColor drives the SELECTED base; the unselected one is still plain color=");
        }

        [Test]
        public void Toggle_WithAStateColour_FollowsAVariantThatChangesItsBase()
        {
            UI.Variants.Set("alt", false);
            var screen = Open(
                "<Toggle id='g' color='#E8D2A8' color.alt='#112233' hoverColor='#F5E6C8'>x</Toggle>");
            var toggle = screen.Get<PuiToggle>("g");
            Assume.That(BgOf(toggle), Is.EqualTo("#E8D2A8"), "guard: the base colour landed");

            UI.Variants.Set("alt", true);

            Assert.AreEqual("#112233", BgOf(toggle));
        }

        // The reason the base was captured once in the first place. A re-apply that changes nothing,
        // arriving while the control is hovered, must not adopt the hover tint as the new base — the
        // button would keep the hover colour forever after the pointer left.
        [Test]
        public void AReApplyWhileHovered_DoesNotPromoteTheTintIntoTheBase()
        {
            var screen = Open(
                "<Btn id='b' width='40' height='20' color='#888888' hoverColor='#FF0000'>x</Btn>");
            var btn = screen.Get<Btn>("b");
            var pui = btn.GameObject.GetComponent<PuiButton>();

            pui.SimulateState(Highlighted);
            Assume.That(BgOf(btn), Is.EqualTo("#FF0000"), "guard: hovered shows the hover colour");

            screen.ReSolve();               // a resize / Variant / theme pass arriving mid-hover
            pui.SimulateState(Normal);

            Assert.AreEqual("#888888", BgOf(btn),
                "the base is what color= declares, never whatever the pixels happened to show");
        }

        // Nothing declared → nothing to push, so the first-init peek stays the base. This is the
        // path every control without state colours takes, and it was never broken; it is here so the
        // fix cannot quietly regress it.
        [Test]
        public void AControlWithNoAuthoredColour_KeepsItsBuiltInBase()
        {
            var screen = Open("<Btn id='b' width='40' height='20' hoverColor='#FF0000'>x</Btn>");
            var btn = screen.Get<Btn>("b");
            var pui = btn.GameObject.GetComponent<PuiButton>();
            var atRest = BgOf(btn);

            pui.SimulateState(Highlighted);
            Assume.That(BgOf(btn), Is.EqualTo("#FF0000"));
            screen.ReSolve();
            pui.SimulateState(Normal);

            Assert.AreEqual(atRest, BgOf(btn), "the control's own default bg colour is the base");
        }
    }
}
