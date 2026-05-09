using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.I18n;
using TMPro;

namespace PromptUGUI.Tests.E2E
{
    public class I18nHotReloadTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text id='lbl'>开始游戏</Text>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            UI.PoResolver = _ => System.Linq.Enumerable.Empty<PoEntry>();
            TranslationStore.Instance.UnloadAll();
            UI.LoadDocument("S", Xml);
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry { Msgid = "开始游戏", Msgstr = "Start" },
            });
            UI.Locale.Set("en");
        }
        [TearDown]
        public void Teardown()
        {
            UI.ResetForTests();
            TranslationStore.Instance.UnloadAll();
        }

        [Test]
        public void TableMutation_AfterReSolve_UpdatesText()
        {
            var screen = UI.Open("S");
            var go = ((Control)screen.Get<Text>("lbl")).GameObject;
            Assert.AreEqual("Start", go.GetComponent<TMP_Text>().text);

            // Simulate hot-reload: replace the entry, ReSolve manually
            TranslationStore.Instance.UnloadLocale("en");
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry { Msgid = "开始游戏", Msgstr = "Begin" },
            });
            UI.NotifyVariantChangedForReSolve();

            Assert.AreEqual("Begin", go.GetComponent<TMP_Text>().text);
        }
    }
}
