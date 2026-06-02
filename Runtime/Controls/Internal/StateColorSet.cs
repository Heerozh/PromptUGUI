using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Four optional colours keyed by <see cref="InteractState"/> (Hover / Pressed / Selected /
    /// Disabled; Normal has no entry). Used twice by <see cref="StateTintReactor"/>: as per-state
    /// ABSOLUTE base overrides (applied only to the control's <c>targetGraphic</c>) and as per-state
    /// relative MULTIPLIERS (white = identity, fanned out to the whole subtree). A null entry means
    /// "no override for that state".
    /// </summary>
    internal readonly struct StateColorSet
    {
        public readonly Color? Hover;
        public readonly Color? Pressed;
        public readonly Color? Selected;
        public readonly Color? Disabled;

        public StateColorSet(Color? hover, Color? pressed, Color? selected, Color? disabled)
        {
            Hover = hover;
            Pressed = pressed;
            Selected = selected;
            Disabled = disabled;
        }

        public Color? For(InteractState state) => state switch
        {
            InteractState.Hover => Hover,
            InteractState.Pressed => Pressed,
            InteractState.Selected => Selected,
            InteractState.Disabled => Disabled,
            _ => null,
        };

        // "HasAny" (not "Any") so a use-site like `set.HasAny` never reads as a LINQ Enumerable.Any call.
        public bool HasAny => Hover.HasValue || Pressed.HasValue || Selected.HasValue || Disabled.HasValue;

        /// <summary>Resolve four raw attribute strings (hex / CSS named / theme token; null / empty /
        /// whitespace ⇒ no override) into resolved colours via <see cref="UI.Theme"/>.</summary>
        public static StateColorSet Resolve(string hover, string pressed, string selected, string disabled)
            => new(R(hover), R(pressed), R(selected), R(disabled));

        // IsNullOrWhiteSpace (not IsNullOrEmpty): a whitespace-only attribute value from XML is
        // treated as "no override" rather than passed to Theme.Resolve, which would error.
        private static Color? R(string v)
            => string.IsNullOrWhiteSpace(v) ? (Color?)null : UI.Theme.Resolve(v);
    }
}
