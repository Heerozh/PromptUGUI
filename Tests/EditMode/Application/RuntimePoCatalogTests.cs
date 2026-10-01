using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// Runtime .po catalogs layered over TranslationStore (spec 2026-10-01-runtime-sprite-sets-design §8).
    /// </summary>
    public class RuntimePoCatalogTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // Built-in translations: what PoResolver serves.
            UI.PoResolver = locale => Done(locale == "fr" ? new[] { ("hi", "Bonjour") } : new[] { ("hi", "Hello") });
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Awaitable<IEnumerable<PoEntry>> Done(params (string id, string str)[] es)
        {
            var list = new List<PoEntry>();
            foreach (var (id, str) in es) list.Add(new PoEntry { Msgid = id, Msgstr = str });
            return AwaitableHelpers.Completed<IEnumerable<PoEntry>>(list);
        }

        private static string Tr(string msgid) => UI.Tr(msgid);

        // ── registration ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Duplicate_catalog_name_throws()
        {
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done());
            Assert.Throws<InvalidOperationException>(() => UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done()));
            Assert.Throws<InvalidOperationException>(() => UI.Locale.RegisterRuntimeCatalogAsync("ugc", _ => Done()),
                "the Async version throws synchronously too");
        }

        [Test]
        public void Invalid_catalog_arguments_throw()
        {
            Assert.Catch<ArgumentException>(() => UI.Locale.RegisterRuntimeCatalog(null, _ => Done()));
            Assert.Catch<ArgumentException>(() => UI.Locale.RegisterRuntimeCatalog("", _ => Done()));
            Assert.Throws<ArgumentNullException>(() => UI.Locale.RegisterRuntimeCatalog("ugc", null));
        }

        [Test]
        public void Unregister_unknown_catalog_returns_false()
        {
            Assert.IsFalse(UI.Locale.UnregisterRuntimeCatalog("nope"));
        }

        [Test]
        public void Registered_before_Set_is_loaded_on_Set()
        {
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done(("item", "Sword")));

            UI.Locale.Set("en");

            Assert.AreEqual("Sword", Tr("item"));
            Assert.AreEqual("Hello", Tr("hi"));
        }

        [Test]
        public void Registered_after_Set_loads_current_and_retranslates_open_text()
        {
            UI.Locale.Set("en");
            UI.LoadDocument("test", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                                    "<Screen name='S'><Text id='t'>item</Text></Screen></PromptUGUI>");
            var tmp = UI.Open("S").Get("t").GameObject.GetComponent<TMP_Text>();
            Assume.That(tmp.text, Is.EqualTo("item"));

            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done(("item", "Sword")));

            Assert.AreEqual("Sword", tmp.text, "registering re-applies the open Screens once");
        }

        [Test]
        public void Unregister_restores_builtin_translation()
        {
            UI.Locale.Set("en");
            UI.Locale.RegisterRuntimeCatalog("patch", _ => Done(("hi", "Howdy")));
            Assume.That(Tr("hi"), Is.EqualTo("Howdy"));

            Assert.IsTrue(UI.Locale.UnregisterRuntimeCatalog("patch"));

            Assert.AreEqual("Hello", Tr("hi"));
        }

        [Test]
        public void Later_catalog_overrides_earlier_and_builtin()
        {
            UI.Locale.Set("en");
            UI.Locale.RegisterRuntimeCatalog("a", _ => Done(("hi", "From A")));
            UI.Locale.RegisterRuntimeCatalog("b", _ => Done(("hi", "From B")));

            Assert.AreEqual("From B", Tr("hi"));
        }

        [Test]
        public void RegisterAsync_propagates_loader_exception()
        {
            UI.Locale.Set("en");
            var ex = Assert.Throws<System.IO.IOException>(() =>
                UI.Locale.RegisterRuntimeCatalogAsync("bad",
                    _ => AwaitableHelpers.Faulted<IEnumerable<PoEntry>>(new System.IO.IOException("offline")))
                  .GetAwaiter().GetResult());
            StringAssert.Contains("offline", ex.Message);
        }

        [Test]
        public void RegisterAsync_failure_keeps_registration_for_next_switch()
        {
            UI.Locale.Set("en");
            var fail = true;
            Assert.Catch(() => UI.Locale.RegisterRuntimeCatalogAsync("ugc", _ => fail
                    ? AwaitableHelpers.Faulted<IEnumerable<PoEntry>>(new System.IO.IOException("offline"))
                    : Done(("item", "Épée")))
                .GetAwaiter().GetResult());

            fail = false;
            UI.Locale.Set("fr");

            Assert.AreEqual("Épée", Tr("item"), "still registered: the next switch loads it");
        }

        [Test]
        public void Unregister_during_pending_load_drops_result()
        {
            UI.Locale.Set("en");
            var src = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => src.Awaitable);

            UI.Locale.UnregisterRuntimeCatalog("ugc");
            src.SetResult(new[] { new PoEntry { Msgid = "item", Msgstr = "Sword" } });

            Assert.AreEqual("item", Tr("item"));
        }

        // ── locale switch integration ─────────────────────────────────────────────────────────

        [Test]
        public void Locale_switch_loads_catalog_for_new_locale()
        {
            var asked = new List<string>();
            UI.Locale.RegisterRuntimeCatalog("ugc", locale =>
            {
                asked.Add(locale);
                return Done(("item", locale == "fr" ? "Épée" : "Sword"));
            });

            UI.Locale.Set("en");
            UI.Locale.Set("fr");

            CollectionAssert.AreEqual(new[] { "en", "fr" }, asked);
            Assert.AreEqual("Épée", Tr("item"));
        }

        [Test]
        public void Catalog_failure_does_not_block_locale_switch()
        {
            UI.Locale.RegisterRuntimeCatalog("bad", _ => throw new InvalidOperationException("broken"));

            LogAssert.Expect(LogType.Error, new Regex("runtime catalog 'bad'.*broken"));
            UI.Locale.Set("en");

            Assert.IsTrue(UI.Variants.IsActive("en"));
            Assert.AreEqual("Hello", Tr("hi"));
        }

        [Test]
        public void Stale_catalog_load_after_locale_switch_is_dropped()
        {
            var sources = new Dictionary<string, AwaitableCompletionSource<IEnumerable<PoEntry>>>();
            UI.Locale.RegisterRuntimeCatalog("ugc", locale =>
            {
                var src = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
                sources[locale] = src;
                return src.Awaitable;
            });

            UI.Locale.Set("en");
            UI.Locale.Set("fr");
            sources["fr"].SetResult(new[] { new PoEntry { Msgid = "item", Msgstr = "Épée" } });
            sources["en"].SetResult(new[] { new PoEntry { Msgid = "item", Msgstr = "Sword" } });

            Assert.AreEqual("Épée", Tr("item"));
            Assert.IsNull(TranslationStore.Instance.Lookup("en", null, "item"), "the stale en result is dropped");
        }

        [Test]
        public void ReloadCurrent_reloads_catalogs()
        {
            var version = "v1";
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done(("item", version)));
            UI.Locale.Set("en");
            Assume.That(Tr("item"), Is.EqualTo("v1"));

            version = "v2";
            UI.Locale.ReloadCurrentAsync().GetAwaiter().GetResult();

            Assert.AreEqual("v2", Tr("item"));
        }

        [Test]
        public void Set_with_sync_catalogs_completes_synchronously()
        {
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done(("item", "Sword")));

            UI.Locale.Set("en");   // fire-and-forget: everything already completed

            Assert.IsTrue(UI.Variants.IsActive("en"));
            Assert.AreEqual("Sword", Tr("item"));
        }

        [Test]
        public void Pending_catalog_delays_variant_flip_until_loaded()
        {
            var src = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => src.Awaitable);

            UI.Locale.Set("en");
            Assert.IsFalse(UI.Variants.IsActive("en"), "the switch waits for the catalogs");

            src.SetResult(new[] { new PoEntry { Msgid = "item", Msgstr = "Sword" } });

            Assert.IsTrue(UI.Variants.IsActive("en"));
            Assert.AreEqual("Sword", Tr("item"));
        }

        // ── lifecycle ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void ResetForTests_clears_catalogs()
        {
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done());

            UI.ResetForTests();

            Assert.IsFalse(UI.Locale.UnregisterRuntimeCatalog("ugc"));
            Assert.DoesNotThrow(() => UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done()));
        }

        [Test]
        public void ClearRuntimeRegistrations_clears_catalogs_and_layers()
        {
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => Done(("item", "Sword")));
            UI.Locale.Set("en");
            Assume.That(Tr("item"), Is.EqualTo("Sword"));

            UI.ClearRuntimeRegistrations();

            Assert.AreEqual("item", Tr("item"));
            Assert.IsFalse(UI.Locale.UnregisterRuntimeCatalog("ugc"));
        }
    }
}
