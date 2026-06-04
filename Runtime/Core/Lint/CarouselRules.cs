using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for &lt;Carousel&gt;. Consumed by both IRWalker (CLI) and ScreenInstantiator (runtime warnings).
    /// 卡片的 anchor/margin 交给现有 LayoutGroupChildRules（Carousel 在 selfIsLayoutGroup 名单里）；
    /// 这里只管两件 layout-group 规则不覆盖的事：卡片不能写 size，以及 dots= 锚点合法性。
    /// </summary>
    public static class CarouselRules
    {
        public const string CardSizeCode = "PUI-CAROUSEL-CARD-SIZE";
        public const string DotsAnchorCode = "PUI-CAROUSEL-DOTS-ANCHOR";

        // Exact set of keywords accepted by AnchorPreset.Parse (Runtime/Core/IR/AnchorPreset.cs):
        //   Shorthands: "center" (= center-center), "stretch" / "fill" (= stretch-stretch)
        //   Two-part:   {top|center|bottom|stretch}-{left|center|right|stretch}  (4×4 = 16)
        // "middle" is NOT a valid vertical axis keyword ("center" is correct).
        // "diagonal" and similar freestyle words are not accepted.
        private static readonly HashSet<string> ValidAnchors = new HashSet<string>
        {
            // shorthands
            "center", "stretch", "fill",
            // top row
            "top-left", "top-center", "top-right", "top-stretch",
            // center row
            "center-left", "center-center", "center-right", "center-stretch",
            // bottom row
            "bottom-left", "bottom-center", "bottom-right", "bottom-stretch",
            // stretch row
            "stretch-left", "stretch-center", "stretch-right", "stretch-stretch",
        };

        // self-check: dots= 合法性。
        public static IEnumerable<LintIssue> CheckCarousel(ElementNode n)
        {
            if (n.Attributes.TryGetValue("dots", out var d)
                && !string.IsNullOrEmpty(d) && d != "none"
                && !ValidAnchors.Contains(d))
                yield return new LintIssue(
                    DotsAnchorCode, n.Tag, n.Id,
                    $"<Carousel id='{n.Id}'>: dots='{d}' is not an anchor keyword (e.g. bottom-center) or empty/none. Runtime falls back to bottom-center.");
        }

        // parent-relative: 检查 Carousel 的一个直接子（卡片）是否写了 size/width/height。
        public static IEnumerable<LintIssue> CheckCard(ElementNode child)
        {
            if (child.Attributes.ContainsKey("size")
                || child.Attributes.ContainsKey("width")
                || child.Attributes.ContainsKey("height")
                || child.VariantOverrides.ContainsKey("size")
                || child.VariantOverrides.ContainsKey("width")
                || child.VariantOverrides.ContainsKey("height"))
                yield return new LintIssue(
                    CardSizeCode, child.Tag, child.Id,
                    $"<{child.Tag} id='{child.Id}'>: a Carousel card is sized to the viewport by the control; remove size/width/height.");
        }
    }
}
