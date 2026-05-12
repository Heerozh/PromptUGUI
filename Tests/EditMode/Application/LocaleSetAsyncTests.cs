using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.Application
{
    public class LocaleSetAsyncTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void Set_with_completed_PoResolver_loads_translations_synchronously()
        {
            // Sync-completed Awaitable: integral regression contract that the new
            // async PoResolver shape keeps sync-completion semantics for callers
            // who don't actually need to defer (Resources path, in-memory tests).
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.Set("en");
            Assert.AreEqual("en", UI.Locale.Current);
            Assert.AreEqual("Hello", UI.Tr("hi"),
                "Sync-completed PoResolver path must populate TranslationStore " +
                "before Set returns (preserves pre-async-shape behavior).");
        }

        [Test]
        public void SetAsync_with_completed_PoResolver_loads_translations()
        {
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Bonjour" },
            });
            UI.Locale.SetAsync("fr").GetAwaiter().GetResult();
            Assert.AreEqual("fr", UI.Locale.Current);
            Assert.AreEqual("Bonjour", UI.Tr("hi"));
        }

        [Test]
        public void SetAsync_propagates_resolver_exception()
        {
            UI.PoResolver = _ =>
                AwaitableHelpers.Faulted<IEnumerable<PoEntry>>(
                    new System.IO.IOException("boom"));
            var ex = Assert.Throws<System.IO.IOException>(
                () => UI.Locale.SetAsync("en").GetAwaiter().GetResult(),
                "SetAsync should surface resolver exceptions to the awaiting caller");
            StringAssert.Contains("boom", ex.Message);
        }

        [Test]
        public void Set_fire_and_forget_logs_error_on_resolver_throw()
        {
            UI.PoResolver = _ =>
                AwaitableHelpers.Faulted<IEnumerable<PoEntry>>(
                    new System.IO.IOException("boom-sync"));
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "locale load failed for 'en'.*boom-sync"));
            UI.Locale.Set("en");
            // Locale.Current still advances even when load fails — caller can retry.
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void Set_rapid_consecutive_with_pending_resolver_discards_stale_load()
        {
            // Race scenario: Set("zh-Hans") starts a deferred load; before it
            // completes, Set("en") supersedes it. When the zh-Hans load finally
            // resolves, the guard inside LoadPoFilesAsync must drop the result.
            var srcZh = new UnityEngine.AwaitableCompletionSource<IEnumerable<PoEntry>>();
            var srcEn = new UnityEngine.AwaitableCompletionSource<IEnumerable<PoEntry>>();
            UI.PoResolver = locale =>
                locale == "zh-Hans" ? srcZh.Awaitable : srcEn.Awaitable;

            UI.Locale.Set("zh-Hans");
            UI.Locale.Set("en");
            Assert.AreEqual("en", UI.Locale.Current);

            // Resume en first, then zh — the guard must drop zh entries.
            srcEn.SetResult(new[] { new PoEntry { Msgid = "hi", Msgstr = "Hello" } });
            srcZh.SetResult(new[] { new PoEntry { Msgid = "hi", Msgstr = "你好" } });

            Assert.AreEqual("Hello", UI.Tr("hi"),
                "Race guard must keep en translations; zh-Hans load is stale.");
            Assert.IsFalse(UI.Variants.IsActive("zh-Hans"),
                "Stale zh-Hans load must not flip its variant back on.");
        }

        [Test]
        public void ReloadCurrentAsync_with_completed_PoResolver_reloads_translations()
        {
            // Initial load: "hi" → "Hello"
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.Set("en");
            Assert.AreEqual("Hello", UI.Tr("hi"));

            // Swap the resolver to return a new translation, then ReloadCurrent.
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Howdy" },
            });
            UI.Locale.ReloadCurrentAsync().GetAwaiter().GetResult();
            Assert.AreEqual("Howdy", UI.Tr("hi"));
        }

        [Test]
        public void ReloadCurrentAsync_returns_immediately_when_Current_is_null()
        {
            // Current is null after ResetForTests; ReloadCurrentAsync should be a no-op
            // (no resolver invocation, no exception).
            var invocations = 0;
            UI.PoResolver = _ =>
            {
                invocations++;
                return AwaitableHelpers.Completed<IEnumerable<PoEntry>>(System.Array.Empty<PoEntry>());
            };
            UI.Locale.ReloadCurrentAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, invocations);
        }
    }
}
