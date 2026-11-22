using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// Screen and Modal open / close cues (spec 2026-11-23-ui-sfx-cues §7.10): the opener decides the role; a normal close
    /// is one that goes through <c>Screen.Close()</c>; rebuilds, teardown and the library's own overlays raise nothing.
    /// </summary>
    public class SfxScreenTests
    {
        private Dictionary<string, string> _files;
        private List<SfxCue> _cues;
        private string _messageBoxSrc;
        private string _loadingSrc;

        private const string Box = Head + @"
  <Screen name='test/Box1'>
    <Image id='backdrop' anchor='stretch' color='#0000007F' raycastTarget='true'/>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/><Text id='text' fontSize='14'/>
        <Btn id='ok'>OK</Btn><Btn id='cancel'>Cancel</Btn><Btn id='yes'>Yes</Btn><Btn id='no'>No</Btn><Btn id='close'>Close</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        private const string LoadingXml = Head + @"
  <Screen name='test/Loading1'><Frame id='dialog' anchor='center' size='320x160'><Text id='text' anchor='stretch'/></Frame></Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string> { ["test/Box1"] = Box, ["test/Loading1"] = LoadingXml };
            UI.SourceResolver = src => AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            // Process-wide skins: restored in TearDown, or every later test that opens a dialog loads this one.
            _messageBoxSrc = MessageBox.XmlSrc;
            _loadingSrc = Loading.XmlSrc;
            MessageBox.XmlSrc = "test/Box1";
            Loading.XmlSrc = "test/Loading1";
            _cues = Record(screens: true);
        }

        [TearDown]
        public void TearDown()
        {
            UI.Variants.Set("mobile", false);
            MessageBox.XmlSrc = _messageBoxSrc;
            Loading.XmlSrc = _loadingSrc;
            UI.ResetForTests();
        }

        private void Load(string src, string screens)
        {
            _files[src] = Head + screens + "</PromptUGUI>";
            UI.LoadDocumentAsync(src).GetAwaiter().GetResult();
        }

        [Test]
        public void UI_Open_and_Close_cue_screen_open_and_close()
        {
            Load("main", "<Screen name='S'><Frame/></Screen>");

            var s = UI.Open("S");
            UI.Close("S");

            Assert.AreEqual("[screen.open→screen.open, screen.close→screen.close]", Describe(_cues));
            Assert.IsNull(_cues[0].Source);
            Assert.AreSame(s, _cues[0].Screen);
        }

        [Test]
        public void Opening_a_screen_that_is_already_open_cues_nothing()
        {
            Load("main", "<Screen name='S'><Frame/></Screen>");
            UI.Open("S");
            UI.Open("S");
            Assert.AreEqual(1, _cues.Count, Describe(_cues));
        }

        [Test]
        public void Teardown_and_immediate_closes_cue_nothing()
        {
            Load("main", "<Screen name='S'><Frame/></Screen>");

            UI.Open("S").Dispose();
            UI.CloseImmediate("S");   // Dispose() leaves UI's entry behind (spec §15); this clears it
            UI.Open("S");
            UI.CloseImmediate("S");
            UI.Open("S");
            UI.UnloadAll();

            Assert.AreEqual(3, _cues.Count(c => c.Action == "open"), Describe(_cues));
            Assert.AreEqual(0, _cues.Count(c => c.Action == "close"), Describe(_cues));
        }

        [Test]
        public void Screen_sfx_decides_the_keys_and_follows_variants()
        {
            Load("main", "<Screen name='S' sfx='open:shop_open close:none' sfx.mobile='none'><Frame/></Screen>");

            UI.Open("S");
            UI.Close("S");
            UI.Variants.Set("mobile", true);
            UI.Open("S");

            Assert.AreEqual("[screen.open→shop_open]", Describe(_cues));
        }

        [Test]
        public void A_dialog_cues_modal_open_and_its_button_then_modal_close()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest { Text = "x", Buttons = MsgBtn.OK });
            Assert.AreEqual("[modal.open→modal.open]", Describe(_cues));

            Click(UI.Modal.TopScreen.Get<Btn>("ok").GameObject);

            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
            Assert.AreEqual("[modal.open→modal.open, btn.click→btn.click, modal.close→modal.close]", Describe(_cues));
        }

        [Test]
        public void Loading_and_toasts_are_silent()
        {
            var handle = Loading.Open("hi");
            handle.Close();
            UI.Toast.Show("saved");
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void Router_pages_cue_screen_and_routed_modals_cue_modal()
        {
            _files["home"] = Head + "<Screen name='home'><Frame/></Screen></PromptUGUI>";
            _files["settings"] = Head + "<Screen name='settings'><Frame/></Screen></PromptUGUI>";
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");

            UI.Router.Open("settings").GetAwaiter().GetResult();
            UI.Router.Open("home").GetAwaiter().GetResult();

            Assert.AreEqual("[screen.open→screen.open, modal.open→modal.open, modal.close→modal.close]", Describe(_cues));
        }

        [Test]
        public void A_hot_reload_reopens_silently_and_keeps_its_role()
        {
            Load("main", "<Screen name='S'><Frame/></Screen>");
            UI.Open("S");
            _cues.Clear();

            UI.ReloadAsync("S").GetAwaiter().GetResult();
            Assert.IsEmpty(_cues, "a rebuild is not an open: " + Describe(_cues));

            UI.Close("S");
            Assert.AreEqual("[screen.close→screen.close]", Describe(_cues), "still a screen that was open");
        }

        [Test]
        public void A_document_host_is_silent()
        {
            var hostGo = new GameObject("Host");
            try
            {
                var host = hostGo.AddComponent<PromptUGUIDocumentHost>();
                var asset = new TextAsset(Head + "<Screen name='Hosted'><Frame/></Screen></PromptUGUI>") { name = "Hosted" };
                host.xmlAsset = asset;
                host.Refresh();
                host.Refresh();
            }
            finally { Object.DestroyImmediate(hostGo); }
            Assert.IsEmpty(_cues, Describe(_cues));
        }
    }
}
