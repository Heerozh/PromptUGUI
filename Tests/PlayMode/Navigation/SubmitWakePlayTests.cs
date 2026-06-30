using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    // 端到端复现用户报告的 bug：键盘选中 Cancel → 鼠标移动隐藏光标（选区仍 Cancel）→
    // 真实回车不应触发 Cancel，而应唤回光标；第二次回车才触发。
    public class SubmitWakePlayTests : UnityEngine.InputSystem.InputTestFixture
    {
        private GameObject _es;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            DestroyAllEventSystems();   // 防前序 nav 测试泄漏的 EventSystem 抢 current
            _es = new GameObject("EventSystem", typeof(EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            DestroyAllEventSystems();
            _es = null;
            base.TearDown();
        }

        private static void DestroyAllEventSystems()
        {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        [UnityTest]
        public IEnumerator EnterInPointerMode_WakesCursor_DoesNotTriggerStaleFocus()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = InputSystem.AddDevice<Keyboard>();
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='ok'>OK</Btn><Btn id='cancel'>Cancel</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            yield return null;
            var cancel = screen.Get<Btn>("cancel");
            bool cancelFired = false;
            cancel.OnClick.Subscribe(_ => cancelFired = true).AddTo(screen);

            // 1) 键盘导航到 Cancel（Directional）
            UI.Navigation.NoteDirectionalInput();
            EventSystem.current.SetSelectedGameObject(cancel.GameObject);
            yield return null;

            // 2) 鼠标移动 → Pointer 模式，光标隐藏（选区仍是 Cancel）
            UI.Navigation.NotePointerInput();
            yield return null;
            Assert.AreEqual(UI.Navigation.NavMode.Pointer, UI.Navigation.Mode);

            // 3) 真实回车
            Press(kb.enterKey);
            yield return null; yield return null;
            Release(kb.enterKey);
            yield return null;

            Assert.IsFalse(cancelFired,
                "Enter while cursor hidden must NOT trigger the stale focus (Cancel)");
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode,
                "first Enter wakes the cursor → Directional");

            // 4) 现在 Directional + 选区 Cancel → 第二次回车正常触发 Cancel
            Press(kb.enterKey);
            yield return null; yield return null;
            Release(kb.enterKey);
            yield return null;
            Assert.IsTrue(cancelFired,
                "second Enter (cursor visible) triggers the focused button");
            UI.ResetForTests();
#else
            yield break;
#endif
        }
    }
}
