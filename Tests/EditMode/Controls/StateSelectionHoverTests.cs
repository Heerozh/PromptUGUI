using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// uGUI's <c>currentSelectionState</c> ranks <c>hasSelection</c> ABOVE <c>isPointerInside</c>.
    /// A mouse click selects the control (<c>Selectable.OnPointerDown</c> →
    /// <c>EventSystem.SetSelectedGameObject</c>), so from then on every hover evaluates to
    /// <c>SelectionState.Selected</c> and never <c>Highlighted</c>. Folding that to Normal in Pointer
    /// mode — right for "a click must not leave the control stuck-highlighted" — also ate the hover
    /// visual for as long as the control kept the selection: <c>hoverModulate</c> worked once, then
    /// went dead until the user clicked some other control.
    /// </summary>
    public class StateSelectionHoverTests
    {
        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            UI.ResetForTests();
        }

        private static PointerEventData Pointer() => new(EventSystem.current);
        private static BaseEventData Selection() => new(EventSystem.current);

        private T New<T>() where T : Selectable
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void Hover_SurvivesTheSelectionLeftBehindByAClick()
        {
            var btn = New<PuiButton>();

            btn.OnPointerEnter(Pointer());
            Assert.AreEqual(InteractState.Hover, btn.Current, "pointer inside, nothing clicked yet");

            btn.OnSelect(Selection());                 // what the click leaves behind
            Assert.AreEqual(InteractState.Hover, btn.Current,
                "the pointer never left — the button is still hovered, not merely focused");
        }

        [Test]
        public void ReEnter_WhileStillSelected_ReportsHoverAgain()
        {
            var btn = New<PuiButton>();
            btn.OnPointerEnter(Pointer());
            btn.OnSelect(Selection());

            btn.OnPointerExit(Pointer());
            Assert.AreEqual(InteractState.Normal, btn.Current,
                "pointer gone: a click must not leave the control stuck-highlighted");

            btn.OnPointerEnter(Pointer());             // the selection is still ours
            Assert.AreEqual(InteractState.Hover, btn.Current, "hovering it again must hover again");
        }

        [Test]
        public void Toggle_HoversWhileSelected_Too()
        {
            var tog = New<PuiToggle>();
            tog.InitStateBroadcast();

            tog.OnPointerEnter(Pointer());
            tog.OnSelect(Selection());
            Assert.AreEqual(InteractState.Hover, tog.Current);
        }

        [Test]
        public void Directional_KeepsFocusedEvenWithThePointerInside()
        {
            var btn = New<PuiButton>();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;

            btn.OnPointerEnter(Pointer());
            btn.OnSelect(Selection());
            Assert.AreEqual(InteractState.Focused, btn.Current,
                "directional focus owns the state while the cursor is visible (spec §3)");
        }

        [Test]
        public void Deselect_WithPointerInside_FallsBackToHover()
        {
            var btn = New<PuiButton>();
            btn.OnPointerEnter(Pointer());
            btn.OnSelect(Selection());

            btn.OnDeselect(Selection());               // another control took the selection
            Assert.AreEqual(InteractState.Hover, btn.Current);
        }
    }
}
