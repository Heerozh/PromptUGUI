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
                                 ControlRegistry.Entry entry, VariantStore variants,
                                 bool initial = true)
        {

            // Determine tr opt-out and ctx (common attrs not registered on Meta)
            var tr = !(node.Attributes.TryGetValue("tr", out var trVal) && trVal == "false");
            node.Attributes.TryGetValue("ctx", out var ctx);

            // 检测 caller 是否在 initial Apply 之后通过 setter 接管了 default-text attribute
            // (e.g. MessageBoxRequest.Bind 改 TextValue)。如果接管了, ReSolve 不该把它打回
            // XML 声明值; 但 i18n 场景 (control text 是上次 Apply 自己写的, locale 切换后
            // Tr 结果变了) 必须重新 apply —— 区分两者靠 control 当前 text 是否 == 上次
            // Apply 写下的 _lastAppliedDefaultText。
            bool DefaultTextLockedByRuntime()
            {
                if (initial) return false;
                if (entry.DefaultTextAttr == null) return false;
                var current = control.PeekDefaultText();
                return current != null && current != control._lastAppliedDefaultText;
            }

            // 运行时独占状态属性（Tab/Toggle 的 isOn 选中、Slider/Dropdown/Progress 的 value）。
            // 与 default-text 同构的「runtime 接管即不打回」逻辑：声明值是初始值，但
            //   - Variant 覆盖（value.low / isOn.portrait）切到对应 variant 时必须重应用 → 不能无脑跳过；
            //   - 用户/代码运行期改过的值，ReSolve（resize / Variant / Theme）不得打回声明默认。
            // 区分两者：control 当前值（PeekRuntimeState）是否 == 上次 Apply 写下的 _lastAppliedRuntimeState。
            // 注意：这里存的是「应用后回读」的归一化字符串（见末尾 capture），不是 XML 字面量 ——
            // 数值属性 "1.0" 经 float 往返会变成 "1"，直接跟字面量比会误判；回读对回读才稳。
            bool RuntimeStateLockedByRuntime()
            {
                if (initial) return false;
                if (entry.RuntimeStateAttr == null) return false;
                var current = control.PeekRuntimeState();
                return current != null && current != control._lastAppliedRuntimeState;
            }

            var runtimeStateReapplied = false;

            // Control-specific attributes: union of base + variant keys.
            var allKeys = new HashSet<string>(node.Attributes.Keys);
            foreach (var k in node.VariantOverrides.Keys) allKeys.Add(k);
            foreach (var attrName in allKeys)
            {
                if (IsCommonAttribute(attrName)) continue;
                if (attrName == "tr" || attrName == "ctx") continue;
                if (!entry.Meta.HasAttribute(attrName)) continue;
                // 跳过 default-text attribute 的 re-apply, 当 runtime 已经通过 setter 接管。
                if (attrName == entry.DefaultTextAttr && DefaultTextLockedByRuntime()) continue;
                // 同理跳过 runtime-state 属性 (isOn / value)，当 runtime 已经改过它。
                if (attrName == entry.RuntimeStateAttr && RuntimeStateLockedByRuntime()) continue;
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
                if (attrName == entry.DefaultTextAttr) control._lastAppliedDefaultText = v;
                if (attrName == entry.RuntimeStateAttr) runtimeStateReapplied = true;
            }

            // Text shorthand
            if (!string.IsNullOrEmpty(node.TextContent) && entry.DefaultTextAttr != null
                && !DefaultTextLockedByRuntime())
            {
                var raw = node.TextContentRaw ?? node.TextContent;
                var final = tr
                    ? TrResolver.Resolve(raw, node.TextArgs, ctx)
                    : node.TextContent;
                final ??= "";
                ApplyOne(entry.Meta, control, node, entry.DefaultTextAttr, final);
                control._lastAppliedDefaultText = final;
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
            bool? hidden = hiddenStr == null ? null : hiddenStr == "true";
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

            // Capture the runtime-state baseline AFTER everything settles (OnAfterApply + any
            // clamping by sibling attrs like Slider min/max), and only when we actually (re)applied
            // it this pass — a locked/skipped attr must keep its prior baseline so the lock persists.
            // Store the read-back (PeekRuntimeState), not the XML literal: numeric round-trips
            // ("1.0" → 1f → "1") would otherwise read as a runtime change on the next ReSolve.
            if (runtimeStateReapplied && entry.RuntimeStateAttr != null)
                control._lastAppliedRuntimeState = control.PeekRuntimeState();
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
                // 'scale' deliberately NOT listed: it is applied by Screen.ApplyScales (independent
                // of the ApplyCommon path) for controls without their own setter, and dispatched
                // through the normal [UIAttr("scale")] loop for controls that handle it themselves
                // (e.g. <Animation>, which interprets scale="from:to" as keyframe values).
                _ => false,
            };
        }
    }
}
