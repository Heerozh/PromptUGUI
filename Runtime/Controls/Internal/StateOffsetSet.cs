using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Per-state content offset (pixels) for a clickable control: how far the content-holder shifts
    /// while Pressed / Selected. Unset states — and Normal / Hover / Disabled — resolve to
    /// <see cref="Vector2.zero"/>. Pure data + parsing; mirrors <see cref="StateColorSet"/>.
    /// </summary>
    internal readonly struct StateOffsetSet
    {
        public readonly Vector2? Pressed;
        public readonly Vector2? Selected;

        public StateOffsetSet(Vector2? pressed, Vector2? selected)
        {
            Pressed = pressed;
            Selected = selected;
        }

        public bool HasAny => Pressed.HasValue || Selected.HasValue;

        /// <summary>Offset for a state; unset / Normal / Hover / Disabled → zero.</summary>
        public Vector2 For(InteractState state) => state switch
        {
            InteractState.Pressed => Pressed ?? Vector2.zero,
            InteractState.Selected => Selected ?? Vector2.zero,
            _ => Vector2.zero,
        };

        /// <summary>
        /// Parse an <c>"x,y"</c> pixel offset (Unity sign: negative y = down). <c>null</c> / empty /
        /// whitespace / <c>"none"</c> → <c>null</c> (state has no offset). Mirrors AnimationSpec's
        /// per-endpoint translate parse (kept local — AnimationSpec's is private).
        /// </summary>
        public static Vector2? Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v) || v == "none") return null;
            var parts = v.Split(',');
            if (parts.Length != 2)
                throw new ArgumentException($"Expected offset 'x,y', got '{v}'");
            return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
        }

        private static float ParseFloat(string s)
            => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
