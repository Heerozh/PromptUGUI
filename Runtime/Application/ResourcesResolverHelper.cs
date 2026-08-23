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
        ///
        /// <para><b>src 的形态：相对 <paramref name="rootPath"/>，且带 <c>.ui</c> 后缀。</b>
        /// Unity 的 Resources 查找名只剥掉**最后一个**扩展名，所以磁盘上的
        /// <c>Resources/UI/Home.ui.xml</c> 的资源名是 <c>UI/Home.ui</c>，不是 <c>UI/Home</c>。
        /// 因此 <c>UseResourcesResolver("UI")</c> 之后要写
        /// <c>LoadDocumentAsync("Home.ui")</c> / <c>&lt;Import src="Skin.ui"/&gt;</c>。
        /// 库自带的模态一直是这么写的（见 <c>MessageBoxRequest.XmlSrc</c>）。</para>
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
                // 只剥 ".xml"，保留 ".ui" —— 产出的 src 必须能被上面那个 resolver 直接查到，
                // 也必须跟调用方注册进 DepGraph 的 key 逐字相同。剥掉 ".ui.xml" 会得到
                // "Home"，而 DepGraph 里存的是 "Home.ui"，两者对不上 →
                // ScreensDependingOn 永远查不到，Resources 方式的热重载静默失效。
                const string suffix = ".ui.xml";
                return rel.EndsWith(suffix, StringComparison.Ordinal)
                    ? rel.Substring(0, rel.Length - ".xml".Length)
                    : null;
            };
#endif
        }
    }
}
