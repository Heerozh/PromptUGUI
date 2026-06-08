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

        [TearDown]
        public void Teardown()
        {
            foreach (var p in _cleanup) AssetDatabase.DeleteAsset(p);
            _cleanup.Clear();
            if (AssetDatabase.IsValidFolder(TestRoot)) AssetDatabase.DeleteAsset(TestRoot);
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
    }
}
