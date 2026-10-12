using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>A parsed <see cref="FontFloorTarget.screen"/>: physical pixels at an OS scale.</summary>
    public readonly struct FontFloorScreen
    {
        public float Width { get; }
        public float Height { get; }

        /// <summary>Physical pixels per dp (iOS <c>@2x</c>, Android density, Windows 125% = 1.25).</summary>
        public float Density { get; }

        /// <summary>
        /// The reserved variant this screen activates: taller than wide is <c>portrait</c>, anything
        /// else (square included) <c>landscape</c> — the same call <c>OrientationTracker.Apply</c> makes.
        /// </summary>
        public string Orientation => Height > Width ? "portrait" : "landscape";

        public FontFloorScreen(float width, float height, float density)
        {
            Width = width;
            Height = height;
            Density = density;
        }
    }

    /// <summary>
    /// <c>WxH[@D[x]]</c> — the <c>screen</c> of a font-floor target (2026-10-12 font-floor spec §5.2).
    /// Shared by <c>PromptUGUISettings.OnValidate</c>, <see cref="LintRun"/> and the CLI's
    /// <c>--target</c>, so a row reads the same everywhere.
    /// </summary>
    public static class FontFloorTargetSyntax
    {
        private const string Expected = "expected WxH[@D] (e.g. \"750x1334@2x\")";
        private static readonly char[] VariantSeparators = { ' ', ',', '\t' };

        public static bool TryParse(string raw, out FontFloorScreen screen, out string error)
        {
            screen = default;
            error = null;
            var s = raw?.Trim();
            if (string.IsNullOrEmpty(s))
            {
                error = $"screen is empty: {Expected}";
                return false;
            }

            var density = 1f;
            var size = s;
            var at = s.IndexOf('@');
            if (at >= 0)
            {
                var d = s.Substring(at + 1);
                if (d.EndsWith("x")) d = d.Substring(0, d.Length - 1);
                size = s.Substring(0, at);
                if (!TryPositive(d, out density))
                {
                    error = $"screen=\"{s}\": {Expected}";
                    return false;
                }
            }

            var x = size.IndexOf('x');
            if (x <= 0 || x != size.LastIndexOf('x')
                || !TryPositive(size.Substring(0, x), out var w)
                || !TryPositive(size.Substring(x + 1), out var h))
            {
                error = $"screen=\"{s}\": {Expected}";
                return false;
            }

            screen = new FontFloorScreen(w, h, density);
            return true;
        }

        /// <summary>The extra variants of a row: space- or comma-separated, blanks dropped.</summary>
        public static string[] SplitVariants(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new string[0];
            var result = new List<string>();
            foreach (var part in raw.Split(VariantSeparators))
                if (part.Length > 0) result.Add(part);
            return result.ToArray();
        }

        /// <summary>
        /// What is wrong with <paramref name="row"/>, or null. A blank row is inert, not wrong. The
        /// orientation pair is reserved: it follows the aspect, so writing it by hand could only
        /// contradict the screen.
        /// </summary>
        public static string Validate(FontFloorTarget row)
        {
            if (row == null || row.IsBlank) return null;
            var label = $"font-floor target '{row.DisplayName}'";
            if (!TryParse(row.screen, out _, out var error)) return $"{label}: {error}";
            foreach (var v in SplitVariants(row.variants))
                if (v == "portrait" || v == "landscape")
                    return $"{label}: variants=\"{row.variants}\" names '{v}' — the orientation follows the " +
                           "screen's aspect (taller = portrait); list only the other variants";
            return null;
        }

        private static bool TryPositive(string raw, out float value)
            => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f;
    }
}
