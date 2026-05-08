using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Editor {
    public class IconAtlasSyncerTests {
        const string TestRoot = "Assets/__test_iconsync__";
        readonly List<string> _toCleanup = new();

        [SetUp] public void Setup() {
            if (!AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.CreateFolder("Assets", "__test_iconsync__");
        }

        [TearDown] public void Teardown() {
            foreach (var p in _toCleanup) AssetDatabase.DeleteAsset(p);
            _toCleanup.Clear();
            AssetDatabase.DeleteAsset(TestRoot);
        }

        [Test]
        public void Scan_finds_icon_refs_in_ui_xml() {
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
        public void Scan_skips_malformed_xml() {
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
        public void Scan_picks_up_variant_overrides() {
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
        public void EnumeratePngs_returns_dict_keyed_by_filename() {
            var folder = $"{TestRoot}/icons";
            AssetDatabase.CreateFolder(TestRoot, "icons");
            var pngPath = $"{folder}/foo.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            // Force import as Sprite synchronously before calling EnumeratePngs
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer != null) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            var dict = IconAtlasSyncer.EnumeratePngs(folder);
            Assert.IsTrue(dict.ContainsKey("foo"),
                $"Expected 'foo' in dict; keys: {string.Join(", ", dict.Keys)}");
        }

        [Test]
        public void SyncAll_aborts_on_duplicate_setname() {
            var a = MakeIconSetAsset("a", "ui");
            var b = MakeIconSetAsset("b", "ui");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("duplicate IconSet"));
            IconAtlasSyncer.SyncAll(new[] { a, b });
        }

        // ---- helpers ----

        IconSet MakeIconSetAsset(string fileName, string setName) {
            var s = ScriptableObject.CreateInstance<IconSet>();
            var so = new SerializedObject(s);
            so.FindProperty("setName").stringValue = setName;
            so.ApplyModifiedProperties();
            var path = $"{TestRoot}/{fileName}.asset";
            AssetDatabase.CreateAsset(s, path);
            _toCleanup.Add(path);
            return AssetDatabase.LoadAssetAtPath<IconSet>(path);
        }

        byte[] MakeBlankPng() {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            var bytes = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return bytes;
        }
    }
}
