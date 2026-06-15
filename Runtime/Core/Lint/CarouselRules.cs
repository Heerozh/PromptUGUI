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

        public const string PeekNoSizeCode = "PUI-CAROUSEL-PEEK-NO-SIZE";

        // 无 GetNativeSize() override 的纯容器（Control 基类返回 null）：peek 模式下这种卡根
        // 不写 size 会兜成视口尺寸（不 peek）。Image/Text/Progress/Icon 等自带原生尺寸，放过。
        private static readonly HashSet<string> NoNativeSizeContainers = new HashSet<string>
        { "Frame", "VStack", "HStack", "Grid" };

        private static bool HasOwnSize(ElementNode n)
            => n.Attributes.ContainsKey("size")
            || n.Attributes.ContainsKey("width")
            || n.Attributes.ContainsKey("height")
            || n.VariantOverrides.ContainsKey("size")
            || n.VariantOverrides.ContainsKey("width")
            || n.VariantOverrides.ContainsKey("height");

        // parent-relative：检查 Carousel 的一个直接子（卡片）。需要父 Carousel 读 `fill`：
        // fill=true（默认）禁止卡片写 size；fill="false"（peek）放开，但对「无原生尺寸容器且没写 size」
        // 的卡给 warning。fill 只读基础属性（base）——peek carousel 的 fill="false" 一定写在 base。
        public static IEnumerable<LintIssue> CheckCard(ElementNode carousel, ElementNode child)
        {
            bool peek = carousel.Attributes.TryGetValue("fill", out var f) && f == "false";
            if (!peek)
            {
                if (HasOwnSize(child))
                    yield return new LintIssue(
                        CardSizeCode, child.Tag, child.Id,
                        $"<{child.Tag} id='{child.Id}'>: a Carousel card is sized to the viewport by the control; " +
                        "remove size/width/height (or set fill=\"false\" for a peek selector).");
            }
            else if (!HasOwnSize(child) && NoNativeSizeContainers.Contains(child.Tag))
            {
                yield return new LintIssue(
                    PeekNoSizeCode, child.Tag, child.Id,
                    $"<{child.Tag} id='{child.Id}'>: fill=\"false\" card has no size and no native size; " +
                    "it will fill the viewport and neighbours won't peek. Add size= on the card root, " +
                    "or use a control with a native size (e.g. <Image>).");
            }
        }
    }
}
