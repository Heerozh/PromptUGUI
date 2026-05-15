using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Application
{
    internal static class ReferenceResolutionParser
    {
        public static Vector2? Parse(string raw, string contextLabel)
        {
            var parsed = ReferenceSyntax.Parse(raw, contextLabel);
            return parsed.HasValue ? new Vector2(parsed.Value.W, parsed.Value.H) : null;
        }
    }
}
