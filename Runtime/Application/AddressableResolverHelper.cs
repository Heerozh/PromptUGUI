#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

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

        /// <summary>
        /// 接受拖拽进 Inspector 的 AssetReferenceT&lt;TextAsset&gt;，转发到字符串管线
        /// （AssetGUID 直接作为 Addressables key）。需要先调用 UseAddressableResolver()
        /// 或自行配置一个能解析 GUID 的 SourceResolver。
        /// </summary>
        public static Awaitable<IReadOnlyList<string>> LoadDocumentAsync(
            AssetReferenceT<TextAsset> xmlRef)
        {
            if (xmlRef == null)
                throw new ArgumentNullException(nameof(xmlRef));
            if (!xmlRef.RuntimeKeyIsValid())
                throw new ArgumentException(
                    "AssetReferenceT<TextAsset> has no asset assigned (RuntimeKey invalid)",
                    nameof(xmlRef));
            return LoadDocumentAsync(xmlRef.AssetGUID);
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
            var settings = AddressableAssetSettingsDefaultObject.Settings;
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
            AddressableAssetSettings settings,
            AddressableAssetSettings.ModificationEvent evt,
            object obj)
        {
            switch (evt)
            {
                case AddressableAssetSettings.ModificationEvent.EntryAdded:
                case AddressableAssetSettings.ModificationEvent.EntryCreated:
                case AddressableAssetSettings.ModificationEvent.EntryMoved:
                case AddressableAssetSettings.ModificationEvent.EntryRemoved:
                case AddressableAssetSettings.ModificationEvent.EntryModified:
                case AddressableAssetSettings.ModificationEvent.GroupAdded:
                case AddressableAssetSettings.ModificationEvent.GroupRemoved:
                case AddressableAssetSettings.ModificationEvent.BatchModification:
                    BuildAddressablesReverseMapping();
                    break;
            }
        }

        private static string AddressablesAssetPathToSrc(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (!assetPath.EndsWith(".ui.xml")) return null;
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;

            // LoadDocumentAsync(AssetReferenceT<TextAsset>) forwards xmlRef.AssetGUID as src,
            // so DepGraph is keyed by GUID. LoadDocumentAsync(string addressKey) keys by address.
            // Prefer whichever the DepGraph actually registered; otherwise fall back to address.
            if (_depGraph.SrcToDeps.ContainsKey(guid) || _depGraph.IsCommons(guid))
                return guid;

            return _guidToKey != null && _guidToKey.TryGetValue(guid, out var key) ? key : null;
        }
#endif
    }
}
#endif
