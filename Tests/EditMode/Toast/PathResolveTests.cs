using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class PathResolveTests
    {
        private const string ScreenXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='Hud'>
    <Frame id='panel' anchor='center' size='100x100'>
      <Image id='coin' anchor='center' size='20x20'/>
    </Frame>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "Hud" ? ScreenXml : null);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Resolves_screen_and_idpath()
        {
            UI.LoadDocumentAsync("Hud").GetAwaiter().GetResult();
            UI.Open("Hud");
            Assert.IsTrue(UI.TryResolvePath("Hud/panel/coin", out var rt));
            Assert.AreEqual("coin", rt.gameObject.name);   // ScreenInstantiator names GO by id
        }

        [Test]
        public void Empty_idpath_returns_screen_root()
        {
            UI.LoadDocumentAsync("Hud").GetAwaiter().GetResult();
            UI.Open("Hud");
            Assert.IsTrue(UI.TryResolvePath("Hud", out var rt));
            Assert.AreEqual(UI.Get("Hud").RootGameObject, rt.gameObject);
        }

        [Test]
        public void Missing_screen_returns_false()
        {
            Assert.IsFalse(UI.TryResolvePath("Nope/x", out var rt));
            Assert.IsNull(rt);
        }

        [Test]
        public void Missing_id_returns_false()
        {
            UI.LoadDocumentAsync("Hud").GetAwaiter().GetResult();
            UI.Open("Hud");
            Assert.IsFalse(UI.TryResolvePath("Hud/nope", out _));
        }
    }
}
