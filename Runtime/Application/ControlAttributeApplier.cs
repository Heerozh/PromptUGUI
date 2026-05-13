using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using PromptUGUI.Variants;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 把 ElementNode 上的属性（基础值 + Variant 覆盖）解算后应用到一个已构造好的
    /// Control 实例上。被 ScreenInstantiator 在初次实例化与 Screen.ReSolve 共用,
    /// 是 spec §8.1 "切换 Variant 触发已实例化 Screen 的重解算" 的核心算法承载者。
    /// </summary>
    internal static class ControlAttributeApplier
    {
        public static void Apply(ElementNode node, Control control,
                                 ControlRegistry.Entry entry, VariantStore variants)
        {

            // Determine tr opt-out and ctx (common attrs not registered on Meta)
            var tr = !(node.Attributes.TryGetValue("tr", out var trVal) && trVal == "false");
            node.Attributes.TryGetValue("ctx", out var ctx);

            // Control-specific attributes: union of base + variant keys.
            var allKeys = new HashSet<string>(node.Attributes.Keys);
            foreach (var k in node.VariantOverrides.Keys) allKeys.Add(k);
            foreach (var attrName in allKeys)
            {
                if (IsCommonAttribute(attrName)) continue;
                if (attrName == "tr" || attrName == "ctx") continue;
                if (!entry.Meta.HasAttribute(attrName)) continue;
                var v = VariantResolver.ResolveAttribute(node, attrName, variants);
                if (v == null) continue;
                // Translate string-valued attrs that are commonly text-bearing.
                // Default: only "text" attr goes through Tr (others like "color", "sprite" don't translate).
                if (tr && attrName == "text")
                {
                    // Use raw value if available so we Tr the un-substituted template.
                    var raw = (node.AttributesRaw != null &&
                                  node.AttributesRaw.TryGetValue("text", out var r)) ? r : v;
                    v = TrResolver.Resolve(raw, node.TextArgs, ctx);
                }
                ApplyOne(entry.Meta, control, node, attrName, v);
            }

            // Text shorthand
            if (!string.IsNullOrEmpty(node.TextContent) && entry.DefaultTextAttr != null)
            {
                var raw = node.TextContentRaw ?? node.TextContent;
                var final = tr
                    ? TrResolver.Resolve(raw, node.TextArgs, ctx)
                    : node.TextContent;
                ApplyOne(entry.Meta, control, node, entry.DefaultTextAttr, final ?? "");
            }

            // Common attributes
            var anchor = VariantResolver.ResolveAttribute(node, "anchor", variants);
            var size = VariantResolver.ResolveAttribute(node, "size", variants);
            var width = VariantResolver.ResolveAttribute(node, "width", variants);
            var height = VariantResolver.ResolveAttribute(node, "height", variants);
            var margin = VariantResolver.ResolveAttribute(node, "margin", variants);
            var pivot = VariantResolver.ResolveAttribute(node, "pivot", variants);
            var hiddenStr = VariantResolver.ResolveAttribute(node, "hidden", variants);
            var interactableStr = VariantResolver.ResolveAttribute(node, "interactable", variants);
            var hidden = hiddenStr == "true";
            var interactable = interactableStr != "false";

            try
            {
                control.ApplyCommon(anchor, size, width, height, margin, pivot, hidden, interactable);
                control.OnAfterApply();
            }
            catch (Exception ex) when (!(ex is ParseException))
            {
                // 不挂 InnerException：Unity 的 StackTraceUtility 会把 inner 顶到日志最前面、
                // 把我们附带上下文的外层 message 埋到中间，作者一眼看不到关键诊断。
                throw new ParseException(FormatNodeContext(node) + ": " + ex.Message);
            }
        }

        private static void ApplyOne(ControlMeta meta, Control control,
                                     ElementNode node, string attrName, string value)
        {
            try { meta.Apply(control, attrName, value); }
            catch (Exception ex) when (!(ex is ParseException))
            {
                throw new ParseException(
                    $"{FormatNodeContext(node)} attribute {attrName}=\"{value}\": {ex.Message}");
            }
        }

        private static string FormatNodeContext(ElementNode node)
        {
            var id = string.IsNullOrEmpty(node.Id) ? "" : $" id='{node.Id}'";
            return $"<{node.Tag}{id}>";
        }

        public static bool IsCommonAttribute(string name)
        {
            return name switch
            {
                "anchor" or "size" or "width" or "height" or "margin" or "pivot" or "hidden" or "interactable" => true,
                _ => false,
            };
        }
    }
}
