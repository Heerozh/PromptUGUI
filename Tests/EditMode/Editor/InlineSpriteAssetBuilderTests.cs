using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class InlineSpriteAssetBuilderTests
    {
        [Test]
        public void GenerateTmpSpriteAsset_defaults_false_and_reflects_serialized_value()
        {
            var set = ScriptableObject.CreateInstance<SpriteSet>();
            try
            {
                Assert.IsFalse(set.GenerateTmpSpriteAsset, "default must be false");

                var so = new SerializedObject(set);
                so.FindProperty("generateTmpSpriteAsset").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(set.GenerateTmpSpriteAsset);
            }
            finally { Object.DestroyImmediate(set); }
        }

        private static Sprite Dummy()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4f);
        }

        [Test]
        public void BuildInlineGlyphTable_merges_distinct_names_across_sets()
        {
            var candidates = new List<(string set, string name, Sprite sprite)>
            {
                ("ui", "coin", Dummy()),
                ("emoji", "smile", Dummy()),
            };

            var glyphs = InlineSpriteAssetBuilder.BuildInlineGlyphTable(candidates, out var collisions);

            Assert.IsEmpty(collisions);
            CollectionAssert.AreEquivalent(
                new[] { "coin", "smile" }, glyphs.ConvertAll(g => g.name));
        }

        [Test]
        public void BuildInlineGlyphTable_reports_cross_set_name_collision()
        {
            var candidates = new List<(string set, string name, Sprite sprite)>
            {
                ("ui", "heart", Dummy()),
                ("emoji", "heart", Dummy()),
            };

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var glyphs = InlineSpriteAssetBuilder.BuildInlineGlyphTable(candidates, out var collisions);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.That(collisions, Does.Contain("heart"));
            Assert.IsEmpty(glyphs, "no glyphs emitted when a collision aborts the merge");
        }

        // ── CollectCandidates tests ─────────────────────────────────────────────

        private const string TestRoot = "Assets/__test_inlinesprite__";
        private readonly List<string> _cleanup = new List<string>();
        private TMPro.TMP_SpriteAsset _origDefaultSpriteAsset;

        [SetUp]
        public void SetUp()
        {
            _origDefaultSpriteAsset = TMPro.TMP_Settings.defaultSpriteAsset;
        }

        [TearDown]
        public void Teardown()
        {
            // Generate() rewrites the HOST project's TMP default sprite asset. Restore the
            // captured original before deleting the temp asset so nothing is left referencing
            // a deleted asset (harmless no-op for the pure tests that never call Generate).
            var settings = TMPro.TMP_Settings.instance;
            if (settings != null)
            {
                var so = new UnityEditor.SerializedObject(settings);
                var prop = so.FindProperty("m_defaultSpriteAsset");
                if (prop != null)
                {
                    prop.objectReferenceValue = _origDefaultSpriteAsset;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            foreach (var p in _cleanup) AssetDatabase.DeleteAsset(p);
            _cleanup.Clear();
            if (AssetDatabase.IsValidFolder(TestRoot)) AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.SaveAssets();
        }

        private string WriteSpritePng(string folder, string name)
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color32[64];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 0, 255);
            tex.SetPixels32(px); tex.Apply();
            var path = $"{folder}/{name}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.SaveAndReimport();
            Object.DestroyImmediate(tex);
            return path;
        }

        private SpriteSet MakeFlaggedSet(string setName, string folderPath)
        {
            var set = ScriptableObject.CreateInstance<SpriteSet>();
            var so = new SerializedObject(set);
            so.FindProperty("setName").stringValue = setName;
            so.FindProperty("generateTmpSpriteAsset").boolValue = true;
            so.FindProperty("sourceFolder").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            var setPath = $"{folderPath}/{setName}.asset";
            AssetDatabase.CreateAsset(set, setPath);
            _cleanup.Add(setPath);
            return set;
        }

        [Test]
        public void CollectCandidates_returns_bare_names_from_flagged_set()
        {
            AssetDatabase.CreateFolder("Assets", "__test_inlinesprite__");
            WriteSpritePng(TestRoot, "coin");
            WriteSpritePng(TestRoot, "smile");
            var set = MakeFlaggedSet("ui", TestRoot);

            var candidates = InlineSpriteAssetBuilder.CollectCandidates(new[] { set });
            var names = candidates.ConvertAll(c => c.name);

            CollectionAssert.AreEquivalent(new[] { "coin", "smile" }, names);
            Assert.That(candidates.TrueForAll(c => c.set == "ui"));
        }

        // ── Generate integration tests ──────────────────────────────────────────

        [Test]
        public void Generate_creates_sprite_asset_with_expected_characters_and_sets_tmp_default()
        {
            AssetDatabase.CreateFolder("Assets", "__test_inlinesprite__");
            WriteSpritePng(TestRoot, "coin");
            WriteSpritePng(TestRoot, "smile");
            var set = MakeFlaggedSet("ui", TestRoot);
            var outPath = $"{TestRoot}/InlineSprites.asset";
            _cleanup.Add(outPath);

            var asset = InlineSpriteAssetBuilder.Generate(new[] { set }, outPath);

            Assert.IsNotNull(asset);
            var names = new List<string>();
            foreach (var ch in asset.spriteCharacterTable) names.Add(ch.name);
            CollectionAssert.AreEquivalent(new[] { "coin", "smile" }, names);
            Assert.AreEqual(asset.spriteCharacterTable.Count, asset.spriteGlyphTable.Count);
            Assert.IsNotNull(asset.spriteSheet, "must have a packed texture");
            Assert.AreSame(asset, TMPro.TMP_Settings.defaultSpriteAsset);
        }

        [Test]
        public void Generate_returns_null_when_no_glyphs()
        {
            Assert.IsNull(InlineSpriteAssetBuilder.Generate(
                new PromptUGUI.Application.SpriteSet[0], $"{TestRoot}/none.asset"));
        }

        // ── In-place reuse (no churn in git) ───────────────────────────────────

        /// <summary>Every object in the asset, tagged with its local file ID — that ID is what
        /// `spriteSheet:` / `m_Material:` serialize as, so re-anchoring the sub-assets on each
        /// rebuild is exactly what churns the file in git.</summary>
        private static List<string> SubAssetIds(string path)
        {
            var ids = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out _, out long localId);
                ids.Add($"{o.GetType().Name}:{localId}");
            }
            ids.Sort(System.StringComparer.Ordinal);
            return ids;
        }

        private SpriteSet TwoGlyphSet(out string outPath)
        {
            AssetDatabase.CreateFolder("Assets", "__test_inlinesprite__");
            WriteSpritePng(TestRoot, "coin");
            WriteSpritePng(TestRoot, "smile");
            outPath = $"{TestRoot}/InlineSprites.asset";
            _cleanup.Add(outPath);
            return MakeFlaggedSet("ui", TestRoot);
        }

        [Test]
        public void Generate_twice_keeps_sub_asset_local_file_ids_stable()
        {
            var set = TwoGlyphSet(out var outPath);

            Assert.IsNotNull(InlineSpriteAssetBuilder.Generate(new[] { set }, outPath));
            var before = SubAssetIds(outPath);
            Assert.AreEqual(3, before.Count,
                "fixture: the asset must hold exactly sprite asset + atlas texture + material");

            Assert.IsNotNull(InlineSpriteAssetBuilder.Generate(new[] { set }, outPath));
            var after = SubAssetIds(outPath);

            CollectionAssert.AreEqual(before, after,
                "a rebuild from unchanged sources must reuse the sub-assets, not re-anchor them");
        }

        [Test]
        public void Generate_twice_leaves_the_asset_file_byte_identical()
        {
            var set = TwoGlyphSet(out var outPath);

            InlineSpriteAssetBuilder.Generate(new[] { set }, outPath);
            var before = System.IO.File.ReadAllBytes(outPath);
            Assert.Greater(before.Length, 0, "fixture: the asset must have been written to disk");

            InlineSpriteAssetBuilder.Generate(new[] { set }, outPath);
            var after = System.IO.File.ReadAllBytes(outPath);

            CollectionAssert.AreEqual(before, after,
                "rebuilding unchanged sources must not produce a git diff");
        }

        [Test]
        public void Generate_rebuilds_from_scratch_when_the_existing_asset_has_a_foreign_shape()
        {
            var set = TwoGlyphSet(out var outPath);

            // A plain ScriptableObject squatting on the output path: not a TMP_SpriteAsset,
            // and carrying no sub-assets.
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SpriteSet>(), outPath);

            var asset = InlineSpriteAssetBuilder.Generate(new[] { set }, outPath);

            Assert.IsNotNull(asset, "an unusable existing asset must be replaced, not aborted on");
            Assert.AreEqual(3, SubAssetIds(outPath).Count);
        }
    }
}
