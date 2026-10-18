using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Which keys the suggestion list takes from the field (spec 2026-10-18 §6.2 / §6.6 / §6.11), as a
    /// pure function of the key and the state. Everything the list does not take goes to TMP untouched.
    /// </summary>
    public class SuggestionKeysTests
    {
        private static KeyAction Route(string key, bool open = true, bool highlight = false,
                                       bool composing = false, bool compositionJustEnded = false,
                                       bool escapeSpent = false)
            => SuggestionKeys.Route(Event.KeyboardEvent(key), new KeyState(open, highlight, composing,
                                                                            compositionJustEnded, escapeSpent));

        // ── list open ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Arrows_move_the_highlight()
        {
            Assert.AreEqual(KeyAction.HighlightNext, Route("down"));
            Assert.AreEqual(KeyAction.HighlightPrev, Route("up"));
        }

        [Test]
        public void Enter_with_a_highlight_commits_it()
        {
            Assert.AreEqual(KeyAction.CommitHighlighted, Route("return", highlight: true));
            Assert.AreEqual(KeyAction.CommitHighlighted, Route("[enter]", highlight: true));
        }

        [Test]
        public void Enter_without_a_highlight_is_TMPs_submit()
        {
            Assert.AreEqual(KeyAction.PassToTmp, Route("return"));
            Assert.AreEqual(KeyAction.PassToTmp, Route("[enter]"));
        }

        [Test]
        public void Escape_is_swallowed_the_chain_closes_the_list()
            => Assert.AreEqual(KeyAction.Swallow, Route("escape"));

        [Test]
        public void Editing_keys_go_to_TMP()
        {
            foreach (var key in new[] { "a", "left", "right", "home", "end", "backspace", "delete", "^v", "^a", "^c" })
                Assert.AreEqual(KeyAction.PassToTmp, Route(key), key);
        }

        [Test]
        public void Arrows_with_a_modifier_go_to_TMP()
        {
            // Shift+↓ extends a text selection in a multi-line field; the list only takes the bare arrow.
            Assert.AreEqual(KeyAction.PassToTmp, Route("#down"));
            Assert.AreEqual(KeyAction.PassToTmp, Route("^up"));
        }

        // ── list closed ────────────────────────────────────────────────────────────────────

        [Test]
        public void With_the_list_closed_every_key_goes_to_TMP()
        {
            foreach (var key in new[] { "down", "up", "return", "escape", "a" })
                Assert.AreEqual(KeyAction.PassToTmp, Route(key, open: false), key);
        }

        [Test]
        public void An_escape_the_chain_already_spent_is_swallowed_even_after_the_list_closed()
        {
            // The chain closes the list in the Input System callbacks, before the EventSystem update in
            // which TMP sees the same Escape — and would cancel the edit, rolling the text back.
            Assert.AreEqual(KeyAction.Swallow, Route("escape", open: false, escapeSpent: true));
            Assert.AreEqual(KeyAction.PassToTmp, Route("a", open: false, escapeSpent: true));
        }

        // ── input method ───────────────────────────────────────────────────────────────────

        [Test]
        public void Nothing_is_taken_while_composing_or_in_the_frame_composition_ends()
        {
            foreach (var key in new[] { "down", "up", "return", "escape" })
            {
                Assert.AreEqual(KeyAction.PassToTmp, Route(key, highlight: true, composing: true), key);
                Assert.AreEqual(KeyAction.PassToTmp, Route(key, highlight: true, compositionJustEnded: true), key);
            }
        }
    }
}
