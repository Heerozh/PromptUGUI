using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using PInputField = PromptUGUI.Controls.InputField;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>InputField.BeginEdit()</c> (spec 2026-10-19-input-begin-edit §4.1): a request TMP carries out in its
    /// next <c>LateUpdate</c> — refused, with nothing left pending, when the field is inactive, not interactable
    /// or read-only. EditMode runs no LateUpdate; what a live edit does is in the PlayMode fixture.
    /// </summary>
    public class InputFieldBeginEditTests
    {
        private PromptUGUI.Application.Screen _s;
        private GameObject _es;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                                 + "<InputField id='f' anchor='center' size='200x40'/>"
                                 + "<InputField id='ro' anchor='bottom-center' size='200x40' readOnly='true'/>"
                                 + "<InputField id='off' anchor='top-center' size='200x40' interactable='false'/>"
                                 + "<Frame id='box' anchor='top-left' size='200x40' hidden='true'>"
                                 + "<InputField id='hid' anchor='stretch'/></Frame>"
                                 + "</Screen></PromptUGUI>");
            _s = UI.Open("S");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) Object.DestroyImmediate(_es);
        }

        private PInputField F(string id) => _s.Get<PInputField>(id);
        private static PuiInputField Tmp(PInputField f) => f.GameObject.GetComponent<PuiInputField>();

        [Test]
        public void An_editable_field_takes_the_request()
        {
            var f = F("f");
            Assert.IsTrue(f.BeginEdit());
            Assert.IsTrue(Tmp(f).EditRequestPending, "TMP activates it in its next LateUpdate");
            Assert.IsFalse(f.IsEditing, "not yet: the request is carried out in LateUpdate");
        }

        [Test]
        public void A_read_only_field_refuses()
        {
            var f = F("ro");
            Assert.IsFalse(f.BeginEdit());
            Assert.IsFalse(Tmp(f).EditRequestPending);
        }

        [Test]
        public void A_field_that_is_not_interactable_refuses()
        {
            var f = F("off");
            Assert.IsFalse(f.BeginEdit());
            Assert.IsFalse(Tmp(f).EditRequestPending);
        }

        [Test]
        public void An_inactive_field_refuses()
        {
            var f = F("hid");
            Assert.IsFalse(f.GameObject.activeInHierarchy, "precondition");
            Assert.IsFalse(f.BeginEdit());
            Assert.IsFalse(Tmp(f).EditRequestPending);
        }

        [Test]
        public void The_test_hook_pinned_to_not_editing_is_pinned_to_editing()
        {
            var f = F("f");
            f.SetEditingForTests(false);
            var edges = 0;
            f.EditingChanged += _ => edges++;

            Assert.IsTrue(f.BeginEdit());

            Assert.IsTrue(f.IsEditing);
            Assert.AreEqual(1, edges);
        }

        [Test]
        public void Under_directional_navigation_the_select_it_causes_is_not_undone()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            _es = new GameObject("ES", typeof(EventSystem));
            var es = _es.GetComponent<EventSystem>();
            var f = F("f");
            var gate = f.GameObject.GetComponent<InputFieldNavGate>();

            f.BeginEdit();
            es.SetSelectedGameObject(f.GameObject);   // what TMP's activation does

            Assert.IsFalse(gate.SuppressingForTests, "the code asked for an edit: the gate lets TMP activate it");
        }

        [Test]
        public void A_plain_directional_select_is_still_undone()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            _es = new GameObject("ES", typeof(EventSystem));
            var f = F("f");

            _es.GetComponent<EventSystem>().SetSelectedGameObject(f.GameObject);

            Assert.IsTrue(f.GameObject.GetComponent<InputFieldNavGate>().SuppressingForTests,
                "the two-level model is untouched for navigation");
        }
    }
}
