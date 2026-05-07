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

        public static UIDocument Expand(UIDocument doc) {
            // 模板内 Slot 计数检查（一次性、与 instance 数无关）
            foreach (var t in doc.Templates.Values)
                ValidateSlotCount(t);

            var result = new UIDocument { Version = doc.Version };
            foreach (var kv in doc.Templates)
                result.Templates[kv.Key] = kv.Value;

            foreach (var s in doc.Screens) {
                var newRoot = new ElementNode(s.Root.Tag);
                foreach (var c in s.Root.Children) {
                    EnsureNoSlot(c, $"Screen '{s.Name}'");
                    var ec = ExpandTree(c, doc.Templates, new HashSet<string>());
                    if (ec != null) newRoot.Children.Add(ec);
                }
                result.Screens.Add(new ScreenDef(s.Name, newRoot));
            }
            return result;
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

        static ElementNode ExpandTree(ElementNode src,
                                      IReadOnlyDictionary<string, TemplateDef> templates,
                                      HashSet<string> visiting) {
            if (templates.TryGetValue(src.Tag, out var tpl))
                return ExpandInvocation(src, tpl, templates, visiting);

            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children) {
                var ec = ExpandTree(c, templates, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        static ElementNode ExpandInvocation(
            ElementNode invocation,
            TemplateDef tpl,
            IReadOnlyDictionary<string, TemplateDef> templates,
            HashSet<string> visiting) {

            if (!visiting.Add(tpl.Name))
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

                return instanceRoot;
            } finally {
                visiting.Remove(tpl.Name);
            }
        }

        static ElementNode ExpandNode(
            ElementNode src,
            IReadOnlyDictionary<string, string> args,
            IReadOnlyList<ElementNode> slotContent,
            IReadOnlyDictionary<string, TemplateDef> templates,
            HashSet<string> visiting) {

            if (src.Attributes.TryGetValue("if", out var rawIf)) {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            ElementNode prepared = SubstituteAttrs(src, args);

            if (templates.ContainsKey(prepared.Tag))
                return ExpandTree(prepared, templates, visiting);

            var dst = new ElementNode(prepared.Tag) {
                Id = prepared.Id,
                TextContent = Substitution.Apply(prepared.TextContent, args),
            };
            foreach (var kv in prepared.Attributes) {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = kv.Value;
            }
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
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            foreach (var c in src.Children)
                dst.Children.Add(c);
            return dst;
        }

        static ElementNode DeepClone(ElementNode src) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes) dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children) dst.Children.Add(DeepClone(c));
            return dst;
        }
    }
}
