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
        private GlassGroupPanel _group;

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

        /// <summary>
        /// 玻璃模式：填充改为采样模糊后的 backdrop + 边缘折射 / 打光，形状仍是同一套 SDF。
        /// 见 <c>UI.Glass</c>：没有可用 backdrop（无 URP / 关闭画质选项 / 无相机）时自动退化成
        /// 半透明面板。
        /// </summary>
        [UIAttr, Preserve]
        public string Glass
        {
            set => Panel.SetGlass(GlassAttrParser.ParseFlag(GlassAttrParser.Glass, value));
        }

        /// <summary>磨砂强度 0–1。</summary>
        [UIAttr, Preserve]
        public string Frost
        {
            set => Panel.SetFrost(GlassAttrParser.ParseValue(GlassAttrParser.Frost, value));
        }

        /// <summary>玻璃厚度（px）：边缘折射带的宽度。0 = 完全平板，无折射也无边缘打光。</summary>
        [UIAttr, Preserve]
        public string Depth
        {
            set => Panel.SetDepth(GlassAttrParser.ParseValue(GlassAttrParser.Depth, value));
        }

        /// <summary>色散强度 0–1：折射带内 RGB 分量的偏移差。</summary>
        [UIAttr, Preserve]
        public string Dispersion
        {
            set => Panel.SetDispersion(GlassAttrParser.ParseValue(GlassAttrParser.Dispersion, value));
        }

        /// <summary>光源方向（度）：0 = 正上方，顺时针增。</summary>
        [UIAttr, Preserve]
        public string LightAngle
        {
            set => Panel.SetLightAngle(GlassAttrParser.ParseValue(GlassAttrParser.LightAngle, value));
        }

        /// <summary>边缘高光强度 0–1。0 = 关闭打光层。</summary>
        [UIAttr, Preserve]
        public string LightIntensity
        {
            set => Panel.SetLightIntensity(
                GlassAttrParser.ParseValue(GlassAttrParser.LightIntensity, value));
        }

        /// <summary>backdrop 饱和度乘子（vibrancy）。1 = 原样，0 = 灰度。</summary>
        [UIAttr, Preserve]
        public string Saturation
        {
            set => Panel.SetSaturation(GlassAttrParser.ParseValue(GlassAttrParser.Saturation, value));
        }

        /// <summary>磨砂颗粒强度 0–1：兼作去 banding。</summary>
        [UIAttr, Preserve]
        public string Noise
        {
            set => Panel.SetNoise(GlassAttrParser.ParseValue(GlassAttrParser.Noise, value));
        }

        /// <summary>
        /// 融合半径（px）：把本 Frame 的**直接玻璃子级**焊成一整片连续玻璃，交界处用厚度台阶而不是
        /// 分割线区分。写了 weld 的 Frame 自身不画玻璃，只当承载者；组级参数（frost / lightAngle /
        /// lightIntensity / saturation / noise / dispersion 和描边、发光）写在它身上，
        /// 逐块参数（radius / depth / color）写在子级身上。
        /// </summary>
        [UIAttr, Preserve]
        public string Weld
        {
            set
            {
                var weld = GlassAttrParser.ParseValue(GlassAttrParser.Weld, value);
                // weld="" / "0" 是作者取消融合的唯一写法（Variant 只能改值不能删属性），
                // 此时不为它凭空建组。
                if (weld <= 0f && _group == null) return;
                _group ??= GlassGroupPanel.Attach(GameObject);
                _group.SetWeld(weld);
            }
        }

        /// <summary>
        /// 属性应用是无序的（<c>ControlAttributeApplier</c> 遍历的是 HashSet），而且一次实例化会连写
        /// 十几个视觉属性。setter 只打脏标记，材质在这里统一解算一次 —— 顺带让 <c>glass</c> 写在
        /// <c>frost</c> 前面还是后面都不再有区别。
        ///
        /// 子树先于本节点实例化完毕（<c>ScreenInstantiator</c> 把 Apply 放在递归之后），所以这里
        /// 也是收集 weld 成员的正确时机；ReSolve 会再跑一次，Variant 新增的块因此能被接上。
        /// </summary>
        internal override void OnAfterApply()
        {
            // Unity `== null` rather than `?.`: these components read as null once destroyed (a
            // Screen torn down out from under a pending ReSolve), and `?.` uses CLR semantics, which
            // would happily call into the carcass and throw MissingReferenceException — aborting the
            // whole ReSolve pass, not just this Frame.
            if (_panel != null) _panel.FlushParams();
            if (_group != null) _group.SyncMembers(_panel != null ? _panel : null);
        }

        private static float ParsePixels(string value, string attrName)
        {
            // Variant 只能改值不能删属性，空串是作者退回"无"的唯一写法（同 RadiusParser）。
            if (string.IsNullOrWhiteSpace(value)) return 0f;
            if (!float.TryParse(value.Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var px))
                throw new ParseException(
                    $"{attrName}=\"{value}\": expected a number of pixels (e.g. \"1\", \"2.5\")");
            // "NaN" / "Infinity" parse fine, and NaN slips past the negative test below.
            if (float.IsNaN(px) || float.IsInfinity(px))
                throw new ParseException($"{attrName}=\"{value}\": must be a finite number");
            if (px < 0f)
                throw new ParseException($"{attrName}=\"{value}\": must not be negative");
            return px;
        }
    }
}
