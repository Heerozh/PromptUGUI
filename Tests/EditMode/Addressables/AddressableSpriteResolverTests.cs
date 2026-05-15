using System;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Wiring smoke tests for SpriteResolverHelpers.UseAddressableSpriteSetResolver.
    ///
    /// End-to-end "label → SpriteSet → SpriteResolver returns sprite" is NOT tested
    /// in EditMode: AsyncOperationHandle continuations need the player-loop
    /// SynchronizationContext which is absent in EditMode test runners. Same
    /// limitation documented in AddressableResolverTests. The synchronous prefix
    /// of the async method (release-previous-handle, start-load, store-handle,
    /// hook-reset) is what gets covered here.
    ///
    /// FixtureLabel may or may not resolve to a registered AA label in the host
    /// project, and AA emits an InvalidKeyException only for *unknown* keys —
    /// registered-but-zero-entries labels resolve silently to an empty list.
    /// To tolerate both code paths we set <c>LogAssert.ignoreFailingMessages =
    /// true</c> in SetUp and only assert the behavioral contract (non-null
    /// Awaitable / release counter increments). This trades log-level precision
    /// for stability across host project AA settings.
    /// </summary>
    public class AddressableSpriteResolverTests
    {
        private const string FixtureLabel = "promptugui-test/icons";

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
            // Tolerate whichever path AA takes for FixtureLabel: unknown key →
            // InvalidKeyException error log, or registered-but-empty label →
            // silent empty result. ignoreFailingMessages must be set inside the
            // test method (not SetUp) because the Unity Test Framework's
            // per-test LogScope is created after SetUp runs.
            LogAssert.ignoreFailingMessages = true;
            var awaitable =
                SpriteResolverHelpers.UseAddressableSpriteSetResolver(FixtureLabel);
            Assert.IsNotNull(awaitable,
                "UseAddressableSpriteSetResolver should return non-null Awaitable");
            // Note: Awaitable intentionally not awaited; underlying AsyncOperationHandle
            // remains pending until TearDown's ResetForTests releases it. We don't
            // assert on UI.SpriteResolver state — its post-await value depends on
            // whether LoadAssetsAsync completed synchronously (rare but possible),
            // which is a C# state-machine detail rather than this helper's contract.
        }

        [Test]
        public void Releases_previous_handle_on_second_call()
        {
            LogAssert.ignoreFailingMessages = true;
            _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(FixtureLabel);
            var beforeSecond = SpriteResolverHelpers._testReleaseCount;
            _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(FixtureLabel);
            Assert.AreEqual(beforeSecond + 1, SpriteResolverHelpers._testReleaseCount,
                "Second call should release the first call's handle exactly once");
        }

        [Test]
        public void ResetForTests_releases_handle()
        {
            LogAssert.ignoreFailingMessages = true;
            _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(FixtureLabel);
            var beforeReset = SpriteResolverHelpers._testReleaseCount;
            UI.ResetForTests();
            Assert.AreEqual(beforeReset + 1, SpriteResolverHelpers._testReleaseCount,
                "ResetForTests should trigger OnReset → helper releases the handle");
        }

        [Test]
        public void MultiLabel_null_labels_throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SpriteResolverHelpers.UseAddressableSpriteSetResolver(
                    (System.Collections.Generic.IEnumerable<string>)null));
        }

        [Test]
        public void MultiLabel_empty_labels_throws_ArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                SpriteResolverHelpers.UseAddressableSpriteSetResolver(
                    System.Array.Empty<string>()));
        }

        [Test]
        public void MultiLabel_invocation_returns_an_awaitable()
        {
            LogAssert.ignoreFailingMessages = true;
            var awaitable = SpriteResolverHelpers.UseAddressableSpriteSetResolver(
                new[] { FixtureLabel, FixtureLabel + "-extra" },
                UnityEngine.AddressableAssets.Addressables.MergeMode.Union);
            Assert.IsNotNull(awaitable,
                "multi-label UseAddressableSpriteSetResolver should return non-null Awaitable");
        }

        [Test]
        public void MultiLabel_releases_previous_handle_on_second_call()
        {
            LogAssert.ignoreFailingMessages = true;
            _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(FixtureLabel);
            var beforeSecond = SpriteResolverHelpers._testReleaseCount;
            _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(
                new[] { FixtureLabel, FixtureLabel + "-extra" });
            Assert.AreEqual(beforeSecond + 1, SpriteResolverHelpers._testReleaseCount,
                "multi-label call should release single-label predecessor exactly once");
        }
    }
}
