using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DisabledGrayscaleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void GrayscaleShader_LoadsFromResources_WithExpectedName()
        {
            var shader = Resources.Load<Shader>("PromptUGUI/Material/UI-Grayscale");
            Assert.IsNotNull(shader, "UI-Grayscale shader must live in Resources");
            Assert.AreEqual("UI/Grayscale", shader.name);
        }
    }
}
