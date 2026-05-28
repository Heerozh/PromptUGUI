using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class ThemeSwitchTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void RegisterTwoThemes()
        {
            var light = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString("#ff8800", out var lp); light["primary"] = lp;
            ThemeStore.Instance.Register("light", null, light, "test");

            var dark = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString("#cc6600", out var dp); dark["primary"] = dp;
            ThemeStore.Instance.Register("dark", null, dark, "test");
            ThemeStore.Instance.ResolveBases();
        }

        [Test]
        public void Switch_Theme_ReSolves_Open_Screen_Colors()
        {
            RegisterTwoThemes();
            UI.Theme.Set("light");
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Image id='x' color='primary'/></Screen></PromptUGUI>");
            var s = UI.Open("S");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);

            UI.Theme.Set("dark");
            Assert.AreEqual(new Color32(0xcc, 0x66, 0, 0xff), (Color32)img.color);

            UI.Theme.Set("light");
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);
        }

        [Test]
        public void Closed_Screen_Does_Not_ReSolve_On_Theme_Change()
        {
            RegisterTwoThemes();
            UI.Theme.Set("light");
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Image id='x' color='primary'/></Screen></PromptUGUI>");
            var s = UI.Open("S");
            s.Close();
            // Should not throw / leak — closed Screen unsubscribed from Theme.Changed.
            Assert.DoesNotThrow(() => UI.Theme.Set("dark"));
        }
    }
}
