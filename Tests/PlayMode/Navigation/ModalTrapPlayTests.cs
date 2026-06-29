using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class ModalTrapPlayTests
    {
        private GameObject _es;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
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
            // Plain EventSystem — no InputModule needed; navigation focus tests drive
            // EventSystem.SetSelectedGameObject directly, no polling of real input.
            _es = new GameObject("EventSystem");
            _es.AddComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) Object.DestroyImmediate(_es);
        }

        [UnityTest]
        public IEnumerator ModalOpen_SetsContainmentRoot_AndRestoresOnClose()
        {
            UI.UseGamepadNavigation();

            // A background selectable to represent "previous selection" before modal opens.
            var bgGo = new GameObject("BackgroundBtn",
                typeof(RectTransform), typeof(UnityEngine.UI.Button));
            EventSystem.current.SetSelectedGameObject(bgGo);
            Assert.AreSame(bgGo, EventSystem.current.currentSelectedGameObject, "pre-open selection");

            // Open modal and wait for it to materialize.
            var task = MessageBox.Open("trap test", MsgBtn.OK);
            yield return null;
            yield return null;

            var modalScreen = UI.Modal.TopScreen;
            Assert.IsNotNull(modalScreen, "modal screen must be open");

            // ContainmentRoot must point at the modal's root.
            Assert.AreSame(modalScreen.RootGameObject, UI.Navigation.ContainmentRoot,
                "ContainmentRoot must be modal root while open");

            // Selection must be inside the modal (ApplyInitialFocus was called).
            var selected = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(selected, "something inside the modal must be selected");
            Assert.IsTrue(selected.transform.IsChildOf(modalScreen.RootGameObject.transform),
                "selected GO must be within modal subtree");

            // Close the modal via the OK button.
            modalScreen.Get<Btn>("ok").SimulateClick();
            yield return null;

            // ContainmentRoot cleared and previous selection restored.
            Assert.IsNull(UI.Navigation.ContainmentRoot,
                "ContainmentRoot must be null after modal stack empties");
            Assert.AreSame(bgGo, EventSystem.current.currentSelectedGameObject,
                "PrevSelected must be restored after close");

            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
            Object.DestroyImmediate(bgGo);
        }

        [UnityTest]
        public IEnumerator StackedModals_ContainmentRoot_TracksMostRecentModal()
        {
            UI.UseGamepadNavigation();

            // Open first modal.
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "first", Buttons = MsgBtn.OK });
            yield return null;
            yield return null;
            var screen1 = UI.Modal.TopScreen;
            Assert.IsNotNull(screen1, "first modal must be open");
            var root1 = screen1.RootGameObject;

            // Open second modal on top.
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "second", Buttons = MsgBtn.OK });
            yield return null;
            yield return null;
            var screen2 = UI.Modal.TopScreen;
            Assert.IsNotNull(screen2, "second modal must be open");
            Assert.AreNotSame(screen1, screen2, "stack must have two distinct screens");

            Assert.AreSame(screen2.RootGameObject, UI.Navigation.ContainmentRoot,
                "ContainmentRoot must follow new top of stack");

            // Close top modal — trap should revert to first modal.
            screen2.Get<Btn>("ok").SimulateClick();
            yield return null;

            Assert.AreSame(root1, UI.Navigation.ContainmentRoot,
                "ContainmentRoot must revert to first modal after second closes");

            // Close first modal.
            screen1.Get<Btn>("ok").SimulateClick();
            yield return null;

            Assert.IsNull(UI.Navigation.ContainmentRoot,
                "ContainmentRoot must be null when stack is empty");
        }
    }
}
