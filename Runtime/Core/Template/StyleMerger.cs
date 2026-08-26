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
    /// The expanded tree carries no <c>class</c> attribute, so ScreenInstantiator and everything
    /// downstream stay unaware that styles exist. That is what makes this feature free at runtime.
    /// </summary>
    internal static class StyleMerger
    {
        public const string ClassAttr = "class";

        private static readonly char[] ClassSeparators = { ' ', '\t', '\n', '\r' };

        /// <summary>
        /// Returns <paramref name="src"/> unchanged when it carries no <c>class</c> (the common
        /// case, zero allocation); otherwise a copy with the pack merged in and <c>class</c> removed.
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

            var names = classValue == null
                ? System.Array.Empty<string>()
                : classValue.Split(ClassSeparators, System.StringSplitOptions.RemoveEmptyEntries);

            if (names.Length == 0)
                throw new TemplateException(
                    $"<{src.Tag}>: class=\"{classValue}\" names no style " +
                    "(write class=\"some-style\" or drop the attribute)");

            // Fold left→right: a later class fully masks an earlier one for any attribute NAME it
            // declares, in either base or .variant form (same atomic rule inline attributes get).
            var packAttrs = new Dictionary<string, string>();
            var packVariants = new Dictionary<string, List<(string Variant, string Value)>>();

            foreach (var reference in names)
            {
                var style = Lookup(reference, styles, src);
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

            var dst = CloneWithoutClass(src);

            foreach (var kv in packAttrs)
            {
                if (NodeDeclares(src, kv.Key)) continue;
                if (invocationTarget != null && !AcceptsBase(kv.Key, invocationTarget)) continue;
                dst.Attributes[kv.Key] = kv.Value;
            }

            foreach (var kv in packVariants)
            {
                if (NodeDeclares(src, kv.Key)) continue;
                // On a template invocation only common attributes may carry .variant suffixes
                // (ExpandInvocation rejects variant overrides on Params). Filtering here keeps a
                // broadcast style from turning into that error on an unrelated node.
                if (invocationTarget != null && !TemplateExpander.CommonAttrs.Contains(kv.Key)) continue;
                dst.VariantOverrides[kv.Key] = kv.Value;
            }

            return dst;
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

        private static ElementNode CloneWithoutClass(ElementNode src)
        {
            var dst = new ElementNode(src.Tag, src.Namespace)
            {
                OriginSrc = src.OriginSrc,
                Id = src.Id,
                TextContent = src.TextContent,
                TextContentRaw = src.TextContentRaw,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
            {
                if (kv.Key == ClassAttr) continue;
                dst.Attributes[kv.Key] = kv.Value;
            }
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
