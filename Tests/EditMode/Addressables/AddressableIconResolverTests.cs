using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Wiring smoke tests for SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver.
    ///
    /// End-to-end "label → SpriteSet → IconResolver returns sprite" is NOT tested
    /// in EditMode: AsyncOperationHandle continuations need the player-loop
    /// SynchronizationContext which is absent in EditMode test runners. Same
    /// limitation documented in AddressableResolverTests. The synchronous prefix
    /// of the async method (release-previous-handle, start-load, store-handle,
    /// hook-reset) is what gets covered here.
    ///
    /// FixtureLabel intentionally doesn't resolve to any registered asset, so
    /// Addressables logs an InvalidKeyException synchronously inside LoadAssetsAsync.
    /// Each test declares the expected error via LogAssert.Expect; Unity Test
    /// Framework consumes the matched entry instead of failing the test on it.
    ///
    /// FRAGILE: this hinges on `promptugui-test/icons` never being registered as a
    /// label in the host project's AddressableAssetSettings. AA only emits
    /// InvalidKeyException for *unknown* keys — registered-but-zero-entries labels
    /// resolve silently to an empty list. The sibling LocaleAddressableResolver
    /// test hit this exact failure after `AddressablePoLabelSyncer` registered
    /// `Locale:zh-Hans` permanently in the project's AA labels (see commit fixing
    /// LocaleAddressableResolverTests). If `promptugui-test/icons` ever gets added
    /// as an AA label, mirror that fix here: drop `LogAssert.Expect(...)` and use
    /// `LogAssert.ignoreFailingMessages = true` so the test only asserts the
    /// behavioral contract (non-null Awaitable / release counter) and tolerates
    /// either AA code path.
    /// </summary>
    public class AddressableIconResolverTests
    {
        private const string FixtureLabel = "promptugui-test/icons";
        private static readonly Regex InvalidKeyError = new(".*InvalidKeyException.*");

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            // Ensure AddressableAssetSettings exists; without it
            // Addressables.LoadAssetsAsync throws synchronously in a fresh project.
            _ = AddressableAssetSettingsDefaultObject.Settings
                ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            SpriteResolverHelpers._testReleaseCount = 0;
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Invocation_returns_an_awaitable()
        {
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            var awaitable =
                SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            Assert.IsNotNull(awaitable,
                "UseAddressableSpriteAtlasIconResolver should return non-null Awaitable");
            // Note: Awaitable intentionally not awaited; underlying AsyncOperationHandle
            // remains pending until TearDown's ResetForTests releases it. We don't
            // assert on UI.SpriteResolver state — its post-await value depends on
            // whether LoadAssetsAsync completed synchronously (rare but possible),
            // which is a C# state-machine detail rather than this helper's contract.
        }

        [Test]
        public void Releases_previous_handle_on_second_call()
        {
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            _ = SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            var beforeSecond = SpriteResolverHelpers._testReleaseCount;
            _ = SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            Assert.AreEqual(beforeSecond + 1, SpriteResolverHelpers._testReleaseCount,
                "Second call should release the first call's handle exactly once");
        }

        [Test]
        public void ResetForTests_releases_handle()
        {
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            _ = SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            var beforeReset = SpriteResolverHelpers._testReleaseCount;
            UI.ResetForTests();
            Assert.AreEqual(beforeReset + 1, SpriteResolverHelpers._testReleaseCount,
                "ResetForTests should trigger OnReset → helper releases the handle");
        }

        [Test]
        public void MultiLabel_null_labels_throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(
                    (System.Collections.Generic.IEnumerable<string>)null));
        }

        [Test]
        public void MultiLabel_empty_labels_throws_ArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(
                    System.Array.Empty<string>()));
        }

        [Test]
        public void MultiLabel_invocation_returns_an_awaitable()
        {
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            var awaitable = SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(
                new[] { FixtureLabel, FixtureLabel + "-extra" },
                UnityEngine.AddressableAssets.Addressables.MergeMode.Union);
            Assert.IsNotNull(awaitable,
                "multi-label UseAddressableSpriteAtlasIconResolver should return non-null Awaitable");
        }

        [Test]
        public void MultiLabel_releases_previous_handle_on_second_call()
        {
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            _ = SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(FixtureLabel);
            var beforeSecond = SpriteResolverHelpers._testReleaseCount;
            _ = SpriteResolverHelpers.UseAddressableSpriteAtlasIconResolver(
                new[] { FixtureLabel, FixtureLabel + "-extra" });
            Assert.AreEqual(beforeSecond + 1, SpriteResolverHelpers._testReleaseCount,
                "multi-label call should release single-label predecessor exactly once");
        }
    }
}
