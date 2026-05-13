using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.I18n
{
    public class TrResolverTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void Resolve_NullRaw_ReturnsNull()
        {
            Assert.IsNull(TrResolver.Resolve(null, null, null));
        }

        [Test]
        public void Resolve_LocaleNull_ReturnsRaw()
        {
            Assert.AreEqual("hi", TrResolver.Resolve("hi", null, null));
        }

        [Test]
        public void Resolve_Miss_ReturnsRaw()
        {
            UI.Locale.Set("zh-Hans");
            Assert.AreEqual("hi", TrResolver.Resolve("hi", null, null));
        }

        [Test]
        public void Resolve_Hit_ReturnsMsgstr()
        {
            TranslationStore.Instance.Load("zh-Hans", new[] {
                new PoEntry { Msgid = "hi", Msgstr = "你好" },
            });
            UI.Locale.Set("zh-Hans");
            Assert.AreEqual("你好", TrResolver.Resolve("hi", null, null));
        }

        [Test]
        public void Resolve_HitWithCtx_ReturnsCtxScopedMsgstr()
        {
            TranslationStore.Instance.Load("zh-Hans", new[] {
                new PoEntry { Msgid = "Open", Msgstr = "打开" },
                new PoEntry { Msgctxt = "door", Msgid = "Open", Msgstr = "开门" },
            });
            UI.Locale.Set("zh-Hans");
            Assert.AreEqual("开门", TrResolver.Resolve("Open", null, "door"));
            Assert.AreEqual("打开", TrResolver.Resolve("Open", null, null));
        }

        [Test]
        public void Resolve_WithArgs_AppliesSubstitutionAfterLookup()
        {
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry { Msgid = "金币: {{n}}", Msgstr = "Gold: {{n}}" },
            });
            UI.Locale.Set("en");
            var args = new Dictionary<string, string> { { "n", "123" } };
            Assert.AreEqual("Gold: 123", TrResolver.Resolve("金币: {{n}}", args, null));
        }

        [Test]
        public void Resolve_ArgsOnMiss_StillSubstitutesIntoRaw()
        {
            UI.Locale.Set("ja");  // empty store
            var args = new Dictionary<string, string> { { "n", "5" } };
            Assert.AreEqual("金币: 5", TrResolver.Resolve("金币: {{n}}", args, null));
        }

        [Test]
        public void Resolve_PureBracesRaw_LooksUpSubstitutedValue()
        {
            // When the raw text is *just* a placeholder (e.g. template body
            // <Text>{{label}}</Text>), the user-visible string is the parameter
            // value at the invocation site. The po file holds the param VALUE as
            // msgid, so the resolver must substitute first, then look up.
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry { Msgid = "开始", Msgstr = "Start" },
            });
            UI.Locale.Set("en");
            var args = new Dictionary<string, string> { { "label", "开始" } };
            Assert.AreEqual("Start", TrResolver.Resolve("{{label}}", args, null));
        }

        [Test]
        public void Resolve_PureBracesRaw_FallsThroughOnMiss()
        {
            // If the substituted value isn't in the po, render the substituted
            // value verbatim — same fallback behavior as a plain miss.
            UI.Locale.Set("en");  // empty store
            var args = new Dictionary<string, string> { { "label", "Quit" } };
            Assert.AreEqual("Quit", TrResolver.Resolve("{{label}}", args, null));
        }
    }
}
