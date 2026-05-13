using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Verifies <c>UI.HotReload.AssetPathToSrc</c> reverse mapping after
    /// <c>UseAddressableResolver()</c>: asset path → Addressables key lookup via
    /// the Editor-only guid→key dictionary maintained by
    /// <see cref="PromptUGUI.Application.UI"/>.
    /// </summary>
    public class AddressableHotReloadTests
    {
        private const string FixturesRoot = "Assets/PromptUGUI_TestFixtures";
        private const string FixtureKey = "promptugui-test/hr";
        private AddressableAssetGroup _testGroup;
        private string _xmlPath;

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            Directory.CreateDirectory(FixturesRoot);
            _xmlPath = $"{FixturesRoot}/hr.ui.xml";
            File.WriteAllText(_xmlPath,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame/></Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(_xmlPath);

            var settings = AddressableAssetSettingsDefaultObject.Settings
                          ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            _testGroup = settings.CreateGroup(
                "PromptUGUI_HR_Test", false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));

            var guid = AssetDatabase.AssetPathToGUID(_xmlPath);
            var entry = settings.CreateOrMoveEntry(guid, _testGroup);
            entry.address = FixtureKey;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && _testGroup != null)
                settings.RemoveGroup(_testGroup);
            if (File.Exists(_xmlPath)) AssetDatabase.DeleteAsset(_xmlPath);
            if (Directory.Exists(FixturesRoot)) AssetDatabase.DeleteAsset(FixturesRoot);
        }

        [Test]
        public void AssetPathToSrc_resolves_addressables_key()
        {
            UI.UseAddressableResolver();
            Assert.AreEqual(FixtureKey, UI.HotReload.AssetPathToSrc(_xmlPath));
        }

        [Test]
        public void AssetPathToSrc_returns_null_for_unknown_path()
        {
            UI.UseAddressableResolver();
            Assert.IsNull(UI.HotReload.AssetPathToSrc("Assets/NotAnAddressable/x.ui.xml"));
        }

        [Test]
        public void AssetPathToSrc_returns_null_for_non_uixml_extension()
        {
            UI.UseAddressableResolver();
            Assert.IsNull(UI.HotReload.AssetPathToSrc(_xmlPath.Replace(".ui.xml", ".txt")));
        }

        // Regression: when callers load via the AssetReferenceT<TextAsset> overload,
        // the DepGraph is keyed by AssetGUID (see LoadDocumentAsync(AssetReferenceT)).
        // AssetPathToSrc must report that GUID so HotReload.NotifyAssetChanged →
        // _depGraph.ScreensDependingOn(src) actually matches; returning the Addressables
        // address here would silently no-op the reload.
        [Test]
        public void AssetPathToSrc_returns_guid_when_AssetReference_overload_registered()
        {
            UI.UseAddressableResolver();
            UI.SourceResolver = _ => AwaitableHelpers.Completed(
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='HR'><Frame/></Screen>
                  </PromptUGUI>");

            var guid = AssetDatabase.AssetPathToGUID(_xmlPath);
            var assetRef = new AssetReferenceT<TextAsset>(guid);
            UI.LoadDocumentAsync(assetRef).GetAwaiter().GetResult();

            Assert.AreEqual(guid, UI.HotReload.AssetPathToSrc(_xmlPath),
                "DepGraph holds GUID via the AssetReferenceT overload; " +
                "AssetPathToSrc must return GUID so reload matches.");
        }
    }
}
