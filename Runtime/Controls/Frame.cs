using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Frame : Control
    {
        // 无视觉、纯 RectTransform 容器；可选 RectMask2D（mask="rect"）。
        private RectMask2D _rectMask;
        private string _pendingMaskPadding;

        // DSS-D13: Frame 没写 anchor 时，按 size 是否存在分轴决定 stretch 或 top/left。
        // 镜像 CSS 块流默认：未约束的轴 fill 父容器，约束的轴用作者写的值。
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(
                sizeSpec.HasHeight ? AnchorVertical.Top : AnchorVertical.Stretch,
                sizeSpec.HasWidth ? AnchorHorizontal.Left : AnchorHorizontal.Stretch);

        [UIAttr]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                // 其他值 (空 / self / 无效): lint 已 warn; runtime 静默忽略 (FIM-D9 safety net)
            }
        }

        [UIAttr]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = MaskPaddingParser.Parse(value);
            }
        }
    }
}
