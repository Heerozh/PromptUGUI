using System;
using System.Collections.Generic;
using UnityEditor;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// Per-user UI Preview state (2026-09-18 ui-preview-tool spec §4.12): the file that was open,
    /// which Screen of a multi-Screen file and which page of each <c>&lt;Pages&gt;</c> was being
    /// looked at, the panel's collapsed state and filter. <c>UserSettings/PromptUGUI/Preview.asset</c>
    /// — the Unity project template ignores <c>UserSettings/</c>, so none of this reaches git, unlike
    /// the PlayerPrefs the host's tool used to write (a Player registry key, unrelated to the project).
    /// </summary>
    [FilePath("UserSettings/PromptUGUI/Preview.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UIPreviewUserState : ScriptableSingleton<UIPreviewUserState>
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            public string value;
        }

        /// <summary>Asset path of the last file loaded; auto-loaded at the next launch when <see cref="autoLoadLast"/>.</summary>
        public string lastFile = "";
        public bool autoLoadLast = true;
        public bool collapsed;
        public string filter = "";

        /// <summary>file asset path → Screen name last opened from it.</summary>
        public List<Entry> screenByFile = new List<Entry>();

        /// <summary><c>file|pagesId#index</c> → page id last selected (spec §4.8).</summary>
        public List<Entry> pageByKey = new List<Entry>();

        public string GetScreen(string file) => Get(screenByFile, file);
        public void SetScreen(string file, string screen) => Set(screenByFile, file, screen);
        public string GetPage(string key) => Get(pageByKey, key);
        public void SetPage(string key, string page) => Set(pageByKey, key, page);

        public void SaveNow() => Save(saveAsText: true);

        private static string Get(List<Entry> list, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var e in list)
                if (e.key == key) return e.value;
            return null;
        }

        private static void Set(List<Entry> list, string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            foreach (var e in list)
                if (e.key == key) { e.value = value; return; }
            list.Add(new Entry { key = key, value = value });
        }
    }
}
