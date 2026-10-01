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

        // ── layers: runtime .po catalogs (spec 2026-10-01-runtime-sprite-sets-design §8.2) ────

        private static PoEntry[] One(string msgid, string msgstr) =>
            new[] { new PoEntry { Msgid = msgid, Msgstr = msgstr } };

        [Test]
        public void Layer_entry_overrides_base()
        {
            _store.Load("en", One("hi", "Hello"));
            var layer = _store.AddLayer();
            _store.LoadLayer(layer, "en", One("hi", "Howdy"));

            Assert.AreEqual("Howdy", _store.Lookup("en", null, "hi"));
        }

        [Test]
        public void Later_layer_overrides_earlier_layer()
        {
            var first = _store.AddLayer();
            var second = _store.AddLayer();
            _store.LoadLayer(second, "en", One("hi", "Second"));
            _store.LoadLayer(first, "en", One("hi", "First"));

            Assert.AreEqual("Second", _store.Lookup("en", null, "hi"), "order is registration order, not load order");
        }

        [Test]
        public void Removing_layer_restores_base()
        {
            _store.Load("en", One("hi", "Hello"));
            var layer = _store.AddLayer();
            _store.LoadLayer(layer, "en", One("hi", "Howdy"));

            Assert.IsTrue(_store.RemoveLayer(layer));

            Assert.AreEqual("Hello", _store.Lookup("en", null, "hi"));
            Assert.IsFalse(_store.RemoveLayer(layer));
        }

        [Test]
        public void UnloadLocale_clears_base_and_layers()
        {
            _store.Load("en", One("hi", "Hello"));
            var layer = _store.AddLayer();
            _store.LoadLayer(layer, "en", One("bye", "Bye"));
            _store.LoadLayer(layer, "fr", One("bye", "Salut"));

            _store.UnloadLocale("en");

            Assert.IsNull(_store.Lookup("en", null, "hi"));
            Assert.IsNull(_store.Lookup("en", null, "bye"));
            Assert.AreEqual("Salut", _store.Lookup("fr", null, "bye"));
        }

        [Test]
        public void UnloadAll_clears_layer_entries_but_keeps_layers()
        {
            var layer = _store.AddLayer();
            _store.LoadLayer(layer, "en", One("hi", "Howdy"));

            _store.UnloadAll();
            Assert.IsNull(_store.Lookup("en", null, "hi"));

            _store.Load("en", One("hi", "Hello"));
            _store.LoadLayer(layer, "en", One("hi", "Howdy"));
            Assert.AreEqual("Howdy", _store.Lookup("en", null, "hi"), "the layer is still there, still on top");
        }

        [Test]
        public void Load_into_removed_layer_is_ignored()
        {
            var layer = _store.AddLayer();
            _store.RemoveLayer(layer);

            _store.LoadLayer(layer, "en", One("hi", "Howdy"));

            Assert.IsNull(_store.Lookup("en", null, "hi"));
        }

        [Test]
        public void Empty_msgstr_in_layer_falls_through()
        {
            _store.Load("en", One("hi", "Hello"));
            var layer = _store.AddLayer();
            _store.LoadLayer(layer, "en", One("hi", ""));

            Assert.AreEqual("Hello", _store.Lookup("en", null, "hi"));
        }

        [Test]
        public void ClearLayers_removes_every_layer()
        {
            _store.Load("en", One("hi", "Hello"));
            var layer = _store.AddLayer();
            _store.LoadLayer(layer, "en", One("hi", "Howdy"));

            _store.ClearLayers();

            Assert.AreEqual("Hello", _store.Lookup("en", null, "hi"));
            Assert.IsFalse(_store.RemoveLayer(layer));
        }
    }
}
