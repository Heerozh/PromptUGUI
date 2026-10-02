using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// A locale switch commits once the new language has arrived (spec 2026-10-02-locale-commit-on-load-design):
    /// until then the old locale stays in effect, then table, Current, both locale variants and Locale.Changed
    /// move in one step.
    /// </summary>
    public class LocaleCommitOnLoadTests
    {
        private DeferredPo _po;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _po = new DeferredPo();
            UI.PoResolver = _po.Load;
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        // A PoResolver whose loads finish when the test says so — the shape of an Addressables download.
        private sealed class DeferredPo
        {
            public readonly List<string> Asked = new();
            private readonly Dictionary<string, AwaitableCompletionSource<IEnumerable<PoEntry>>> _loads = new();

            public Awaitable<IEnumerable<PoEntry>> Load(string locale)
            {
                Asked.Add(locale);
                var load = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
                _loads[locale] = load;
                return load.Awaitable;
            }

            public void Complete(string locale, params (string id, string str)[] entries) =>
                _loads[locale].SetResult(Entries(entries));

            public void Fail(string locale, Exception e) => _loads[locale].SetException(e);
        }

        private static IEnumerable<PoEntry> Entries(params (string id, string str)[] entries) =>
            entries.Select(e => new PoEntry { Msgid = e.id, Msgstr = e.str }).ToList();

        private void OnZhHans()
        {
            UI.Locale.Set("zh-Hans");
            _po.Complete("zh-Hans", ("hi", "你好"));
            Assume.That(UI.Locale.Current, Is.EqualTo("zh-Hans"));
        }

        // ── the pending window ────────────────────────────────────────────────────────────────

        [Test]
        public void Pending_switch_keeps_the_old_locale_in_effect()
        {
            OnZhHans();
            UI.LoadDocument("test", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                                    "<Screen name='S'><Text id='t'>hi</Text></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var tmp = screen.Get("t").GameObject.GetComponent<TMP_Text>();
            Assume.That(tmp.text, Is.EqualTo("你好"));

            UI.Locale.Set("en");
            screen.ReSolve();   // anything that re-renders while the download runs

            Assert.AreEqual("zh-Hans", UI.Locale.Current);
            Assert.AreEqual("en", UI.Locale.Pending);
            Assert.AreEqual("你好", tmp.text, "the old locale stays in effect — never the msgid");
            Assert.IsTrue(UI.Variants.IsActive("zh-Hans"));
            Assert.IsFalse(UI.Variants.IsActive("en"));

            _po.Complete("en", ("hi", "Hello"));

            Assert.AreEqual("en", UI.Locale.Current);
            Assert.IsNull(UI.Locale.Pending);
            Assert.AreEqual("Hello", tmp.text);
        }

        [Test]
        public void Commit_flips_both_variants_in_one_Changed_then_fires_Locale_Changed()
        {
            OnZhHans();
            var variants = new List<string>();
            using var sub = UI.VariantStore.Changed.Subscribe(_ =>
                variants.Add($"zh-Hans={UI.Variants.IsActive("zh-Hans")}, en={UI.Variants.IsActive("en")}"));
            var changed = 0;
            UI.Locale.Changed += () => changed++;

            UI.Locale.Set("en");
            Assert.IsEmpty(variants, "nothing moves while the new locale is loading");
            Assert.AreEqual(0, changed);

            _po.Complete("en", ("hi", "Hello"));

            CollectionAssert.AreEqual(new[] { "zh-Hans=False, en=True" }, variants);
            Assert.AreEqual(1, changed);
            Assert.IsNull(TranslationStore.Instance.Lookup("zh-Hans", null, "hi"), "the old table is unloaded");
        }

        [Test]
        public void A_synchronous_switch_re_solves_once()
        {
            UI.PoResolver = locale => AwaitableHelpers.Completed(
                Entries(("hi", locale == "en" ? "Hello" : "你好")));
            UI.Locale.Set("zh-Hans");
            var changes = 0;
            using var sub = UI.VariantStore.Changed.Subscribe(_ => changes++);

            UI.Locale.Set("en");

            Assert.AreEqual("en", UI.Locale.Current, "a synchronous loader still switches before Set returns");
            Assert.AreEqual("Hello", UI.Tr("hi"));
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Commit_keeps_entries_loaded_before_Set()
        {
            OnZhHans();
            TranslationStore.Instance.Load("en", Entries(("bye", "Bye")));

            UI.Locale.Set("en");
            _po.Complete("en", ("hi", "Hello"));

            Assert.AreEqual("Bye", UI.Tr("bye"), "the commit merges, it does not replace");
            Assert.AreEqual("Hello", UI.Tr("hi"));
        }

        // ── superseding / cancelling ──────────────────────────────────────────────────────────

        [Test]
        public void A_newer_request_supersedes_the_pending_one()
        {
            OnZhHans();
            UI.Locale.Set("en");
            UI.Locale.Set("fr");
            Assert.AreEqual("fr", UI.Locale.Pending);

            _po.Complete("en", ("hi", "Hello"));
            Assert.AreEqual("zh-Hans", UI.Locale.Current, "the superseded load commits nothing");
            Assert.IsNull(TranslationStore.Instance.Lookup("en", null, "hi"));

            _po.Complete("fr", ("hi", "Bonjour"));
            Assert.AreEqual("fr", UI.Locale.Current);
            Assert.AreEqual("Bonjour", UI.Tr("hi"));
            Assert.IsFalse(UI.Variants.IsActive("en"));
        }

        [Test]
        public void Asking_for_Current_cancels_the_pending_switch()
        {
            OnZhHans();
            var changed = 0;
            UI.Locale.Changed += () => changed++;
            UI.Locale.Set("en");

            UI.Locale.Set("zh-Hans");
            Assert.IsNull(UI.Locale.Pending);

            _po.Complete("en", ("hi", "Hello"));
            Assert.AreEqual("zh-Hans", UI.Locale.Current);
            Assert.AreEqual("你好", UI.Tr("hi"));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Set_null_while_pending_commits_null_at_once()
        {
            OnZhHans();
            UI.Locale.Set("en");

            UI.Locale.Set(null);
            Assert.IsNull(UI.Locale.Current);
            Assert.IsNull(UI.Locale.Pending);
            Assert.IsFalse(UI.Variants.IsActive("zh-Hans"));

            _po.Complete("en", ("hi", "Hello"));
            Assert.IsNull(UI.Locale.Current, "the cancelled load commits nothing");
            Assert.IsFalse(UI.Variants.IsActive("en"));
        }

        // ── SetAsync settles with its request ─────────────────────────────────────────────────

        [Test]
        public void A_superseded_SetAsync_completes_at_once()
        {
            OnZhHans();
            var en = UI.Locale.SetAsync("en");
            Assume.That(en.GetAwaiter().IsCompleted, Is.False);

            UI.Locale.Set("fr");

            Assert.IsTrue(en.GetAwaiter().IsCompleted, "no waiting for a download nobody wants");
            Assert.DoesNotThrow(() => en.GetAwaiter().GetResult());
            Assert.AreEqual("zh-Hans", UI.Locale.Current);
        }

        [Test]
        public void SetAsync_for_the_locale_already_loading_settles_with_it()
        {
            OnZhHans();
            var first = UI.Locale.SetAsync("en");
            var second = UI.Locale.SetAsync("en");
            Assert.IsFalse(second.GetAwaiter().IsCompleted, "the second call waits for the same load");

            _po.Complete("en", ("hi", "Hello"));

            Assert.IsTrue(first.GetAwaiter().IsCompleted);
            Assert.IsTrue(second.GetAwaiter().IsCompleted);
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.AreEqual(1, _po.Asked.Count(l => l == "en"), "one load serves both");
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void ResetForTests_drops_the_pending_switch()
        {
            OnZhHans();
            var en = UI.Locale.SetAsync("en");

            UI.ResetForTests();

            Assert.IsNull(UI.Locale.Pending);
            Assert.IsTrue(en.GetAwaiter().IsCompleted, "a waiting SetAsync is settled, not left hanging");
            _po.Complete("en", ("hi", "Hello"));
            Assert.IsNull(UI.Locale.Current, "the load that lands afterwards is dropped");
        }

        // ── failure ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void A_failed_load_keeps_the_old_locale()
        {
            OnZhHans();
            LogAssert.Expect(LogType.Error, new Regex("locale load failed for 'en'.*offline"));

            UI.Locale.Set("en");
            _po.Fail("en", new System.IO.IOException("offline"));

            Assert.AreEqual("zh-Hans", UI.Locale.Current);
            Assert.IsNull(UI.Locale.Pending);
            Assert.AreEqual("你好", UI.Tr("hi"));
            Assert.IsTrue(UI.Variants.IsActive("zh-Hans"));
        }

        [Test]
        public void A_failed_SetAsync_throws_and_keeps_the_old_locale()
        {
            OnZhHans();
            var en = UI.Locale.SetAsync("en");

            _po.Fail("en", new System.IO.IOException("offline"));

            Assert.Throws<System.IO.IOException>(() => en.GetAwaiter().GetResult());
            Assert.AreEqual("zh-Hans", UI.Locale.Current);
            Assert.AreEqual("你好", UI.Tr("hi"));
        }

        // ── runtime catalogs / InitializeIfNeeded ─────────────────────────────────────────────

        [Test]
        public void A_catalog_registered_mid_switch_loads_for_the_new_locale_after_the_commit()
        {
            // "slow" holds the switch in its catalog phase, after the gather has taken its catalog snapshot.
            var slow = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
            UI.Locale.RegisterRuntimeCatalog("slow", locale =>
                locale == "en" ? slow.Awaitable : AwaitableHelpers.Completed(Entries()));
            OnZhHans();
            UI.Locale.Set("en");
            _po.Complete("en", ("hi", "Hello"));
            UI.Locale.RegisterRuntimeCatalog("ugc", locale =>
                AwaitableHelpers.Completed(Entries(("item", locale == "en" ? "Sword" : "剑"))));
            Assume.That(UI.Tr("item"), Is.EqualTo("剑"), "it loads for the locale in effect right away");

            slow.SetResult(Entries());

            Assert.AreEqual("en", UI.Locale.Current);
            Assert.AreEqual("Sword", UI.Tr("item"));
        }

        [Test]
        public void InitializeIfNeeded_leaves_a_pending_switch_alone()
        {
            UI.Locale.Set("zh-Hans");   // the player's saved choice, still loading

            UI.Locale.InitializeIfNeededCore(SystemLanguage.English, new[] { "en", "zh-Hans" });

            Assert.AreEqual("zh-Hans", UI.Locale.Pending, "the system default does not supersede it");
            CollectionAssert.AreEqual(new[] { "zh-Hans" }, _po.Asked);
        }

        // ── ReloadCurrent ─────────────────────────────────────────────────────────────────────

        [Test]
        public void An_async_reload_keeps_the_current_entries_until_the_new_ones_arrive()
        {
            OnZhHans();
            var reload = UI.Locale.ReloadCurrentAsync();
            Assert.AreEqual("你好", UI.Tr("hi"), "no window: the old entries stay in effect");

            _po.Complete("zh-Hans", ("hi", "您好"));

            Assert.IsTrue(reload.GetAwaiter().IsCompleted);
            reload.GetAwaiter().GetResult();
            Assert.AreEqual("您好", UI.Tr("hi"));
        }

        [Test]
        public void A_reload_replaces_the_table()
        {
            UI.Locale.Set("zh-Hans");
            _po.Complete("zh-Hans", ("hi", "你好"), ("bye", "再见"));
            var reload = UI.Locale.ReloadCurrentAsync();

            _po.Complete("zh-Hans", ("hi", "您好"));

            reload.GetAwaiter().GetResult();
            Assert.AreEqual("bye", UI.Tr("bye"), "an entry the reload no longer has is gone");
        }

        [Test]
        public void A_failed_reload_keeps_the_current_entries()
        {
            OnZhHans();
            var reload = UI.Locale.ReloadCurrentAsync();

            _po.Fail("zh-Hans", new System.IO.IOException("offline"));

            Assert.Throws<System.IO.IOException>(() => reload.GetAwaiter().GetResult());
            Assert.AreEqual("你好", UI.Tr("hi"));
        }

        [Test]
        public void A_reload_that_lands_after_a_switch_is_dropped()
        {
            OnZhHans();
            var reload = UI.Locale.ReloadCurrentAsync();
            UI.Locale.Set("en");
            _po.Complete("en", ("hi", "Hello"));

            _po.Complete("zh-Hans", ("hi", "您好"));

            reload.GetAwaiter().GetResult();
            Assert.AreEqual("en", UI.Locale.Current);
            Assert.IsNull(TranslationStore.Instance.Lookup("zh-Hans", null, "hi"), "the stale reload writes nothing");
        }

        [Test]
        public void A_reload_keeps_a_catalog_registered_while_it_loads()
        {
            // "slow" holds the reload in its catalog phase; "ugc" registers meanwhile and loads for Current at once.
            var slow = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
            var slowCalls = 0;
            UI.Locale.RegisterRuntimeCatalog("slow", _ =>
                ++slowCalls == 1 ? AwaitableHelpers.Completed(Entries()) : slow.Awaitable);
            OnZhHans();
            var reload = UI.Locale.ReloadCurrentAsync();
            _po.Complete("zh-Hans", ("hi", "您好"));
            UI.Locale.RegisterRuntimeCatalog("ugc", _ => AwaitableHelpers.Completed(Entries(("item", "剑"))));
            Assume.That(UI.Tr("item"), Is.EqualTo("剑"));

            slow.SetResult(Entries());

            reload.GetAwaiter().GetResult();
            Assert.AreEqual("剑", UI.Tr("item"), "the reload replaces what it gathered, nothing else");
        }
    }
}
