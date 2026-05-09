using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

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
        }

        internal readonly struct TemplateKey : IEquatable<TemplateKey>
        {
            public readonly string Namespace;   // null = 裸名
            public readonly string Name;
            public TemplateKey(string ns, string name) { Namespace = ns; Name = name; }
            public bool Equals(TemplateKey o) => Namespace == o.Namespace && Name == o.Name;
            public override bool Equals(object o) => o is TemplateKey k && Equals(k);
            public override int GetHashCode() =>
                (Namespace?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0);
            public override string ToString() =>
                Namespace == null ? Name : $"{Namespace}.{Name}";
        }

        internal static LoadedDoc Load(string src,
                                       Func<string, string> resolver,
                                       bool allowScreens)
        {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");

            var loaded = new LoadedDoc { EntrySrc = src };
            var visiting = new Stack<string>();
            LoadInternal(src, resolver, allowScreens, loaded, visiting,
                         applyNamespace: null);
            return loaded;
        }

        /// <summary>
        /// 加载 src 并把 commons 池合并进 LoadedDoc.Templates。供 LoadDocumentFromSrc 与 Reload 复用。
        /// commons 与 entry 同名 → 抛 TemplateException。
        /// </summary>
        internal static LoadedDoc LoadAndMerge(
            string src,
            Func<string, string> resolver,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool)
        {

            var loaded = Load(src, resolver, allowScreens: true);

            foreach (var kv in commonsPool)
            {
                if (loaded.Templates.ContainsKey(kv.Key))
                    throw new TemplateException(
                        $"template '{kv.Key}' conflicts with commons pool");
                loaded.Templates[kv.Key] = kv.Value;
            }
            return loaded;
        }

        private static void LoadInternal(
            string src,
            Func<string, string> resolver,
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
            if (!agg.AllSrcs.Add(src)) return;   // already loaded once during this call

            var xml = resolver(src);
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

            // 当前文件的 Screens（仅 entry 允许）
            if (allowScreens)
            {
                foreach (var s in doc.Screens) agg.Screens.Add(s);
            }

            // 当前文件的 Templates 入合并表，按 applyNamespace 决定 ns
            foreach (var kv in doc.Templates)
            {
                var key = new TemplateKey(applyNamespace, kv.Key);
                if (agg.Templates.ContainsKey(key))
                    throw new TemplateException(
                        $"duplicate template '{key}' (loaded from src='{src}')");
                agg.Templates[key] = kv.Value;
            }

            // 递归解析 Imports
            visiting.Push(src);
            try
            {
                foreach (var imp in doc.Imports)
                {
                    var childNs = imp.Namespace ?? applyNamespace;
                    LoadInternal(imp.Src, resolver, allowScreens: false,
                                 agg, visiting, childNs);
                }
            }
            finally { visiting.Pop(); }
        }
    }
}
