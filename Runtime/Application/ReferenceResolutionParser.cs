using System.Globalization;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Application
{
    internal static class ReferenceResolutionParser
    {
        public static Vector2? Parse(string raw, string contextLabel)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            var x = raw.IndexOf('x');
            if (x <= 0 || x >= raw.Length - 1 || raw.IndexOf('x', x + 1) >= 0)
                throw new ParseException(
                    $"{contextLabel}: invalid reference '{raw}', expected WxH " +
                    $"(e.g. '1920x1080')");

            var wStr = raw.Substring(0, x);
            var hStr = raw.Substring(x + 1);

            if (!float.TryParse(wStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || !float.TryParse(hStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                throw new ParseException(
                    $"{contextLabel}: invalid reference '{raw}', both dimensions must be numeric");

            if (w <= 0f || h <= 0f)
                throw new ParseException(
                    $"{contextLabel}: reference '{raw}' both dimensions must be positive");

            return new Vector2(w, h);
        }
    }
}
