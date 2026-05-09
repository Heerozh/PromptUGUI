using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Editor
{
    public class IconAtlasSyncerTests
    {
        private const string TestRoot = "Assets/__test_iconsync__";
        private readonly List<string> _toCleanup = new();

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.CreateFolder("Assets", "__test_iconsync__");
        }

        [TearDown]
        public void Teardown()
        {
            foreach (var p in _toCleanup) AssetDatabase.DeleteAsset(p);
            _toCleanup.Clear();
            AssetDatabase.DeleteAsset(TestRoot);
        }

        [Test]
        public void Scan_finds_icon_refs_in_ui_xml()
        {
            var path = $"{TestRoot}/sample.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon name='ui:settings'/>
                      <Icon name='art:gold-coin'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.That(refs, Does.Contain(("ui", "settings")));
            Assert.That(refs, Does.Contain(("art", "gold-coin")));
        }

        [Test]
        public void Scan_skips_malformed_xml()
        {
            var path = $"{TestRoot}/dyn.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon name='ui:{{kind}}'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            // The parser rejects 'ui:{{kind}}' (invalid icon name pattern).
            // ScanXmlReferences logs warning and skips the file; no entries from it.
            LogAssert.ignoreFailingMessages = true;
            var refs = IconAtlasSyncer.ScanXmlReferences();
            LogAssert.ignoreFailingMessages = false;
            foreach (var (ns, _) in refs) Assert.AreNotEqual("ui", ns);
        }

        [Test]
        public void Scan_picks_up_variant_overrides()
        {
            var path = $"{TestRoot}/variant.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon name='ui:sun' name.dark='ui:moon'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.That(refs, Does.Contain(("ui", "sun")));
            Assert.That(refs, Does.Contain(("ui", "moon")));
        }

        [Test]
        public void EnumeratePngs_returns_dict_keyed_by_filename()
        {
            var folder = $"{TestRoot}/icons";
            AssetDatabase.CreateFolder(TestRoot, "icons");
            var pngPath = $"{folder}/foo.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            // Force import as Sprite synchronously before calling EnumeratePngs
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            var dict = IconAtlasSyncer.EnumeratePngs(folder);
            Assert.IsTrue(dict.ContainsKey("foo"),
                $"Expected 'foo' in dict; keys: {string.Join(", ", dict.Keys)}");
        }

        [Test]
        public void EnumeratePngs_forces_sprite_single_on_default_texture()
        {
            var folder = $"{TestRoot}/icons_default";
            AssetDatabase.CreateFolder(TestRoot, "icons_default");
            var pngPath = $"{folder}/baz.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.IsNotNull(importer);
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();

            IconAtlasSyncer.EnumeratePngs(folder);

            var after = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.AreEqual(TextureImporterType.Sprite, after.textureType);
            Assert.AreEqual(SpriteImportMode.Single, after.spriteImportMode);
        }

        [Test]
        public void EnumeratePngs_leaves_existing_sprite_importer_untouched()
        {
            var folder = $"{TestRoot}/icons_multi";
            AssetDatabase.CreateFolder(TestRoot, "icons_multi");
            var pngPath = $"{folder}/sheet.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.IsNotNull(importer);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.SaveAndReimport();

            IconAtlasSyncer.EnumeratePngs(folder);

            var after = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.AreEqual(TextureImporterType.Sprite, after.textureType);
            Assert.AreEqual(SpriteImportMode.Multiple, after.spriteImportMode,
                "Author-configured Multiple sheet must not be silently flipped to Single");
        }

        [Test]
        public void UpdateAtlas_v2_does_not_accumulate_packables_on_repeated_sync()
        {
            var folder = $"{TestRoot}/v2";
            AssetDatabase.CreateFolder(TestRoot, "v2");

            // Bootstrap a V2 atlas asset on disk via AssetDatabase.CreateAsset.
            // Unity logs an error about CreateAsset for .spriteatlasv2 but it does
            // produce a usable file; LogAssert.Expect absorbs that diagnostic.
            var atlasPath = $"{folder}/test.spriteatlasv2";
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("CreateAsset.*spriteatlasv2"));
            var v2 = new SpriteAtlasAsset();
            AssetDatabase.CreateAsset(v2, atlasPath);
            AssetDatabase.SaveAssets();
            _toCleanup.Add(atlasPath);

            // Two distinct sprite assets so we can verify the second sync replaces, not appends.
            var pngA = $"{folder}/a.png";
            File.WriteAllBytes(pngA, MakeBlankPng());
            ImportAsSprite(pngA);
            _toCleanup.Add(pngA);
            var spriteA = AssetDatabase.LoadAssetAtPath<Sprite>(pngA);

            var pngB = $"{folder}/b.png";
            File.WriteAllBytes(pngB, MakeBlankPng());
            ImportAsSprite(pngB);
            _toCleanup.Add(pngB);
            var spriteB = AssetDatabase.LoadAssetAtPath<Sprite>(pngB);

            var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(atlasPath);
            Assume.That(atlas, Is.Not.Null, "V2 atlas bootstrap failed; cannot run test");

            IconAtlasSyncer.UpdateAtlas(atlas, new[] { spriteA });
            IconAtlasSyncer.UpdateAtlas(atlas, new[] { spriteB });
            AssetDatabase.SaveAssets();

            // Read packables directly from the V2 asset's serialized data — master's
            // runtime view doesn't reflect the V2 input list.
            var reloaded = SpriteAtlasAsset.Load(atlasPath);
            var so = new SerializedObject(reloaded);
            var prop = so.FindProperty("m_ImporterData.packables");
            Assert.IsNotNull(prop, "V2 packables property path changed; test fixture stale");
            Assert.AreEqual(1, prop.arraySize,
                $"V2 atlas should have exactly 1 packable after two syncs, got {prop.arraySize}");
        }

        private void ImportAsSprite(string pngPath)
        {
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        [Test]
        public void SyncAll_aborts_on_duplicate_setname()
        {
            var a = MakeIconSetAsset("a", "ui");
            var b = MakeIconSetAsset("b", "ui");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("duplicate IconSet"));
            IconAtlasSyncer.SyncAll(new[] { a, b });
        }

        // ---- helpers ----

        private IconSet MakeIconSetAsset(string fileName, string setName)
        {
            var s = ScriptableObject.CreateInstance<IconSet>();
            var so = new SerializedObject(s);
            so.FindProperty("setName").stringValue = setName;
            so.ApplyModifiedProperties();
            var path = $"{TestRoot}/{fileName}.asset";
            AssetDatabase.CreateAsset(s, path);
            _toCleanup.Add(path);
            return AssetDatabase.LoadAssetAtPath<IconSet>(path);
        }

        private byte[] MakeBlankPng()
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            var bytes = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return bytes;
        }
    }
}
