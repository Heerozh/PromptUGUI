using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// <see cref="LocaleFontCopier.CopyToEmptySlots"/> fills empty font/material
    /// slots in every other locale from a source locale's same-type entry, treating
    /// font and material as independent slots and never overwriting set values.
    /// </summary>
    public class LocaleFontCopierTests
    {
        private readonly List<Object> _created = new();

        private TMP_FontAsset Font()
        {
            var f = ScriptableObject.CreateInstance<TMP_FontAsset>();
            _created.Add(f);
            return f;
        }

        private Material Mat()
        {
            var m = new Material(Shader.Find("UI/Default"));
            _created.Add(m);
            return m;
        }

        private static PromptUGUISettings.LocaleConfig Locale(
            string locale, params PromptUGUISettings.FontEntry[] fonts) =>
            new() { locale = locale, fonts = new List<PromptUGUISettings.FontEntry>(fonts) };

        private static PromptUGUISettings.FontEntry Entry(
            string type, TMP_FontAsset font = null, Material material = null) =>
            new() { type = type, font = font, material = material };

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void CopyToEmptySlots_FillsEmptyFontSlots()
        {
            var enDefault = Font();
            var enTitle = Font();
            var locales = new List<PromptUGUISettings.LocaleConfig>
            {
                Locale("en", Entry("default", enDefault), Entry("title", enTitle)),
                Locale("zh", Entry("default"), Entry("title")),
            };

            LocaleFontCopier.CopyToEmptySlots(locales, "en");

            Assert.AreSame(enDefault, locales[1].fonts[0].font);
            Assert.AreSame(enTitle, locales[1].fonts[1].font);
        }

        [Test]
        public void CopyToEmptySlots_DoesNotOverwriteSetFont()
        {
            var enDefault = Font();
            var zhDefault = Font();
            var locales = new List<PromptUGUISettings.LocaleConfig>
            {
                Locale("en", Entry("default", enDefault)),
                Locale("zh", Entry("default", zhDefault)),
            };

            LocaleFontCopier.CopyToEmptySlots(locales, "en");

            Assert.AreSame(zhDefault, locales[1].fonts[0].font, "set font must not be overwritten");
        }

        [Test]
        public void CopyToEmptySlots_FillsFontAndMaterialIndependently()
        {
            var enFont = Font();
            var enMat = Mat();
            var zhFont = Font();
            var locales = new List<PromptUGUISettings.LocaleConfig>
            {
                Locale("en", Entry("default", enFont, enMat)),
                Locale("zh", Entry("default", zhFont)),  // font set, material empty
            };

            LocaleFontCopier.CopyToEmptySlots(locales, "en");

            Assert.AreSame(zhFont, locales[1].fonts[0].font, "already-set font kept");
            Assert.AreSame(enMat, locales[1].fonts[0].material, "empty material filled");
        }

        [Test]
        public void CopyToEmptySlots_LeavesSourceLocaleUnchanged()
        {
            var enFont = Font();
            var enMat = Mat();
            var locales = new List<PromptUGUISettings.LocaleConfig>
            {
                Locale("en", Entry("default", enFont, enMat)),
                Locale("zh", Entry("default")),
            };

            LocaleFontCopier.CopyToEmptySlots(locales, "en");

            Assert.AreSame(enFont, locales[0].fonts[0].font);
            Assert.AreSame(enMat, locales[0].fonts[0].material);
        }

        [Test]
        public void CopyToEmptySlots_MatchesByTypeNotIndex()
        {
            var enDefault = Font();
            var enTitle = Font();
            var locales = new List<PromptUGUISettings.LocaleConfig>
            {
                Locale("en", Entry("default", enDefault), Entry("title", enTitle)),
                // reversed order in the destination locale
                Locale("zh", Entry("title"), Entry("default")),
            };

            LocaleFontCopier.CopyToEmptySlots(locales, "en");

            Assert.AreSame(enTitle, locales[1].fonts[0].font, "title slot filled from en title");
            Assert.AreSame(enDefault, locales[1].fonts[1].font, "default slot filled from en default");
        }
    }
}
