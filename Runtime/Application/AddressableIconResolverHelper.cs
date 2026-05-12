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
