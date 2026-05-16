using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Modals
{
    public class ModalEscapePlayModeTests
    {
        private GameObject _es;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // Resources path structure: Assets/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml
            // Resources.Load("PromptUGUI/Modals/MessageBox.ui") loads the above file
            UI.SourceResolver = src =>
            {
                if (string.IsNullOrEmpty(src))
                    return AwaitableHelpers.Faulted<string>(
                        new System.IO.IOException("Resources lookup with empty src"));
                var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(src);
                if (ta == null)
                    return AwaitableHelpers.Faulted<string>(
                        new System.IO.IOException($"Resources lookup failed: {src}"));
                return AwaitableHelpers.Completed(ta.text);
            };
            _es = new GameObject("EventSystem");
            _es.AddComponent<EventSystem>();
            // No InputModule: the tests trigger ESC via ModalEscapeListener.FireForTests(),
            // they never poll real input. StandaloneInputModule.UpdateModule reads
            // UnityEngine.Input.mousePosition every frame, which throws under
            // Input System-only Player Settings. ModalEscapeListener itself adapts
            // via #if ENABLE_INPUT_SYSTEM / ELIF ENABLE_LEGACY_INPUT_MANAGER, so no
            // input module is needed for the listener to operate.
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) Object.DestroyImmediate(_es);
        }

        [UnityTest]
        public IEnumerator Escape_returns_Cancel_when_OK_Cancel_combo()
        {
            var task = MessageBox.Open("first", MsgBtn.OK | MsgBtn.Cancel);
            yield return null;
            yield return null;

            var screen = UI.Get(MessageBox.XmlSrc);
            Assert.IsNotNull(screen, "Screen should be loaded");
            var listener = screen.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "ModalEscapeListener should be attached");

            listener.FireForTests();
            yield return null;

            Assert.AreEqual(MsgBtn.Cancel, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [UnityTest]
        public IEnumerator Escape_only_OK_does_not_close()
        {
            var task = MessageBox.Open("only ok", MsgBtn.OK);
            yield return null;
            yield return null;

            var listener = UI.Get(MessageBox.XmlSrc).RootGameObject.GetComponent<ModalEscapeListener>();
            listener.FireForTests();
            yield return null;

            Assert.IsTrue(UI.Modal.IsAnyOpen, "ESC with OK-only must not close");
            UI.Modal.CloseAll();
            Assert.Throws<System.OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator SortingOrder_uses_SortingOrderBase()
        {
            UI.Modal.SortingOrderBase = 777;
            var task = MessageBox.Open("x", MsgBtn.OK);
            yield return null;
            yield return null;

            var canvas = UI.Get(MessageBox.XmlSrc).RootGameObject.GetComponent<Canvas>();
            Assert.AreEqual(777, canvas.sortingOrder);

            UI.Get(MessageBox.XmlSrc).Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            yield return null;
            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
        }
    }
}
