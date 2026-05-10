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
        public void Scan_skips_dynamic_icon_outside_template()
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

            // Parser now accepts '{{...}}' (Template substitution placeholder).
            // Outside a <Template>, no Param flow exists; syncer skips with a warning
            // — caller must list candidates in IconSet.alwaysInclude.
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
        public void Scan_follows_template_param_full_placeholder()
        {
            // <Icon name="{{iconName}}"/> — invocation arg is the full set:icon string.
            // Use TestSyncer_* prefixes so assertions aren't contaminated by real
            // .ui.xml files in the host project (the scan walks the whole project).
            var path = $"{TestRoot}/tpl_full.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='TestSyncerIconBtn'>
                      <Param name='iconName'/>
                      <Icon name='{{iconName}}'/>
                    </Template>
                    <Screen name='S'>
                      <TestSyncerIconBtn iconName='testset:TestSyncer_Foo'/>
                      <TestSyncerIconBtn iconName='testset:TestSyncer_Bar'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Foo")));
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Bar")));
        }

        [Test]
        public void Scan_follows_template_param_partial_placeholder()
        {
            // <Icon name="solar:{{x}}"/> — invocation arg is the icon-name half.
            // Don't use Param name 'id' — that's a reserved attribute consumed by
            // the parser into ElementNode.Id, not ElementNode.Attributes.
            var path = $"{TestRoot}/tpl_partial.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='TestSyncerSolar'>
                      <Param name='which'/>
                      <Icon name='testset:{{which}}'/>
                    </Template>
                    <Screen name='S'>
                      <TestSyncerSolar which='TestSyncer_Knife'/>
                      <TestSyncerSolar which='TestSyncer_Spoon'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Knife")));
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Spoon")));
        }

        [Test]
        public void Scan_follows_template_variant_param_placeholder()
        {
            // Param can drive a variant override (name.dark="{{x}}").
            var path = $"{TestRoot}/tpl_variant.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='TestSyncerThemeIcon'>
                      <Param name='dark'/>
                      <Icon name='testset:TestSyncer_Sun' name.dark='{{dark}}'/>
                    </Template>
                    <Screen name='S'>
                      <TestSyncerThemeIcon dark='testset:TestSyncer_Moon'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Sun")));
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Moon")));
        }

        [Test]
        public void Scan_uses_template_param_default_when_invocation_omits_arg()
        {
            // Param has default; invocation may omit the arg. Treat default as the
            // effective value so default-only templates still pack the right icon.
            var path = $"{TestRoot}/tpl_default.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='TestSyncerCoin'>
                      <Param name='kind' default='TestSyncer_Gold'/>
                      <Icon name='testset:{{kind}}'/>
                    </Template>
                    <Screen name='S'>
                      <TestSyncerCoin/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            var refs = IconAtlasSyncer.ScanXmlReferences();
            Assert.That(refs, Does.Contain(("testset", "TestSyncer_Gold")));
        }

        [Test]
        public void Scan_warns_on_forwarded_template_arg()
        {
            // Outer Template forwards its own Param into inner Template's Param —
            // syncer can't statically resolve the chain; warn and skip.
            var path = $"{TestRoot}/tpl_forward.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='TestSyncerInner'>
                      <Param name='i'/>
                      <Icon name='{{i}}'/>
                    </Template>
                    <Template name='TestSyncerOuter'>
                      <Param name='passthrough'/>
                      <TestSyncerInner i='{{passthrough}}'/>
                    </Template>
                    <Screen name='S'>
                      <TestSyncerOuter passthrough='testset:TestSyncer_Forwarded'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            LogAssert.ignoreFailingMessages = true;
            var refs = IconAtlasSyncer.ScanXmlReferences();
            LogAssert.ignoreFailingMessages = false;
            // Forwarded form is unanalyzable — must NOT silently include.
            Assert.IsFalse(refs.Contains(("testset", "TestSyncer_Forwarded")));
        }

        [Test]
        public void Scan_warns_on_complex_substitution_in_template_body()
        {
            // Multi-placeholder body shapes (e.g. "solar:{{a}}-{{b}}") aren't
            // covered by the simple full/partial recognizer — warn, no flow.
            var path = $"{TestRoot}/tpl_complex.ui.xml";
            File.WriteAllText(path,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='TestSyncerCombo'>
                      <Param name='a'/>
                      <Param name='b'/>
                      <Icon name='testset:{{a}}-{{b}}'/>
                    </Template>
                    <Screen name='S'>
                      <TestSyncerCombo a='TestSyncer_X' b='TestSyncer_Y'/>
                    </Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);

            LogAssert.ignoreFailingMessages = true;
            var refs = IconAtlasSyncer.ScanXmlReferences();
            LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(refs.Contains(("testset", "TestSyncer_X-TestSyncer_Y")));
        }

        [Test]
        public void EnumeratePngs_returns_pathkey_for_root_file()
        {
            var folder = $"{TestRoot}/icons";
            AssetDatabase.CreateFolder(TestRoot, "icons");
            var pngPath = $"{folder}/foo.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            var entries = IconAtlasSyncer.EnumeratePngs(folder);
            var keys = new List<string>();
            foreach (var (k, _) in entries) keys.Add(k);
            Assert.That(keys, Does.Contain("foo"),
                $"Expected 'foo' in entries; keys: {string.Join(", ", keys)}");
        }

        [Test]
        public void EnumeratePngs_returns_pathkey_with_subfolder()
        {
            // File at <folder>/UI/heart.png should produce key "UI/heart" (relative
            // path under sourceFolder, '/' separator, no extension).
            var folder = $"{TestRoot}/icons_sub";
            AssetDatabase.CreateFolder(TestRoot, "icons_sub");
            AssetDatabase.CreateFolder(folder, "UI");
            var pngPath = $"{folder}/UI/heart.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            ImportAsSprite(pngPath);

            var entries = IconAtlasSyncer.EnumeratePngs(folder);
            var keys = new List<string>();
            foreach (var (k, _) in entries) keys.Add(k);
            Assert.That(keys, Does.Contain("UI/heart"),
                $"Expected 'UI/heart' in entries; keys: {string.Join(", ", keys)}");
        }

        [Test]
        public void EnumeratePngs_returns_both_entries_on_basename_collision()
        {
            // Two PNGs sharing a basename in different subfolders must each appear
            // with their own pathKey — no first-wins, no warning.
            var folder = $"{TestRoot}/icons_dup";
            AssetDatabase.CreateFolder(TestRoot, "icons_dup");
            AssetDatabase.CreateFolder(folder, "UI");
            AssetDatabase.CreateFolder(folder, "Combat");
            var pngA = $"{folder}/UI/heart.png";
            var pngB = $"{folder}/Combat/heart.png";
            File.WriteAllBytes(pngA, MakeBlankPng());
            File.WriteAllBytes(pngB, MakeBlankPng());
            ImportAsSprite(pngA);
            ImportAsSprite(pngB);

            var entries = IconAtlasSyncer.EnumeratePngs(folder);
            var keys = new HashSet<string>();
            foreach (var (k, _) in entries) keys.Add(k);
            Assert.That(keys, Does.Contain("UI/heart"));
            Assert.That(keys, Does.Contain("Combat/heart"));
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
        public void EnumeratePngs_sets_uncompressed_on_default_texture()
        {
            // Repro for SpriteAtlas pack-time warning: "Source Texture (...) is using
            // compressed format. To ensure no loss in source pixel details when
            // packing to SpriteAtlas, please use uncompressed format in TextureImporter."
            // Default TextureImporter.textureCompression is Compressed; the syncer's
            // one-time Sprite conversion must also flip compression to Uncompressed.
            // (Existing Sprite importers are intentionally left alone — author choice.)
            var folder = $"{TestRoot}/icons_compress_default";
            AssetDatabase.CreateFolder(TestRoot, "icons_compress_default");
            var pngPath = $"{folder}/raw.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.IsNotNull(importer);
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();

            IconAtlasSyncer.EnumeratePngs(folder);

            var after = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.AreEqual(TextureImporterCompression.Uncompressed, after.textureCompression,
                "Fresh PNG conversion to Sprite must set Uncompressed to avoid atlas warning.");
        }

        [Test]
        public void ResetPngImportSettings_forces_canonical_format()
        {
            // Inspector "Reset All PNGs Format" button entry point. Unlike the
            // implicit-on-sync flow (which respects author tweaks on already-Sprite
            // imports), this is an explicit user-triggered force: every PNG in the
            // folder ends up Sprite + Single + Uncompressed, overriding prior config.
            var folder = $"{TestRoot}/icons_reset";
            AssetDatabase.CreateFolder(TestRoot, "icons_reset");
            var pngPath = $"{folder}/r.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.IsNotNull(importer);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();

            var n = IconAtlasSyncer.ResetPngImportSettings(folder);

            Assert.AreEqual(1, n);
            var after = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            Assert.AreEqual(TextureImporterType.Sprite, after.textureType);
            Assert.AreEqual(SpriteImportMode.Single, after.spriteImportMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, after.textureCompression);
        }

        [Test]
        public void ResetPngImportSettings_walks_subfolders()
        {
            // Mirrors EnumeratePngs: recursive over subfolders.
            var folder = $"{TestRoot}/icons_reset_sub";
            AssetDatabase.CreateFolder(TestRoot, "icons_reset_sub");
            AssetDatabase.CreateFolder(folder, "Sub");
            var rootPng = $"{folder}/root.png";
            var subPng = $"{folder}/Sub/leaf.png";
            File.WriteAllBytes(rootPng, MakeBlankPng());
            File.WriteAllBytes(subPng, MakeBlankPng());
            AssetDatabase.ImportAsset(rootPng, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(subPng, ImportAssetOptions.ForceUpdate);

            var n = IconAtlasSyncer.ResetPngImportSettings(folder);

            Assert.AreEqual(2, n);
        }

        [Test]
        public void ApplyImportSettingsToFolder_propagates_template_settings_to_others()
        {
            // Inspector "Apply to All PNGs" entry point: copy a chosen template PNG's
            // TextureImporter onto every other PNG in the folder, preserving whatever
            // settings the user dialed in via the embedded importer inspector.
            var folder = $"{TestRoot}/icons_apply";
            AssetDatabase.CreateFolder(TestRoot, "icons_apply");
            var templatePng = $"{folder}/template.png";
            var otherPng = $"{folder}/other.png";
            File.WriteAllBytes(templatePng, MakeBlankPng());
            File.WriteAllBytes(otherPng, MakeBlankPng());
            AssetDatabase.ImportAsset(templatePng, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(otherPng, ImportAssetOptions.ForceUpdate);

            var t = AssetImporter.GetAtPath(templatePng) as TextureImporter;
            Assert.IsNotNull(t);
            t.textureType = TextureImporterType.Sprite;
            t.spriteImportMode = SpriteImportMode.Single;
            t.textureCompression = TextureImporterCompression.Compressed;
            t.filterMode = FilterMode.Point;
            t.SaveAndReimport();

            var beforeOther = AssetImporter.GetAtPath(otherPng) as TextureImporter;
            Assert.IsNotNull(beforeOther);
            beforeOther.filterMode = FilterMode.Bilinear;
            beforeOther.textureCompression = TextureImporterCompression.Uncompressed;
            beforeOther.SaveAndReimport();

            var n = IconAtlasSyncer.ApplyImportSettingsToFolder(templatePng, folder);

            Assert.AreEqual(1, n, "Should have updated 1 PNG (excluding the template).");
            var after = AssetImporter.GetAtPath(otherPng) as TextureImporter;
            Assert.AreEqual(FilterMode.Point, after.filterMode);
            Assert.AreEqual(TextureImporterCompression.Compressed, after.textureCompression);
            Assert.AreEqual(TextureImporterType.Sprite, after.textureType);
        }

        [Test]
        public void ApplyImportSettingsToFolder_walks_subfolders()
        {
            var folder = $"{TestRoot}/icons_apply_sub";
            AssetDatabase.CreateFolder(TestRoot, "icons_apply_sub");
            AssetDatabase.CreateFolder(folder, "Sub");
            var template = $"{folder}/tpl.png";
            var leaf = $"{folder}/Sub/leaf.png";
            File.WriteAllBytes(template, MakeBlankPng());
            File.WriteAllBytes(leaf, MakeBlankPng());
            AssetDatabase.ImportAsset(template, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(leaf, ImportAssetOptions.ForceUpdate);
            var t = AssetImporter.GetAtPath(template) as TextureImporter;
            Assert.IsNotNull(t);
            t.textureType = TextureImporterType.Sprite;
            t.filterMode = FilterMode.Point;
            t.SaveAndReimport();

            var n = IconAtlasSyncer.ApplyImportSettingsToFolder(template, folder);

            Assert.AreEqual(1, n);
            var after = AssetImporter.GetAtPath(leaf) as TextureImporter;
            Assert.AreEqual(FilterMode.Point, after.filterMode);
        }

        [Test]
        public void ApplyImportSettingsToFolder_skips_template_itself()
        {
            var folder = $"{TestRoot}/icons_apply_skip";
            AssetDatabase.CreateFolder(TestRoot, "icons_apply_skip");
            var only = $"{folder}/sole.png";
            File.WriteAllBytes(only, MakeBlankPng());
            AssetDatabase.ImportAsset(only, ImportAssetOptions.ForceUpdate);
            var t = AssetImporter.GetAtPath(only) as TextureImporter;
            Assert.IsNotNull(t);
            t.textureType = TextureImporterType.Sprite;
            t.SaveAndReimport();

            var n = IconAtlasSyncer.ApplyImportSettingsToFolder(only, folder);

            Assert.AreEqual(0, n, "Template itself must not be counted.");
        }

        [Test]
        public void FindFirstPng_returns_alphabetically_first_under_folder()
        {
            var folder = $"{TestRoot}/icons_first";
            AssetDatabase.CreateFolder(TestRoot, "icons_first");
            var b = $"{folder}/b.png";
            var a = $"{folder}/a.png";
            File.WriteAllBytes(b, MakeBlankPng());
            File.WriteAllBytes(a, MakeBlankPng());
            AssetDatabase.ImportAsset(b, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(a, ImportAssetOptions.ForceUpdate);

            var first = IconAtlasSyncer.FindFirstPng(folder);

            Assert.AreEqual(a, first);
        }

        [Test]
        public void FindFirstPng_returns_null_for_empty_folder()
        {
            var folder = $"{TestRoot}/icons_empty";
            AssetDatabase.CreateFolder(TestRoot, "icons_empty");

            Assert.IsNull(IconAtlasSyncer.FindFirstPng(folder));
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

        [Test]
        public void SyncAll_persists_pathkey_entries_for_subfolder_files()
        {
            // XML uses 'ui:UI/heart' → IconSet.entries should contain key "UI/heart".
            var folder = MakeFolder("sub_a");
            AssetDatabase.CreateFolder(folder, "UI");
            var pngPath = $"{folder}/UI/heart.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            ImportAsSprite(pngPath);

            WriteXml("a.ui.xml", "<Icon name='ui:UI/heart'/>");
            var set = MakeIconSetAssetWithFolder("setA", "ui", folder);

            IconAtlasSyncer.SyncAll(new[] { set });

            var keys = new List<string>();
            foreach (var (k, _) in set.Entries) keys.Add(k);
            Assert.That(keys, Does.Contain("UI/heart"),
                $"Expected 'UI/heart' in entries; got: {string.Join(", ", keys)}");
        }

        [Test]
        public void SyncAll_persists_bare_alias_when_unique()
        {
            // Single 'UI/heart.png' (no collision) → both 'UI/heart' and 'heart' should
            // be in entries, so XML can reference either form.
            var folder = MakeFolder("sub_b");
            AssetDatabase.CreateFolder(folder, "UI");
            var pngPath = $"{folder}/UI/heart.png";
            File.WriteAllBytes(pngPath, MakeBlankPng());
            ImportAsSprite(pngPath);

            WriteXml("b.ui.xml", "<Icon name='ui:UI/heart'/>");
            var set = MakeIconSetAssetWithFolder("setB", "ui", folder);

            IconAtlasSyncer.SyncAll(new[] { set });

            var keys = new HashSet<string>();
            foreach (var (k, _) in set.Entries) keys.Add(k);
            Assert.That(keys, Does.Contain("UI/heart"));
            Assert.That(keys, Does.Contain("heart"),
                $"Bare alias 'heart' missing; got: {string.Join(", ", keys)}");
        }

        [Test]
        public void SyncAll_no_bare_alias_on_collision()
        {
            // Two heart.png files in different subfolders; XML references both via path
            // form. IconSet.entries should contain both pathKeys but NO bare 'heart'.
            var folder = MakeFolder("sub_c");
            AssetDatabase.CreateFolder(folder, "UI");
            AssetDatabase.CreateFolder(folder, "Combat");
            var pngA = $"{folder}/UI/heart.png";
            var pngB = $"{folder}/Combat/heart.png";
            File.WriteAllBytes(pngA, MakeBlankPng());
            File.WriteAllBytes(pngB, MakeBlankPng());
            ImportAsSprite(pngA);
            ImportAsSprite(pngB);

            WriteXml("c.ui.xml",
                "<Icon name='ui:UI/heart'/><Icon name='ui:Combat/heart'/>");
            var set = MakeIconSetAssetWithFolder("setC", "ui", folder);

            IconAtlasSyncer.SyncAll(new[] { set });

            var keys = new HashSet<string>();
            foreach (var (k, _) in set.Entries) keys.Add(k);
            Assert.That(keys, Does.Contain("UI/heart"));
            Assert.That(keys, Does.Contain("Combat/heart"));
            Assert.That(keys, Does.Not.Contain("heart"),
                $"Bare 'heart' must NOT alias when ambiguous; got: {string.Join(", ", keys)}");
        }

        [Test]
        public void SyncAll_errors_on_ambiguous_bare_reference()
        {
            // Two heart.png files; XML uses bare 'ui:heart' → must error and list candidates.
            var folder = MakeFolder("sub_d");
            AssetDatabase.CreateFolder(folder, "UI");
            AssetDatabase.CreateFolder(folder, "Combat");
            File.WriteAllBytes($"{folder}/UI/heart.png", MakeBlankPng());
            File.WriteAllBytes($"{folder}/Combat/heart.png", MakeBlankPng());
            ImportAsSprite($"{folder}/UI/heart.png");
            ImportAsSprite($"{folder}/Combat/heart.png");

            WriteXml("d.ui.xml", "<Icon name='ui:heart'/>");
            var set = MakeIconSetAssetWithFolder("setD", "ui", folder);

            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "ambiguous.*UI/heart.*Combat/heart|ambiguous.*Combat/heart.*UI/heart"));
            IconAtlasSyncer.SyncAll(new[] { set });
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

        private IconSet MakeIconSetAssetWithFolder(string fileName, string setName,
            string folderAssetPath)
        {
            var s = ScriptableObject.CreateInstance<IconSet>();
            var so = new SerializedObject(s);
            so.FindProperty("setName").stringValue = setName;
            so.FindProperty("sourceFolder").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderAssetPath);
            so.ApplyModifiedProperties();
            var path = $"{TestRoot}/{fileName}.asset";
            AssetDatabase.CreateAsset(s, path);
            _toCleanup.Add(path);
            return AssetDatabase.LoadAssetAtPath<IconSet>(path);
        }

        private string MakeFolder(string name)
        {
            AssetDatabase.CreateFolder(TestRoot, name);
            return $"{TestRoot}/{name}";
        }

        private void WriteXml(string fileName, string innerXml)
        {
            var path = $"{TestRoot}/{fileName}";
            File.WriteAllText(path,
                "<?xml version='1.0'?><PromptUGUI version='1'>" +
                "<Screen name='S_" + Path.GetFileNameWithoutExtension(fileName) + "'>" +
                innerXml + "</Screen></PromptUGUI>");
            AssetDatabase.ImportAsset(path);
            _toCleanup.Add(path);
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
