using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PromptUGUI.Editor.I18n
{
    internal sealed class TranslationItem
    {
        public string Msgid { get; set; }
        public string Msgctxt { get; set; }
        public List<string> Comments { get; set; } = new();
    }

    internal sealed class BatchResult
    {
        public Dictionary<(string, string), string> Translations { get; }
        public string RawResponse { get; }
        public BatchResult(Dictionary<(string, string), string> translations, string rawResponse)
        {
            Translations = translations;
            RawResponse = rawResponse;
        }
    }

    internal sealed class TranslationClient
    {
        private readonly HttpClient _http;

        public TranslationClient(HttpClient http = null)
        {
            _http = http ?? new HttpClient();
        }

        public async Task<BatchResult> TranslateBatch(
            IList<TranslationItem> items,
            string targetLocale,
            string endpoint, string model, string apiKey, string systemPrompt,
            CancellationToken ct)
        {

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var prompt = systemPrompt.Replace("{{targetLocale}}", targetLocale);
            var inputJson = JsonConvert.SerializeObject(new
            {
                target_locale = targetLocale,
                items = items.Select(i => new
                {
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
            var body = new
            {
                model,
                messages = new object[] {
                    new { role = "system", content = prompt },
                    new { role = "user", content = userMessage },
                },
                response_format = new { type = "json_object" },
            };
            req.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json");

            var resp = await _http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {text}");
            }

            var doc = JObject.Parse(text);
            var content = (string)doc["choices"][0]["message"]["content"];
            var inner = JObject.Parse(content);
            var result = new Dictionary<(string, string), string>();
            foreach (var t in (JArray)inner["translations"])
            {
                var msgid = (string)t["msgid"];
                var ctxToken = t["msgctxt"];
                var msgctxt = ctxToken != null && ctxToken.Type != JTokenType.Null
                    ? (string)ctxToken
                    : null;
                var msgstr = (string)t["msgstr"];
                result[(msgid, msgctxt)] = msgstr;
            }
            return new BatchResult(result, text);
        }
    }
}
