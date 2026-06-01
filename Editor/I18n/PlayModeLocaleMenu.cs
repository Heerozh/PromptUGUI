// Editor/I18n/PlayModeLocaleMenu.cs
using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PromptUGUI.Editor.I18n
{
    /// <summary>
    /// While in Play mode, overlays a small top-right row on every Game view holding
    /// a locale switcher and (once any &lt;Theme&gt; is registered) a theme switcher.
    /// Locales come from <see cref="PromptUGUISettings"/> and are known before Play, so
    /// the locale dropdown is built up front. Themes register at runtime as documents
    /// load and <see cref="UI.Theme"/> has no "available changed" event, so the theme
    /// dropdown is (re)populated by polling <see cref="UI.Theme.Available"/> on
    /// <see cref="EditorApplication.update"/> while playing.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeLocaleMenu
    {
        private sealed class Overlay
        {
            public VisualElement Host;
            public PopupField<string> Locale;
            public PopupField<string> Theme; // null until at least one theme registers
        }

        private static readonly List<Overlay> s_Overlays = new();

        // Last snapshot of UI.Theme.Available the theme dropdowns were built from.
        // null means "not yet sampled" — forces the next poll to (re)build.
        private static List<string> s_ThemeSnapshot;

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

            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;

            ClearOverlays();

            UI.Locale.Changed -= SyncLocaleToCurrent;
            UI.Locale.Changed += SyncLocaleToCurrent;
            UI.Theme.Changed -= SyncThemeToCurrent;
            UI.Theme.Changed += SyncThemeToCurrent;
            EditorApplication.update -= PollThemes;
            EditorApplication.update += PollThemes;
            s_ThemeSnapshot = null;

            var locales = new List<string>(configured);
            var current = UI.Locale.Current;
            var idx = current != null ? locales.IndexOf(current) : -1;
            if (idx < 0) idx = 0;

            foreach (var obj in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                if (obj is not EditorWindow gv) continue;
                if (gv.rootVisualElement == null) continue;

                var host = new VisualElement();
                host.style.flexDirection = FlexDirection.Row;
                host.style.alignSelf = Align.FlexEnd;
                host.style.top = 22;

                var localeMenu = new PopupField<string>(locales, idx);
                localeMenu.style.minWidth = 100;
                localeMenu.focusable = false;
                localeMenu.RegisterValueChangedCallback(evt =>
                {
                    if (!string.IsNullOrEmpty(evt.newValue)) UI.Locale.Set(evt.newValue);
                });
                host.Add(localeMenu);

                gv.rootVisualElement.Add(host);
                host.BringToFront();
                s_Overlays.Add(new Overlay { Host = host, Locale = localeMenu });
            }

            // Populate theme dropdowns immediately if themes are already registered
            // (e.g. re-entering Play with Domain Reload disabled).
            PollThemes();
        }

        private static void Hide()
        {
            UI.Locale.Changed -= SyncLocaleToCurrent;
            UI.Theme.Changed -= SyncThemeToCurrent;
            EditorApplication.update -= PollThemes;
            s_ThemeSnapshot = null;
            ClearOverlays();
        }

        private static void ClearOverlays()
        {
            foreach (var o in s_Overlays)
            {
                if (o.Host == null) continue;
                o.Host.RemoveFromHierarchy();
            }
            s_Overlays.Clear();
        }

        private static void PollThemes()
        {
            if (!UnityEngine.Application.isPlaying) return;
            if (s_Overlays.Count == 0) return;

            var available = UI.Theme.Available;
            if (SameThemeSet(available)) return;

            s_ThemeSnapshot = available != null ? new List<string>(available) : new List<string>();
            RebuildThemeMenus();
        }

        private static bool SameThemeSet(IReadOnlyCollection<string> available)
        {
            if (s_ThemeSnapshot == null) return false;
            var n = available?.Count ?? 0;
            if (s_ThemeSnapshot.Count != n) return false;
            if (n == 0) return true;
            foreach (var t in available)
                if (!s_ThemeSnapshot.Contains(t)) return false;
            return true;
        }

        private static void RebuildThemeMenus()
        {
            var themes = s_ThemeSnapshot;
            var current = UI.Theme.Current;

            foreach (var o in s_Overlays)
            {
                if (o.Host == null) continue;

                if (themes == null || themes.Count == 0)
                {
                    if (o.Theme != null)
                    {
                        o.Theme.RemoveFromHierarchy();
                        o.Theme = null;
                    }
                    continue;
                }

                if (o.Theme == null)
                {
                    var idx = current != null ? themes.IndexOf(current) : -1;
                    if (idx < 0) idx = 0;

                    var themeMenu = new PopupField<string>(new List<string>(themes), idx);
                    themeMenu.style.minWidth = 100;
                    themeMenu.style.marginLeft = 4;
                    themeMenu.focusable = false;
                    themeMenu.RegisterValueChangedCallback(evt =>
                    {
                        if (!string.IsNullOrEmpty(evt.newValue)) UI.Theme.Set(evt.newValue);
                    });
                    o.Host.Add(themeMenu);
                    o.Theme = themeMenu;
                }
                else
                {
                    o.Theme.choices = new List<string>(themes);
                    if (current != null && themes.Contains(current))
                        o.Theme.SetValueWithoutNotify(current);
                }
            }
        }

        private static void SyncLocaleToCurrent()
        {
            var current = UI.Locale.Current;
            if (current == null) return;
            foreach (var o in s_Overlays)
            {
                if (o.Locale == null) continue;
                o.Locale.SetValueWithoutNotify(current);
            }
        }

        private static void SyncThemeToCurrent(string name)
        {
            var current = UI.Theme.Current;
            if (string.IsNullOrEmpty(current)) return;
            foreach (var o in s_Overlays)
            {
                if (o.Theme == null) continue;
                if (!o.Theme.choices.Contains(current)) continue;
                o.Theme.SetValueWithoutNotify(current);
            }
        }
    }
}
