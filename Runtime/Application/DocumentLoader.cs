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
    /// </summary>
    internal static class DocumentLoader
    {
        internal static async Awaitable<LoadedDoc> LoadAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            bool allowScreens)
        {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");

            var parsed = new Dictionary<string, UIDocument>();
            await PrefetchAsync(src, resolver, parsed);
            return DocumentAssembler.Assemble(
                src, s => parsed.TryGetValue(s, out var d) ? d : null, allowScreens);
        }

        internal static async Awaitable<LoadedDoc> LoadAndMergeAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool,
            IReadOnlyDictionary<StyleKey, StyleDef> commonsStyles = null)
        {
            var loaded = await LoadAsync(src, resolver, allowScreens: true);
            DocumentAssembler.MergeCommons(loaded, commonsPool, commonsStyles);
            return loaded;
        }

        /// <summary>
        /// Depth-first over the Import graph, fetching and parsing each src exactly once.
        /// Deliberately does NOT detect cycles: recording the document before recursing makes a
        /// cycle terminate here, and <see cref="Template.DocumentAssembler"/> is the one that reports it —
        /// keeping that diagnostic in the shared layer so the CLI produces the identical message.
        /// </summary>
        private static async Awaitable PrefetchAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            Dictionary<string, UIDocument> parsed)
        {
            if (parsed.ContainsKey(src)) return;

            var xml = await resolver(src);
            if (string.IsNullOrEmpty(xml))
                throw new System.IO.IOException(
                    $"SourceResolver returned null/empty for src='{src}'");

            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xml); }
            catch (ParseException) { throw; }
            catch (Exception e)
            {
                throw new ParseException($"parsing src='{src}' failed: {e.Message}", e);
            }

            parsed[src] = doc;

            foreach (var imp in doc.Imports)
                await PrefetchAsync(imp.Src, resolver, parsed);
        }
    }
}
