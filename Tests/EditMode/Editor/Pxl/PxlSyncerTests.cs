using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlSyncerTests
    {
        private const string TestRoot = "Assets/__test_pxlsync__";

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.CreateFolder("Assets", "__test_pxlsync__");
        }

        [TearDown]
        public void Teardown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
        }

        private static void WritePxl(string relPath, string content)
        {
            var abs = Path.Combine(UnityEngine.Application.dataPath, "__test_pxlsync__",
                relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, content);
        }

        private static void ImportAll() =>
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        private const string SinglePxl = "chars:\n  K: #000000\ngrid:\n  K\n";
        private const string MultiPxl =
            "chars:\n  K: #000000\n  W: #ffffff\n[normal]\ngrid:\n  K\n[pressed]\ngrid:\n  W\n";

        [Test]
        public void Enumerate_single_section_key_is_pathkey()
        {
            WritePxl("icon.pxl", SinglePxl);
            ImportAll();
            var entries = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("icon", entries[0].pathKey);
        }

        [Test]
        public void Enumerate_multi_section_keys_append_section_name()
        {
            WritePxl("Buttons/ok.pxl", MultiPxl);
            ImportAll();
            var keys = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot)
                .Select(e => e.pathKey).OrderBy(k => k).ToArray();
            Assert.AreEqual(new[] { "Buttons/ok/normal", "Buttons/ok/pressed" }, keys);
        }

        [Test]
        public void Enumerate_mixed_png_and_pxl()
        {
            WritePxl("a.pxl", SinglePxl);
            var png = new Texture2D(1, 1);
            png.SetPixel(0, 0, Color.red);
            File.WriteAllBytes(
                Path.Combine(UnityEngine.Application.dataPath, "__test_pxlsync__", "b.png"),
                png.EncodeToPNG());
            Object.DestroyImmediate(png);
            ImportAll();
            var keys = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot)
                .Select(e => e.pathKey).OrderBy(k => k).ToArray();
            Assert.AreEqual(new[] { "a", "b" }, keys);
        }

        [Test]
        public void BuildLookup_promotes_unique_bare_alias_for_section_key()
        {
            WritePxl("Buttons/ok.pxl", MultiPxl);
            ImportAll();
            var entries = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot);
            var lookup = SpriteAtlasSyncer.BuildLookup(entries, out _);
            Assert.IsTrue(lookup.ContainsKey("Buttons/ok/pressed"));
            Assert.IsTrue(lookup.ContainsKey("pressed"), "unique bare alias promoted");
        }

        [Test]
        public void ResetTextureImportSettings_skips_pxl()
        {
            WritePxl("icon.pxl", SinglePxl);
            ImportAll();
            Assert.AreEqual(0, SpriteAtlasSyncer.ResetTextureImportSettings(TestRoot),
                ".pxl has no TextureImporter; reset must skip it");
        }

        [Test]
        public void EnumerateSpriteSources_collects_tiled_sprites()
        {
            WritePxl("vineframe.pxl",
                "chars:\n  K: #000000\n\n[vine]\nborder: 1,1,1,1\ntiled: true\ngrid:\n  KKK\n  KKK\n  KKK\n\n[flat]\ngrid:\n  KK\n  KK\n");
            ImportAll();
            var tiled = new HashSet<Sprite>();
            var entries = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot, null, tiled);
            var vine = entries.Single(e => e.pathKey == "vineframe/vine").sprite;
            var flat = entries.Single(e => e.pathKey == "vineframe/flat").sprite;
            Assert.IsTrue(tiled.Contains(vine));
            Assert.IsFalse(tiled.Contains(flat));
        }

        [Test]
        public void EnsureAtlas_pxl_only_folder_gets_point_filter()
        {
            WritePxl("icon.pxl", SinglePxl);
            ImportAll();
            var set = ScriptableObject.CreateInstance<PromptUGUI.Application.SpriteSet>();
            var so = new SerializedObject(set);
            so.FindProperty("setName").stringValue = "pxltest";
            so.FindProperty("sourceFolder").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(TestRoot);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(set, $"{TestRoot}/pxltest.asset");

            var atlas = SpriteAtlasSyncer.EnsureAtlasAsset(set);
            Assert.IsNotNull(atlas);
            Assert.AreEqual(FilterMode.Point, atlas.GetTextureSettings().filterMode);
        }
    }
}
