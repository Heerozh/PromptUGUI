using System;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    public sealed class IconAtlasAutoSync : AssetPostprocessor {
        const string PrefKey = "PromptUGUI.IconAtlas.AutoSyncOnSave";

        public static bool Enabled {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save")]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save", true)]
        static bool ToggleValidate() {
            Menu.SetChecked("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save", Enabled);
            return true;
        }

        static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
            if (!Enabled) return;
            bool xmlChanged = false;
            foreach (var p in imported)
                if (p.EndsWith(".ui.xml", StringComparison.Ordinal)) { xmlChanged = true; break; }
            if (!xmlChanged) {
                foreach (var p in deleted)
                    if (p.EndsWith(".ui.xml", StringComparison.Ordinal)) { xmlChanged = true; break; }
            }
            if (!xmlChanged) return;

            var sets = new System.Collections.Generic.List<IconSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0) return;
            IconAtlasSyncer.SyncAll(sets);
            UI.HotReload.NotifyIconAssetsChanged();
        }
    }
}
