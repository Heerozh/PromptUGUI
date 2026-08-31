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
        /// 装载走 <see cref="UI.LoadDocumentWithCommonsAsync"/>:和普通 Screen 一样合并 commons 池、
        /// 解析 &lt;Import&gt; 链,所以自定义模态皮可以复用项目的共享模板(如 ModalFrame / ModalBtn)。
        /// 同一 src 的并发装载由 UI.Modal 的 pump 串行化(一次只 await 一个 EnsureLoaded),
        /// 这里的双检只防 Modal 与 LoadingOverlay 两条线交错。
        /// </summary>
        internal static async Awaitable EnsureLoaded(string src)
        {
            if (_loaded.Contains(src)) return;
            var xml = await ModalSourceLoader.LoadAsync(src);
            if (_loaded.Contains(src)) return;      // 并发双检:await 期间别人加载完了
            await UI.LoadDocumentWithCommonsAsync(src, xml);
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
