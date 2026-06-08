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
    }
}
