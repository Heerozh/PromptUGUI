using System.Globalization;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Frame : Control
    {
        // 默认无视觉、纯 RectTransform 容器；可选 RectMask2D（mask="rect"）。
        // 作者写了任一程序化视觉属性（color / radius / border* / glow*）时才 lazy 挂
        // ProceduralPanel —— 没写就一个 Graphic 都不挂，行为与历史完全一致、零成本。
        private RectMask2D _rectMask;
        private string _pendingMaskPadding;
        private ProceduralPanel _panel;

        private ProceduralPanel Panel => _panel ??= GameObject.AddComponent<ProceduralPanel>();

        // DSS-D13: Frame 没写 anchor 时，按 size 是否存在分轴决定 stretch 或 top/left。
        // 镜像 CSS 块流默认：未约束的轴 fill 父容器，约束的轴用作者写的值。
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(
                sizeSpec.HasHeight ? AnchorVertical.Top : AnchorVertical.Stretch,
                sizeSpec.HasWidth ? AnchorHorizontal.Left : AnchorHorizontal.Stretch);

        [UIAttr, Preserve]
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

        [UIAttr, Preserve]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = MaskPaddingParser.Parse(value);
            }
        }

        /// <summary>
        /// 填充色。支持 theme token / hex / CSS 命名色 / <c>/alpha</c> 后缀，以及逗号双色
        /// （上→下纵向渐变），与其它控件的 <c>color</c> 走同一套解析。
        /// </summary>
        [UIAttr, Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Panel.SetFill(spec.Top, spec.Bottom);
            }
        }

        /// <summary>圆角半径：单值 / <c>TL,TR,BR,BL</c>（CSS border-radius 顺序）/ <c>pill</c>。</summary>
        [UIAttr, Preserve]
        public string Radius
        {
            set => Panel.SetRadius(RadiusParser.Parse(value));
        }

        /// <summary>内描边宽度（px，向内绘制，不影响布局）。</summary>
        [UIAttr, Preserve]
        public string BorderWidth
        {
            set => Panel.SetBorderWidth(ParsePixels(value, "borderWidth"));
        }

        /// <summary>描边色。纯色 only —— 渐变值由 <c>UI.Theme.Resolve</c> 报错。</summary>
        [UIAttr, Preserve]
        public string BorderColor
        {
            set => Panel.SetBorderColor(UI.Theme.Resolve(value));
        }

        /// <summary>外发光半径（px）。会把绘制四边形外扩同样的距离。</summary>
        [UIAttr, Preserve]
        public string Glow
        {
            set => Panel.SetGlowSize(ParsePixels(value, "glow"));
        }

        /// <summary>发光色。纯色 only；不写时跟随填充色（无填充则白）。</summary>
        [UIAttr, Preserve]
        public string GlowColor
        {
            set => Panel.SetGlowColor(UI.Theme.Resolve(value));
        }

        private static float ParsePixels(string value, string attrName)
        {
            // Variant 只能改值不能删属性，空串是作者退回"无"的唯一写法（同 RadiusParser）。
            if (string.IsNullOrWhiteSpace(value)) return 0f;
            if (!float.TryParse(value.Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var px))
                throw new ParseException(
                    $"{attrName}=\"{value}\": expected a number of pixels (e.g. \"1\", \"2.5\")");
            if (px < 0f)
                throw new ParseException($"{attrName}=\"{value}\": must not be negative");
            return px;
        }
    }
}
