using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using R3;
using TMPro;
using UnityEngine.TestTools;
using PInputField = PromptUGUI.Controls.InputField;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class InputFieldRuntimeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Typing_FiresOnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            yield return null;  // wait one frame for canvas init

            string last = null;
            f.OnValueChanged.Subscribe(v => last = v);
            f.GameObject.GetComponent<TMP_InputField>().text = "typed";
            yield return null;

            Assert.AreEqual("typed", last);
        }
    }
}
