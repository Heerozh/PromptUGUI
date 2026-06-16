using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class DisabledGrayscalePlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator DisabledBtn_InLiveCanvas_UsesGrayscaleMaterial()
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Btn id='b' interactable='false'>Hi</Btn></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;  // 走一帧，确保布局/状态稳定

            var bg = screen.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name);

            screen.Get<Btn>("b").Interactable = true;
            yield return null;
            Assert.AreEqual(bg.defaultMaterial, bg.material);
        }
    }
}
