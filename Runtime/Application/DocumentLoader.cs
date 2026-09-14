using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 把一个 src 解析成"已合并 Templates 与 Screens 的 IR 文档"。
    /// 递归解析其 Import 链；同 src 在一次 Load 内只解析一次（cache）；A→B→A 循环报错。
    /// 不接触 commons pool；不入 depGraph。这两件事由 UI 上层负责。
    ///
    /// <para>这一层只负责 <b>取</b>：把 <c>SourceResolver</c> 的异步取字符串 + 解析收拢成一张
    /// src → <see cref="UIDocument"/> 的表（<see cref="PrefetchAsync"/>），随后把合并语义整个交给
    /// 纯 C# 的 <see cref="Template.DocumentAssembler"/>。这样 UIXmlLint CLI 能用同一份合并实现跟进
    /// <c>&lt;Import&gt;</c>，而不必在 Unity 之外重写一遍 —— 见 2026-08-26 theme-driven-style spec §9。</para>
    ///
    /// <para><b>取是并行的</b>（2026-09-14 document-load spec §4）：一份文档到手后，它的全部
    /// <c>&lt;Import&gt;</c> 同层一起起飞，往返轮数 = Import 图深度 + 1，而不是文件数。宿主用 Addressables
    /// 时每次取源至少一帧（编辑器 Fast Mode 还各加 0.1 s 模拟延迟），8 个文件顺序取曾占面板首开 1.24 s 里的 0.95 s。</para>
    ///
    /// <para>两套入口：<c>Load*Async(src, resolver, …)</c> 接 <c>Func&lt;string, Awaitable&lt;string&gt;&gt;</c>
    /// （取字符串，这里解析）；<c>Load*ParsedAsync(src, fetch, …)</c> 接 <c>Func&lt;string, Awaitable&lt;UIDocument&gt;&gt;</c>
    /// （取 + 解析一体，给 <c>UI</c> 挂 <c>DocumentCache</c> 用）。名字分开是因为 <c>null</c> 实参对两种委托都能转换，
    /// 同名重载会让 <c>LoadAsync("x", null, …)</c> 产生二义。</para>
    /// </summary>
    internal static class DocumentLoader
    {
        internal static Awaitable<LoadedDoc> LoadAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            bool allowScreens)
        {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");
            return LoadParsedAsync(src, ParsingFetch(resolver), allowScreens);
        }

        internal static async Awaitable<LoadedDoc> LoadParsedAsync(
            string src,
            Func<string, Awaitable<UIDocument>> fetch,
            bool allowScreens)
        {
            if (fetch == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");

            var parsed = new Dictionary<string, UIDocument>();
            await PrefetchAsync(src, fetch, parsed, new HashSet<string>());
            return DocumentAssembler.Assemble(
                src, s => parsed.TryGetValue(s, out var d) ? d : null, allowScreens);
        }

        internal static Awaitable<LoadedDoc> LoadAndMergeAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool,
            IReadOnlyDictionary<StyleKey, StyleDef> commonsStyles = null)
        {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");
            return LoadAndMergeParsedAsync(src, ParsingFetch(resolver), commonsPool, commonsStyles);
        }

        internal static async Awaitable<LoadedDoc> LoadAndMergeParsedAsync(
            string src,
            Func<string, Awaitable<UIDocument>> fetch,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool,
            IReadOnlyDictionary<StyleKey, StyleDef> commonsStyles = null)
        {
            var loaded = await LoadParsedAsync(src, fetch, allowScreens: true);
            DocumentAssembler.MergeCommons(loaded, commonsPool, commonsStyles);
            return loaded;
        }

        /// <summary>把"取字符串"的 resolver 包成"取 + 解析"的 fetch。</summary>
        internal static Func<string, Awaitable<UIDocument>> ParsingFetch(
            Func<string, Awaitable<string>> resolver) =>
            async src => ParseSource(await resolver(src), src);

        /// <summary>
        /// 一份源文的解析 + 错误包装：空源报 <see cref="System.IO.IOException"/>，
        /// 解析器之外的异常包成带 src 的 <see cref="ParseException"/>。resolver 路径与 <c>DocumentCache</c>
        /// 共用，保证两边报错文案一致。
        /// </summary>
        internal static UIDocument ParseSource(string xml, string src)
        {
            if (string.IsNullOrEmpty(xml))
                throw new System.IO.IOException(
                    $"SourceResolver returned null/empty for src='{src}'");
            try { return UIDocumentParser.Parse(xml, src); }
            catch (ParseException) { throw; }
            catch (Exception e)
            {
                throw new ParseException($"parsing src='{src}' failed: {e.Message}", e);
            }
        }

        /// <summary>
        /// Over the Import graph, fetching and parsing each src exactly once: a document's imports are
        /// all started as soon as the document itself has arrived (same level in parallel), then awaited
        /// together — a failure in one still lets the others settle, and the first one in declaration
        /// order is what gets reported (<see cref="AwaitableHelpers.WhenAll"/>).
        /// <paramref name="started"/> is what stops a diamond from being fetched twice and makes a cycle
        /// terminate here; <see cref="Template.DocumentAssembler"/> is the one that reports the cycle —
        /// keeping that diagnostic in the shared layer so the CLI produces the identical message.
        /// </summary>
        private static async Awaitable PrefetchAsync(
            string src,
            Func<string, Awaitable<UIDocument>> fetch,
            Dictionary<string, UIDocument> parsed,
            HashSet<string> started)
        {
            if (!started.Add(src)) return;

            var doc = await fetch(src);
            parsed[src] = doc;

            List<Awaitable> pending = null;
            foreach (var imp in doc.Imports)
            {
                if (started.Contains(imp.Src)) continue;
                pending ??= new List<Awaitable>();
                // 同步跑到它的第一个 await 为止：imp.Src 在这一行就进了 started，
                // 同一文档里重复的 Import 在下一轮循环被跳过
                pending.Add(PrefetchAsync(imp.Src, fetch, parsed, started));
            }
            if (pending != null)
                await AwaitableHelpers.WhenAll(pending);
        }
    }
}
