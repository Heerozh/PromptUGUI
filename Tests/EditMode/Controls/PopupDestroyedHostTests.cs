using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A popup whose panel was destroyed without being closed — its Screen's GameObjects torn down by a scene
    /// change or by leaving Play mode, never through <c>UI.Close</c> — is a dead chain entry. The chain must
    /// drop it without touching what is gone: closing it the usual way writes to the destroyed panel and
    /// throws, so a later <c>DismissBelow</c> (a routed page appearing) or <c>Prune</c> failed. And a new
    /// Play session (domain reload off) starts from an empty chain.
    /// </summary>
    public class PopupDestroyedHostTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Btn id='a' anchor='center' size='40x40'>A</Btn>
    <Popover id='pop'><Btn id='inside' width='80' height='30'>1</Btn></Popover>
    <TabMenu id='menu' anchor='top-left' transition='0'><Tab id='x' text='X' isOn='true'/></TabMenu>
  </Screen>
</PromptUGUI>";

        private PuguiScreen _s;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", Xml);
            _s = UI.Open("S");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static bool IsDestroyed(Object o) => !ReferenceEquals(o, null) && o == null;

        [Test]
        public void A_popover_whose_screen_was_destroyed_leaves_the_chain_quietly()
        {
            _s.Get<Popover>("pop").Open(_s.Get<Btn>("a"));
            Object.DestroyImmediate(_s.RootGameObject);

            Assert.DoesNotThrow(() => PopupChain.DismissBelow(int.MaxValue), "a routed page appearing");
            Assert.IsTrue(PopupChain.IsEmpty);
        }

        [Test]
        public void A_tabmenu_whose_screen_was_destroyed_leaves_the_chain_quietly()
        {
            _s.Get<TabMenu>("menu").Expand();
            Object.DestroyImmediate(_s.RootGameObject);

            Assert.DoesNotThrow(PopupChain.Prune);
            Assert.IsTrue(PopupChain.IsEmpty);
        }

        [Test]
        public void The_focus_cage_of_a_destroyed_panel_is_let_go()
        {
            UI.UseGamepadNavigation();
            _s.Get<Popover>("pop").Open(_s.Get<Btn>("a"));
            Assume.That(UI.Navigation.ContainmentRoot, Is.SameAs(_s.Get<Popover>("pop").GameObject));
            Object.DestroyImmediate(_s.RootGameObject);

            PopupChain.Prune();

            Assert.IsFalse(IsDestroyed(UI.Navigation.ContainmentRoot), "no cage left on a panel that is gone");
        }

        [Test]
        public void A_new_play_session_starts_with_an_empty_chain()
        {
            _s.Get<Popover>("pop").Open(_s.Get<Btn>("a"));
            Assume.That(PopupChain.IsEmpty, Is.False);

            PopupChain.ResetForPlaySession();   // what SubsystemRegistration runs with domain reload off

            Assert.IsTrue(PopupChain.IsEmpty);
        }
    }
}
