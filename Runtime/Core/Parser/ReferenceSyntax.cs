using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// Pure-C# syntax validator for the <c>WxH</c> string used by
    /// <c>&lt;Screen reference="..."&gt;</c>. Lives in Core/Parser so it can be
    /// reused by <see cref="UIDocumentParser"/> (parse-time validation) and by
    /// the external <c>UIXmlLint</c> CLI (no Unity dependency). The Unity-side
    /// helper <c>PromptUGUI.Application.ReferenceResolutionParser</c> thin-wraps
    /// this to return a <c>UnityEngine.Vector2</c> for runtime callers.
    /// </summary>
    public static class ReferenceSyntax
    {
        public static (float W, float H)? Parse(string raw, string contextLabel)
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

            return (w, h);
        }
    }
}
