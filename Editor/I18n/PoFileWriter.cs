using System.Collections.Generic;
using System.Linq;
using PromptUGUI.I18n;

namespace PromptUGUI.Editor.I18n {
    internal static class PoFileWriter {
        /// <summary>
        /// Take an existing .po file's text and a fresh extraction; produce a new .po text where
        /// existing non-empty msgstr values are preserved (keyed by (msgctxt, msgid)), comments are
        /// replaced by the current extraction, and entries no longer in extraction are dropped.
        /// </summary>
        public static string Merge(string existingPoText, IEnumerable<ExtractedString> extracted) {
            var existingByKey = string.IsNullOrEmpty(existingPoText)
                ? new Dictionary<(string, string), PoEntry>()
                : PoParser.Parse(existingPoText)
                    .ToDictionary(e => (e.Msgctxt, e.Msgid));

            // Group extracted by key so multi-occurrence msgid merge their comments.
            var grouped = new Dictionary<(string ctx, string id), ExtractedString>();
            foreach (var e in extracted) {
                var k = (e.Msgctxt, e.Msgid);
                if (!grouped.TryGetValue(k, out var existing)) {
                    grouped[k] = new ExtractedString {
                        Msgid = e.Msgid,
                        Msgctxt = e.Msgctxt,
                        Comments = new List<string>(e.Comments),
                        ExtractedComments = new List<string>(e.ExtractedComments),
                        References = new List<string>(e.References),
                    };
                } else {
                    existing.ExtractedComments.AddRange(e.ExtractedComments);
                    existing.References.AddRange(e.References);
                    existing.Comments.AddRange(e.Comments);
                }
            }

            var output = new List<PoEntry>();
            foreach (var kv in grouped) {
                var es = kv.Value;
                var entry = new PoEntry {
                    Msgctxt = es.Msgctxt,
                    Msgid = es.Msgid,
                    Msgstr = existingByKey.TryGetValue(kv.Key, out var prev) ? prev.Msgstr : "",
                    TranslatorComments = new List<string>(),
                };
                // Merge comment streams: # translator (free) + #. extracted + #: refs.
                foreach (var c in es.Comments) entry.TranslatorComments.Add(c);
                foreach (var c in es.ExtractedComments) entry.TranslatorComments.Add($". {c}");
                foreach (var r in es.References) entry.TranslatorComments.Add($": {r}");
                output.Add(entry);
            }
            // Stable order: by ctx, then by msgid.
            output.Sort((a, b) => {
                int c = string.Compare(a.Msgctxt ?? "", b.Msgctxt ?? "", System.StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.Msgid ?? "", b.Msgid ?? "", System.StringComparison.Ordinal);
            });
            return PoParser.Serialize(output);
        }
    }
}
