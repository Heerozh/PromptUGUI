using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Template
{
    /// <summary>
    /// The <c>&lt;Import&gt;</c> merge algorithm, with the fetching taken out: given a way to look
    /// up an already-parsed <see cref="UIDocument"/> by src, it walks the import closure and folds
    /// everything into one <see cref="LoadedDoc"/> — namespace application (<c>as=</c>), cycle
    /// detection, cross-file duplicate detection, and the "imports may not declare
    /// <c>&lt;Screen&gt;</c>" rule.
    ///
    /// <para><b>Why the split.</b> Fetching is the only asynchronous part, and <c>Awaitable</c> is a
    /// Unity type. Keeping the merge SEMANTICS here — pure C#, no Unity — is what lets the UIXmlLint
    /// CLI follow <c>&lt;Import&gt;</c> and lint the same expanded tree ScreenInstantiator sees,
    /// instead of guessing at what an unseen commons library declares. Single source of truth: the
    /// Unity path (<c>DocumentLoader</c>, async prefetch) and the CLI path (filesystem prefetch)
    /// both land here. See the 2026-08-26 theme-driven-style spec §9.</para>
    ///
    /// <para><b>Namespace note.</b> This lives in <c>PromptUGUI.Template</c> — next to the expander
    /// it feeds — rather than in a <c>PromptUGUI.Loading</c> of its own: that name would shadow the
    /// public <c>PromptUGUI.Application.Modals.Loading</c> API for every file inside a
    /// <c>PromptUGUI.*</c> namespace, since C# resolves the bare identifier outward.</para>
    /// </summary>
    internal static class DocumentAssembler
    {
        /// <summary>
        /// <paramref name="lookup"/> must answer for <paramref name="entrySrc"/> and every src
        /// reachable from it by <c>&lt;Import&gt;</c>; returning null is reported as a hard error
        /// rather than silently producing a half-merged document.
        /// </summary>
        public static LoadedDoc Assemble(
            string entrySrc, Func<string, UIDocument> lookup, bool allowScreens)
        {
            var loaded = new LoadedDoc { EntrySrc = entrySrc };
            MergeInto(entrySrc, lookup, allowScreens, loaded, new Stack<string>(),
                      applyNamespace: null);
            return loaded;
        }

        /// <summary>
        /// Folds the commons pool onto an already-assembled document. A name declared on both sides
        /// is a hard conflict — commons are meant to be the shared baseline, so silently letting the
        /// entry document shadow one would make the same markup mean different things per screen.
        /// </summary>
        public static void MergeCommons(
            LoadedDoc loaded,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool,
            IReadOnlyDictionary<StyleKey, StyleDef> commonsStyles = null)
        {
            if (commonsPool != null)
            {
                foreach (var kv in commonsPool)
                {
                    if (loaded.Templates.ContainsKey(kv.Key))
                        throw new TemplateException(
                            $"template '{kv.Key}' conflicts with commons pool");
                    loaded.Templates[kv.Key] = kv.Value;
                }
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
        }

        private static void MergeInto(
            string src,
            Func<string, UIDocument> lookup,
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

            var doc = lookup(src)
                ?? throw new ParseException(
                    $"no parsed document available for src='{src}' (the caller must supply every " +
                    "src reachable through <Import> before assembling)");

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
                    MergeInto(imp.Src, lookup, allowScreens: false, agg, visiting, childNs);
                }
            }
            finally { visiting.Pop(); }
        }
    }
}
