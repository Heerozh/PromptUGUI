using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.Modals
{
    /// <summary>
    /// The last step of the Escape chain (spec 2026-10-19-input-begin-edit §4.4): a text field being edited
    /// owns Escape and B — the chain records the press as spent, TMP cancels the edit. Start is not the
    /// field's; a popup or a dialog above it still answers first.
    /// </summary>
    public class EscapeChainFieldTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='page'>
    <InputField id='f' anchor='center' size='200x40'/>
    <Btn id='a' anchor='top-left' size='40x40'>A</Btn>
    <Popover id='pop' on='click@a' transition='0'><InputField id='pf' width='160' height='40'/></Popover>
  </Screen>
  <Screen name='form'>
    <Frame id='panel' anchor='center' size='300x200'><InputField id='mf' anchor='center' size='200x40'/></Frame>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "doc" ? Xml : null);
            UI.Router.Map("page", "doc", screen: "page");
            UI.Router.Map("form", "doc", screen: "form", present: RoutePresent.Modal, parent: "page");
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static bool Press(EscapeKey key = EscapeKey.Escape) =>
            EscapeChain.Dispatch(key, EscapeChain.NextTestPress());

        private static InputField OpenPageField()
        {
            UI.Router.Open("page").GetAwaiter().GetResult();
            return UI.Get("page").Get<InputField>("f");
        }

        [Test]
        public void Escape_on_a_field_being_edited_is_consumed()
        {
            var f = OpenPageField();
            f.SetEditingForTests(true);

            Assert.IsTrue(Press());
            Assert.IsTrue(UI.EscapeConsumedThisFrame);
        }

        [Test]
        public void B_on_a_field_being_edited_is_consumed()
        {
            OpenPageField().SetEditingForTests(true);
            Assert.IsTrue(Press(EscapeKey.CancelButton), "TMP cancels the edit on B too");
        }

        [Test]
        public void Start_on_a_field_being_edited_is_not()
        {
            OpenPageField().SetEditingForTests(true);
            Assert.IsFalse(Press(EscapeKey.StartButton), "TMP does not cancel on Start; the game may pause on it");
        }

        [Test]
        public void A_field_not_being_edited_takes_nothing()
        {
            var f = OpenPageField();
            f.SetEditingForTests(false);
            Assert.IsFalse(Press());
        }

        [Test]
        public void A_field_that_stopped_editing_is_forgotten()
        {
            var f = OpenPageField();
            f.SetEditingForTests(true);
            f.SetEditingForTests(false);
            Assert.IsFalse(Press());
        }

        [Test]
        public void A_field_closed_while_editing_is_forgotten()
        {
            OpenPageField().SetEditingForTests(true);
            UI.CloseImmediate("page");
            Assert.IsFalse(Press());
        }

        [Test]
        public void A_routed_modal_with_a_field_being_edited_still_goes_back()
        {
            UI.Router.Open("page").GetAwaiter().GetResult();
            UI.Router.Open("form").GetAwaiter().GetResult();
            UI.Get("form").Get<InputField>("mf").SetEditingForTests(true);

            Assert.IsTrue(Press());

            CollectionAssert.AreEqual(new[] { "page" }, UI.Router.Chain.ToList(), "the dialog answers, as before");
        }

        [Test]
        public void Start_still_answers_a_routed_modal()
        {
            UI.Router.Open("page").GetAwaiter().GetResult();
            UI.Router.Open("form").GetAwaiter().GetResult();

            Assert.IsTrue(Press(EscapeKey.StartButton));

            CollectionAssert.AreEqual(new[] { "page" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void A_popover_with_a_field_being_edited_still_closes_first()
        {
            UI.Router.Open("page").GetAwaiter().GetResult();
            var page = UI.Get("page");
            var pop = page.Get<Popover>("pop");
            pop.Open();
            page.Get<InputField>("pf").SetEditingForTests(true);

            Assert.IsTrue(Press());

            Assert.IsFalse(pop.IsOpen, "the popup step comes before the field");
        }

        [Test]
        public void The_field_listens_only_while_editing()
        {
            var f = OpenPageField();
            var before = f.GameObject.GetComponent<ModalEscapeListener>();
            Assert.IsTrue(before == null || !before.enabled, "no key source while not editing");

            f.SetEditingForTests(true);
            var listener = f.GameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener);
            Assert.IsTrue(listener.enabled);
            Assert.IsTrue(listener.AlsoCancelButton, "B is a field's key too");

            f.SetEditingForTests(false);
            Assert.IsFalse(listener.enabled);
        }

        [Test]
        public void The_field_listener_is_a_key_source_for_the_chain()
        {
            var f = OpenPageField();
            f.SetEditingForTests(true);

            f.GameObject.GetComponent<ModalEscapeListener>().FireForTests();

            Assert.IsTrue(UI.EscapeConsumedThisFrame);
        }
    }
}
