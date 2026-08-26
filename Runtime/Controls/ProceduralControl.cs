using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// A control whose primary surface can be drawn procedurally instead of by an <c>Image</c> —
    /// the same rounded-rect SDF (fill / border / glow / glass) <c>&lt;Frame&gt;</c> draws, so a
    /// theme can swap a control's <em>shape</em> and not just its colours.
    ///
    /// <para>Declaring any of the attributes below lazily attaches a <see cref="ProceduralSurface"/>;
    /// a control that declares none is byte-for-byte what it always was, with not one extra
    /// component. Same rule <c>Frame</c> has always had.</para>
    ///
    /// <para><c>color</c> is deliberately NOT here. On <c>Frame</c> it attaches the panel, because a
    /// Frame has nothing else to colour; on an Image-backed control it is an ordinary tint and must
    /// not drag the control into procedural mode. Each subclass keeps its own <c>color</c> and hands
    /// the value to <see cref="Surface"/>, which routes it to whichever layer is drawing.</para>
    /// </summary>
    /// <remarks>
    /// The attributes are declared once here rather than per control because
    /// <c>ControlMeta.Build</c> reflects with <c>BindingFlags.Public | BindingFlags.Instance</c>,
    /// which includes inherited properties — so a subclass gets all thirteen for free.
    /// </remarks>
    public abstract class ProceduralControl : Control
    {
        private ProceduralSurface _surface;

        internal ProceduralSurface Surface =>
            _surface ??= new ProceduralSurface(SurfaceHost, SurfaceSelectable);

        /// <summary>
        /// The GameObject whose <c>Image</c> is this control's primary surface. Usually the control's
        /// own node; for controls that keep their background on a child (<c>Toggle</c>,
        /// <c>Slider</c>, <c>Progress</c>) it is that child, so the panel covers exactly what the
        /// Image covered.
        /// </summary>
        private protected abstract GameObject SurfaceHost { get; }

        /// <summary>
        /// The control's <c>Selectable</c>, if any — its <c>targetGraphic</c> follows whichever
        /// Graphic is visible, so <c>hoverColor</c> and friends keep working in procedural mode.
        /// </summary>
        private protected virtual Selectable SurfaceSelectable => null;

        internal override void OnBeforeApply()
        {
            base.OnBeforeApply();
            _surface?.BeginPass();
        }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _surface?.Reconcile();
        }

        /// <summary>圆角半径：单值 / <c>TL,TR,BR,BL</c>（CSS border-radius 顺序）/ <c>pill</c>。</summary>
        [UIAttr, Preserve]
        public string Radius
        {
            set { var v = RadiusParser.Parse(value); Surface.Declare(p => p.SetRadius(v)); }
        }

        /// <summary>内描边宽度（px，向内绘制，不影响布局）。</summary>
        [UIAttr, Preserve]
        public string BorderWidth
        {
            set { var v = ProceduralValueParser.Pixels(value, "borderWidth"); Surface.Declare(p => p.SetBorderWidth(v)); }
        }

        /// <summary>描边色。纯色 only。</summary>
        [UIAttr, Preserve]
        public string BorderColor
        {
            set { var v = UI.Theme.Resolve(value); Surface.Declare(p => p.SetBorderColor(v)); }
        }

        /// <summary>外发光半径（px）。会把绘制四边形外扩同样的距离。</summary>
        [UIAttr, Preserve]
        public string Glow
        {
            set { var v = ProceduralValueParser.Pixels(value, "glow"); Surface.Declare(p => p.SetGlowSize(v)); }
        }

        /// <summary>发光色。纯色 only；不写时跟随填充色。</summary>
        [UIAttr, Preserve]
        public string GlowColor
        {
            set { var v = UI.Theme.Resolve(value); Surface.Declare(p => p.SetGlowColor(v)); }
        }

        /// <summary>玻璃模式：填充改为采样模糊后的 backdrop + 边缘折射 / 打光。</summary>
        [UIAttr, Preserve]
        public string Glass
        {
            set { var v = GlassAttrParser.ParseFlag(GlassAttrParser.Glass, value); Surface.Declare(p => p.SetGlass(v)); }
        }

        /// <summary>磨砂强度 0–1。</summary>
        [UIAttr, Preserve]
        public string Frost
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.Frost, value); Surface.Declare(p => p.SetFrost(v)); }
        }

        /// <summary>玻璃厚度（px）：边缘折射带的宽度。</summary>
        [UIAttr, Preserve]
        public string Depth
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.Depth, value); Surface.Declare(p => p.SetDepth(v)); }
        }

        /// <summary>色散强度 0–1。</summary>
        [UIAttr, Preserve]
        public string Dispersion
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.Dispersion, value); Surface.Declare(p => p.SetDispersion(v)); }
        }

        /// <summary>光源方向（度）：0 = 正上方，顺时针增。</summary>
        [UIAttr, Preserve]
        public string LightAngle
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.LightAngle, value); Surface.Declare(p => p.SetLightAngle(v)); }
        }

        /// <summary>边缘高光强度 0–1。</summary>
        [UIAttr, Preserve]
        public string LightIntensity
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.LightIntensity, value); Surface.Declare(p => p.SetLightIntensity(v)); }
        }

        /// <summary>backdrop 饱和度乘子（vibrancy）。</summary>
        [UIAttr, Preserve]
        public string Saturation
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.Saturation, value); Surface.Declare(p => p.SetSaturation(v)); }
        }

        /// <summary>磨砂颗粒强度 0–1。</summary>
        [UIAttr, Preserve]
        public string Noise
        {
            set { var v = GlassAttrParser.ParseValue(GlassAttrParser.Noise, value); Surface.Declare(p => p.SetNoise(v)); }
        }
    }
}
