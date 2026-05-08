using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template {
    public static class TemplateExpander {
        // 通用属性集合：模板调用上写的这些不算 Param
        static readonly HashSet<string> CommonAttrs = new() {
            "anchor", "size", "width", "height", "margin", "pivot",
            "padding", "spacing",
            "hidden", "interactable",
        };

        /// <summary>
        /// Real entry point — takes the merged LoadedDoc produced by DocumentLoader.
        /// Uses (Namespace, Name) keyed lookup for template resolution.
        /// </summary>
        internal static UIDocument Expand(PromptUGUI.Application.DocumentLoader.LoadedDoc loaded) {
            foreach (var t in loaded.Templates.Values) ValidateSlotCount(t);

            var result = new UIDocument { Version = 1 };
            foreach (var kv in loaded.Templates)
                result.Templates[kv.Key.ToString()] = kv.Value;   // 调试可读，运行时不再用

            foreach (var s in loaded.Screens) {
                var newRoot = new ElementNode(s.Root.Tag, s.Root.Namespace);
                foreach (var c in s.Root.Children) {
                    EnsureNoSlot(c, $"Screen '{s.Name}'");
                    var ec = ExpandTree(c, loaded.Templates,
                                        new HashSet<PromptUGUI.Application.DocumentLoader.TemplateKey>());
                    if (ec != null) newRoot.Children.Add(ec);
                }
                var newScreen = new ScreenDef(s.Name, newRoot) { CanvasMode = s.CanvasMode };
                foreach (var block in s.Variants) {
                    var newBlock = new VariantBlock(block.When);
                    foreach (var add in block.Adds) {
                        var newAdd = new AddDirective {
                            IntoPath = add.IntoPath,
                            At = add.At,
                        };
                        foreach (var ch in add.Children) {
                            EnsureNoSlot(ch, $"<Variant when='{block.When}'> in Screen '{s.Name}'");
                            var ec = ExpandTree(ch, loaded.Templates,
                                                new HashSet<PromptUGUI.Application.DocumentLoader.TemplateKey>());
                            if (ec != null) newAdd.Children.Add(ec);
                        }
                        newBlock.Adds.Add(newAdd);
                    }
                    newScreen.Variants.Add(newBlock);
                }
                result.Screens.Add(newScreen);
            }
            return result;
        }

        /// <summary>
        /// Backward-compat adapter: wraps a UIDocument (single-string keyed) and calls the real Expand.
        /// All M1/M2/M3 callers continue to work unchanged.
        /// </summary>
        public static UIDocument Expand(UIDocument doc) {
            var loaded = new PromptUGUI.Application.DocumentLoader.LoadedDoc {
                EntrySrc = "<inline>",
            };
            foreach (var s in doc.Screens) loaded.Screens.Add(s);
            foreach (var kv in doc.Templates)
                loaded.Templates[new PromptUGUI.Application.DocumentLoader.TemplateKey(null, kv.Key)] = kv.Value;
            return Expand(loaded);
        }

        static void ValidateSlotCount(TemplateDef tpl) {
            int count = 0;
            CountSlots(tpl.Body, ref count);
            if (count > 1)
                throw new TemplateException(
                    $"<Template name='{tpl.Name}'>: at most one <Slot/> allowed (found {count})");
        }

        static void CountSlots(ElementNode n, ref int count) {
            if (n.Tag == "Slot") count++;
            foreach (var c in n.Children) CountSlots(c, ref count);
        }

        static void EnsureNoSlot(ElementNode n, string contextLabel) {
            if (n.Tag == "Slot")
                throw new TemplateException(
                    $"<Slot/> is only allowed inside <Template>, but found in {contextLabel}");
            foreach (var c in n.Children) EnsureNoSlot(c, contextLabel);
        }

        static ElementNode ExpandTree(
            ElementNode src,
            IReadOnlyDictionary<PromptUGUI.Application.DocumentLoader.TemplateKey, TemplateDef> templates,
            HashSet<PromptUGUI.Application.DocumentLoader.TemplateKey> visiting) {

            var key = new PromptUGUI.Application.DocumentLoader.TemplateKey(src.Namespace, src.Tag);
            if (templates.TryGetValue(key, out var tpl))
                return ExpandInvocation(src, tpl, key, templates, visiting);

            // Namespace was specified but no matching template → error
            if (src.Namespace != null)
                throw new TemplateException(
                    $"unknown template '{src.Namespace}.{src.Tag}'");

            var dst = new ElementNode(src.Tag, src.Namespace) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            CopyVariantOverrides(src, dst);
            foreach (var c in src.Children) {
                var ec = ExpandTree(c, templates, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        static ElementNode ExpandInvocation(
            ElementNode invocation,
            TemplateDef tpl,
            PromptUGUI.Application.DocumentLoader.TemplateKey key,
            IReadOnlyDictionary<PromptUGUI.Application.DocumentLoader.TemplateKey, TemplateDef> templates,
            HashSet<PromptUGUI.Application.DocumentLoader.TemplateKey> visiting) {

            // Cycle tracking uses (Namespace, Name) key to allow same-named templates across different namespaces
            if (!visiting.Add(key))
                throw new TemplateException(
                    $"cyclic template reference detected: {string.Join(" → ", visiting)} → {tpl.Name}");

            try {
                var args = new Dictionary<string, string>();
                foreach (var p in tpl.Params) {
                    if (invocation.Attributes.TryGetValue(p.Name, out var v))
                        args[p.Name] = v;
                    else if (p.HasDefault)
                        args[p.Name] = p.DefaultValue;
                    else
                        throw new TemplateException(
                            $"<{tpl.Name}>: required <Param name='{p.Name}'> not provided");
                }

                foreach (var kv in invocation.Attributes) {
                    if (CommonAttrs.Contains(kv.Key)) continue;
                    if (args.ContainsKey(kv.Key)) continue;
                    throw new TemplateException(
                        $"<{tpl.Name}>: unknown attribute '{kv.Key}'");
                }

                foreach (var kv in invocation.VariantOverrides) {
                    if (CommonAttrs.Contains(kv.Key)) continue;
                    if (args.ContainsKey(kv.Key))
                        throw new TemplateException(
                            $"<{tpl.Name}>: variant override on template parameter '{kv.Key}' " +
                            $"is not supported (only common attributes like anchor/size/margin " +
                            $"can carry .variant suffixes on a template invocation)");
                    throw new TemplateException(
                        $"<{tpl.Name}>: unknown attribute '{kv.Key}' (with variant suffix)");
                }

                var slotContent = new List<ElementNode>();
                foreach (var c in invocation.Children) {
                    var ec = ExpandTree(c, templates, visiting);
                    if (ec != null) slotContent.Add(ec);
                }

                var instanceRoot = ExpandNode(tpl.Body, args, slotContent, templates, visiting);
                if (instanceRoot == null)
                    throw new TemplateException(
                        $"<{tpl.Name}>: template body root was excluded by if; not allowed");

                instanceRoot.IsTemplateInstanceRoot = true;
                if (!string.IsNullOrEmpty(invocation.Id))
                    instanceRoot.Id = invocation.Id;
                foreach (var kv in invocation.Attributes) {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    instanceRoot.Attributes[kv.Key] = kv.Value;
                }
                foreach (var kv in invocation.VariantOverrides) {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    if (!instanceRoot.VariantOverrides.TryGetValue(kv.Key, out var list)) {
                        list = new List<(string Variant, string Value)>();
                        instanceRoot.VariantOverrides[kv.Key] = list;
                    }
                    list.AddRange(kv.Value);
                }

                return instanceRoot;
            } finally {
                visiting.Remove(key);
            }
        }

        static ElementNode ExpandNode(
            ElementNode src,
            IReadOnlyDictionary<string, string> args,
            IReadOnlyList<ElementNode> slotContent,
            IReadOnlyDictionary<PromptUGUI.Application.DocumentLoader.TemplateKey, TemplateDef> templates,
            HashSet<PromptUGUI.Application.DocumentLoader.TemplateKey> visiting) {

            if (src.Attributes.TryGetValue("if", out var rawIf)) {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            ElementNode prepared = SubstituteAttrs(src, args);

            var key2 = new PromptUGUI.Application.DocumentLoader.TemplateKey(prepared.Namespace, prepared.Tag);
            if (templates.ContainsKey(key2))
                return ExpandTree(prepared, templates, visiting);

            var dst = new ElementNode(prepared.Tag, prepared.Namespace) {
                Id = prepared.Id,
                TextContent = Substitution.Apply(prepared.TextContent, args),
            };
            foreach (var kv in prepared.Attributes) {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = kv.Value;
            }
            CopyVariantOverrides(prepared, dst);
            foreach (var c in src.Children) {
                if (c.Tag == "Slot") {
                    if (slotContent != null)
                        foreach (var sc in slotContent)
                            dst.Children.Add(DeepClone(sc));
                    continue;
                }
                var ec = ExpandNode(c, args, slotContent, templates, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        static ElementNode SubstituteAttrs(ElementNode src,
                                           IReadOnlyDictionary<string, string> args) {
            var dst = new ElementNode(src.Tag, src.Namespace) {
                Id = src.Id,
                TextContent = src.TextContent,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            foreach (var kv in src.VariantOverrides) {
                var newList = new List<(string Variant, string Value)>();
                foreach (var (variant, value) in kv.Value)
                    newList.Add((variant, Substitution.Apply(value, args)));
                dst.VariantOverrides[kv.Key] = newList;
            }
            foreach (var c in src.Children)
                dst.Children.Add(c);
            return dst;
        }

        static void CopyVariantOverrides(ElementNode src, ElementNode dst) {
            foreach (var kv in src.VariantOverrides)
                dst.VariantOverrides[kv.Key] =
                    new List<(string Variant, string Value)>(kv.Value);
        }

        static ElementNode DeepClone(ElementNode src) {
            var dst = new ElementNode(src.Tag, src.Namespace) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes) dst.Attributes[kv.Key] = kv.Value;
            CopyVariantOverrides(src, dst);
            foreach (var c in src.Children) dst.Children.Add(DeepClone(c));
            return dst;
        }
    }
}
