using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 跨文档源缓存：src → 已解析的 <see cref="UIDocument"/>。经 <c>UI.SourceResolver</c> 取到的每个 src
    /// 在一个场景内只取一次、只解析一次——公共模板（皮肤词汇表、壳）被十来个文档 Import，此前每个文档都
    /// 重取一遍（宿主用 Addressables 时每次至少一帧，编辑器 Fast Mode 再各加 0.1 s）。
    ///
    /// <para><b>缓存解析结果而非文本</b>：<c>DocumentAssembler.Assemble</c> 本来就按 src 查已解析文档，
    /// 而展开层全程克隆、不改写输入 IR（<c>TemplateExpander</c> / <c>StyleMerger</c>）；<c>ThemeStore</c> 还靠
    /// 「同 src 得到同一个 <c>ThemeBlock</c> 实例」判定主题块没变（spec §6.1）。</para>
    ///
    /// <para><b>在飞去重</b>：同一 src 的并发请求共享一次取源——第二个请求者挂一个
    /// <see cref="AwaitableCompletionSource{T}"/> 等着（<see cref="Awaitable{T}"/> 只能 await 一次，
    /// 不能把首个请求的句柄再给别人）。失败不缓存、同一个异常发给所有等待者，下次请求重新取。</para>
    ///
    /// <para><b>谁清</b>：<c>UI.UnloadAll</c>（连带 Play→Stop→Play 与切场景）与 <c>UI.ResetForTests</c> 清空；
    /// 编辑器热重载 <c>HotReload.NotifyAssetChanged</c> 先失效改动的 src；<c>ReloadAsync</c> /
    /// <c>ReloadCommonLibraryAsync</c> 失效入口 + 整个闭包（reload = 重读）。失效发生在某次取源进行中时，
    /// 那次结果只发给等待者、不落缓存（<see cref="_epoch"/>）。</para>
    ///
    /// <para>只覆盖经 <c>UI.SourceResolver</c> 的 src；模态框的入口 xml 在调用方手上、label 未必是真 src，
    /// 由 <c>UI</c> 组合 fetch 时绕过（spec §2.D）。spec：2026-09-14 document-load §5。</para>
    /// </summary>
    internal static class DocumentCache
    {
        private static readonly Dictionary<string, UIDocument> s_docs = new();
        private static readonly Dictionary<string, List<AwaitableCompletionSource<UIDocument>>> s_inflight = new();
        // 任何一次 Clear / Invalidate 都自增；取源开始时记下，完成时不一致就不落缓存
        private static int s_epoch;

        internal static int Count => s_docs.Count;

        internal static bool Contains(string src) => s_docs.ContainsKey(src);

        internal static Awaitable<UIDocument> GetOrFetchAsync(
            string src, Func<string, Awaitable<string>> resolver)
        {
            if (s_docs.TryGetValue(src, out var doc))
                return AwaitableHelpers.Completed(doc);

            var waiter = new AwaitableCompletionSource<UIDocument>();
            if (s_inflight.TryGetValue(src, out var waiters))
            {
                waiters.Add(waiter);
                return waiter.Awaitable;
            }

            s_inflight[src] = new List<AwaitableCompletionSource<UIDocument>> { waiter };
            _ = FetchAsync(src, resolver, s_epoch);
            return waiter.Awaitable;
        }

        internal static void Invalidate(string src)
        {
            if (s_docs.Remove(src)) s_epoch++;
            else if (s_inflight.ContainsKey(src)) s_epoch++;
        }

        internal static void Clear()
        {
            s_docs.Clear();
            s_epoch++;
        }

        private static async Awaitable FetchAsync(
            string src, Func<string, Awaitable<string>> resolver, int epoch)
        {
            UIDocument doc = null;
            Exception error = null;
            try
            {
                if (resolver == null)
                    throw new InvalidOperationException(
                        "UI.SourceResolver is not set; required for src-based loading");
                doc = DocumentLoader.ParseSource(await resolver(src), src);
            }
            catch (Exception e) { error = e; }

            // 先摘表再唤醒：等待者的续体会同步跑，可能再次请求同一个 src
            var waiters = s_inflight[src];
            s_inflight.Remove(src);
            if (error == null && epoch == s_epoch)
                s_docs[src] = doc;

            foreach (var w in waiters)
            {
                if (error != null) w.SetException(error);
                else w.SetResult(doc);
            }
        }
    }
}
