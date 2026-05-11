using System.Collections;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Tests for AddressableResolverHelper. Loading tests use [UnityTest] + IEnumerator
    /// because Addressables returns a genuinely-async Awaitable; EditMode requires
    /// yield-return frame pumping to let the awaiter complete (Unity Awaitable.GetResult
    /// returns default(T) on incomplete awaiters rather than blocking).
    /// </summary>
    public class AddressableResolverTests
    {
        private const string FixturesRoot = "Assets/PromptUGUI_TestFixtures";
        private const string FixtureKey = "promptugui-test/main";
        private AddressableAssetGroup _testGroup;
        private string _xmlPath;

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();

            Directory.CreateDirectory(FixturesRoot);
            _xmlPath = $"{FixturesRoot}/main.ui.xml";
            File.WriteAllText(_xmlPath,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame id='a'/></Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(_xmlPath);

            var settings = AddressableAssetSettingsDefaultObject.Settings
                          ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            _testGroup = settings.CreateGroup(
                "PromptUGUI_Test", false, false, false, null,
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
        public void UseAddressableResolver_sets_source_resolver()
        {
            Assert.IsNull(UI.SourceResolver,
                "SourceResolver should be null before UseAddressableResolver");
            UI.UseAddressableResolver();
            Assert.IsNotNull(UI.SourceResolver,
                "SourceResolver should be set after UseAddressableResolver");
        }

        [UnityTest]
        public IEnumerator UseAddressableResolver_loads_by_key()
        {
            UI.UseAddressableResolver();

            var awaitable = UI.LoadDocumentAsync(FixtureKey);
            var awaiter = awaitable.GetAwaiter();
            int maxIter = 600;
            while (!awaiter.IsCompleted && maxIter-- > 0) yield return null;
            if (!awaiter.IsCompleted) Assert.Fail("LoadDocumentAsync timed out after ~10 seconds");

            var names = awaiter.GetResult();
            CollectionAssert.Contains(names, "S");

            var s = UI.Open("S");
            Assert.IsNotNull(s.Get<Frame>("a"));
        }

        [UnityTest]
        public IEnumerator UseAddressableResolver_unknown_key_throws()
        {
            UI.UseAddressableResolver();

            // Addressables logs an Error for InvalidKeyException; expect it.
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "nonexistent|InvalidKeyException|not found|Exception",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));

            var awaitable = UI.LoadDocumentAsync("nonexistent-key");
            var awaiter = awaitable.GetAwaiter();
            int maxIter = 600;
            while (!awaiter.IsCompleted && maxIter-- > 0) yield return null;
            if (!awaiter.IsCompleted) Assert.Fail("LoadDocumentAsync timed out after ~10 seconds");

            // IOException (subclass of Exception) is thrown; use InstanceOf to match subclasses.
            Assert.That(() => awaiter.GetResult(), Throws.InstanceOf<System.Exception>());
        }
    }
}
