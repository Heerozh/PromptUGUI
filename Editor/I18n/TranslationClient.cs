using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PromptUGUI.Editor.I18n {
    internal sealed class TranslationItem {
        public string Msgid { get; set; }
        public string Msgctxt { get; set; }
        public List<string> Comments { get; set; } = new();
    }

    internal sealed class TranslationClient {
        readonly HttpClient _http;

        public TranslationClient(HttpClient http = null) {
            _http = http ?? new HttpClient();
        }

        public async Task<Dictionary<(string, string), string>> TranslateBatch(
            IList<TranslationItem> items,
            string targetLocale,
            string endpoint, string model, string apiKey, string systemPrompt,
            CancellationToken ct) {

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var prompt = systemPrompt.Replace("{{targetLocale}}", targetLocale);
            var inputJson = JsonSerializer.Serialize(new {
                target_locale = targetLocale,
                items = items.Select(i => new {
                    msgid = i.Msgid,
                    msgctxt = i.Msgctxt,
                    comments = i.Comments,
                }),
            });
            var userMessage =
                "Translate every item below and respond with a single JSON object of the form:\n" +
                "{\"translations\":[{\"msgid\":\"...\",\"msgctxt\":\"... or null\",\"msgstr\":\"...\"}]}\n" +
                "Echo each msgid and msgctxt verbatim from the input. Do not add, omit, or merge items.\n\n" +
                "Input:\n" + inputJson;
            var body = new {
                model,
                messages = new object[] {
                    new { role = "system", content = prompt },
                    new { role = "user", content = userMessage },
                },
                response_format = new { type = "json_object" },
            };
            req.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var text = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(text);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content").GetString();
            using var inner = JsonDocument.Parse(content);
            var result = new Dictionary<(string, string), string>();
            foreach (var t in inner.RootElement.GetProperty("translations").EnumerateArray()) {
                var msgid = t.GetProperty("msgid").GetString();
                var msgctxt = t.TryGetProperty("msgctxt", out var ctxProp)
                              && ctxProp.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? ctxProp.GetString()
                    : null;
                var msgstr = t.GetProperty("msgstr").GetString();
                result[(msgid, msgctxt)] = msgstr;
            }
            return result;
        }
    }
}
