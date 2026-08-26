using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template
{
    /// <summary>
    /// Consumes <c>class="a b"</c> by merging the referenced <see cref="StyleDef"/> attribute packs
    /// onto a node. Runs as a step of <see cref="TemplateExpander"/>'s existing deep copy — no extra
    /// tree walk, and (because it only ever writes to the copy) a shared commons template body can
    /// never be polluted in place.
    ///
    /// The expanded tree keeps <c>class</c> so a theme switch can re-derive the pack
    /// (<see cref="ReMerge"/>), but the VALUES are already folded into Attributes — ScreenInstantiator
    /// and everything downstream still read plain attributes and stay unaware that styles exist.
    /// </summary>
    internal static class StyleMerger
    {
        public const string ClassAttr = "class";

        private static readonly char[] ClassSeparators = { ' ', '\t', '\n', '\r' };

        /// <summary>
        /// Returns <paramref name="src"/> unchanged when it carries no <c>class</c> (the common
        /// case, zero allocation); otherwise a copy with the pack merged in, <c>class</c> kept, and
        /// <see cref="ElementNode.StyleAttrNames"/> recording what the pack contributed.
        /// </summary>
        /// <param name="invocationTarget">
        /// Non-null when <paramref name="src"/> invokes a Template. A style is broadcast, so instead
        /// of the hard "unknown attribute" error inline attributes get, the pack is filtered down to
        /// what the invocation can actually accept: declared <c>&lt;Param&gt;</c>s plus
        /// <see cref="TemplateExpander.CommonAttrs"/>. Everything else is dropped silently.
        /// </param>
        public static ElementNode Apply(
            ElementNode src,
            IReadOnlyDictionary<StyleKey, StyleDef> styles,
            TemplateDef invocationTarget)
        {
            if (src == null || !src.Attributes.TryGetValue(ClassAttr, out var classValue))
                return src;

            ComputePack(classValue, styles, src, out var packAttrs, out var packVariants);

            var dst = CloneForMerge(src);
            var contributed = new HashSet<string>();

            foreach (var kv in packAttrs)
            {
                if (NodeDeclares(src, kv.Key)) continue;
                if (invocationTarget != null && !AcceptsBase(kv.Key, invocationTarget)) continue;
                dst.Attributes[kv.Key] = kv.Value;
                contributed.Add(kv.Key);
            }

            foreach (var kv in packVariants)
            {
                if (NodeDeclares(src, kv.Key)) continue;
                // On a template invocation only common attributes may carry .variant suffixes
                // (ExpandInvocation rejects variant overrides on Params). Filtering here keeps a
                // broadcast style from turning into that error on an unrelated node.
                if (invocationTarget != null && !TemplateExpander.CommonAttrs.Contains(kv.Key)) continue;
                dst.VariantOverrides[kv.Key] = kv.Value;
                contributed.Add(kv.Key);
            }

            dst.StyleAttrNames = contributed;
            return dst;
        }

        /// <summary>
        /// Recomputes an already-expanded node's pack against a different style table — what a theme
        /// switch runs. Idempotent, and safe to call on a node whose class was never merged.
        ///
        /// <para>Correctness rests on <see cref="ElementNode.StyleAttrNames"/>: dropping exactly the
        /// names the previous pack contributed leaves everything else in place — the author's inline
        /// attributes, and the common attributes a template invocation merged onto an instance root
        /// after the style pack was applied. Rebuilding from a snapshot of the inline values instead
        /// would silently lose that second group.</para>
        /// </summary>
        public static void ReMerge(
            ElementNode node, IReadOnlyDictionary<StyleKey, StyleDef> styles)
        {
            if (node == null) return;
            if (!node.Attributes.TryGetValue(ClassAttr, out var classValue)) return;

            if (node.StyleAttrNames != null)
            {
                foreach (var name in node.StyleAttrNames)
                {
                    node.Attributes.Remove(name);
                    node.VariantOverrides.Remove(name);
                }
            }

            ComputePack(classValue, styles, node, out var packAttrs, out var packVariants);

            // Snapshot what the NODE declares before laying the pack down. Testing the live node
            // instead would let the pack's own base value mask its matching .variant entry — Apply
            // avoids this for free by testing the pre-merge source node.
            var selfDeclared = new HashSet<string>(node.Attributes.Keys);
            foreach (var name in node.VariantOverrides.Keys) selfDeclared.Add(name);

            var contributed = new HashSet<string>();
            foreach (var kv in packAttrs)
            {
                if (selfDeclared.Contains(kv.Key)) continue;
                node.Attributes[kv.Key] = kv.Value;
                contributed.Add(kv.Key);
            }
            foreach (var kv in packVariants)
            {
                if (selfDeclared.Contains(kv.Key)) continue;
                node.VariantOverrides[kv.Key] = kv.Value;
                contributed.Add(kv.Key);
            }
            node.StyleAttrNames = contributed;
        }

        /// <summary>
        /// Folds <c>class="a b"</c> left to right. A later class fully masks an earlier one for any
        /// attribute NAME it declares, in either base or <c>.variant</c> form — the same atomic rule
        /// inline attributes get.
        /// </summary>
        private static void ComputePack(
            string classValue,
            IReadOnlyDictionary<StyleKey, StyleDef> styles,
            ElementNode context,
            out Dictionary<string, string> packAttrs,
            out Dictionary<string, List<(string Variant, string Value)>> packVariants)
        {
            var names = classValue == null
                ? System.Array.Empty<string>()
                : classValue.Split(ClassSeparators, System.StringSplitOptions.RemoveEmptyEntries);

            if (names.Length == 0)
                throw new TemplateException(
                    $"<{context.Tag}>: class=\"{classValue}\" names no style " +
                    "(write class=\"some-style\" or drop the attribute)");

            packAttrs = new Dictionary<string, string>();
            packVariants = new Dictionary<string, List<(string Variant, string Value)>>();

            foreach (var reference in names)
            {
                var style = Lookup(reference, styles, context);
                foreach (var name in style.DeclaredNames)
                {
                    packAttrs.Remove(name);
                    packVariants.Remove(name);
                }
                foreach (var kv in style.Attributes)
                    packAttrs[kv.Key] = kv.Value;
                foreach (var kv in style.VariantOverrides)
                    packVariants[kv.Key] = new List<(string, string)>(kv.Value);
            }
        }

        private static StyleDef Lookup(
            string reference,
            IReadOnlyDictionary<StyleKey, StyleDef> styles,
            ElementNode context)
        {
            var key = StyleKey.ParseReference(reference);
            if (styles != null && styles.TryGetValue(key, out var style)) return style;

            var known = new List<string>();
            if (styles != null)
                foreach (var k in styles.Keys) known.Add(k.ToString());
            known.Sort(System.StringComparer.Ordinal);

            throw new TemplateException(
                $"<{context.Tag}>: unknown style '{reference}' in class=\"" +
                context.Attributes[ClassAttr] + "\"" +
                (known.Count == 0
                    ? " (no <Style> is declared in this document or its imports)"
                    : $" (known: {string.Join(", ", known)})"));
        }

        /// <summary>An attribute name the node itself spells out — in either form — wins outright.</summary>
        private static bool NodeDeclares(ElementNode node, string attrName)
            => node.Attributes.ContainsKey(attrName) || node.VariantOverrides.ContainsKey(attrName);

        private static bool AcceptsBase(string attrName, TemplateDef tpl)
        {
            if (TemplateExpander.CommonAttrs.Contains(attrName)) return true;
            foreach (var p in tpl.Params)
                if (p.Name == attrName) return true;
            return false;
        }

        /// <summary>
        /// <c>class=</c> is deliberately KEPT on the copy: a theme switch re-derives the pack from
        /// it (<see cref="ReMerge"/>). No control declares an attribute by that name, so
        /// <c>ControlAttributeApplier</c> skips it like any other unknown one.
        /// </summary>
        private static ElementNode CloneForMerge(ElementNode src)
        {
            var dst = new ElementNode(src.Tag, src.Namespace)
            {
                OriginSrc = src.OriginSrc,
                StyleAttrNames = src.StyleAttrNames,
                Id = src.Id,
                TextContent = src.TextContent,
                TextContentRaw = src.TextContentRaw,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var kv in src.AttributesRaw)
                dst.AttributesRaw[kv.Key] = kv.Value;
            foreach (var kv in src.VariantOverrides)
                dst.VariantOverrides[kv.Key] = new List<(string, string)>(kv.Value);
            if (src.TextArgs != null)
                dst.TextArgs = new Dictionary<string, string>(src.TextArgs);
            // Children by reference: callers recurse into them separately, exactly as
            // TemplateExpander.SubstituteAttrs does.
            foreach (var c in src.Children)
                dst.Children.Add(c);
            return dst;
        }
    }
}
