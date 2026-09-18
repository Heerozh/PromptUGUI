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
                ScheduleFullSync();
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
            if (atlases.Count > 0) SchedulePack(atlases);
        }

        // Everything below writes assets (SaveAssets on the SpriteSet, SpriteAtlasAsset.Save +
        // ImportAsset on a V2 atlas, CreateAsset for the inline TMP_SpriteAsset, PackAtlases
        // re-emitting the .spriteatlas import), which is UNSAFE to do re-entrantly from inside
        // OnPostprocessAllAssets — the import batch is still open, so the asset database sees a
        // source it just imported change under it ("Importer(NativeFormatImporter) generated
        // inconsistent result") and can detach sub-asset links on the asset being imported.
        // Defer to the next editor tick, after the import settles; debounce so a multi-asset
        // import batch triggers a single run.

        private static bool _fullSyncScheduled;

        private static void ScheduleFullSync()
        {
            if (_fullSyncScheduled) return;
            _fullSyncScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _fullSyncScheduled = false;
                // Enumerate after the import settles so a SpriteSet that arrived in the same
                // batch is included.
                var sets = new System.Collections.Generic.List<SpriteSet>();
                foreach (var s in SpriteAtlasSyncer.FindAllSpriteSets()) sets.Add(s);
                if (sets.Count == 0) return;
                SpriteAtlasSyncer.SyncAll(sets);
                ScheduleInlineRegen();
                UI.HotReload.NotifySpriteAssetsChanged();
            };
        }

        private static readonly System.Collections.Generic.HashSet<UnityEngine.U2D.SpriteAtlas>
            _pendingPack = new System.Collections.Generic.HashSet<UnityEngine.U2D.SpriteAtlas>();
        private static bool _packScheduled;

        private static void SchedulePack(
            System.Collections.Generic.List<UnityEngine.U2D.SpriteAtlas> atlases)
        {
            foreach (var a in atlases) _pendingPack.Add(a);
            if (_packScheduled) return;
            _packScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _packScheduled = false;
                var arr = new System.Collections.Generic.List<UnityEngine.U2D.SpriteAtlas>();
                foreach (var a in _pendingPack)
                    if (a != null) arr.Add(a);   // atlas may have been deleted meanwhile
                _pendingPack.Clear();
                if (arr.Count == 0) return;
                UnityEditor.U2D.SpriteAtlasUtility.PackAtlases(
                    arr.ToArray(),
                    EditorUserBuildSettings.activeBuildTarget);
            };
        }

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
