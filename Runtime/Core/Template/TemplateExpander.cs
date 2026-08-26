using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template
{
    public static class TemplateExpander
    {
        // 通用属性集合：模板调用上写的这些不算 Param
        // internal (not private) so the lint layer's PUI-VARIANT-NO-BASE mirror can be drift-guarded
        // against it (VariantBaseRules.InvocationMergeableOntoTemplateRoot) — see VariantBaseRulesTests.
        internal static readonly HashSet<string> CommonAttrs = new() {
            "anchor", "size", "width", "height", "margin", "pivot",
            "padding", "spacing",
            "hidden", "interactable",
        };

        /// <summary>
        /// Real entry point — takes the merged LoadedDoc produced by DocumentLoader.
        /// Uses (Namespace, Name) keyed lookup for template resolution.
        /// </summary>
        internal static UIDocument Expand(LoadedDoc loaded)
        {
            foreach (var t in loaded.Templates.Values) ValidateSlotCount(t);

            var result = new UIDocument { Version = 1 };
            foreach (var kv in loaded.Templates)
                result.Templates[kv.Key.ToString()] = kv.Value;   // 调试可读，运行时不再用
            foreach (var kv in loaded.Styles)
                result.Styles[kv.Key.ToString()] = kv.Value;      // 同上：诊断用，展开产物已不含 class

            var styles = loaded.Styles;
            var runtimeTemplates = BuildRuntimeTemplates(loaded, styles);

            foreach (var s in loaded.Screens)
            {
                var newRoot = new ElementNode(s.Root.Tag, s.Root.Namespace)
                {
                    OriginSrc = s.Root.OriginSrc,
                };
                // Screen-level attributes (e.g. reference=, reference.<variant>=) live on
                // ScreenDef.Root and must survive expansion so runtime VariantResolver can read them.
                foreach (var kv in s.Root.Attributes)
                    newRoot.Attributes[kv.Key] = kv.Value;
                foreach (var kv in s.Root.VariantOverrides)
                    newRoot.VariantOverrides[kv.Key] =
                        new List<(string Variant, string Value)>(kv.Value);
                foreach (var c in s.Root.Children)
                {
                    EnsureNoSlot(c, $"Screen '{s.Name}'");
                    var ec = ExpandTree(c, loaded.Templates, styles,
                                        new HashSet<TemplateKey>());
                    if (ec != null) newRoot.Children.Add(ec);
                }
                var newScreen = new ScreenDef(s.Name, newRoot)
                {
                    CanvasMode = s.CanvasMode,
                    // <FocusCursor> is extracted from Root.Children by the parser and must be
                    // forwarded here; otherwise SetupFocusCursor receives null and no overlay is built.
                    FocusCursor = s.FocusCursor == null ? null
                        : ExpandTree(s.FocusCursor, loaded.Templates, styles,
                                     new HashSet<TemplateKey>()),
                };
                // 把全局 Template 表附到本 Screen，供 ScrollList 等运行时控件按 tag 反查。
                foreach (var kv in runtimeTemplates)
                    newScreen.Templates[kv.Key.ToString()] = kv.Value;
                // 同理附上样式表：切主题时要把主题 pack 折在它之上重算 class=。
                foreach (var kv in styles)
                    newScreen.Styles[kv.Key] = kv.Value;
                foreach (var block in s.Variants)
                {
                    var newBlock = new VariantBlock(block.When);
                    foreach (var add in block.Adds)
                    {
                        var newAdd = new AddDirective
                        {
                            IntoPath = add.IntoPath,
                            At = add.At,
                        };
                        foreach (var ch in add.Children)
                        {
                            EnsureNoSlot(ch, $"<Variant when='{block.When}'> in Screen '{s.Name}'");
                            var ec = ExpandTree(ch, loaded.Templates, styles,
                                                new HashSet<TemplateKey>());
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
        public static UIDocument Expand(UIDocument doc)
        {
            var loaded = new LoadedDoc
            {
                EntrySrc = "<inline>",
            };
            foreach (var s in doc.Screens) loaded.Screens.Add(s);
            foreach (var kv in doc.Templates)
                loaded.Templates[new TemplateKey(null, kv.Key)] = kv.Value;
            foreach (var kv in doc.Styles)
                loaded.Styles[new StyleKey(null, kv.Key)] = kv.Value;
            return Expand(loaded);
        }

        /// <summary>
        /// The Template table a Screen carries for RUNTIME instantiation (<c>itemTemplate=</c>).
        /// <c>ScrollList</c> / <c>TabBar</c> / <c>Carousel</c> hand the body straight to
        /// <c>ScreenInstantiator</c>, so everything expansion normally does has to have happened
        /// already — <c>class=</c> merged, <c>{{param}}</c> substituted, nested invocations inlined.
        /// Handing over the raw body meant the first two silently produced wrong values and the third
        /// threw <c>unregistered tag</c> inside the bind, where R3 swallows it into a console error
        /// and leaves the list empty.
        ///
        /// <para>With no invocation to supply them, a template's own <c>&lt;Param&gt;</c> defaults ARE
        /// the arguments. A required Param has no such value, so those templates cannot be expanded
        /// here — they are kept as a deep copy instead, and rejected by name if actually used as an
        /// itemTemplate. Expansion failures fall back the same way: a template that is only ever
        /// invoked normally must not start failing at load.</para>
        ///
        /// <para>Every entry is a copy the ScreenDef OWNS, never the shared (possibly commons-pool)
        /// TemplateDef — that is what lets a theme switch re-merge these bodies in place without
        /// leaking across documents.</para>
        /// </summary>
        private static Dictionary<PromptUGUI.IR.TemplateKey, TemplateDef> BuildRuntimeTemplates(
            PromptUGUI.IR.LoadedDoc loaded,
            IReadOnlyDictionary<PromptUGUI.IR.StyleKey, StyleDef> styles)
        {
            var result = new Dictionary<PromptUGUI.IR.TemplateKey, TemplateDef>(loaded.Templates.Count);
            foreach (var kv in loaded.Templates)
            {
                var tpl = kv.Value;
                var copy = new TemplateDef(tpl.Name) { OriginSrc = tpl.OriginSrc };
                copy.Params.AddRange(tpl.Params);

                var args = new Dictionary<string, string>();
                var expandable = tpl.Body != null;
                foreach (var p in tpl.Params)
                {
                    if (!p.HasDefault) { expandable = false; break; }
                    args[p.Name] = p.DefaultValue;
                }

                ElementNode body = null;
                if (expandable)
                {
                    try
                    {
                        body = ExpandNode(tpl.Body, args, slotContent: null, loaded.Templates, styles,
                                          new HashSet<PromptUGUI.IR.TemplateKey>());
                    }
                    catch (TemplateException) { body = null; }
                    catch (Parser.ParseException) { body = null; }
                }

                copy.Body = body ?? (tpl.Body == null ? null : DeepClone(tpl.Body));
                copy.BodyExpanded = body != null;
                result[kv.Key] = copy;
            }
            return result;
        }

        private static void ValidateSlotCount(TemplateDef tpl)
        {
            var count = 0;
            CountSlots(tpl.Body, ref count);
            if (count > 1)
                throw new TemplateException(
                    $"<Template name='{tpl.Name}'>: at most one <Slot/> allowed (found {count})");
        }

        private static void CountSlots(ElementNode n, ref int count)
        {
            if (n.Tag == "Slot") count++;
            foreach (var c in n.Children) CountSlots(c, ref count);
        }

        private static void EnsureNoSlot(ElementNode n, string contextLabel)
        {
            if (n.Tag == "Slot")
                throw new TemplateException(
                    $"<Slot/> is only allowed inside <Template>, but found in {contextLabel}");
            foreach (var c in n.Children) EnsureNoSlot(c, contextLabel);
        }

        private static ElementNode ExpandTree(
            ElementNode src,
            IReadOnlyDictionary<TemplateKey, TemplateDef> templates,
            IReadOnlyDictionary<StyleKey, StyleDef> styles,
            HashSet<TemplateKey> visiting)
        {

            var key = new TemplateKey(src.Namespace, src.Tag);
            if (templates.TryGetValue(key, out var tpl))
                return ExpandInvocation(StyleMerger.Apply(src, styles, tpl), tpl, key,
                                        templates, styles, visiting);

            // Namespace was specified but no matching template → error
            if (src.Namespace != null)
                throw new TemplateException(
                    $"unknown template '{src.Namespace}.{src.Tag}'");

            var merged = StyleMerger.Apply(src, styles, null);
            var dst = new ElementNode(merged.Tag, merged.Namespace)
            {
                OriginSrc = merged.OriginSrc,
                StyleAttrNames = merged.StyleAttrNames,
                Id = merged.Id,
                TextContent = merged.TextContent,
                TextContentRaw = merged.TextContentRaw ?? merged.TextContent,
                IsTemplateInstanceRoot = merged.IsTemplateInstanceRoot,
            };
            foreach (var kv in merged.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var kv in merged.AttributesRaw)
                dst.AttributesRaw[kv.Key] = kv.Value;
            CopyVariantOverrides(merged, dst);
            foreach (var c in merged.Children)
            {
                var ec = ExpandTree(c, templates, styles, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        private static ElementNode ExpandInvocation(
            ElementNode invocation,
            TemplateDef tpl,
            TemplateKey key,
            IReadOnlyDictionary<TemplateKey, TemplateDef> templates,
            IReadOnlyDictionary<StyleKey, StyleDef> styles,
            HashSet<TemplateKey> visiting)
        {

            // Cycle tracking uses (Namespace, Name) key to allow same-named templates across different namespaces
            if (!visiting.Add(key))
                throw new TemplateException(
                    $"cyclic template reference detected: {string.Join(" → ", visiting)} → {tpl.Name}");

            try
            {
                var args = new Dictionary<string, string>();
                foreach (var p in tpl.Params)
                {
                    if (invocation.Attributes.TryGetValue(p.Name, out var v))
                        args[p.Name] = v;
                    else if (p.HasDefault)
                        args[p.Name] = p.DefaultValue;
                    else
                        throw new TemplateException(
                            $"<{tpl.Name}>: required <Param name='{p.Name}'> not provided");
                }

                foreach (var kv in invocation.Attributes)
                {
                    if (CommonAttrs.Contains(kv.Key)) continue;
                    if (args.ContainsKey(kv.Key)) continue;
                    // StyleMerger has already consumed class= into args / CommonAttrs above and
                    // leaves the attribute in place so a theme switch can re-derive the pack.
                    if (kv.Key == StyleMerger.ClassAttr) continue;
                    throw new TemplateException(
                        $"<{tpl.Name}>: unknown attribute '{kv.Key}'");
                }

                foreach (var kv in invocation.VariantOverrides)
                {
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
                foreach (var c in invocation.Children)
                {
                    var ec = ExpandTree(c, templates, styles, visiting);
                    if (ec != null) slotContent.Add(ec);
                }

                var instanceRoot = ExpandNode(tpl.Body, args, slotContent, templates, styles, visiting) ?? throw new TemplateException(
                        $"<{tpl.Name}>: template body root was excluded by if; not allowed");
                instanceRoot.IsTemplateInstanceRoot = true;
                if (!string.IsNullOrEmpty(invocation.Id))
                    instanceRoot.Id = invocation.Id;
                foreach (var kv in invocation.Attributes)
                {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    instanceRoot.Attributes[kv.Key] = kv.Value;
                }
                foreach (var kv in invocation.VariantOverrides)
                {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    if (!instanceRoot.VariantOverrides.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<(string Variant, string Value)>();
                        instanceRoot.VariantOverrides[kv.Key] = list;
                    }
                    list.AddRange(kv.Value);
                }

                return instanceRoot;
            }
            finally
            {
                visiting.Remove(key);
            }
        }

        private static ElementNode ExpandNode(
            ElementNode src,
            IReadOnlyDictionary<string, string> args,
            IReadOnlyList<ElementNode> slotContent,
            IReadOnlyDictionary<TemplateKey, TemplateDef> templates,
            IReadOnlyDictionary<StyleKey, StyleDef> styles,
            HashSet<TemplateKey> visiting)
        {

            if (src.Attributes.TryGetValue("if", out var rawIf))
            {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            ElementNode prepared = SubstituteAttrs(src, args);

            var key2 = new TemplateKey(prepared.Namespace, prepared.Tag);
            if (templates.ContainsKey(key2))
                return ExpandTree(prepared, templates, styles, visiting);

            // After substitution, so class="{{skin}}" picks a style by template argument.
            prepared = StyleMerger.Apply(prepared, styles, null);

            var dst = new ElementNode(prepared.Tag, prepared.Namespace)
            {
                OriginSrc = prepared.OriginSrc,
                StyleAttrNames = prepared.StyleAttrNames,
                Id = prepared.Id,
                TextContent = Substitution.Apply(prepared.TextContent, args),
                TextContentRaw = src.TextContentRaw ?? src.TextContent,
            };
            foreach (var kv in prepared.Attributes)
            {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = kv.Value;
            }
            foreach (var kv in src.AttributesRaw)
                dst.AttributesRaw[kv.Key] = kv.Value;
            if (args != null && args.Count > 0)
                dst.TextArgs = new System.Collections.Generic.Dictionary<string, string>(args);
            CopyVariantOverrides(prepared, dst);
            foreach (var c in src.Children)
            {
                if (c.Tag == "Slot")
                {
                    if (slotContent != null)
                        foreach (var sc in slotContent)
                            dst.Children.Add(DeepClone(sc));
                    continue;
                }
                var ec = ExpandNode(c, args, slotContent, templates, styles, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        private static ElementNode SubstituteAttrs(ElementNode src,
                                           IReadOnlyDictionary<string, string> args)
        {
            var dst = new ElementNode(src.Tag, src.Namespace)
            {
                OriginSrc = src.OriginSrc,
                StyleAttrNames = src.StyleAttrNames,
                Id = src.Id,
                TextContent = src.TextContent,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            // Preserve raw attribute values — substitution does NOT clobber raw
            foreach (var kv in src.AttributesRaw)
                dst.AttributesRaw[kv.Key] = kv.Value;
            foreach (var kv in src.VariantOverrides)
            {
                var newList = new List<(string Variant, string Value)>();
                foreach (var (variant, value) in kv.Value)
                    newList.Add((variant, Substitution.Apply(value, args)));
                dst.VariantOverrides[kv.Key] = newList;
            }
            foreach (var c in src.Children)
                dst.Children.Add(c);
            return dst;
        }

        private static void CopyVariantOverrides(ElementNode src, ElementNode dst)
        {
            foreach (var kv in src.VariantOverrides)
                dst.VariantOverrides[kv.Key] =
                    new List<(string Variant, string Value)>(kv.Value);
        }

        private static ElementNode DeepClone(ElementNode src)
        {
            var dst = new ElementNode(src.Tag, src.Namespace)
            {
                OriginSrc = src.OriginSrc,
                StyleAttrNames = src.StyleAttrNames,
                Id = src.Id,
                TextContent = src.TextContent,
                TextContentRaw = src.TextContentRaw ?? src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes) dst.Attributes[kv.Key] = kv.Value;
            foreach (var kv in src.AttributesRaw) dst.AttributesRaw[kv.Key] = kv.Value;
            if (src.TextArgs != null)
                dst.TextArgs = new System.Collections.Generic.Dictionary<string, string>(src.TextArgs);
            CopyVariantOverrides(src, dst);
            foreach (var c in src.Children) dst.Children.Add(DeepClone(c));
            return dst;
        }
    }
}
