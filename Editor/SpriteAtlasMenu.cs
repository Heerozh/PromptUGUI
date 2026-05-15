using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    public static class SpriteAtlasMenu
    {
        [MenuItem("Tools/PromptUGUI/Sprite/Sync Atlases (All Sets)")]
        public static void SyncAll()
        {
            var sets = new System.Collections.Generic.List<SpriteSet>();
            foreach (var s in SpriteAtlasSyncer.FindAllSpriteSets()) sets.Add(s);
            if (sets.Count == 0)
            {
                Debug.Log("[PromptUGUI] No SpriteSet assets found");
                return;
            }
            SpriteAtlasSyncer.SyncAll(sets);
            UI.HotReload.NotifySpriteAssetsChanged();
            Debug.Log($"[PromptUGUI] Synced {sets.Count} SpriteSet(s)");
        }

        [MenuItem("Tools/PromptUGUI/Sprite/Sync Atlases (Selected Set)")]
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
            SpriteAtlasSyncer.SyncAll(picked);
            UI.HotReload.NotifySpriteAssetsChanged();
        }

        [MenuItem("Tools/PromptUGUI/Sprite/Sync Atlases (Selected Set)", true)]
        public static bool SyncSelectedValidate()
        {
            foreach (var o in Selection.objects)
                if (o is SpriteSet) return true;
            return false;
        }
    }
}
