using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    // End-to-end repro of the reported bug: in CommonControls' MessageBox, pressing a direction
    // could not reach Cancel because a button on the page BEHIND the modal was a closer geometric
    // neighbour, so focus escaped and the trap snapped it back to OK. Drives a REAL key press
    // through the live InputSystemUIInputModule with an adversarial background button.
    public class ModalNavRealInputPlayTests : UnityEngine.InputSystem.InputTestFixture
    {
        private GameObject _es;
        private GameObject _bgPage;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            // Defensive isolation: prior nav tests leak their EventSystem (UI.ResetForTests keeps
            // it). A stray second EventSystem would make our module non-current, so the real key
            // press would generate no nav event and selection would never move. Start from one.
            DestroyAllEventSystems();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            UI.SourceResolver = src =>
            {
                var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(src);
                if (ta == null)
                    return AwaitableHelpers.Faulted<string>(
                        new System.IO.IOException($"Resources lookup failed: {src}"));
                return AwaitableHelpers.Completed(ta.text);
            };
            _es = new GameObject("EventSystem", typeof(EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            if (_bgPage != null) Object.DestroyImmediate(_bgPage);
            DestroyAllEventSystems();   // also removes the one UseGamepadNavigation may have kept
            _es = null;
            base.TearDown();
        }

        private static void DestroyAllEventSystems()
        {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        [UnityTest]
        public IEnumerator RealRightKey_ReachesCancel_DespiteCloserBackgroundButton()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = InputSystem.AddDevice<Keyboard>();
            UI.UseGamepadNavigation();

            var task = MessageBox.Open("e2e", MsgBtn.OK | MsgBtn.Cancel, title: "确认");
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            var modal = UI.Modal.TopScreen;
            Assert.IsNotNull(modal, "modal must be open");
            var okGo = modal.Get<Btn>("ok").GameObject;
            var cancelGo = modal.Get<Btn>("cancel").GameObject;
            var okPos = okGo.transform.position;
            var cancelPos = cancelGo.transform.position;

            // Adversarial background button (outside the modal subtree) placed BETWEEN OK and
            // Cancel — closer to OK's right edge than Cancel, so uGUI's scene-wide Automatic search
            // would prefer it. The cage (applied at modal open) must make this irrelevant.
            _bgPage = new GameObject("BgCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            _bgPage.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var bg = new GameObject("adversarialBg", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button));
            bg.transform.SetParent(_bgPage.transform, false);
            var bgRt = (RectTransform)bg.transform;
            bgRt.sizeDelta = new Vector2(40, 40);
            bgRt.position = new Vector3(okPos.x + (cancelPos.x - okPos.x) * 0.5f, okPos.y, okPos.z);
            yield return null;

            // Focus OK and enter Directional mode (what NavigationController does on a key press).
            UI.Navigation.NoteDirectionalInput();
            EventSystem.current.SetSelectedGameObject(okGo);
            yield return null;

            Press(kb.rightArrowKey);
            yield return null;
            yield return null;
            yield return null;
            Release(kb.rightArrowKey);
            yield return null;

            var after = EventSystem.current.currentSelectedGameObject;
            Assert.AreSame(cancelGo, after,
                "real Right key must reach Cancel, not escape to the background button / bounce back to OK");

            modal.Get<Btn>("cancel").SimulateClick();
            yield return null;
#else
            yield break;
#endif
        }
    }
}
