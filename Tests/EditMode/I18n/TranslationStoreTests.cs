using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.I18n
{
    public class TranslationStoreTests
    {
        private TranslationStore _store;
        [SetUp] public void Setup() => _store = new TranslationStore();

        [Test]
        public void Lookup_BeforeAnyLoad_ReturnsNull()
        {
            Assert.IsNull(_store.Lookup("zh-Hans", null, "x"));
        }

        [Test]
        public void Load_ThenLookup_ReturnsMsgstr()
        {
            _store.Load("zh-Hans", new[] {
                new PoEntry { Msgid = "hello", Msgstr = "你好" },
            });
            Assert.AreEqual("你好", _store.Lookup("zh-Hans", null, "hello"));
        }

        [Test]
        public void Lookup_WithCtx_OnlyMatchesEntryWithSameCtx()
        {
            _store.Load("zh-Hans", new[] {
                new PoEntry { Msgid = "Open", Msgstr = "打开" },
                new PoEntry { Msgctxt = "door", Msgid = "Open", Msgstr = "开门" },
            });
            Assert.AreEqual("打开", _store.Lookup("zh-Hans", null, "Open"));
            Assert.AreEqual("开门", _store.Lookup("zh-Hans", "door", "Open"));
            Assert.IsNull(_store.Lookup("zh-Hans", "missing", "Open"));
        }

        [Test]
        public void Load_LaterCallsOverridePrior()
        {
            // i18n-custom override semantics: load auto first, then custom.
            _store.Load("zh-Hans", new[] { new PoEntry { Msgid = "x", Msgstr = "auto" } });
            _store.Load("zh-Hans", new[] { new PoEntry { Msgid = "x", Msgstr = "custom" } });
            Assert.AreEqual("custom", _store.Lookup("zh-Hans", null, "x"));
        }

        [Test]
        public void EmptyMsgstr_TreatedAsMiss()
        {
            _store.Load("zh-Hans", new[] { new PoEntry { Msgid = "x", Msgstr = "" } });
            Assert.IsNull(_store.Lookup("zh-Hans", null, "x"));
        }

        [Test]
        public void UnloadLocale_RemovesOnlyThatLocale()
        {
            _store.Load("zh-Hans", new[] { new PoEntry { Msgid = "x", Msgstr = "y" } });
            _store.Load("en", new[] { new PoEntry { Msgid = "x", Msgstr = "Y" } });
            _store.UnloadLocale("zh-Hans");
            Assert.IsNull(_store.Lookup("zh-Hans", null, "x"));
            Assert.AreEqual("Y", _store.Lookup("en", null, "x"));
        }

        [Test]
        public void UnloadAll_ClearsEverything()
        {
            _store.Load("en", new[] { new PoEntry { Msgid = "x", Msgstr = "Y" } });
            _store.UnloadAll();
            Assert.IsNull(_store.Lookup("en", null, "x"));
        }
    }
}
