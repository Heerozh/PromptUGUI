#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// 把 SourceResolver 设为 Addressables.LoadAssetAsync&lt;TextAsset&gt;(src)。
        /// src 直接当 Addressables key 用，不做 prefix 拼接。
        /// （未装 com.unity.addressables 时本方法不存在 —— PROMPTUGUI_HAS_ADDRESSABLES 未定义）
        ///
        /// Editor：同时设置 HotReload.AssetPathToSrc 走 guid → key 反查，让 AssetPostprocessor
        /// 在保存 .ui.xml 时能匹配到 Addressables 入口并触发热重载。
        /// </summary>
        public static void UseAddressableResolver()
        {
            SourceResolver = LoadFromAddressablesAsync;

#if UNITY_EDITOR
            BuildAddressablesReverseMapping();
            HotReload.AssetPathToSrc = AddressablesAssetPathToSrc;
#endif
        }

        private static Awaitable<string> LoadFromAddressablesAsync(string src)
        {
            if (string.IsNullOrEmpty(src))
                return AwaitableHelpers.Faulted<string>(
                    new IOException("Addressables lookup with empty src"));
            return LoadFromAddressablesInternalAsync(src);
        }

        private static async Awaitable<string> LoadFromAddressablesInternalAsync(string src)
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(src);
            try
            {
                // AsyncOperationHandle<T>.Task 在 Addressables 1.x 全系列稳定；
                // 1.21+ 也支持直接 `await handle`，但 .Task 兼容更广。
                var ta = await handle.Task;
                if (ta == null)
                    throw new IOException($"Addressables key not found or wrong type: {src}");
                return ta.text;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

#if UNITY_EDITOR
        private static System.Collections.Generic.Dictionary<string, string> _guidToKey;

        private static void BuildAddressablesReverseMapping()
        {
            _guidToKey = new System.Collections.Generic.Dictionary<string, string>();
            var settings = UnityEditor.AddressableAssets
                                       .AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.guid)) continue;
                    _guidToKey[entry.guid] = entry.address;
                }
            }
            settings.OnModification -= OnAddressableSettingsModified;
            settings.OnModification += OnAddressableSettingsModified;
        }

        private static void OnAddressableSettingsModified(
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent evt,
            object obj)
        {
            switch (evt)
            {
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryAdded:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryCreated:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryMoved:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryRemoved:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryModified:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.GroupAdded:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.GroupRemoved:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.BatchModification:
                    BuildAddressablesReverseMapping();
                    break;
            }
        }

        private static string AddressablesAssetPathToSrc(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (!assetPath.EndsWith(".ui.xml")) return null;
            var guid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;
            return _guidToKey != null && _guidToKey.TryGetValue(guid, out var key)
                ? key
                : null;
        }
#endif
    }
}
#endif
