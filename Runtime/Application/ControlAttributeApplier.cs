using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using PromptUGUI.Variants;

namespace PromptUGUI.Application {
    /// <summary>
    /// 把 ElementNode 上的属性（基础值 + Variant 覆盖）解算后应用到一个已构造好的
    /// Control 实例上。被 ScreenInstantiator 在初次实例化与 Screen.ReSolve 共用,
    /// 是 spec §8.1 "切换 Variant 触发已实例化 Screen 的重解算" 的核心算法承载者。
    /// </summary>
    internal static class ControlAttributeApplier {
        public static void Apply(ElementNode node, Control control,
                                 ControlRegistry.Entry entry, VariantStore variants) {

            // 控件特定属性：基础 + 变体 keys 求并集
            var allKeys = new HashSet<string>(node.Attributes.Keys);
            foreach (var k in node.VariantOverrides.Keys) allKeys.Add(k);
            foreach (var attrName in allKeys) {
                if (IsCommonAttribute(attrName)) continue;
                if (!entry.Meta.HasAttribute(attrName)) continue;
                var v = VariantResolver.ResolveAttribute(node, attrName, variants);
                if (v != null) entry.Meta.Apply(control, attrName, v);
            }

            // 文本简写
            if (!string.IsNullOrEmpty(node.TextContent) && entry.DefaultTextAttr != null)
                entry.Meta.Apply(control, entry.DefaultTextAttr, node.TextContent);

            // 通用属性
            var anchor = VariantResolver.ResolveAttribute(node, "anchor", variants);
            var size   = VariantResolver.ResolveAttribute(node, "size",   variants);
            var width  = VariantResolver.ResolveAttribute(node, "width",  variants);
            var height = VariantResolver.ResolveAttribute(node, "height", variants);
            var margin = VariantResolver.ResolveAttribute(node, "margin", variants);
            var pivot  = VariantResolver.ResolveAttribute(node, "pivot",  variants);
            var hiddenStr       = VariantResolver.ResolveAttribute(node, "hidden", variants);
            var interactableStr = VariantResolver.ResolveAttribute(node, "interactable", variants);
            bool hidden       = hiddenStr == "true";
            bool interactable = interactableStr != "false";

            control.ApplyCommon(anchor, size, width, height, margin, pivot, hidden, interactable);
        }

        public static bool IsCommonAttribute(string name) {
            switch (name) {
                case "anchor":
                case "size":
                case "width":
                case "height":
                case "margin":
                case "pivot":
                case "hidden":
                case "interactable":
                    return true;
            }
            return false;
        }
    }
}
