using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class LocaleSetToSystemDefaultTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        // ---- Pure resolver behavior ----

        [Test]
        public void Core_UsesSystem_WhenInConfigured()
        {
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.ChineseSimplified, new[] { "en", "zh-Hans" }, fallback: null);
            Assert.AreEqual("zh-Hans", UI.Locale.Current);
        }

        [Test]
        public void Core_FallsBackToParam_WhenSystemMappedButNotConfigured()
        {
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.Japanese, new[] { "en", "zh-Hans" }, fallback: "en");
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void Core_FallsBackToParam_WhenSystemUnknown()
        {
            // Estonian is not in LocaleHelpers.MapSystemLanguage → returns null.
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.Estonian, new[] { "en" }, fallback: "en");
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void Core_PreservesOldBehavior_WhenSystemMappedButNotConfigured_NoFallback()
        {
            // Old behavior: Set(mapped) even if mapped is outside Configured —
            // "宁愿不翻译也不阻塞" escape hatch. Test runs without PoResolver so the
            // async PO load is a no-op (Resources path returns empty), no LogError.
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.Japanese, new[] { "en", "zh-Hans" }, fallback: null);
            Assert.AreEqual("ja", UI.Locale.Current);
        }

        [Test]
        public void Core_PreservesOldBehavior_WhenSystemUnknown_NoFallback()
        {
            // Old behavior: Set(null) → Current stays null, all msgids show as msgid.
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.Estonian, new[] { "en" }, fallback: null);
            Assert.IsNull(UI.Locale.Current);
        }

        [Test]
        public void Core_FallbackAcceptedEvenIfOutsideConfigured()
        {
            // Mirrors the "trust the caller" stance of Set(): if caller passes a
            // fallback that's not in Configured, we still honor it.
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.Estonian, new[] { "en" }, fallback: "ar");
            Assert.AreEqual("ar", UI.Locale.Current);
        }

        [Test]
        public void Core_ConfiguredNull_TreatedAsEmpty()
        {
            UI.Locale.SetToSystemDefaultCore(
                SystemLanguage.Japanese, configured: null, fallback: "en");
            Assert.AreEqual("en", UI.Locale.Current);
        }

        // ---- Async path wiring ----

        [Test]
        public void AsyncCore_FallsBackToParam_AndAwaitsPoLoad()
        {
            // Confirms the async core: resolves to fallback, awaits SetAsync,
            // which awaits PoResolver. The PO entries are visible after the await.
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.SetToSystemDefaultAsyncCore(
                    SystemLanguage.Japanese, new[] { "en" }, fallback: "en")
                .GetAwaiter().GetResult();
            Assert.AreEqual("en", UI.Locale.Current);
            Assert.AreEqual("Hello", UI.Tr("hi"));
        }

        [Test]
        public void AsyncCore_UsesSystem_WhenInConfigured()
        {
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(
                System.Array.Empty<PoEntry>());
            UI.Locale.SetToSystemDefaultAsyncCore(
                    SystemLanguage.ChineseSimplified, new[] { "en", "zh-Hans" }, fallback: "en")
                .GetAwaiter().GetResult();
            Assert.AreEqual("zh-Hans", UI.Locale.Current);
        }
    }
}
