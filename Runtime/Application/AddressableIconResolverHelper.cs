#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PromptUGUI.Application
{
    public static partial class IconResolverHelpers
    {
        // Held alive so the IconSet refs (and their dependent SpriteAtlas) stay
        // loaded for the lifetime of UI.IconResolver. Released on second-call /
        // UI.ResetForTests. (PROMPTUGUI_HAS_ADDRESSABLES only.)
        private static AsyncOperationHandle<IList<IconSet>>? _addressableIconHandle;
        // Static; intentionally survives UI.ResetForTests so we don't double-subscribe
        // OnReset across test sessions.
        private static bool _addressableResetHooked;

        // Test observation point. Tests assert the increment to verify Release was
        // called, without inspecting Addressables internals.
        internal static int _testReleaseCount;

        /// <summary>
        /// Loads every <see cref="IconSet"/> tagged with <paramref name="label"/> via
        /// Addressables, builds the icon lookup map, and installs it as
        /// <c>UI.IconResolver</c>.
        ///
        /// The underlying <c>AsyncOperationHandle</c> is held for the lifetime of the
        /// resolver so the loaded IconSet assets (and their dependent SpriteAtlas refs)
        /// stay resident. It is released on the next call to this method or on
        /// <c>UI.ResetForTests</c>; calling twice in a row is therefore safe and acts
        /// as a rebind.
        ///
        /// In the Editor this also wires <c>UI.HotReload.IconResolverRebuilder</c> so
        /// the lookup map is rebuilt in-place when an IconSet asset is re-imported,
        /// without re-downloading via Addressables.
        ///
        /// Only available when <c>com.unity.addressables</c> is installed
        /// (<c>PROMPTUGUI_HAS_ADDRESSABLES</c> compile define).
        /// </summary>
        /// <param name="label">Addressables label tagging the IconSet assets to load.</param>
        public static async Awaitable UseAddressableSpriteAtlasIconResolver(
            string label = "IconSets")
        {
            ReleaseAddressableIconHandle();
            HookResetOnce();

            var handle = Addressables.LoadAssetsAsync<IconSet>(label, null);
            _addressableIconHandle = handle;
            var sets = await handle.Task;
            var snapshot = new List<IconSet>(sets ?? Array.Empty<IconSet>());

            void Rebuild()
            {
                var map = BuildLookup(snapshot);
                UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
        }

        private static void HookResetOnce()
        {
            if (_addressableResetHooked) return;
            UI.OnReset += ReleaseAddressableIconHandle;
            _addressableResetHooked = true;
        }

        private static void ReleaseAddressableIconHandle()
        {
            if (_addressableIconHandle.HasValue && _addressableIconHandle.Value.IsValid())
            {
                Addressables.Release(_addressableIconHandle.Value);
                _testReleaseCount++;
            }
            _addressableIconHandle = null;
        }
    }
}
#endif
