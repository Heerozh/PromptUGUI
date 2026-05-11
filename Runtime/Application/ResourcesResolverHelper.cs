using System;
using System.IO;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// 内置 helper：把 SourceResolver 设为 Resources.Load(rootPath/{src}).text；
        /// 同时（仅 Editor）把 HotReload.AssetPathToSrc 设为反向映射，
        /// 让 AssetPostprocessor 能从 AssetDatabase 路径反推 src。
        /// </summary>
        public static void UseResourcesResolver(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("rootPath must be non-empty");
            var root = rootPath.TrimEnd('/');

            SourceResolver = src =>
            {
                if (string.IsNullOrEmpty(src))
                    return AwaitableHelpers.Faulted<string>(
                        new IOException("Resources lookup with empty src"));
                var ta = Resources.Load<TextAsset>($"{root}/{src}");
                if (ta == null)
                    return AwaitableHelpers.Faulted<string>(
                        new IOException($"Resources lookup failed: {root}/{src}"));
                return AwaitableHelpers.Completed(ta.text);
            };

#if UNITY_EDITOR
            HotReload.AssetPathToSrc = assetPath =>
            {
                if (string.IsNullOrEmpty(assetPath)) return null;
                var marker = $"/Resources/{root}/";
                var idx = assetPath.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) return null;
                var rel = assetPath.Substring(idx + marker.Length);
                return rel.EndsWith(".ui.xml")
                    ? rel.Substring(0, rel.Length - ".ui.xml".Length)
                    : null;
            };
#endif
        }
    }
}
