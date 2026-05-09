// Editor/I18n/PlayModeLocaleMenu.cs
using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PromptUGUI.Editor.I18n
{
    [InitializeOnLoad]
    internal static class PlayModeLocaleMenu
    {
        private static readonly List<PopupField<string>> s_Menus = new();

        static PlayModeLocaleMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (UnityEngine.Application.isPlaying) Show();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Show();
            else if (state == PlayModeStateChange.ExitingPlayMode) Hide();
        }

        private static void Show()
        {
            var configured = UI.Locale.Configured;
            if (configured == null || configured.Count == 0) return;

            UI.Locale.Changed -= SyncMenusToCurrent;
            UI.Locale.Changed += SyncMenusToCurrent;

            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;

            ClearMenus();

            var locales = new List<string>(configured);
            var current = UI.Locale.Current;
            var idx = current != null ? locales.IndexOf(current) : -1;
            if (idx < 0) idx = 0;

            foreach (var obj in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                if (obj is not EditorWindow gv) continue;
                if (gv.rootVisualElement == null) continue;

                var menu = new PopupField<string>(locales, idx);
                menu.style.alignSelf = Align.FlexEnd;
                menu.style.top = 22;
                menu.style.minWidth = 100;
                menu.focusable = false;
                menu.RegisterValueChangedCallback(evt =>
                {
                    if (!string.IsNullOrEmpty(evt.newValue)) UI.Locale.Set(evt.newValue);
                });
                gv.rootVisualElement.Add(menu);
                menu.BringToFront();
                s_Menus.Add(menu);
            }
        }

        private static void Hide()
        {
            UI.Locale.Changed -= SyncMenusToCurrent;
            ClearMenus();
        }

        private static void ClearMenus()
        {
            foreach (var m in s_Menus)
            {
                if (m == null) continue;
                m.RemoveFromHierarchy();
            }
            s_Menus.Clear();
        }

        private static void SyncMenusToCurrent()
        {
            var current = UI.Locale.Current;
            if (current == null) return;
            foreach (var m in s_Menus)
            {
                if (m == null) continue;
                m.SetValueWithoutNotify(current);
            }
        }
    }
}
