using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class AnimationCharColorTokenTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Seed(string token, string hex)
        {
            var d = new Dictionary<string, ColorSpec>();
            ColorUtility.TryParseHtmlString(hex, out var c);
            d[token] = ColorSpec.Solid(c);
            ThemeStore.Instance.Register("t", null, d, "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("t");
        }

        [Test]
        public void Token_To_Token_Resolves()
        {
            Seed("primary", "#ff8800");
            var spec = new AnimationSpec();
            spec.SetCharColor("primary:primary");
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)spec.CharColorFrom);
        }

        [Test]
        public void Token_To_Literal_Mixes()
        {
            Seed("primary", "#ff8800");
            var spec = new AnimationSpec();
            spec.SetCharColor("primary:#00ff00");
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)spec.CharColorFrom);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)spec.CharColorTo);
        }

        [Test]
        public void Bad_Token_Throws()
        {
            Seed("primary", "#ff8800");
            var spec = new AnimationSpec();
            Assert.Throws<System.Exception>(() => spec.SetCharColor("primaru:#000"));
        }

        [Test]
        public void Wrong_Shape_Throws()
        {
            var spec = new AnimationSpec();
            Assert.Throws<System.Exception>(() => spec.SetCharColor("#ff0000"));
        }
    }
}
