using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Answers "what attributes will this node actually have?" for rules that reason about a node's
    /// configuration rather than a single written value — <see cref="GlassRules"/> above all, which
    /// has to know whether a node ends up in glass mode.
    ///
    /// <para>Reading only <see cref="ElementNode.Attributes"/> is wrong here, because
    /// <c>&lt;Style&gt;</c> / <c>class=</c> is the idiomatic way to carry these attributes — it is
    /// what the shipped skin sample does and what the glass documentation tells authors to do. A rule
    /// blind to styles reports a correct layout as broken, and the CLI turns that into a non-zero
    /// exit code, so the false positive costs more than the silent attribute the rule guards.</para>
    ///
    /// <para><b>What cannot be known stays unknown.</b> A class naming a style this document does not
    /// declare almost certainly comes from an imported commons library, which the single-file CLI
    /// never sees — so the node is reported as <see cref="IsUncertain"/> and structural rules skip it
    /// rather than guess. That is the same reasoning behind <see cref="StyleRules"/> deliberately
    /// having no "unknown class name" check.</para>
    /// </summary>
    public sealed class StyleAttributeView
    {
        /// <summary>No styles known — every class reference is unresolvable.</summary>
        public static readonly StyleAttributeView Empty = new StyleAttributeView(null);

        private static readonly char[] ClassSeparators = { ' ', '\t', '\n', '\r' };
        private static readonly (string Variant, string Value)[] NoVariants =
            System.Array.Empty<(string, string)>();

        private readonly IReadOnlyDictionary<string, StyleDef> _styles;

        public StyleAttributeView(IReadOnlyDictionary<string, StyleDef> styles) => _styles = styles;

        /// <summary>
        /// True when the node pulls in a class whose contents this document cannot resolve — an
        /// imported style, or a name still holding a <c>{{param}}</c> placeholder. Callers should
        /// treat every "does this node declare X" answer as unreliable and stay quiet.
        /// </summary>
        public bool IsUncertain(ElementNode n)
        {
            if (n == null) return false;
            if (!n.Attributes.TryGetValue(StyleClassAttr, out var value)) return false;
            if (value == null) return false;
            if (value.Contains("{{")) return true;

            foreach (var reference in Split(value))
                if (Find(reference) == null) return true;
            return false;
        }

        /// <summary>The node has this attribute, whether written inline or pulled in by a class.</summary>
        public bool Declares(ElementNode n, string attr)
        {
            if (n == null) return false;
            if (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr)) return true;
            return Provider(n, attr) != null;
        }

        /// <summary>
        /// The attribute's base value and variant overrides as the merge will leave them: the node's
        /// own declaration of that name wins outright (atomically, base and variants together), and
        /// otherwise the right-most class declaring it does.
        /// </summary>
        public void Resolve(ElementNode n, string attr,
                            out string baseValue,
                            out IReadOnlyList<(string Variant, string Value)> variants)
        {
            baseValue = null;
            variants = NoVariants;
            if (n == null) return;

            // Atomic by name, mirroring StyleMerger: a node that spells out `glass.mobile` alone
            // masks a class's plain `glass` too, so both halves have to come from the same source.
            if (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr))
            {
                n.Attributes.TryGetValue(attr, out baseValue);
                if (n.VariantOverrides.TryGetValue(attr, out var own)) variants = own;
                return;
            }

            var style = Provider(n, attr);
            if (style == null) return;
            style.Attributes.TryGetValue(attr, out baseValue);
            if (style.VariantOverrides.TryGetValue(attr, out var fromStyle)) variants = fromStyle;
        }

        internal const string StyleClassAttr = "class";

        /// <summary>The right-most class declaring <paramref name="attr"/>, or null.</summary>
        private StyleDef Provider(ElementNode n, string attr)
        {
            if (!n.Attributes.TryGetValue(StyleClassAttr, out var value) || value == null) return null;

            var names = Split(value);
            // Right-to-left: a later class masks an earlier one for any name it declares.
            for (var i = names.Length - 1; i >= 0; i--)
            {
                var style = Find(names[i]);
                if (style == null) continue;
                if (style.Attributes.ContainsKey(attr) || style.VariantOverrides.ContainsKey(attr))
                    return style;
            }
            return null;
        }

        private StyleDef Find(string reference)
        {
            if (_styles == null || reference == null) return null;
            return _styles.TryGetValue(reference, out var style) ? style : null;
        }

        private static string[] Split(string classValue)
            => classValue.Split(ClassSeparators, System.StringSplitOptions.RemoveEmptyEntries);
    }
}
