using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Four optional colour specs keyed by <see cref="InteractState"/> (Hover / Pressed / Selected /
    /// Disabled; Normal has no entry). Used twice by <see cref="StateTintReactor"/>: as per-state
    /// ABSOLUTE base overrides (targetGraphic only — gradients allowed) and as per-state relative
    /// MULTIPLIERS (white = identity, solid-only, fanned out to the whole subtree). null = no override.
    /// </summary>
    internal readonly struct StateColorSet
    {
        public readonly ColorSpec? Hover;
        public readonly ColorSpec? Pressed;
        public readonly ColorSpec? Selected;
        public readonly ColorSpec? Disabled;

        public StateColorSet(ColorSpec? hover, ColorSpec? pressed, ColorSpec? selected, ColorSpec? disabled)
        {
            Hover = hover;
            Pressed = pressed;
            Selected = selected;
            Disabled = disabled;
        }

        public ColorSpec? For(InteractState state) => state switch
        {
            InteractState.Hover => Hover,
            InteractState.Pressed => Pressed,
            InteractState.Selected => Selected,
            InteractState.Disabled => Disabled,
            _ => null,
        };

        // "HasAny" (not "Any") so a use-site like `set.HasAny` never reads as a LINQ Enumerable.Any call.
        public bool HasAny => Hover.HasValue || Pressed.HasValue || Selected.HasValue || Disabled.HasValue;

        /// <summary>Absolute per-state base overrides — gradients allowed (spec §5). null/empty/whitespace
        /// ⇒ no override. Resolved via <see cref="UI.Theme.ResolveSpec"/>.</summary>
        public static StateColorSet ResolveAbsolutes(string hover, string pressed, string selected, string disabled)
            => new(RSpec(hover), RSpec(pressed), RSpec(selected), RSpec(disabled));

        /// <summary>Relative per-state multipliers — SOLID ONLY (spec §6). A gradient value throws via
        /// <see cref="UI.Theme.Resolve"/> ("does not support gradient colors"). null/empty/whitespace ⇒ none.</summary>
        public static StateColorSet ResolveModulates(string hover, string pressed, string selected, string disabled)
            => new(RSolid(hover), RSolid(pressed), RSolid(selected), RSolid(disabled));

        // IsNullOrWhiteSpace (not IsNullOrEmpty): a whitespace-only XML attr value is "no override".
        private static ColorSpec? RSpec(string v)
            => string.IsNullOrWhiteSpace(v) ? (ColorSpec?)null : UI.Theme.ResolveSpec(v);
        private static ColorSpec? RSolid(string v)
            => string.IsNullOrWhiteSpace(v) ? (ColorSpec?)null : ColorSpec.Solid(UI.Theme.Resolve(v));

        /// <summary>把禁用槽的 <c>none</c> 哨兵归一化为 null（不进颜色管线，避免 <see cref="UI.Theme.Resolve"/>
        /// 对非颜色值抛异常）。仅用于 <c>disabledModulate</c>："none" ⇒ 显式关闭禁用态视觉。</summary>
        internal static string NoneToNull(string v)
            => string.Equals(v, "none", System.StringComparison.OrdinalIgnoreCase) ? null : v;
    }
}
