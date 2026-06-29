using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusCursorParseTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void FocusCursor_NotInstantiatedAsLayoutChild_ButHoisted()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <FocusCursor side='left'><Image id='hand' size='16x16'/></FocusCursor>
  <VStack id='stack'><Btn id='a'>A</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            // VStack 只有 1 个布局子（Btn a），光标不在其中
            Assert.AreEqual(1, screen.Get<VStack>("stack").GameObject.transform.childCount);
            // 光标 overlay 在 root 下存在（__FocusCursor GO），并持有 CanvasGroup
            var overlayTf = screen.RootGameObject.transform.Find("__FocusCursor");
            Assert.IsNotNull(overlayTf, "__FocusCursor overlay GO must exist");
            Assert.IsNotNull(overlayTf.GetComponent<CanvasGroup>(), "__FocusCursor must have CanvasGroup");
        }
    }
}
