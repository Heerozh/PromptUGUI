using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Verifies UseAddressableResolver() wires the SourceResolver hook.
    ///
    /// End-to-end LoadDocumentAsync via Addressables is NOT tested in EditMode.
    /// EditMode lacks the player-loop SynchronizationContext that Addressables'
    /// AsyncOperationHandle uses to schedule continuations, so an async
    /// `await handle.Task` chain never resumes in [Test] / [UnityTest] EditMode
    /// runners. The async parsing/loading pipeline itself is exhaustively
    /// covered by CommonLibraryTests using the in-memory AwaitableHelpers
    /// fake resolver — only the Addressables-specific resolver wiring needs
    /// platform-specific verification.
    ///
    /// (PlayMode integration test is a future option if needed; see
    /// plan §10 risk acknowledgement.)
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

        [Test]
        public void UseAddressableResolver_invocation_returns_an_awaitable()
        {
            // Smoke check that the registered resolver, when called, returns
            // an Awaitable<string> (not null, not a wrong type). Whether/when
            // it completes is not asserted here — that's the EditMode async
            // limitation documented above. The resolver is a method group,
            // not a lambda, so calling it should always produce a Awaitable
            // handle (even if pending).
            UI.UseAddressableResolver();
            var awaitable = UI.SourceResolver(FixtureKey);
            Assert.IsNotNull(awaitable, "Resolver returned null Awaitable");
        }
    }
}
