using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template
{
    /// <summary>
    /// Folds theme-scoped <c>&lt;Style&gt;</c> packs over the global ones to produce the style table
    /// that <see cref="StyleMerger"/> should see for a given active theme (2026-08-26 spec §4.2).
    ///
    /// <para><b>The global style is the implicit root of every theme chain.</b> Folding order is
    /// global → the chain's most distant <c>base=</c> → … → the active theme. Three things follow,
    /// and they are the reason for this shape:</para>
    /// <list type="number">
    /// <item>A project that declares no theme styles gets <paramref name="globalStyles"/> back
    /// unchanged — byte-for-byte today's behaviour, no cost.</item>
    /// <item>The residue problem (spec §6.1) mostly dissolves: the global pack supplies the baseline
    /// attribute-name set, so a theme that omits <c>radius</c> falls back to the global value rather
    /// than leaving the control stuck at the previous theme's.</item>
    /// <item>Authors get a simple model — the global <c>&lt;Style&gt;</c> says what a component IS,
    /// a theme's says which few parameters differ under that skin.</item>
    /// </list>
    ///
    /// <para>Folding is <b>atomic per attribute name</b>, the same rule the multi-class fold in
    /// <see cref="StyleMerger"/> uses: an override declaring <c>radius.mobile</c> masks a lower
    /// layer's <c>radius</c> too, because a name is claimed as a whole or not at all.</para>
    /// </summary>
    internal static class ThemeStyleResolver
    {
        /// <summary>
        /// Returns <paramref name="globalStyles"/> itself (no allocation) when there is nothing to
        /// fold: no active theme, no themes at all, or a chain that declares no styles.
        /// </summary>
        public static IReadOnlyDictionary<StyleKey, StyleDef> Resolve(
            IReadOnlyDictionary<StyleKey, StyleDef> globalStyles,
            IReadOnlyDictionary<string, ThemeBlock> themes,
            string activeTheme)
        {
            if (string.IsNullOrEmpty(activeTheme) || themes == null || themes.Count == 0)
                return globalStyles;

            var chain = ChainRootFirst(themes, activeTheme);
            if (chain == null) return globalStyles;

            Dictionary<StyleKey, StyleDef> folded = null;
            foreach (var theme in chain)
            {
                foreach (var kv in theme.Styles)
                {
                    folded ??= globalStyles == null
                        ? new Dictionary<StyleKey, StyleDef>()
                        : new Dictionary<StyleKey, StyleDef>(globalStyles);

                    // Theme styles address the un-namespaced pool. A commons library imported with
                    // as="ui" keys its styles StyleKey("ui", name) and class="ui:card" reaches them
                    // directly; a theme has no namespace of its own to match that with, so themed
                    // overrides of a namespaced style are simply not a thing yet. See spec §4.3.
                    var key = new StyleKey(null, kv.Key);
                    folded[key] = folded.TryGetValue(key, out var lower)
                        ? FoldOver(lower, kv.Value)
                        : kv.Value;
                }
            }
            return folded ?? globalStyles;
        }

        /// <summary>
        /// The <c>base=</c> chain from the most distant ancestor down to <paramref name="leaf"/>, so
        /// callers can fold in precedence order. Null when the leaf is unknown. Cycle- and
        /// missing-base-tolerant on purpose: <c>ThemeStore.ResolveBases</c> already reports both with
        /// a good message at registration time, and the lint CLI must not crash on markup it is
        /// being asked to diagnose.
        /// </summary>
        private static List<ThemeBlock> ChainRootFirst(
            IReadOnlyDictionary<string, ThemeBlock> themes, string leaf)
        {
            if (!themes.TryGetValue(leaf, out var block)) return null;

            var chain = new List<ThemeBlock>();
            var seen = new HashSet<string>();
            for (var cur = block; cur != null && seen.Add(cur.Name);)
            {
                chain.Add(cur);
                cur = string.IsNullOrEmpty(cur.BaseName) || !themes.TryGetValue(cur.BaseName, out var b)
                    ? null
                    : b;
            }
            chain.Reverse();
            return chain;
        }

        private static StyleDef FoldOver(StyleDef lower, StyleDef upper)
        {
            var merged = new StyleDef(lower.Name) { OriginSrc = upper.OriginSrc ?? lower.OriginSrc };

            foreach (var kv in lower.Attributes)
                merged.Attributes[kv.Key] = kv.Value;
            foreach (var kv in lower.VariantOverrides)
                merged.VariantOverrides[kv.Key] = new List<(string, string)>(kv.Value);

            // Atomic per name: declaring radius.mobile claims the whole `radius` slot.
            foreach (var name in upper.DeclaredNames)
            {
                merged.Attributes.Remove(name);
                merged.VariantOverrides.Remove(name);
            }

            foreach (var kv in upper.Attributes)
                merged.Attributes[kv.Key] = kv.Value;
            foreach (var kv in upper.VariantOverrides)
                merged.VariantOverrides[kv.Key] = new List<(string, string)>(kv.Value);

            return merged;
        }
    }
}
