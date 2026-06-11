using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Editor
{
    public class PxlImporterTests
    {
        private const string TmpDir = "Assets/__test_pxl__";

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(TmpDir))
                AssetDatabase.CreateFolder("Assets", "__test_pxl__");
        }

        [TearDown]
        public void Teardown()
        {
            AssetDatabase.DeleteAsset(TmpDir);
        }

        private static string Write(string fileName, string content)
        {
            var abs = Path.Combine(UnityEngine.Application.dataPath, "__test_pxl__", fileName);
            File.WriteAllText(abs, content);
            var assetPath = $"{TmpDir}/{fileName}";
            AssetDatabase.ImportAsset(assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        [Test]
        public void Import_single_implicit_section_pixels_and_filter()
        {
            var path = Write("dot.pxl", "chars:\n  K: #102030\ngrid:\n  K.\n  ..\n");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(tex);
            Assert.AreEqual("dot", tex.name);
            Assert.AreEqual(FilterMode.Point, tex.filterMode);
            // grid 第 1 行是顶行 → texture y=1（bottom-up 翻转）
            Assert.AreEqual(new Color32(0x10, 0x20, 0x30, 255), (Color32)tex.GetPixel(0, 1));
            Assert.AreEqual(0, ((Color32)tex.GetPixel(1, 1)).a);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsNotNull(sprite);
            Assert.AreEqual("dot", sprite.name);
        }

        [Test]
        public void Import_border_and_ppu_land_on_sprite()
        {
            var path = Write("framed.pxl",
                "ppu: 16\nchars:\n  K: #000000\n[bg]\nborder: 1,1,1,1\ngrid:\n  KKK\n  KKK\n  KKK\n");
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            Assert.AreEqual("bg", sprite.name);
            Assert.AreEqual(new Vector4(1, 1, 1, 1), sprite.border);
            Assert.AreEqual(16f, sprite.pixelsPerUnit);
        }

        [Test]
        public void Import_multi_section_produces_sub_sprites_main_is_first_texture()
        {
            var path = Write("btn.pxl",
                "chars:\n  K: #000000\n  W: #ffffff\n" +
                "[normal]\ngrid:\n  KW\n[pressed]\ngrid:\n  WK\n");
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .Select(s => s.name).OrderBy(n => n).ToArray();
            Assert.AreEqual(new[] { "normal", "pressed" }, sprites);
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.IsInstanceOf<Texture2D>(main);
            // Unity 强制把 main object 改名为文件名（"btn"），所以按身份断言：
            // main 必须是首节 "normal" 那张贴图。
            var normal = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .Single(s => s.name == "normal");
            Assert.AreSame(main, normal.texture);
        }

        [Test]
        public void Import_palette_ref_resolves_and_offpalette_fails()
        {
            Write("main.gpl", "GIMP Palette\n26 28 44\tdark-blue\n");
            var ok = Write("ok.pxl", "palette: @main\nchars:\n  K: dark-blue\ngrid:\n  K\n");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(ok));

            LogAssert.ignoreFailingMessages = true;
            var bad = Write("bad.pxl", "palette: @main\nchars:\n  K: #010203\ngrid:\n  K\n");
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Sprite>(bad), "off-palette must fail import");
        }

        [Test]
        public void Import_gpl_edit_triggers_dependent_reimport()
        {
            Write("pal.gpl", "GIMP Palette\n255 0 0\tred\n");
            var path = Write("dep.pxl", "palette: @pal\nchars:\n  R: red\ngrid:\n  R\n");
            Assert.AreEqual(new Color32(255, 0, 0, 255),
                (Color32)AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixel(0, 0));

            // 改色板 → 依赖的 .pxl 自动重导入，颜色跟着变
            Write("pal.gpl", "GIMP Palette\n0 255 0\tred\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.AreEqual(new Color32(0, 255, 0, 255),
                (Color32)AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixel(0, 0));
        }

        [Test]
        public void Import_parse_error_fails_import_with_logged_error()
        {
            LogAssert.ignoreFailingMessages = true;
            var path = Write("broken.pxl", "chars:\n  K: #000000\ngrid:\n  KK\n  K\n");
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        [Test]
        public void Import_missing_palette_fails_import()
        {
            LogAssert.ignoreFailingMessages = true;
            var path = Write("orphan.pxl", "palette: @nosuch\nchars:\n  K: #000000\ngrid:\n  K\n");
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }
    }
}
