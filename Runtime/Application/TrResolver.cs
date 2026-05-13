using System.Collections.Generic;
using PromptUGUI.Template;

namespace PromptUGUI.Application
{
    public static class TrResolver
    {
        public static string Resolve(
            string raw,
            IReadOnlyDictionary<string, string> args,
            string ctx)
        {
            if (raw == null) return null;
            var locale = UI.Locale.Current;

            // Pure-braces special case: when raw is *only* a `{{param}}` placeholder
            // (e.g. a Template body's <Text>{{label}}</Text>), the user-visible string
            // is whatever the invocation passed — there is no format string to
            // translate. Substitute first, then look the resulting value up in the
            // po store so each invocation's param value can be translated.
            if (locale != null && args != null && args.Count > 0 && IsPureBraces(raw))
            {
                var substituted = Substitution.Apply(raw, args);
                var hit = TranslationStore.Instance.Lookup(locale, ctx, substituted);
                return string.IsNullOrEmpty(hit) ? substituted : hit;
            }

            var template = raw;
            if (locale != null)
            {
                var hit = TranslationStore.Instance.Lookup(locale, ctx, raw);
                if (!string.IsNullOrEmpty(hit)) template = hit;
            }
            if (args == null || args.Count == 0) return template;
            return Substitution.Apply(template, args);
        }

        private static bool IsPureBraces(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.Trim();
            if (!t.StartsWith("{{") || !t.EndsWith("}}")) return false;
            var open = 0;
            var close = 0;
            foreach (var c in t)
            {
                if (c == '{') open++;
                else if (c == '}') close++;
            }
            return open == 2 && close == 2;
        }
    }
}
