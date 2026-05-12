using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Wiring smoke tests for UI.Locale.UseAddressableResolver.
    ///
    /// End-to-end "label='zh-Hans' → .po TextAssets → TranslationStore" is NOT
    /// tested in EditMode: AsyncOperationHandle continuations need the player-loop
    /// SynchronizationContext, which is absent in EditMode test runners. Same
    /// limitation documented in AddressableResolverTests. Tests here cover only
    /// the synchronous registration prefix: PoResolver is non-null after the
    /// call, and invoking it returns a non-null Awaitable.
    ///
    /// 'zh-Hans' intentionally doesn't resolve to any registered asset, so
    /// Addressables logs an InvalidKeyException synchronously inside
    /// LoadAssetsAsync. The invocation test declares the expected error via
    /// LogAssert.Expect; Unity Test Framework consumes the matched entry
    /// instead of failing the test on it.
    /// </summary>
    public class LocaleAddressableResolverTests
    {
        private static readonly Regex InvalidKeyError = new(".*InvalidKeyException.*");

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            // Ensure AddressableAssetSettings exists; without it
            // Addressables.LoadAssetsAsync throws synchronously in a fresh project.
            _ = AddressableAssetSettingsDefaultObject.Settings
                ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void UseAddressableResolver_sets_PoResolver()
        {
            Assert.IsNull(UI.PoResolver,
                "PoResolver should be null after ResetForTests");
            UI.Locale.UseAddressableResolver();
            Assert.IsNotNull(UI.PoResolver,
                "PoResolver should be set after UseAddressableResolver");
        }

        [Test]
        public void PoResolver_invocation_after_register_returns_awaitable()
        {
            LogAssert.Expect(LogType.Error, InvalidKeyError);
            UI.Locale.UseAddressableResolver();
            var awaitable = UI.PoResolver("zh-Hans");
            Assert.IsNotNull(awaitable,
                "Registered resolver should return a non-null Awaitable. " +
                "Not awaited — EditMode has no player loop, the Addressables " +
                "AsyncOperationHandle won't resume; TearDown's ResetForTests " +
                "releases the handle.");
        }
    }
}
