using System;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    public sealed class SpriteAtlasAutoSync : AssetPostprocessor
    {
        private const string PrefKey = "PromptUGUI.SpriteAtlas.AutoSyncOnSave";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem("Tools/PromptUGUI/Sprite/Auto-sync Atlases on Save")]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem("Tools/PromptUGUI/Sprite/Auto-sync Atlases on Save", true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/PromptUGUI/Sprite/Auto-sync Atlases on Save", Enabled);
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
            if (xmlChanged)
            {
                var sets = new System.Collections.Generic.List<SpriteSet>();
                foreach (var s in SpriteAtlasSyncer.FindAllSpriteSets()) sets.Add(s);
                if (sets.Count == 0) return;
                SpriteAtlasSyncer.SyncAll(sets);
                ScheduleInlineRegen();
                UI.HotReload.NotifySpriteAssetsChanged();
                return;
            }

            // Sprite-source pixel update: SyncAll short-circuits via PackablesEqual
            // when only pixels change, so call PackAtlases directly on the affected
            // SpriteSets' atlases — same effect as clicking "Pack Preview" by hand.
            var atlases = new System.Collections.Generic.List<UnityEngine.U2D.SpriteAtlas>();
            var inlineDirty = false;
            foreach (var set in SpriteAtlasSyncer.FindAllSpriteSets())
            {
                if (set == null) continue;
                var folder = set.SourceFolderPath;
                if (string.IsNullOrEmpty(folder)) continue;
                var prefix = folder.EndsWith("/", StringComparison.Ordinal) ? folder : folder + "/";
                if (!AnyUnder(prefix, imported)) continue;
                // A flagged set bakes its own copy of the pixels into the inline TMP asset,
                // so a source change there (new/edited emoji) must rebuild it — independent
                // of whether the set has a .spriteatlas.
                if (set.GenerateTmpSpriteAsset) inlineDirty = true;
                if (set.Atlas != null) atlases.Add(set.Atlas);
            }
            if (inlineDirty) ScheduleInlineRegen();
            if (atlases.Count == 0) return;

            UnityEditor.U2D.SpriteAtlasUtility.PackAtlases(
                atlases.ToArray(),
                EditorUserBuildSettings.activeBuildTarget);
        }

        // Regenerating the inline TMP_SpriteAsset creates/imports assets, which is UNSAFE to do
        // re-entrantly from inside OnPostprocessAllAssets — it corrupts the asset currently being
        // imported (sub-asset links detach). Defer to the next editor tick, after the import
        // settles; debounce so a multi-asset import batch triggers a single rebuild.
        private static bool _inlineRegenScheduled;

        private static void ScheduleInlineRegen()
        {
            if (_inlineRegenScheduled) return;
            _inlineRegenScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _inlineRegenScheduled = false;
                InlineSpriteAssetBuilder.RegenerateFromProject();
            };
        }

        // Excludes `.spriteatlas` / `.spriteatlasv2` so the atlas asset we just
        // repacked (which re-emits an import event) does not retrigger the loop.
        private static bool AnyUnder(string prefix, string[] paths)
        {
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (!p.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (p.EndsWith(".meta", StringComparison.Ordinal)) continue;
                if (p.EndsWith(".spriteatlas", StringComparison.Ordinal)) continue;
                if (p.EndsWith(".spriteatlasv2", StringComparison.Ordinal)) continue;
                return true;
            }
            return false;
        }
    }
}
