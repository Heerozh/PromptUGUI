using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlPngExporterTests
    {
        [Test]
        public void FileNameFor_explicit_and_implicit_sections()
        {
            Assert.AreEqual("ok.pressed.png",
                PxlPngExporter.FileNameFor("ok", new PxlSection { Name = "pressed" }));
            Assert.AreEqual("ok.png",
                PxlPngExporter.FileNameFor("ok", new PxlSection { Name = null }));
        }

        [Test]
        public void EncodeSection_roundtrips_pixels()
        {
            var doc = PxlParser.Parse("chars:\n  K: #102030\ngrid:\n  K.\n  .K\n");
            var colors = PxlColorResolver.Resolve(doc, null);
            var bytes = PxlPngExporter.EncodeSection(doc.Sections[0], colors);

            var tex = new Texture2D(2, 2);
            Assert.IsTrue(ImageConversion.LoadImage(tex, bytes));
            // grid 第 1 行是顶行 → texture y=1
            Assert.AreEqual(new Color32(0x10, 0x20, 0x30, 255), (Color32)tex.GetPixel(0, 1));
            Assert.AreEqual(0, ((Color32)tex.GetPixel(1, 1)).a);
            Assert.AreEqual(new Color32(0x10, 0x20, 0x30, 255), (Color32)tex.GetPixel(1, 0));
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void IsUnderAnySpriteSetSourceFolder_detects_member_folder()
        {
            const string root = "Assets/__test_pxlexport__";
            if (!AssetDatabase.IsValidFolder(root))
                AssetDatabase.CreateFolder("Assets", "__test_pxlexport__");
            try
            {
                AssetDatabase.CreateFolder(root, "Icons");
                var set = ScriptableObject.CreateInstance<PromptUGUI.Application.SpriteSet>();
                var so = new SerializedObject(set);
                so.FindProperty("setName").stringValue = "exporttest";
                so.FindProperty("sourceFolder").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(root + "/Icons");
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(set, root + "/exporttest.asset");

                Assert.IsTrue(PxlPngExporter.IsUnderAnySpriteSetSourceFolder(root + "/Icons"));
                Assert.IsTrue(PxlPngExporter.IsUnderAnySpriteSetSourceFolder(root + "/Icons/Sub"));
                Assert.IsFalse(PxlPngExporter.IsUnderAnySpriteSetSourceFolder(root));
                Assert.IsFalse(PxlPngExporter.IsUnderAnySpriteSetSourceFolder("Assets/Nowhere"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }
    }
}
