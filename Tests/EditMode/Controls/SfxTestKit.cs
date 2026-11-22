using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// What the sound-cue tests share (spec 2026-11-23-ui-sfx-cues §11): a recorder on <see cref="UI.Sfx.OnCue"/>, real
    /// pointer clicks and Submits through <see cref="ExecuteEvents"/> (what the input module delivers — not
    /// <c>onClick.Invoke()</c>, which a cue must ignore), and a one-Screen document.
    /// </summary>
    internal static class SfxTestKit
    {
        public const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";

        /// <summary>Every cue from now on, in order. Call after <c>UI.ResetForTests()</c> (it rebuilds the stream).</summary>
        public static List<SfxCue> Record()
        {
            var cues = new List<SfxCue>();
            UI.Sfx.OnCue.Subscribe(cues.Add);
            return cues;
        }

        /// <summary>Loads <paramref name="body"/> (top-level elements, no root) and opens Screen <paramref name="screen"/>.</summary>
        public static PromptScreen Open(string body, string screen = "S")
        {
            UI.LoadDocument("sfx-test", Head + body + "</PromptUGUI>");
            return UI.Open(screen);
        }

        /// <summary>Opens a Screen named S holding <paramref name="inner"/>.</summary>
        public static PromptScreen OpenScreen(string inner, string before = "") =>
            Open(before + "<Screen name='S'>" + inner + "</Screen>");

        public static PointerEventData Pointer(PointerEventData.InputButton button = PointerEventData.InputButton.Left) =>
            new PointerEventData(EventSystem.current) { button = button, eligibleForClick = true };

        /// <summary>A pointer click, delivered the way the input module does: to the click handler at or above it.</summary>
        public static void Click(GameObject go, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            Assert.IsNotNull(target, $"something at or above '{go.name}' takes a click");
            ExecuteEvents.Execute(target, Pointer(button), ExecuteEvents.pointerClickHandler);
        }

        /// <summary>A keyboard / gamepad Submit on the selected object (the input module sends it there only).</summary>
        public static void Submit(GameObject go) =>
            ExecuteEvents.Execute(go, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);

        public static string Describe(IEnumerable<SfxCue> cues) =>
            "[" + string.Join(", ", cues.Select(c => $"{c.Kind}.{c.Action}→{c.Key}")) + "]";
    }
}
