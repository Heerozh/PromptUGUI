using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PromptUGUI.Template {
    public static class Substitution {
        static readonly Regex Placeholder =
            new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

        public static string Apply(string raw, IReadOnlyDictionary<string, string> args) {
            if (raw == null) return null;
            return Placeholder.Replace(raw, m => {
                var name = m.Groups[1].Value;
                if (!args.TryGetValue(name, out var val))
                    throw new TemplateException(
                        $"unknown template parameter '{{{{{name}}}}}'");
                return val ?? "";
            });
        }
    }
}
