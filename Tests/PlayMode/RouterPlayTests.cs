using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    public class RouterPlayTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";
        private static string ModalXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/></Screen></PromptUGUI>";
        private const string IBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='ibox'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='dialog' anchor='center' size='400x200'>
    <VStack anchor='stretch' margin='16' spacing='8'>
      <Text id='title'/><InputField id='field'/>
      <Btn id='ok'>OK</Btn><Btn id='cancel'>Cancel</Btn>
    </VStack>
  </Frame>
</Screen></PromptUGUI>";

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Setup(Dictionary<string, string> files)
        {
            UI.ResetForTests();
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
        }

        [UnityTest]
        public IEnumerator Modal_SortsAbovePage()
        {
            Setup(new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["settings"] = ModalXml("settings"),
            });
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");

            _ = UI.Router.Open("settings");
            for (int i = 0; i < 10 && UI.Get("settings") == null; i++) yield return null;

            var home = UI.Get("home").RootGameObject.GetComponent<Canvas>();
            var settings = UI.Get("settings").RootGameObject.GetComponent<Canvas>();
            Assert.Greater(settings.sortingOrder, home.sortingOrder);
            Assert.GreaterOrEqual(settings.sortingOrder, UI.Modal.SortingOrderBase);
        }

        [UnityTest]
        public IEnumerator Prompt_RenameConfirm_AppliesAndAutoPops()
        {
            Setup(new Dictionary<string, string> { ["home"] = PageXml("home"), ["ibox"] = IBoxXml });
            InputBox.XmlSrc = "ibox";
            UI.Router.Map("home", "home");
            string applied = null;
            UI.Router.MapPrompt("rename", parent: "home", run: async (q, ct) =>
            {
                var name = await InputBox.Open("改名", initial: "old", ct: ct);
                if (name != null) applied = name;
            });

            _ = UI.Router.Navigate("appid://rename");
            for (int i = 0; i < 10 && UI.Modal.TopScreen == null; i++) yield return null;
            var ibox = UI.Modal.TopScreen;
            Assert.IsNotNull(ibox);

            ibox.Get<PromptUGUI.Controls.InputField>("field").TextValue = "newName";
            ibox.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();   // = close(field.TextValue)

            for (int i = 0; i < 10 && applied == null; i++) yield return null;

            Assert.AreEqual("newName", applied);
            CollectionAssert.AreEqual(new[] { "home" }, new List<string>(UI.Router.Chain));  // 自动出栈
        }

        [UnityTest]
        public IEnumerator Teardown_DuringAsyncPageLoad_NoCorruptionNoLeak()
        {
            var gate = new AwaitableCompletionSource<string>();
            UI.ResetForTests();
            UI.SourceResolver = src =>
                src == "home" ? gate.Awaitable : AwaitableHelpers.Completed<string>(null);
            UI.Router.Map("home", "home");

            _ = UI.Router.Open("home");           // suspends at LoadDocumentAsync("home")
            yield return null;
            Assert.IsEmpty(new List<string>(UI.Router.Chain));   // not added yet (still loading)

            UI.ResetForTests();                    // teardown mid-load → AbandonPump bumps epoch, clears chain

            gate.SetResult(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='home'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>");
            for (int i = 0; i < 5; i++) yield return null;   // let the orphaned continuation run

            // WITH the fix: reconcile bails → chain stays empty, no leaked screen.
            Assert.IsEmpty(new List<string>(UI.Router.Chain));
            Assert.IsNull(UI.Get("home"));
        }
    }
}
