using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// 内置 / 自定义 modal & overlay XML 的加载缓存。UI.Modal 与 LoadingOverlay 共用。
    /// </summary>
    internal static class ModalDocCache
    {
        private static readonly System.Collections.Generic.HashSet<string> _loaded = new();

        /// <summary>
        /// 把 <paramref name="src"/> 对应的 modal XML 加载进 UI._docs(只一次)。
        /// 契约:<paramref name="src"/> 必须等于该 XML 里 &lt;Screen name="..."&gt; 的值
        /// —— 调用方随后用同一个 key 调 <see cref="UI.OpenModalScreen"/>。
        /// </summary>
        internal static async Awaitable EnsureLoaded(string src)
        {
            if (_loaded.Contains(src)) return;
            var xml = await ModalSourceLoader.LoadAsync(src);
            if (_loaded.Contains(src)) return;      // 并发双检:await 期间别人加载完了
            UI.LoadDocument(src, xml);
            _loaded.Add(src);
        }

        internal static void Clear() => _loaded.Clear();

#if UNITY_EDITOR
        internal static void Invalidate(string src)
        {
            if (string.IsNullOrEmpty(src)) return;
            if (_loaded.Remove(src)) UI.UnloadDocument(src);
        }
#endif
    }
}
