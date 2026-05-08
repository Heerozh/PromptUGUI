#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode {
    public class IconRuntimeTests {
        const string TmpRoot = "Assets/__test_iconruntime__";
        readonly List<string> _toCleanup = new();

        [SetUp] public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
            if (!AssetDatabase.IsValidFolder(TmpRoot))
                AssetDatabase.CreateFolder("Assets", "__test_iconruntime__");
        }

        [TearDown] public void Teardown() {
            UI.ResetForTests();
            foreach (var p in _toCleanup) AssetDatabase.DeleteAsset(p);
            _toCleanup.Clear();
            if (AssetDatabase.IsValidFolder(TmpRoot)) AssetDatabase.DeleteAsset(TmpRoot);
        }

        [Test]
        public void Icon_resolves_sprite_from_atlas() {
            var (set, _) = MakeIconSetWithSprite("ui", "settings");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Icon id='cog' name='ui:settings'/></Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var icon = screen.Get<Icon>("cog");
            var img = icon.GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(img.sprite);
        }

        [Test]
        public void Icon_unknown_name_logs_error_sprite_null() {
            var (set, _) = MakeIconSetWithSprite("ui", "settings");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("resolver returned null"));

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Icon id='x' name='ui:nope'/></Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var icon = screen.Get<Icon>("x");
            Assert.IsNull(icon.GameObject.GetComponent<UnityEngine.UI.Image>().sprite);
        }

        [Test]
        public void Icon_color_applied() {
            var (set, _) = MakeIconSetWithSprite("ui", "x");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Icon id='i' name='ui:x' color='#ff0000'/></Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var img = screen.Get<Icon>("i").GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(Color.red, img.color);
        }

        [Test]
        public void Variant_swap_changes_sprite() {
            var (set, _) = MakeIconSetWithSpritesMulti("ui",
                new[] { "sun", "moon" });
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { set });

            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'>
                      <Icon id='i' name='ui:sun' name.dark='ui:moon'/>
                    </Screen>
                  </PromptUGUI>");
            var screen = UI.Open("S");
            var img = screen.Get<Icon>("i").GameObject.GetComponent<UnityEngine.UI.Image>();
            var before = img.sprite;
            Assert.IsNotNull(before);
            UI.Variants.Set("dark", true);
            var after = img.sprite;
            Assert.IsNotNull(after);
            Assert.AreNotSame(before, after);
        }

        // ---- helpers ----

        (IconSet set, SpriteAtlas atlas) MakeIconSetWithSprite(string setName, string iconName) {
            return MakeIconSetWithSpritesMulti(setName, new[] { iconName });
        }

        (IconSet set, SpriteAtlas atlas) MakeIconSetWithSpritesMulti(
            string setName, string[] iconNames) {
            var folder = $"{TmpRoot}/{setName}";
            AssetDatabase.CreateFolder(TmpRoot, setName);
            var sprites = new List<Sprite>();
            foreach (var n in iconNames) {
                var pngPath = $"{folder}/{n}.png";
                System.IO.File.WriteAllBytes(pngPath, MakeBlankPng());
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(pngPath));
            }

            var atlas = new SpriteAtlas();
            var atlasPath = $"{TmpRoot}/{setName}.spriteatlas";
            AssetDatabase.CreateAsset(atlas, atlasPath);
            var spriteObjects = new UnityEngine.Object[sprites.Count];
            for (int i = 0; i < sprites.Count; i++) spriteObjects[i] = sprites[i];
            atlas.Add(spriteObjects);
            EditorUtility.SetDirty(atlas);
            UnityEditor.U2D.SpriteAtlasUtility.PackAtlases(
                new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
            _toCleanup.Add(atlasPath);

            var set = ScriptableObject.CreateInstance<IconSet>();
            var so = new SerializedObject(set);
            so.FindProperty("setName").stringValue = setName;
            so.FindProperty("atlas").objectReferenceValue = atlas;
            so.ApplyModifiedProperties();
            var setPath = $"{TmpRoot}/{setName}.asset";
            AssetDatabase.CreateAsset(set, setPath);
            _toCleanup.Add(setPath);
            return (AssetDatabase.LoadAssetAtPath<IconSet>(setPath), atlas);
        }

        byte[] MakeBlankPng() {
            var t = new Texture2D(8, 8);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    t.SetPixel(x, y, Color.white);
            t.Apply();
            var bytes = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return bytes;
        }
    }
}
#endif
