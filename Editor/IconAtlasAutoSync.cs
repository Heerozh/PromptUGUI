using System;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    public sealed class IconAtlasAutoSync : AssetPostprocessor
    {
        private const string PrefKey = "PromptUGUI.IconAtlas.AutoSyncOnSave";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save")]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save", true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save", Enabled);
            return true;
        }

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!Enabled) return;
            var xmlChanged = false;
            foreach (var p in imported)
                if (p.EndsWith(".ui.xml", StringComparison.Ordinal)) { xmlChanged = true; break; }
            if (!xmlChanged)
            {
                foreach (var p in deleted)
                    if (p.EndsWith(".ui.xml", StringComparison.Ordinal)) { xmlChanged = true; break; }
            }
            if (!xmlChanged) return;

            var sets = new System.Collections.Generic.List<SpriteSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0) return;
            IconAtlasSyncer.SyncAll(sets);
            UI.HotReload.NotifySpriteAssetsChanged();
        }
    }
}
