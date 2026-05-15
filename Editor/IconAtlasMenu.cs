using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    public static class IconAtlasMenu
    {
        [MenuItem("Tools/PromptUGUI/Icon/Sync Atlases (All Sets)")]
        public static void SyncAll()
        {
            var sets = new System.Collections.Generic.List<SpriteSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0)
            {
                Debug.Log("[PromptUGUI] No SpriteSet assets found");
                return;
            }
            IconAtlasSyncer.SyncAll(sets);
            UI.HotReload.NotifySpriteAssetsChanged();
            Debug.Log($"[PromptUGUI] Synced {sets.Count} SpriteSet(s)");
        }

        [MenuItem("Tools/PromptUGUI/Icon/Sync Atlases (Selected Set)")]
        public static void SyncSelected()
        {
            var picked = new System.Collections.Generic.List<SpriteSet>();
            foreach (var o in Selection.objects)
                if (o is SpriteSet s) picked.Add(s);
            if (picked.Count == 0)
            {
                Debug.LogWarning("[PromptUGUI] No SpriteSet selected");
                return;
            }
            IconAtlasSyncer.SyncAll(picked);
            UI.HotReload.NotifySpriteAssetsChanged();
        }

        [MenuItem("Tools/PromptUGUI/Icon/Sync Atlases (Selected Set)", true)]
        public static bool SyncSelectedValidate()
        {
            foreach (var o in Selection.objects)
                if (o is SpriteSet) return true;
            return false;
        }
    }
}
