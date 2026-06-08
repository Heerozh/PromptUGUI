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
    }
}
