using System.IO;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// <see cref="UiXmlLocator.Locate"/>'s Addressables branch: the address or GUID the runtime
    /// would hand to <c>UI.LoadDocumentAsync</c> maps to the asset path exactly, before any on-disk
    /// guess — so an address that looks nothing like its path (<c>ui-home</c>) still lands on the
    /// file (2026-09-18 ui-preview-tool spec §4.4).
    /// </summary>
    public class AddressableUiXmlLocatorTests
    {
        private const string FixturesRoot = "Assets/PromptUGUI_TestFixtures";
        private const string FixtureKey = "promptugui-test/locator-home";
        private AddressableAssetGroup _testGroup;
        private string _xmlPath;

        [SetUp]
        public void Setup()
        {
            Directory.CreateDirectory(FixturesRoot);
            _xmlPath = $"{FixturesRoot}/locator.ui.xml";
            File.WriteAllText(_xmlPath,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame/></Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(_xmlPath);

            var settings = AddressableAssetSettingsDefaultObject.Settings
                          ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            _testGroup = settings.CreateGroup(
                "PromptUGUI_Locator_Test", false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));

            var guid = AssetDatabase.AssetPathToGUID(_xmlPath);
            var entry = settings.CreateOrMoveEntry(guid, _testGroup);
            entry.address = FixtureKey;
        }

        [TearDown]
        public void TearDown()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && _testGroup != null)
                settings.RemoveGroup(_testGroup);
            if (File.Exists(_xmlPath)) AssetDatabase.DeleteAsset(_xmlPath);
            if (Directory.Exists(FixturesRoot)) AssetDatabase.DeleteAsset(FixturesRoot);
        }

        [Test]
        public void Locate_AnAddress_IsTheAssetPath_WhateverTheAnchor()
        {
            Assert.AreEqual(_xmlPath, UiXmlLocator.Locate(FixtureKey, null));
            Assert.AreEqual(_xmlPath, UiXmlLocator.Locate(FixtureKey, "Assets/Somewhere/Else.ui.xml"),
                "an address is global; the anchoring file only matters for the Resources walk");
        }

        [Test]
        public void Locate_AGuid_IsTheAssetPath()
        {
            Assert.AreEqual(_xmlPath, UiXmlLocator.Locate(AssetDatabase.AssetPathToGUID(_xmlPath), null));
        }

        [Test]
        public void Locate_AnAddressNobodyRegistered_FallsThroughToNull()
        {
            Assert.IsNull(UiXmlLocator.Locate("promptugui-test/never-registered", null));
        }
    }
}
