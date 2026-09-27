using System.IO;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>spec 2026-09-27-ui-xml-comment-stripping §3.1 / §3.3 / §5-11 ~ 15.</summary>
    public class UiXmlImporterTests
    {
        private const string Dir = "Assets/PromptUGUIUiXmlTmp";

        private const string Sample =
            "<PromptUGUI version=\"1\">\n" +
            "  <!-- header\n" +
            "       two lines -->\n" +
            "  <Screen name=\"A\">\n" +
            "    <Frame id=\"f\"/>\n" +
            "  </Screen>\n" +
            "</PromptUGUI>\n";

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(Dir);

        [TearDown]
        public void TearDown()
        {
            UiXmlImporterAssigner.PausedForTests = false;
            AssetDatabase.DeleteAsset(Dir);
            FileUtil.DeleteFileOrDirectory(Dir);
            FileUtil.DeleteFileOrDirectory(Dir + ".meta");
            AssetDatabase.Refresh();
        }

        [Test]
        public void A_new_ui_xml_is_taken_over_and_imports_without_comments()
        {
            var path = Write("a.ui.xml");

            Assert.AreEqual(typeof(UiXmlImporter), AssetDatabase.GetImporterOverride(path));
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.IsNotNull(asset);
            StringAssert.DoesNotContain("<!--", asset.text);
            Assert.AreEqual(LineCount(Sample), LineCount(asset.text));
            Assert.AreEqual("a.ui", asset.name);
        }

        [Test]
        public void A_plain_xml_stays_with_the_native_importer()
        {
            var path = Write("b.xml");

            Assert.IsNull(AssetDatabase.GetImporterOverride(path));
            StringAssert.Contains("<!--", AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
        }

        [Test]
        public void Renaming_to_xml_hands_the_file_back_and_renaming_to_ui_xml_takes_it_again()
        {
            var uiXml = Write("a.ui.xml");
            var xml = Dir + "/a.xml";

            Assert.AreEqual("", AssetDatabase.MoveAsset(uiXml, xml));
            Assert.IsNull(AssetDatabase.GetImporterOverride(xml));
            StringAssert.Contains("<!--", AssetDatabase.LoadAssetAtPath<TextAsset>(xml).text);

            Assert.AreEqual("", AssetDatabase.MoveAsset(xml, uiXml));
            Assert.AreEqual(typeof(UiXmlImporter), AssetDatabase.GetImporterOverride(uiXml));
            StringAssert.DoesNotContain("<!--", AssetDatabase.LoadAssetAtPath<TextAsset>(uiXml).text);
        }

        [Test]
        public void Under_Resources_it_still_loads_by_its_dot_ui_name()
        {
            Write("Resources/UiXmlImporterTests/c.ui.xml");

            var asset = Resources.Load<TextAsset>("UiXmlImporterTests/c.ui");

            Assert.IsNotNull(asset);
            StringAssert.DoesNotContain("<!--", asset.text);
        }

        [Test]
        public void The_sweep_takes_over_a_ui_xml_imported_before_the_importer_existed()
        {
            var path = Write("a.ui.xml");
            // 升级时看到的样子：还是原生导入器的产物。先停掉分派，否则清掉的 override 会在重导时被立刻装回去
            UiXmlImporterAssigner.PausedForTests = true;
            AssetDatabase.ClearImporterOverride(path);
            Assume.That(AssetDatabase.GetImporterOverride(path), Is.Null);
            StringAssert.Contains("<!--", AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
            UiXmlImporterAssigner.PausedForTests = false;

            UiXmlImporterAssigner.Sweep();

            Assert.AreEqual(typeof(UiXmlImporter), AssetDatabase.GetImporterOverride(path));
            StringAssert.DoesNotContain("<!--", AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────

        private static string Write(string relative)
        {
            var path = $"{Dir}/{relative}";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Sample);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static int LineCount(string s) => s.Split('\n').Length;
    }
}
