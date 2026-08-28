using System;
using System.Collections.Generic;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The glass-specific half of a panel's material parameters. Kept as its own struct so the
    /// far more common opaque panel pays neither the comparison nor the hash of seven floats it
    /// does not use.
    /// </summary>
    internal readonly struct GlassParams : IEquatable<GlassParams>
    {
        public readonly float Frost;
        public readonly float Depth;
        public readonly float Dispersion;
        public readonly float LightAngle;      // degrees, clockwise from straight up
        public readonly float LightIntensity;
        public readonly float Saturation;
        public readonly float Noise;

        public GlassParams(float frost, float depth, float dispersion, float lightAngle,
                           float lightIntensity, float saturation, float noise)
        {
            Frost = frost;
            Depth = depth;
            Dispersion = dispersion;
            LightAngle = lightAngle;
            LightIntensity = lightIntensity;
            Saturation = saturation;
            Noise = noise;
        }

        public static readonly GlassParams None = default;

        public bool Equals(GlassParams o) =>
            Frost == o.Frost && Depth == o.Depth && Dispersion == o.Dispersion
            && LightAngle == o.LightAngle && LightIntensity == o.LightIntensity
            && Saturation == o.Saturation && Noise == o.Noise;

        public override bool Equals(object o) => o is GlassParams g && Equals(g);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Frost.GetHashCode();
                h = (h * 397) ^ Depth.GetHashCode();
                h = (h * 397) ^ Dispersion.GetHashCode();
                h = (h * 397) ^ LightAngle.GetHashCode();
                h = (h * 397) ^ LightIntensity.GetHashCode();
                h = (h * 397) ^ Saturation.GetHashCode();
                h = (h * 397) ^ Noise.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// The full material-side parameter set of one <see cref="ProceduralPanel"/>. Deliberately
    /// excludes the panel's size and position — those ride the vertex stream — so every panel
    /// wearing the same style hashes to the same key regardless of how big it is.
    /// </summary>
    /// <remarks>
    /// <see cref="GlassParams"/> is forced to <see cref="GlassParams.None"/> whenever
    /// <see cref="Glass"/> is false (see <c>ProceduralPanel.BuildParams</c>). Stray glass attributes
    /// on an opaque panel would otherwise split the cache into keys that render identically —
    /// two materials, two draw calls, no visible difference.
    /// </remarks>
    internal readonly struct PanelParams : IEquatable<PanelParams>
    {
        public readonly Color FillTop;
        public readonly Color FillBottom;
        public readonly Color BorderColor;
        public readonly Color GlowColor;
        /// <summary>
        /// Inner glow tint. Unlike <see cref="GlowColor"/> it does NOT fall back to the fill — an
        /// inner glow in the fill's own colour is invisible on an opaque fill, which is the common
        /// case, so the default is plain white (spec 2026-08-28 §5.4).
        /// </summary>
        public readonly Color InnerGlowColor;
        /// <summary>Per-corner horizontal reach in CSS order; the radius when the corner is round.</summary>
        public readonly Vector4 CornerWidth;
        /// <summary>Per-corner vertical reach in CSS order; mirrors the width for a round corner.</summary>
        public readonly Vector4 CornerHeight;
        /// <summary>Per-corner <see cref="CornerKind"/> in CSS order, as the shader reads them.</summary>
        public readonly Vector4 CornerKinds;
        public readonly PanelShape Shape;
        /// <summary>Hexagon tip reach; 0 means the shader takes half the rect height.</summary>
        public readonly float HexWidth;
        public readonly float BorderWidth;
        public readonly float GlowSize;
        /// <summary>Inner glow band width, measured inwards from the shape edge.</summary>
        public readonly float InnerGlowSize;
        public readonly bool Glass;
        public readonly GlassParams GlassParams;

        /// <summary>
        /// Packs a parsed shape into the four vectors the shader reads. Sizes stay in canvas units
        /// and whole-shape sentinels stay symbolic — both are resolved per-fragment against the live
        /// rect, which is what keeps two same-styled panels of different sizes on one material.
        /// </summary>
        public PanelParams(Color fillTop, Color fillBottom, Color borderColor, Color glowColor,
                           Color innerGlowColor, in RadiusSpec radius, float borderWidth,
                           float glowSize, float innerGlowSize,
                           bool glass = false, GlassParams glassParams = default)
        {
            FillTop = fillTop;
            FillBottom = fillBottom;
            BorderColor = borderColor;
            GlowColor = glowColor;
            InnerGlowColor = innerGlowColor;
            CornerWidth = new Vector4(radius.TopLeftCorner.Width, radius.TopRightCorner.Width,
                                      radius.BottomRightCorner.Width, radius.BottomLeftCorner.Width);
            CornerHeight = new Vector4(radius.TopLeftCorner.Height, radius.TopRightCorner.Height,
                                       radius.BottomRightCorner.Height, radius.BottomLeftCorner.Height);
            CornerKinds = new Vector4((float)radius.TopLeftCorner.Kind, (float)radius.TopRightCorner.Kind,
                                      (float)radius.BottomRightCorner.Kind, (float)radius.BottomLeftCorner.Kind);
            Shape = radius.Shape;
            HexWidth = radius.HexWidth;
            BorderWidth = borderWidth;
            GlowSize = glowSize;
            InnerGlowSize = innerGlowSize;
            Glass = glass;
            GlassParams = glassParams;
        }

        public bool Pill => Shape == PanelShape.Pill;

        public bool Equals(PanelParams o) =>
            FillTop == o.FillTop && FillBottom == o.FillBottom
            && BorderColor == o.BorderColor && GlowColor == o.GlowColor
            && InnerGlowColor == o.InnerGlowColor
            && CornerWidth == o.CornerWidth && CornerHeight == o.CornerHeight
            && CornerKinds == o.CornerKinds && Shape == o.Shape && HexWidth == o.HexWidth
            && BorderWidth == o.BorderWidth && GlowSize == o.GlowSize
            && InnerGlowSize == o.InnerGlowSize
            && Glass == o.Glass
            // Short-circuit: an opaque panel's glass block is always None, so there is nothing to
            // compare — and opaque is the overwhelmingly common case.
            && (!Glass || GlassParams.Equals(o.GlassParams));

        public override bool Equals(object o) => o is PanelParams p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = FillTop.GetHashCode();
                h = (h * 397) ^ FillBottom.GetHashCode();
                h = (h * 397) ^ BorderColor.GetHashCode();
                h = (h * 397) ^ GlowColor.GetHashCode();
                h = (h * 397) ^ InnerGlowColor.GetHashCode();
                h = (h * 397) ^ CornerWidth.GetHashCode();
                h = (h * 397) ^ CornerHeight.GetHashCode();
                h = (h * 397) ^ CornerKinds.GetHashCode();
                h = (h * 397) ^ Shape.GetHashCode();
                h = (h * 397) ^ HexWidth.GetHashCode();
                h = (h * 397) ^ BorderWidth.GetHashCode();
                h = (h * 397) ^ GlowSize.GetHashCode();
                h = (h * 397) ^ InnerGlowSize.GetHashCode();
                h = (h * 397) ^ Glass.GetHashCode();
                if (Glass) h = (h * 397) ^ GlassParams.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// Hands out one shared <see cref="Material"/> per distinct <see cref="PanelParams"/>.
    ///
    /// Why share rather than instance-per-panel: <c>CanvasRenderer</c> ignores
    /// <c>MaterialPropertyBlock</c>, so per-panel parameters can only live in per-panel materials —
    /// and every distinct material is a batch break. The <c>&lt;Style&gt;</c> system means panels
    /// naturally agree on their parameters (twenty <c>class="card"</c> frames are twenty identical
    /// parameter sets), so keying on the parameters collapses them back into one material and lets
    /// uGUI batch them again.
    ///
    /// Released materials go to a spare stack instead of being destroyed, so a panel whose colour is
    /// tweened frame-by-frame walks through unique keys without ever allocating or destroying a
    /// Material. Steady-state allocation on both paths is zero (slots are structs). The two fill
    /// modes keep separate spare stacks — a material's shader is fixed at construction, so an opaque
    /// spare can never be handed out as a glass panel.
    /// </summary>
    internal static class ProceduralMaterialCache
    {
        internal const string ShaderResourcePath = "PromptUGUI/Material/UI-ProceduralPanel";
        internal const string GlassShaderResourcePath = "PromptUGUI/Material/UI-GlassPanel";

        private static readonly int FillTopId = Shader.PropertyToID("_FillTop");
        private static readonly int FillBottomId = Shader.PropertyToID("_FillBottom");
        private static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int InnerGlowColorId = Shader.PropertyToID("_InnerGlowColor");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int CornerHeightId = Shader.PropertyToID("_CornerH");
        private static readonly int CornerKindId = Shader.PropertyToID("_CornerKind");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int HexWidthId = Shader.PropertyToID("_HexW");
        private static readonly int BorderWidthId = Shader.PropertyToID("_BorderWidth");
        private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");
        private static readonly int InnerGlowSizeId = Shader.PropertyToID("_InnerGlowSize");

        // Seven glass floats ride two vectors: fewer SetX calls, and the light angle arrives as a
        // direction so the shader never runs sin/cos per fragment.
        private static readonly int GlassAId = Shader.PropertyToID("_GlassA");
        private static readonly int GlassBId = Shader.PropertyToID("_GlassB");

        private readonly struct Slot
        {
            public readonly Material Material;
            public readonly int RefCount;
            public Slot(Material material, int refCount) { Material = material; RefCount = refCount; }
        }

        private static readonly Dictionary<PanelParams, Slot> _live = new();
        private static readonly Stack<Material> _spare = new();
        private static readonly Stack<Material> _spareGlass = new();
        private static Shader _shader;
        private static Shader _glassShader;

        /// <summary>Number of distinct live parameter sets. Test-only observability.</summary>
        internal static int LiveMaterialCount => _live.Count;

        public static Material Acquire(in PanelParams p)
        {
            if (_live.TryGetValue(p, out var slot))
            {
                _live[p] = new Slot(slot.Material, slot.RefCount + 1);
                return slot.Material;
            }

            var spare = p.Glass ? _spareGlass : _spare;
            var mat = spare.Count > 0 ? spare.Pop() : CreateMaterial(p.Glass);
            Configure(mat, p);
            _live[p] = new Slot(mat, 1);
            return mat;
        }

        public static void Release(in PanelParams p)
        {
            if (!_live.TryGetValue(p, out var slot)) return;
            if (slot.RefCount > 1)
            {
                _live[p] = new Slot(slot.Material, slot.RefCount - 1);
                return;
            }
            _live.Remove(p);
            if (slot.Material != null) (p.Glass ? _spareGlass : _spare).Push(slot.Material);
        }

        private static Material CreateMaterial(bool glass)
        {
            var path = glass ? GlassShaderResourcePath : ShaderResourcePath;
            var shader = glass
                ? _glassShader ??= Resources.Load<Shader>(path)
                : _shader ??= Resources.Load<Shader>(path);
            if (shader == null)
                throw new InvalidOperationException(
                    $"PromptUGUI: shader not found at Resources/{path}. " +
                    "The package's Runtime/Resources folder is required for procedural panels.");
            return new Material(shader)
            {
                name = glass ? "PromptUGUI/GlassPanel" : "PromptUGUI/ProceduralPanel",
                // Runtime-created and never authored into a scene; without this it would be
                // offered up for serialization and leak into the user's assets.
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static void Configure(Material mat, in PanelParams p)
        {
            mat.SetColor(FillTopId, p.FillTop);
            mat.SetColor(FillBottomId, p.FillBottom);
            mat.SetColor(BorderColorId, p.BorderColor);
            mat.SetColor(GlowColorId, p.GlowColor);
            mat.SetColor(InnerGlowColorId, p.InnerGlowColor);
            mat.SetVector(RadiusId, p.CornerWidth);
            mat.SetVector(CornerHeightId, p.CornerHeight);
            mat.SetVector(CornerKindId, p.CornerKinds);
            mat.SetFloat(ShapeId, (float)p.Shape);
            mat.SetFloat(HexWidthId, p.HexWidth);
            mat.SetFloat(BorderWidthId, p.BorderWidth);
            mat.SetFloat(GlowSizeId, p.GlowSize);
            mat.SetFloat(InnerGlowSizeId, p.InnerGlowSize);
            if (!p.Glass) return;

            var g = p.GlassParams;
            mat.SetVector(GlassAId, new Vector4(g.Frost, g.Depth, g.Dispersion, g.Noise));

            // 0° is straight up, growing clockwise — so the highlight lands where the author
            // expects when they picture a light source over the UI.
            var rad = g.LightAngle * Mathf.Deg2Rad;
            mat.SetVector(GlassBId,
                new Vector4(Mathf.Sin(rad), Mathf.Cos(rad), g.LightIntensity, g.Saturation));
        }

        /// <summary>
        /// Destroys every pooled material. Called from <c>UI.ResetForTests</c> after open Screens
        /// are closed, so EditMode runs don't accumulate HideAndDontSave objects across tests.
        /// </summary>
        internal static void ResetForTests()
        {
            foreach (var slot in _live.Values) DestroyMaterial(slot.Material);
            _live.Clear();
            while (_spare.Count > 0) DestroyMaterial(_spare.Pop());
            while (_spareGlass.Count > 0) DestroyMaterial(_spareGlass.Pop());
        }

        private static void DestroyMaterial(Material mat)
        {
            if (mat == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(mat);
            else UnityEngine.Object.DestroyImmediate(mat);
        }
    }
}
