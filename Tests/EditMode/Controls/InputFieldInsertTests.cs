using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using R3;
using PInputField = PromptUGUI.Controls.InputField;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>InputField.Insert</c> (spec 2026-10-19 §4.12): text goes in at the caret — the live selection while
    /// editing, otherwise the caret the last edit ended with (the end of the text if there was none, clamped
    /// when code shortened the text). A whole insert or nothing: over <c>characterLimit</c>, read-only or
    /// mid-composition it refuses. It is a code write — OnValueChanged fires, OnInput does not.
    /// </summary>
    public class InputFieldInsertTests
    {
        private PInputField _f;
        private PuiInputField _tmp;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                                 + "<InputField id='f' anchor='center' size='200x40'/>"
                                 + "<InputField id='limited' anchor='top-center' size='200x40' characterLimit='6'/>"
                                 + "<InputField id='ro' anchor='bottom-center' size='200x40' readOnly='true'/>"
                                 + "</Screen></PromptUGUI>");
            var s = UI.Open("S");
            _f = s.Get<PInputField>("f");
            _tmp = _f.GameObject.GetComponent<PuiInputField>();
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
        }

        [Test]
        public void Never_edited_appends_at_the_end()
        {
            _f.TextValue = "hello";
            Assert.IsTrue(_f.Insert(":)"));
            Assert.AreEqual("hello:)", _f.TextValue);
        }

        [Test]
        public void Uses_the_caret_from_the_last_edit()
        {
            _f.TextValue = "hello";
            _tmp.RememberCaretForTests(3, 3);

            _f.Insert("XY");

            Assert.AreEqual("helXYlo", _f.TextValue);
        }

        [Test]
        public void Consecutive_inserts_follow_each_other()
        {
            _f.TextValue = "hello";
            _tmp.RememberCaretForTests(2, 2);

            _f.Insert("A");
            _f.Insert("B");

            Assert.AreEqual("heABllo", _f.TextValue, "the caret moves past what was inserted");
        }

        [Test]
        public void Replaces_the_remembered_selection()
        {
            _f.TextValue = "hello";
            _tmp.RememberCaretForTests(4, 1);   // anchor after the focus: a selection made right to left

            _f.Insert("-");

            Assert.AreEqual("h-o", _f.TextValue);
        }

        [Test]
        public void A_caret_past_a_shortened_text_clamps_to_the_end()
        {
            _f.TextValue = "hello world";
            _tmp.RememberCaretForTests(9, 9);
            _f.TextValue = "hi";

            _f.Insert("!");

            Assert.AreEqual("hi!", _f.TextValue);
        }

        [Test]
        public void While_editing_it_replaces_the_live_selection()
        {
            _f.TextValue = "hello";
            _tmp.RememberCaretForTests(0, 0);   // stale: the live selection wins while editing
            _f.SetEditingForTests(true);
            _tmp.selectionStringAnchorPosition = 1;
            _tmp.selectionStringFocusPosition = 3;

            _f.Insert("EE");

            Assert.AreEqual("hEElo", _f.TextValue);
            Assert.IsTrue(_f.IsEditing, "the focus is not touched");
        }

        [Test]
        public void Over_the_character_limit_inserts_nothing()
        {
            var limited = UI.Get("S").Get<PInputField>("limited");
            limited.TextValue = "abcde";

            Assert.IsFalse(limited.Insert("XY"), "6 is the limit: a whole insert or nothing");
            Assert.AreEqual("abcde", limited.TextValue);
            Assert.IsTrue(limited.Insert("X"));
            Assert.AreEqual("abcdeX", limited.TextValue);
        }

        [Test]
        public void ReadOnly_refuses()
        {
            var ro = UI.Get("S").Get<PInputField>("ro");
            ro.TextValue = "fixed";
            Assert.IsFalse(ro.Insert("x"));
            Assert.AreEqual("fixed", ro.TextValue);
        }

        [Test]
        public void Composing_refuses()
        {
            _f.TextValue = "hello";
            _tmp.CompositionForTests = "ni";
            try
            {
                Assert.IsFalse(_f.Insert("x"), "the IME has text it has not committed");
                Assert.AreEqual("hello", _f.TextValue);
            }
            finally { _tmp.CompositionForTests = null; }
        }

        [Test]
        public void Empty_or_null_inserts_nothing_and_succeeds()
        {
            _f.TextValue = "hello";
            var changes = new List<string>();
            using var sub = _f.OnValueChanged.Subscribe(changes.Add);

            Assert.IsTrue(_f.Insert(""));
            Assert.IsTrue(_f.Insert(null));

            Assert.AreEqual("hello", _f.TextValue);
            Assert.IsEmpty(changes);
        }

        [Test]
        public void Raises_OnValueChanged_once()
        {
            _f.TextValue = "hello";
            var changes = new List<string>();
            using var sub = _f.OnValueChanged.Subscribe(changes.Add);

            _f.Insert("!");

            CollectionAssert.AreEqual(new[] { "hello!" }, changes);
        }
    }
}
