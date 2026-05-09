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
            var template = raw;
            if (locale != null)
            {
                var hit = TranslationStore.Instance.Lookup(locale, ctx, raw);
                if (!string.IsNullOrEmpty(hit)) template = hit;
            }
            if (args == null || args.Count == 0) return template;
            return Substitution.Apply(template, args);
        }
    }
}
