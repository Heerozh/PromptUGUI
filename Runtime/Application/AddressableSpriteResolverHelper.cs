#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PromptUGUI.Application
{
    public static partial class SpriteResolverHelpers
    {
        // Held alive so the SpriteSet refs (and their dependent SpriteAtlas) stay
        // loaded for the lifetime of UI.SpriteResolver. Released on second-call /
        // UI.ResetForTests. (PROMPTUGUI_HAS_ADDRESSABLES only.)
        private static AsyncOperationHandle<IList<SpriteSet>>? _addressableSpriteSetHandle;
        // Static; intentionally survives UI.ResetForTests so we don't double-subscribe
        // OnReset across test sessions.
        private static bool _addressableResetHooked;

        // Test observation point. Tests assert the increment to verify Release was
        // called, without inspecting Addressables internals.
        internal static int _testReleaseCount;

        /// <summary>
        /// Loads every <see cref="SpriteSet"/> tagged with <paramref name="label"/> via
        /// Addressables, builds the sprite lookup map, and installs it as
        /// <c>UI.SpriteResolver</c>.
        ///
        /// The underlying <c>AsyncOperationHandle</c> is held for the lifetime of the
        /// resolver so the loaded SpriteSet assets (and their dependent SpriteAtlas refs)
        /// stay resident. It is released on the next call to this method or on
        /// <c>UI.ResetForTests</c>; calling twice in a row is therefore safe and acts
        /// as a rebind.
        ///
        /// In the Editor this also wires <c>UI.HotReload.SpriteResolverRebuilder</c> so
        /// the lookup map is rebuilt in-place when an SpriteSet asset is re-imported,
        /// without re-downloading via Addressables.
        ///
        /// Only available when <c>com.unity.addressables</c> is installed
        /// (<c>PROMPTUGUI_HAS_ADDRESSABLES</c> compile define).
        /// </summary>
        /// <param name="label">Addressables label tagging the SpriteSet assets to load.</param>
        public static Awaitable UseAddressableSpriteSetResolver(
            string label = "SpriteSets") =>
            UseAddressableSpriteSetResolverInternal(
                () => Addressables.LoadAssetsAsync<SpriteSet>(label, null));

        /// <summary>
        /// Multi-label overload. Loads every <see cref="SpriteSet"/> matching the supplied
        /// <paramref name="labels"/> combined via <paramref name="mergeMode"/>
        /// (<see cref="Addressables.MergeMode.Union"/> for OR — default,
        /// <see cref="Addressables.MergeMode.Intersection"/> for AND), then wires
        /// <c>UI.SpriteResolver</c> the same way the single-label overload does.
        /// Same handle-release / HotReload semantics.
        /// </summary>
        public static Awaitable UseAddressableSpriteSetResolver(
            IEnumerable<string> labels,
            Addressables.MergeMode mergeMode = Addressables.MergeMode.Union)
        {
            if (labels == null) throw new ArgumentNullException(nameof(labels));
            var keys = new List<object>();
            foreach (var l in labels) keys.Add(l);
            if (keys.Count == 0)
                throw new ArgumentException(
                    "labels must contain at least one entry", nameof(labels));
            return UseAddressableSpriteSetResolverInternal(
                () => Addressables.LoadAssetsAsync<SpriteSet>(keys, null, mergeMode));
        }

        private static async Awaitable UseAddressableSpriteSetResolverInternal(
            Func<AsyncOperationHandle<IList<SpriteSet>>> loader)
        {
            ReleaseAddressableSpriteSetHandle();
            HookResetOnce();

            var handle = loader();
            _addressableSpriteSetHandle = handle;
            var sets = await handle.Task;
            var snapshot = new List<SpriteSet>(sets ?? Array.Empty<SpriteSet>());

            void Rebuild()
            {
                var map = BuildLookup(snapshot);
                UI.SpriteResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.SpriteResolverRebuilder = Rebuild;
#endif
        }

        private static void HookResetOnce()
        {
            if (_addressableResetHooked) return;
            UI.OnReset += ReleaseAddressableSpriteSetHandle;
            _addressableResetHooked = true;
        }

        private static void ReleaseAddressableSpriteSetHandle()
        {
            if (_addressableSpriteSetHandle.HasValue && _addressableSpriteSetHandle.Value.IsValid())
            {
                Addressables.Release(_addressableSpriteSetHandle.Value);
                _testReleaseCount++;
            }
            _addressableSpriteSetHandle = null;
        }
    }
}
#endif
