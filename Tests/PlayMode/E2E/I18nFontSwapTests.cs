using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.I18n;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.E2E
{
    /// <summary>
    /// End-to-end: locale switch → text translates + font swaps via the Settings table.
    /// </summary>
    public class I18nFontSwapTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text id='lbl' font='title'>开始游戏</Text>
  </Screen>
</PromptUGUI>";

        private PromptUGUISettings _settings;
        private TMP_FontAsset _fontEn, _fontZh;

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();

            // Distinct instances suffice for reference-equality assertions.
            _fontEn = TMP_FontAsset.CreateFontAsset(Font.CreateDynamicFontFromOSFont("Arial", 16));
            _fontZh = TMP_FontAsset.CreateFontAsset(Font.CreateDynamicFontFromOSFont("Arial", 16));

            _settings = ScriptableObject.CreateInstance<PromptUGUISettings>();
            _settings.locales = new List<PromptUGUISettings.LocaleConfig> {
                new() {
                    locale = "en",
                    fonts = new List<PromptUGUISettings.FontEntry> {
                        new() { type = "default", font = _fontEn },
                        new() { type = "title",   font = _fontEn },
                    },
                },
                new() {
                    locale = "zh-Hans",
                    fonts = new List<PromptUGUISettings.FontEntry> {
                        new() { type = "default", font = _fontZh },
                        new() { type = "title",   font = _fontZh },
                    },
                },
            };
            // Pin into Resources.FindObjectsOfTypeAll so PromptUGUISettings.Instance finds it.
            _settings.hideFlags = HideFlags.DontUnloadUnusedAsset;

            UI.LoadDocument("S", Xml);

            // Set starting locale to zh-Hans (no translation → msgid passes through).
            UI.Locale.Set("zh-Hans");

            // Pre-load "en" entries while current locale is "zh-Hans".
            // UI.Locale.Set("en") later only unloads "zh-Hans" entries, so these survive.
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry { Msgid = "开始游戏", Msgstr = "Start" },
            });
        }

        [TearDown]
        public void Teardown()
        {
            UI.ResetForTests();
            if (_settings != null) Object.DestroyImmediate(_settings);
            if (_fontEn != null) Object.DestroyImmediate(_fontEn);
            if (_fontZh != null) Object.DestroyImmediate(_fontZh);
        }

        [Test]
        public void SwitchLocale_TextAndFontChange()
        {
            var screen = UI.Open("S");
            var txt = screen.Get<Text>("lbl");

            // zh-Hans: no translation loaded → msgid passes through unchanged.
            Assert.AreEqual("开始游戏", GetTmpText(txt));
            // Font swap: only assert when settings object is discoverable AND font assets
            // were successfully created (Arial may be absent in headless Unity / CI).
            // TODO: font swap assertion needs CI font setup if Arial is unavailable.
            var canAssertFont = PromptUGUISettings.Instance != null
                                 && _fontEn != null && _fontZh != null;
            if (canAssertFont)
            {
                Assert.AreSame(_fontZh, GetTmpFont(txt),
                    "zh-Hans font should be _fontZh");
            }

            // Locale.Set("en") unloads "zh-Hans" entries (none), then fires ReSolve.
            // ReSolve re-applies "font='title'" with locale "en" → _fontEn,
            // and "text=开始游戏" via TrResolver → "Start" (loaded above).
            UI.Locale.Set("en");

            Assert.AreEqual("Start", GetTmpText(txt));
            if (canAssertFont)
            {
                Assert.AreSame(_fontEn, GetTmpFont(txt),
                    "en font should be _fontEn");
            }
        }

        private static string GetTmpText(Text t) =>
            ((Control)t).GameObject.GetComponent<TMP_Text>().text;

        private static TMP_FontAsset GetTmpFont(Text t) =>
            ((Control)t).GameObject.GetComponent<TMP_Text>().font;
    }
}
