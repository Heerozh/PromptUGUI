using System.Collections.Generic;
using System.IO;
using PromptUGUI.Application;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace PromptUGUI.Editor
{
    /// <summary>
    /// Editor-only: bakes the sprites of every SpriteSet flagged
    /// <c>generateTmpSpriteAsset</c> into a single global <see cref="TMPro.TMP_SpriteAsset"/>
    /// so authors can use native TMP <c>&lt;sprite name="..."&gt;</c> inline markup.
    /// The pure <see cref="BuildInlineGlyphTable"/> is unit-tested; the asset I/O lives in
    /// <see cref="Generate"/> / <see cref="RegenerateFromProject"/>.
    /// </summary>
    public static partial class InlineSpriteAssetBuilder
    {
        public struct Glyph
        {
            public string name;
            public Sprite sprite;
        }

        /// <summary>Merge flat (set, name, sprite) candidates into a unique-by-name glyph
        /// list. A name present in more than one set is a hard collision: it is added to
        /// <paramref name="collisions"/> and the merge returns an EMPTY list (caller aborts,
        /// matching the atlas syncer's no-silent-overwrite contract).</summary>
        public static List<Glyph> BuildInlineGlyphTable(
            IReadOnlyList<(string set, string name, Sprite sprite)> candidates,
            out List<string> collisions)
        {
            collisions = new List<string>();
            var owner = new Dictionary<string, string>(System.StringComparer.Ordinal);   // name -> setName
            var ordered = new List<Glyph>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var (set, name, sprite) in candidates)
            {
                if (string.IsNullOrEmpty(name) || sprite == null) continue;
                if (owner.TryGetValue(name, out var prevSet))
                {
                    if (prevSet != set && !collisions.Contains(name)) collisions.Add(name);
                    continue;
                }
                owner[name] = set;
                if (seen.Add(name)) ordered.Add(new Glyph { name = name, sprite = sprite });
            }

            if (collisions.Count > 0)
            {
                Debug.LogError(
                    "[InlineSprite] glyph name collision across flagged SpriteSets: " +
                    string.Join(", ", collisions) + ". Rename so every inline glyph is unique.");
                return new List<Glyph>();
            }
            return ordered;
        }

        /// <summary>Flatten flagged sets into (set, bareName, sprite) candidates. Only
        /// unambiguous bare basenames become inline glyph names — path-only keys (those
        /// containing '/') and bare names that collide <i>within</i> a set are dropped by the
        /// reused BuildLookup promotion rule.</summary>
        public static List<(string set, string name, Sprite sprite)> CollectCandidates(
            IReadOnlyList<SpriteSet> flaggedSets)
        {
            var result = new List<(string, string, Sprite)>();
            foreach (var set in flaggedSets)
            {
                if (set == null || string.IsNullOrEmpty(set.SetName)) continue;
                var entries = SpriteAtlasSyncer.EnumerateSpriteSources(set.SourceFolderPath);
                var lookup = SpriteAtlasSyncer.BuildLookup(entries, out _);
                foreach (var kv in lookup)
                {
                    if (kv.Key.IndexOf('/') >= 0) continue;    // path-only → not inline-addressable
                    result.Add((set.SetName, kv.Key, kv.Value));
                }
            }
            return result;
        }

        /// <summary>Fixed output location for the single global inline sprite asset, in the
        /// host project (not the package). Mirrors where generated atlases live — next to
        /// nothing in particular, so a stable dedicated folder is used.</summary>
        public const string OutputPath = "Assets/PromptUGUI.Generated/InlineSprites.asset";

        /// <summary>Rebuild the global inline sprite asset from every flagged SpriteSet in the
        /// project. No flagged sets → no asset created, TMP settings left untouched.</summary>
        public static TMP_SpriteAsset RegenerateFromProject()
        {
            var flagged = new List<PromptUGUI.Application.SpriteSet>();
            foreach (var s in SpriteAtlasSyncer.FindAllSpriteSets())
                if (s != null && s.GenerateTmpSpriteAsset) flagged.Add(s);
            if (flagged.Count == 0) return null;
            return Generate(flagged, OutputPath);
        }

        /// <summary>Collect → merge (abort on collision) → pack a dedicated point-filtered
        /// RGBA32 sheet → build glyph/character tables → save the <c>.asset</c> (texture +
        /// material as sub-assets) → wire as the global <see cref="TMP_Settings"/> default
        /// sprite asset. Returns the asset, or <c>null</c> when there are no glyphs (or a
        /// name collision aborted the merge — already logged, nothing written).</summary>
        public static TMP_SpriteAsset Generate(
            IReadOnlyList<SpriteSet> flaggedSets, string outputPath)
        {
            var candidates = CollectCandidates(flaggedSets);
            var glyphs = BuildInlineGlyphTable(candidates, out var collisions);
            if (collisions.Count > 0) return null;   // already logged; do not half-write
            if (glyphs.Count == 0) return null;

            // 1) Pack the source sprites into one point-filtered RGBA32 sheet. Read via a
            //    RenderTexture blit so non-readable source textures still work.
            var copies = new Texture2D[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++) copies[i] = ReadableCopy(glyphs[i].sprite);
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var uv = sheet.PackTextures(copies, 2, 4096, false);
            sheet.Apply(false, false);
            sheet.name = Path.GetFileNameWithoutExtension(outputPath) + " Atlas";
            foreach (var c in copies) Object.DestroyImmediate(c);

            var texW = sheet.width;
            var texH = sheet.height;

            // 2) Build glyph + character tables (GlyphRect origin is bottom-left, matching UV).
            var glyphTable = new List<TMP_SpriteGlyph>(glyphs.Count);
            var charTable = new List<TMP_SpriteCharacter>(glyphs.Count);
            for (var i = 0; i < glyphs.Count; i++)
            {
                var r = uv[i];
                int x = Mathf.RoundToInt(r.x * texW), y = Mathf.RoundToInt(r.y * texH);
                int w = Mathf.RoundToInt(r.width * texW), h = Mathf.RoundToInt(r.height * texH);
                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new GlyphMetrics(w, h, 0f, h, w),     // bearingX 0, bearingY h (baseline-sit), advance w
                    new GlyphRect(x, y, w, h),
                    1.0f, 0)
                { sprite = glyphs[i].sprite };
                glyphTable.Add(glyph);

                charTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph)
                {
                    name = glyphs[i].name,
                    glyphIndex = (uint)i,
                });
            }

            // 3) Assemble the asset (+ material). Overwrite in place: delete the old file so
            //    stale sub-assets don't accumulate, then re-point TMP default (handles GUID).
            if (AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(outputPath) != null)
                AssetDatabase.DeleteAsset(outputPath);
            EnsureFolder(Path.GetDirectoryName(outputPath).Replace('\\', '/'));

            var spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            spriteAsset.name = Path.GetFileNameWithoutExtension(outputPath);
            SetVersion(spriteAsset, "1.1.0");        // TMP_Asset.version setter is internal
            spriteAsset.spriteSheet = sheet;
            // spriteGlyphTable / spriteCharacterTable have internal setters; their getters
            // expose the live backing lists, so fill those in place.
            spriteAsset.spriteGlyphTable.Clear();
            spriteAsset.spriteGlyphTable.AddRange(glyphTable);
            spriteAsset.spriteCharacterTable.Clear();
            spriteAsset.spriteCharacterTable.AddRange(charTable);

            var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { name = spriteAsset.name + " Material" };
            mat.SetTexture(ShaderUtilities.ID_MainTex, sheet);
            spriteAsset.material = mat;

            AssetDatabase.CreateAsset(spriteAsset, outputPath);
            AssetDatabase.AddObjectToAsset(sheet, spriteAsset);
            AssetDatabase.AddObjectToAsset(mat, spriteAsset);
            spriteAsset.UpdateLookupTables();
            EditorUtility.SetDirty(spriteAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(outputPath);

            // 4) Wire as the global default sprite asset.
            SetDefaultSpriteAsset(spriteAsset);
            return spriteAsset;
        }

        private static Texture2D ReadableCopy(Sprite sprite)
        {
            var tex = sprite.texture;
            var rect = sprite.textureRect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        // TMP_Asset.version has an internal setter, so set the backing field via SerializedObject.
        private static void SetVersion(TMP_SpriteAsset asset, string version)
        {
            var so = new SerializedObject(asset);
            var prop = so.FindProperty("m_Version");
            if (prop != null) { prop.stringValue = version; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        private static void SetDefaultSpriteAsset(TMP_SpriteAsset asset)
        {
            var settings = TMP_Settings.instance;
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultSpriteAsset");
            if (prop != null) { prop.objectReferenceValue = asset; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
