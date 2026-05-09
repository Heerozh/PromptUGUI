using System.IO;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Editor
{
    public class PoFileImporterTests
    {
        private const string TmpDir = "Assets/PromptUGUIPoTmp";
        private const string TmpPath = "Assets/PromptUGUIPoTmp/sample.po";

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(TmpDir))
                AssetDatabase.CreateFolder("Assets", "PromptUGUIPoTmp");
        }
        [TearDown]
        public void Teardown()
        {
            AssetDatabase.DeleteAsset(TmpDir);
        }

        [Test]
        public void Import_PoFile_AutoOverrideViaPostprocessor_LoadsAsTextAsset()
        {
            var absPath = System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "PromptUGUIPoTmp", "auto.po");
            System.IO.File.WriteAllText(absPath, "msgid \"a\"\nmsgstr \"b\"\n");

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            // Postprocessor should have set the override automatically — no manual SetImporterOverride here.
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/PromptUGUIPoTmp/auto.po");
            Assert.IsNotNull(asset, "PoFilePostprocessor should auto-route .po to PoFileImporter");
            StringAssert.Contains("msgid", asset.text);
        }

        [Test]
        public void Import_PoFile_LoadsAsTextAsset()
        {
            // Write via absolute path (File.WriteAllText uses CWD which may not be project root)
            var absPath = Path.Combine(UnityEngine.Application.dataPath, "PromptUGUIPoTmp", "sample.po");
            File.WriteAllText(absPath, "msgid \"x\"\nmsgstr \"y\"\n");

            // Unity 6 + com.unity.localization claims .po as a native importer and
            // may log "[Error] File couldn't be read" during the initial discover-import.
            // Suppress unexpected error logs around the asset pipeline calls.
            LogAssert.ignoreFailingMessages = true;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Override to PoFileImporter for this specific path, then force a reimport.
            AssetDatabase.SetImporterOverride<PoFileImporter>(TmpPath);
            AssetDatabase.ImportAsset(TmpPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            LogAssert.ignoreFailingMessages = false;

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TmpPath);
            Assert.IsNotNull(asset, "po file should be loadable as TextAsset after SetImporterOverride");
            StringAssert.Contains("msgid", asset.text);
        }
    }
}
