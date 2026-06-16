using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Modals
{
    // A modal whose XML throws during instantiation must surface the error to the console.
    // The Open(...) awaitable is faulted (entry.Cancel(ex)), but callers commonly await
    // fire-and-forget, so without an explicit log the modal "just doesn't open" with zero
    // diagnostic. The materialize catch in UI.Modal must Debug.LogError such failures.
    public class ModalInstantiationErrorTests : ModalTestFixture
    {
        // `anchor="right"` is an invalid anchor token (bare side words aren't allowed; only
        // 'center' / 'stretch' or '<vertical>-<horizontal>'), so AnchorPreset.Parse throws
        // while the close Btn is instantiated.
        private const string BadAnchorXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/BadAnchor'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame anchor='center' size='100x100'>
      <Btn id='close' anchor='right' size='18x18'>x</Btn>
    </Frame>
  </Screen>
</PromptUGUI>";

        [Test]
        public void Instantiation_failure_is_logged_with_src()
        {
            Files["test/BadAnchor"] = BadAnchorXml;
            MessageBox.XmlSrc = "test/BadAnchor";

            // The error message must name the failing modal src so authors can find it.
            LogAssert.Expect(LogType.Error, new Regex("test/BadAnchor"));
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "x", Buttons = MsgBtn.OK });
        }
    }
}
