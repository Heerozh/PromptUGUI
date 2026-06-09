using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    public class RouterModalTabTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        private static string ModalXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='panel' anchor='center' size='300x200'/>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["settings"] = ModalXml("settings"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Modal_Activates_OnModalSortingBand()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "settings" }, UI.Router.Chain.ToList());
            var canvas = UI.Get("settings").RootGameObject.GetComponent<Canvas>();
            // Note: overrideSorting does not read back as true on a root ScreenSpaceOverlay
            // canvas (only meaningful for nested canvases — same as LoadingOverlayTests).
            // Verify sortingOrder is in the modal sorting band instead.
            Assert.GreaterOrEqual(canvas.sortingOrder, UI.Modal.SortingOrderBase);
        }

        [Test]
        public void Modal_Esc_GoesBackToParent()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            var esc = UI.Get("settings").RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(esc);
            esc.FireForTests();
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
            Assert.IsNull(UI.Get("settings"));
        }

        [Test]
        public void Modal_Deactivate_ClosesScreen()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.IsNull(UI.Get("settings"));
        }
    }
}
