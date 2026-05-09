using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor {
    public class TranslationClientTests {
        sealed class StubHandler : HttpMessageHandler {
            public Func<HttpRequestMessage, HttpResponseMessage> Reply;
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage req, CancellationToken ct) => Task.FromResult(Reply(req));
        }

        [Test]
        public async Task TranslateBatch_ParsesStructuredJson() {
            var stub = new StubHandler {
                Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(@"
                        { ""choices"": [ { ""message"": {
                          ""content"": ""{\""translations\"":[{\""msgid\"":\""hello\"",\""msgctxt\"":null,\""msgstr\"":\""你好\""}]}""
                        } } ] }"),
                },
            };
            var client = new TranslationClient(new HttpClient(stub));
            var input = new List<TranslationItem> {
                new() { Msgid = "hello", Msgctxt = null, Comments = new() { "ctx" } },
            };
            var result = await client.TranslateBatch(
                input, "zh-Hans",
                endpoint: "https://example/v1", model: "x", apiKey: "k", systemPrompt: "p",
                CancellationToken.None);
            Assert.AreEqual("你好", result["hello"]);
        }

        [Test]
        public void TranslateBatch_NonOk_Throws() {
            var stub = new StubHandler {
                Reply = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) {
                    Content = new StringContent(""),
                },
            };
            var client = new TranslationClient(new HttpClient(stub));
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await client.TranslateBatch(
                    new List<TranslationItem>(),
                    "zh-Hans", "https://e/v1", "x", "k", "p", CancellationToken.None));
        }
    }
}
