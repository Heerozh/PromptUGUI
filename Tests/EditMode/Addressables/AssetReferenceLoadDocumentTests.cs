using System;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Covers the AssetReferenceT&lt;TextAsset&gt; overload of UI.LoadDocumentAsync.
    /// Uses an in-memory fake SourceResolver (AwaitableHelpers.Completed) so the
    /// EditMode async limitation noted in AddressableResolverTests does not apply —
    /// no real Addressables handle is created here; we only assert that the overload
    /// forwards AssetGUID into the existing string pipeline.
    /// </summary>
    public class AssetReferenceLoadDocumentTests
    {
        [SetUp]
        public void Setup() => UI.ResetForTests();

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void LoadDocumentAsync_null_AssetReference_throws_ArgumentNullException()
        {
            UI.SourceResolver = _ => AwaitableHelpers.Completed("");
            Assert.Throws<ArgumentNullException>(() =>
                UI.LoadDocumentAsync((AssetReferenceT<TextAsset>)null)
                  .GetAwaiter().GetResult());
        }

        [Test]
        public void LoadDocumentAsync_invalid_runtime_key_throws_ArgumentException()
        {
            UI.SourceResolver = _ => AwaitableHelpers.Completed("");
            var assetRef = new AssetReferenceT<TextAsset>("");
            Assert.IsFalse(assetRef.RuntimeKeyIsValid(),
                "precondition: empty-guid AssetReferenceT should be invalid");
            Assert.Throws<ArgumentException>(() =>
                UI.LoadDocumentAsync(assetRef).GetAwaiter().GetResult());
        }

        [Test]
        public void LoadDocumentAsync_forwards_AssetGUID_to_SourceResolver()
        {
            var guid = Guid.NewGuid().ToString("N");
            string captured = null;
            UI.SourceResolver = src =>
            {
                captured = src;
                return AwaitableHelpers.Completed(
                    @"<?xml version='1.0'?><PromptUGUI version='1'>
                        <Screen name='S'><Frame id='a'/></Screen>
                      </PromptUGUI>");
            };
            var assetRef = new AssetReferenceT<TextAsset>(guid);
            var added = UI.LoadDocumentAsync(assetRef).GetAwaiter().GetResult();

            Assert.AreEqual(guid, captured,
                "AssetGUID should be forwarded to SourceResolver as src");
            CollectionAssert.AreEqual(new[] { "S" }, added);
        }
    }
}
