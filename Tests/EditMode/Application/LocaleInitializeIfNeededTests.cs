using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Application
{
    public class LocaleInitializeIfNeededTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void NoOp_WhenAlreadySet()
        {
            UI.Locale.Set("en");
            UI.Locale.InitializeIfNeededCore(SystemLanguage.German, new[] { "en", "zh-Hans" });
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void NoOp_WhenConfiguredEmpty()
        {
            UI.Locale.InitializeIfNeededCore(SystemLanguage.English, System.Array.Empty<string>());
            Assert.IsNull(UI.Locale.Current);
        }

        [Test]
        public void UsesSystemLocale_WhenInConfigured()
        {
            UI.Locale.InitializeIfNeededCore(
                SystemLanguage.ChineseSimplified, new[] { "en", "zh-Hans" });
            Assert.AreEqual("zh-Hans", UI.Locale.Current);
        }

        [Test]
        public void Warns_AndFallsBack_WhenSystemMappedButNotConfigured()
        {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"\[PromptUGUI\] 丢失 'ja', falling back to 'en'"));
            UI.Locale.InitializeIfNeededCore(
                SystemLanguage.Japanese, new[] { "en", "zh-Hans" });
            Assert.AreEqual("en", UI.Locale.Current);
        }

        [Test]
        public void Warns_AndFallsBack_WhenSystemUnknown()
        {
            // Estonian is not in LocaleHelpers.MapSystemLanguage → returns null →
            // displayName falls back to enum's ToString() ("Estonian").
            LogAssert.Expect(LogType.Warning, new Regex(
                @"\[PromptUGUI\] 丢失 'Estonian', falling back to 'en'"));
            UI.Locale.InitializeIfNeededCore(
                SystemLanguage.Estonian, new[] { "en" });
            Assert.AreEqual("en", UI.Locale.Current);
        }
    }
}
