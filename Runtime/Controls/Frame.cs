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
        private UnityEngine.UI.Mask _stencilMask;
        private string _pendingMaskPadding;
        private string _maskMode;
        private bool? _pendingShowMask;
        private ProceduralPanel _panel;
        private GlassGroupPanel _group;

        private ProceduralPanel Panel => _panel ??= GameObject.AddComponent<ProceduralPanel>();

        // DSS-D13: Frame 没写 anchor 时，按 size 是否存在分轴决定 stretch 或 top/left。
        // 镜像 CSS 块流默认：未约束的轴 fill 父容器，约束的轴用作者写的值。
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(
                sizeSpec.HasHeight ? AnchorVertical.Top : AnchorVertical.Stretch,
                sizeSpec.HasWidth ? AnchorHorizontal.Left : AnchorHorizontal.Stretch);

        /// <summary>
        /// <c>rect</c> = RectMask2D（直角裁剪，便宜、可合批）；<c>self</c> = stencil
        /// <see cref="UnityEngine.UI.Mask"/>，把子内容裁成本 Frame 自绘的那个 SDF 形状 ——
        /// 圆角头像 / 圆角滚动区就是这么来的。
        /// </summary>
        [UIAttr, Preserve]
        public string Mask
        {
            set
            {
                _maskMode = value;
                ReconcileMask();
            }
        }

        /// <summary>
        /// <c>mask="self"</c> 时遮罩源自身画不画。<c>false</c> = 只写 stencil 不出颜色，
        /// 也就是一个隐形的圆角裁剪器。默认 <c>true</c>（作者画了面板，别让它凭空消失）。
        /// </summary>
        [UIAttr, Preserve]
        public string ShowMask
        {
            set
            {
                _pendingShowMask = bool.Parse(value);
                if (_stencilMask != null)
                    _stencilMask.showMaskGraphic = _pendingShowMask.Value;
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
        /// <c>mask=</c> 是一个属性、两套实现，而且它可能先于它依赖的属性到达 ——
        /// <c>ControlAttributeApplier</c> 遍历的是 HashSet，<c>mask</c> 完全可能排在 <c>radius</c>
        /// 前面。所以 setter 和 <see cref="OnAfterApply"/> 都调这里，并且**从当前声明推、不latch**
        /// （spec §8），ReSolve 重放因此是幂等的。
        ///
        /// <para><c>self</c> 需要本节点上有 Graphic：uGUI 的 <c>Mask</c> 要的是 <c>Graphic</c> 而不是
        /// <c>Image</c>，所以懒挂的 <see cref="ProceduralPanel"/> 正好够用。什么都不画的 Frame 没有
        /// Graphic，weld 承载者的融合面在 <c>GlassWeld</c> 子节点上、自身那块还被 suppress 了 ——
        /// 两种情况下挂 Mask 都会把子内容全裁没，所以这里干脆不挂（lint 两条都报）。</para>
        /// </summary>
        private void ReconcileMask()
        {
            if (_maskMode == "rect")
            {
                _rectMask ??= GameObject.AddComponent<RectMask2D>();
                _rectMask.enabled = true;
                if (!string.IsNullOrEmpty(_pendingMaskPadding))
                    _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
                DisableStencilMask();
                return;
            }

            if (_rectMask != null) _rectMask.enabled = false;

            if (_maskMode == "self" && _panel != null && !_panel.IsSuppressed)
            {
                _stencilMask ??= GameObject.AddComponent<UnityEngine.UI.Mask>();
                _stencilMask.enabled = true;
                _stencilMask.showMaskGraphic = _pendingShowMask ?? true;
                // 面板必须知道自己在当遮罩源：它可以什么都不画（无填充的隐形圆角裁剪器），
                // 但 stencil 是它的 fragment 写的，被当成"不可见"剔掉几何就等于把子内容全裁没。
                _panel.SetMaskSource(true);
                return;
            }

            DisableStencilMask();
        }

        private void DisableStencilMask()
        {
            if (_stencilMask != null) _stencilMask.enabled = false;
            if (_panel != null) _panel.SetMaskSource(false);
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
            // 排在 SyncMembers 之后：正在融合的承载者会把自己那块 suppress 掉，而被 suppress 的
            // 面板不出几何、也不持有材质，当不了遮罩源 —— 这个结论要等 SyncMembers 跑完才成立。
            ReconcileMask();
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
