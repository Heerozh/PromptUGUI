using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Parses the <c>padding</c> attribute shared by the layout-group controls
    /// (<see cref="TabBar"/>, <see cref="TabMenu"/>) into a <see cref="RectOffset"/>.
    ///
    /// <para>Three shorthands, CSS-order per segment count: <c>"all"</c> /
    /// <c>"vertical,horizontal"</c> / <c>"top,right,bottom,left"</c>.</para>
    /// </summary>
    /// <remarks>
    /// Lenient by construction, and deliberately so: this is the behaviour <c>TabBar</c> shipped
    /// with, extracted verbatim so the move stays a pure refactor. An unparseable segment reads as
    /// 0 and an unsupported segment count (3, or 5+) yields an all-zero offset — neither raises.
    /// The lint layer is where a malformed <c>padding</c> should be reported; changing it to throw
    /// here would break documents that render fine today.
    /// </remarks>
    internal static class PaddingParser
    {
        /// <param name="value">The raw attribute text. Null / empty returns <paramref name="fallback"/>.</param>
        /// <param name="fallback">Returned only when the value is absent; null means a zero offset.</param>
        public static RectOffset Parse(string value, RectOffset fallback = null)
        {
            if (string.IsNullOrEmpty(value)) return fallback ?? new RectOffset();

            var parts = value.Split(',');
            int t = 0, r = 0, b = 0, l = 0;
            switch (parts.Length)
            {
                case 1:
                    int.TryParse(parts[0], out t);
                    r = b = l = t;
                    break;
                case 2:
                    int.TryParse(parts[0], out t);
                    b = t;
                    int.TryParse(parts[1], out r);
                    l = r;
                    break;
                case 4:
                    int.TryParse(parts[0], out t);
                    int.TryParse(parts[1], out r);
                    int.TryParse(parts[2], out b);
                    int.TryParse(parts[3], out l);
                    break;
            }
            return new RectOffset(l, r, t, b);
        }
    }
}
