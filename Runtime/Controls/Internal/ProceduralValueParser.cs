using System.Globalization;
using PromptUGUI.Parser;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared parsing for the plain-pixel procedural attributes (<c>borderWidth</c>, <c>glow</c>).
    /// Lifted out of <c>Frame</c> when <see cref="ProceduralControl"/> grew the same attributes —
    /// two copies of "what counts as a valid number of pixels" is exactly the kind of drift that
    /// makes one tag reject a value the other accepts.
    /// </summary>
    internal static class ProceduralValueParser
    {
        public static float Pixels(string value, string attrName)
        {
            // Variant 只能改值不能删属性，空串是作者退回"无"的唯一写法（同 RadiusParser）。
            if (string.IsNullOrWhiteSpace(value)) return 0f;
            if (!float.TryParse(value.Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var px))
                throw new ParseException(
                    $"{attrName}=\"{value}\": expected a number of pixels (e.g. \"1\", \"2.5\")");
            // "NaN" / "Infinity" parse fine, and NaN slips past the negative test below.
            if (float.IsNaN(px) || float.IsInfinity(px))
                throw new ParseException($"{attrName}=\"{value}\": must be a finite number");
            if (px < 0f)
                throw new ParseException($"{attrName}=\"{value}\": must not be negative");
            return px;
        }
    }
}
