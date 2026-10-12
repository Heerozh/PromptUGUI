namespace PromptUGUI.Parser
{
    /// <summary>
    /// Value grammar for <c>hazeSpace</c> — where the noise fog is sampled (spec 2026-09-17 haze
    /// H-D9). <c>canvas</c> (the default) samples the canvas, so a panel is a window onto one shared
    /// sky and a moving panel sees the clouds slide past; <c>self</c> samples the panel's own
    /// coordinates offset by a seed taken when it is first drawn (scroll offsets removed), so the
    /// fog travels with it — the spelling for a scrolling row, a carousel page, a dragged item.
    ///
    /// <para>Pure C# — no UnityEngine types — so the UIXmlLint CLI shares this exact implementation
    /// with the runtime setters, and the accepted keywords and the error wording cannot drift.</para>
    /// </summary>
    public static class HazeSpaceAttrParser
    {
        public const string Name = "hazeSpace";
        public const string Canvas = "canvas";
        public const string Self = "self";

        /// <summary>
        /// <paramref name="self"/> is true for <c>self</c>. Empty / whitespace resolves to
        /// <c>canvas</c> rather than erroring: a Variant can only override an attribute's value,
        /// never remove it, so <c>hazeSpace.mobile=""</c> is the author's only way back. Anything else
        /// is an error, not a silent canvas — <c>hazeSpace="local"</c> looks like it works and would
        /// leave the author watching the fog slide across a scrolling row.
        /// </summary>
        public static bool TryParse(string value, out bool self, out string error)
        {
            error = null;
            self = false;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();
            if (string.Equals(raw, Canvas, System.StringComparison.Ordinal)) return true;
            if (string.Equals(raw, Self, System.StringComparison.Ordinal)) { self = true; return true; }

            error = $"{Name}=\"{value}\": expected \"{Canvas}\" (the fog stays put, panels are windows onto it) " +
                    $"or \"{Self}\" (the fog travels with the panel)";
            return false;
        }

        /// <summary>Throwing wrapper for the runtime attribute setters.</summary>
        public static bool Parse(string value)
            => TryParse(value, out var self, out var error)
                ? self
                : throw new ParseException(error);
    }
}
