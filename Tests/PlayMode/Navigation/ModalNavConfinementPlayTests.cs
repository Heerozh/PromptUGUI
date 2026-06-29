using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    // Regression for the "focus stuck on OK, can't reach Cancel" bug: uGUI's Automatic
    // navigation searches the whole scene, so a modal button's geometric neighbour can be a
    // control on the page BEHIND the modal. Directional nav must be confined to the modal.
    public class ModalNavConfinementPlayTests
    {
        private GameObject _es;
        private GameObject _bgPage;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            UI.SourceResolver = src =>
            {
                var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(src);
                if (ta == null)
                    return AwaitableHelpers.Faulted<string>(
                        new System.IO.IOException($"Resources lookup failed: {src}"));
                return AwaitableHelpers.Completed(ta.text);
            };
            _es = new GameObject("EventSystem");
            _es.AddComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) Object.DestroyImmediate(_es);
            if (_bgPage != null) Object.DestroyImmediate(_bgPage);
        }

        private static bool InModalOrNull(Selectable s, GameObject root) =>
            s == null || s.transform.IsChildOf(root.transform);

        // A dense grid of active Buttons on a separate canvas — the "page behind the modal".
        private void BuildBackgroundPage()
        {
            _bgPage = new GameObject("BgCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            _bgPage.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            for (int i = 0; i < 48; i++)
            {
                var b = new GameObject($"bg{i}", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.Button));
                b.transform.SetParent(_bgPage.transform, false);
                var rt = (RectTransform)b.transform;
                rt.sizeDelta = new Vector2(120, 50);
                rt.anchoredPosition = new Vector2((i % 8) * 230 - 805, (i / 8) * 170 - 425);
            }
        }

        [UnityTest]
        public IEnumerator ModalButtons_CagedToInModalNeighbours()
        {
            UI.UseGamepadNavigation();
            BuildBackgroundPage();

            var task = MessageBox.Open("confine", MsgBtn.OK | MsgBtn.Cancel, title: "确认");
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;

            var modal = UI.Modal.TopScreen;
            Assert.IsNotNull(modal, "modal open");
            var root = modal.RootGameObject;
            var ok = modal.Get<Btn>("ok").GameObject.GetComponent<Selectable>();
            var cancel = modal.Get<Btn>("cancel").GameObject.GetComponent<Selectable>();

            // The cage converts Automatic -> Explicit and wires only in-modal neighbours.
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, ok.navigation.mode,
                "OK must be caged to Explicit navigation (not scene-wide Automatic)");
            Assert.AreSame(cancel, ok.navigation.selectOnRight,
                "OK's right neighbour must be the in-modal Cancel, not a background button");

            // No direction may point outside the modal subtree.
            var n = ok.navigation;
            Assert.IsTrue(InModalOrNull(n.selectOnLeft, root), "OK.left must stay in modal");
            Assert.IsTrue(InModalOrNull(n.selectOnUp, root), "OK.up must stay in modal");
            Assert.IsTrue(InModalOrNull(n.selectOnDown, root), "OK.down must stay in modal");

            // Cancel mirrors back to OK.
            Assert.AreSame(ok, cancel.navigation.selectOnLeft,
                "Cancel's left neighbour must be the in-modal OK");

            modal.Get<Btn>("ok").SimulateClick();
            yield return null;
        }
    }
}
