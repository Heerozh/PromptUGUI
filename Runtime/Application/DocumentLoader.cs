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
    /// </summary>
    internal static class DocumentLoader
    {
        internal sealed class LoadedDoc
        {
            public string EntrySrc;
            public HashSet<string> AllSrcs = new();
            public List<ScreenDef> Screens = new();
            public Dictionary<TemplateKey, TemplateDef> Templates = new();
            public Dictionary<StyleKey, StyleDef> Styles = new();
            // Each entry carries the original src so cross-doc duplicate detection
            // can name both conflicting files in the error message.
            public List<(IR.ThemeBlock Theme, string Src)> Themes = new();
        }

        internal readonly struct TemplateKey : IEquatable<TemplateKey>
        {
            public readonly string Namespace;
            public readonly string Name;
            public TemplateKey(string ns, string name) { Namespace = ns; Name = name; }
            public bool Equals(TemplateKey o) => Namespace == o.Namespace && Name == o.Name;
            public override bool Equals(object o) => o is TemplateKey k && Equals(k);
            public override int GetHashCode() =>
                (Namespace?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0);
            public override string ToString() =>
                Namespace == null ? Name : $"{Namespace}.{Name}";
        }

        /// <summary>
        /// Mirrors <see cref="TemplateKey"/> for <c>&lt;Style&gt;</c>. Namespaced references are
        /// written <c>class="ui:card"</c> — a colon, not the dot templates use for tags, matching
        /// the <c>Set:Name</c> form authors already write for sprite / icon references.
        /// </summary>
        internal readonly struct StyleKey : IEquatable<StyleKey>
        {
            public const char NamespaceSeparator = ':';

            public readonly string Namespace;
            public readonly string Name;
            public StyleKey(string ns, string name) { Namespace = ns; Name = name; }
            public bool Equals(StyleKey o) => Namespace == o.Namespace && Name == o.Name;
            public override bool Equals(object o) => o is StyleKey k && Equals(k);
            public override int GetHashCode() =>
                (Namespace?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0);
            public override string ToString() =>
                Namespace == null ? Name : $"{Namespace}{NamespaceSeparator}{Name}";

            /// <summary>Splits an author-written <c>class</c> entry into (namespace, name).</summary>
            public static StyleKey ParseReference(string reference)
            {
                var sep = reference.IndexOf(NamespaceSeparator);
                return sep < 0
                    ? new StyleKey(null, reference)
                    : new StyleKey(reference.Substring(0, sep), reference.Substring(sep + 1));
            }
        }

        internal static async Awaitable<LoadedDoc> LoadAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            bool allowScreens)
        {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");

            var loaded = new LoadedDoc { EntrySrc = src };
            var visiting = new Stack<string>();
            await LoadInternalAsync(src, resolver, allowScreens, loaded, visiting,
                                    applyNamespace: null);
            return loaded;
        }

        internal static async Awaitable<LoadedDoc> LoadAndMergeAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool,
            IReadOnlyDictionary<StyleKey, StyleDef> commonsStyles = null)
        {
            var loaded = await LoadAsync(src, resolver, allowScreens: true);

            foreach (var kv in commonsPool)
            {
                if (loaded.Templates.ContainsKey(kv.Key))
                    throw new TemplateException(
                        $"template '{kv.Key}' conflicts with commons pool");
                loaded.Templates[kv.Key] = kv.Value;
            }
            if (commonsStyles != null)
            {
                foreach (var kv in commonsStyles)
                {
                    if (loaded.Styles.ContainsKey(kv.Key))
                        throw new TemplateException(
                            $"style '{kv.Key}' conflicts with commons pool");
                    loaded.Styles[kv.Key] = kv.Value;
                }
            }
            return loaded;
        }

        private static async Awaitable LoadInternalAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            bool allowScreens,
            LoadedDoc agg,
            Stack<string> visiting,
            string applyNamespace)
        {
            if (visiting.Contains(src))
            {
                var chain = string.Join(" → ", visiting);
                throw new ParseException(
                    $"cyclic Import detected: {chain} → {src}");
            }
            if (!agg.AllSrcs.Add(src)) return;

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

            if (!allowScreens && doc.Screens.Count > 0)
                throw new ParseException(
                    $"src='{src}' is loaded as common library / nested import; <Screen> not allowed");

            if (allowScreens)
            {
                foreach (var s in doc.Screens) agg.Screens.Add(s);
            }

            foreach (var kv in doc.Templates)
            {
                var key = new TemplateKey(applyNamespace, kv.Key);
                if (agg.Templates.ContainsKey(key))
                    throw new TemplateException(
                        $"duplicate template '{key}' (loaded from src='{src}')");
                agg.Templates[key] = kv.Value;
            }

            foreach (var kv in doc.Styles)
            {
                var key = new StyleKey(applyNamespace, kv.Key);
                if (agg.Styles.ContainsKey(key))
                    throw new TemplateException(
                        $"duplicate style '{key}' (loaded from src='{src}')");
                agg.Styles[key] = kv.Value;
            }

            foreach (var theme in doc.Themes)
                agg.Themes.Add((theme, src));

            visiting.Push(src);
            try
            {
                foreach (var imp in doc.Imports)
                {
                    var childNs = imp.Namespace ?? applyNamespace;
                    await LoadInternalAsync(imp.Src, resolver, allowScreens: false,
                                            agg, visiting, childNs);
                }
            }
            finally { visiting.Pop(); }
        }
    }
}
