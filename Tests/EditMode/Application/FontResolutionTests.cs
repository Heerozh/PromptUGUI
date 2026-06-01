using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// Pure resolution logic for <see cref="PromptUGUISettings.ResolveFontEntry"/>:
    /// a font type carries an optional material preset, and may inherit the
    /// locale 'default' type's font when its own font slot is left empty.
    /// </summary>
    public class FontResolutionTests
    {
        private readonly List<Object> _created = new();

        private TMP_FontAsset NewFont()
        {
            var f = ScriptableObject.CreateInstance<TMP_FontAsset>();
            _created.Add(f);
            return f;
        }

        private Material NewMaterial()
        {
            var m = new Material(Shader.Find("UI/Default"));
            _created.Add(m);
            return m;
        }

        private PromptUGUISettings NewSettings(params PromptUGUISettings.FontEntry[] fonts)
        {
            var s = ScriptableObject.CreateInstance<PromptUGUISettings>();
            s.locales = new List<PromptUGUISettings.LocaleConfig>
            {
                new() { locale = "en", fonts = new List<PromptUGUISettings.FontEntry>(fonts) },
            };
            _created.Add(s);
            return s;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void ResolveFontEntry_ReturnsConfiguredFontAndMaterial()
        {
            var font = NewFont();
            var mat = NewMaterial();
            var settings = NewSettings(
                new PromptUGUISettings.FontEntry { type = "default", font = font },
                new PromptUGUISettings.FontEntry { type = "outline", font = font, material = mat });

            var res = settings.ResolveFontEntry("en", "outline");

            Assert.AreSame(font, res.Font);
            Assert.AreSame(mat, res.Material);
        }

        [Test]
        public void ResolveFontEntry_EmptyFontSlot_InheritsDefaultFont()
        {
            var defaultFont = NewFont();
            var outlineMat = NewMaterial();
            var settings = NewSettings(
                new PromptUGUISettings.FontEntry { type = "default", font = defaultFont },
                new PromptUGUISettings.FontEntry { type = "outline", font = null, material = outlineMat });

            var res = settings.ResolveFontEntry("en", "outline");

            Assert.AreSame(defaultFont, res.Font, "empty font slot should inherit default type's font");
            Assert.AreSame(outlineMat, res.Material);
        }

        [Test]
        public void ResolveFontEntry_UnknownType_FallsBackToDefaultEntry()
        {
            var defaultFont = NewFont();
            var defaultMat = NewMaterial();
            var settings = NewSettings(
                new PromptUGUISettings.FontEntry { type = "default", font = defaultFont, material = defaultMat });

            var res = settings.ResolveFontEntry("en", "no-such-type");

            Assert.AreSame(defaultFont, res.Font);
            Assert.AreSame(defaultMat, res.Material);
        }

        [Test]
        public void ResolveFontEntry_NoMaterialConfigured_ReturnsNullMaterial()
        {
            var font = NewFont();
            var settings = NewSettings(
                new PromptUGUISettings.FontEntry { type = "default", font = font });

            var res = settings.ResolveFontEntry("en", "default");

            Assert.AreSame(font, res.Font);
            Assert.IsNull(res.Material);
        }
    }
}
