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
