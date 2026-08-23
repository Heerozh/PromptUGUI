using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The full material-side parameter set of one <see cref="ProceduralPanel"/>. Deliberately
    /// excludes the panel's size and position — those ride the vertex stream — so every panel
    /// wearing the same style hashes to the same key regardless of how big it is.
    /// </summary>
    internal readonly struct PanelParams : IEquatable<PanelParams>
    {
        public readonly Color FillTop;
        public readonly Color FillBottom;
        public readonly Color BorderColor;
        public readonly Color GlowColor;
        public readonly Vector4 Radius;
        public readonly bool Pill;
        public readonly float BorderWidth;
        public readonly float GlowSize;

        public PanelParams(Color fillTop, Color fillBottom, Color borderColor, Color glowColor,
                           Vector4 radius, bool pill, float borderWidth, float glowSize)
        {
            FillTop = fillTop;
            FillBottom = fillBottom;
            BorderColor = borderColor;
            GlowColor = glowColor;
            Radius = radius;
            Pill = pill;
            BorderWidth = borderWidth;
            GlowSize = glowSize;
        }

        public bool Equals(PanelParams o) =>
            FillTop == o.FillTop && FillBottom == o.FillBottom
            && BorderColor == o.BorderColor && GlowColor == o.GlowColor
            && Radius == o.Radius && Pill == o.Pill
            && BorderWidth == o.BorderWidth && GlowSize == o.GlowSize;

        public override bool Equals(object o) => o is PanelParams p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = FillTop.GetHashCode();
                h = (h * 397) ^ FillBottom.GetHashCode();
                h = (h * 397) ^ BorderColor.GetHashCode();
                h = (h * 397) ^ GlowColor.GetHashCode();
                h = (h * 397) ^ Radius.GetHashCode();
                h = (h * 397) ^ Pill.GetHashCode();
                h = (h * 397) ^ BorderWidth.GetHashCode();
                h = (h * 397) ^ GlowSize.GetHashCode();
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
    /// Material. Steady-state allocation on both paths is zero (slots are structs).
    /// </summary>
    internal static class ProceduralMaterialCache
    {
        internal const string ShaderResourcePath = "PromptUGUI/Material/UI-ProceduralPanel";

        private static readonly int FillTopId = Shader.PropertyToID("_FillTop");
        private static readonly int FillBottomId = Shader.PropertyToID("_FillBottom");
        private static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int PillId = Shader.PropertyToID("_Pill");
        private static readonly int BorderWidthId = Shader.PropertyToID("_BorderWidth");
        private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");

        private readonly struct Slot
        {
            public readonly Material Material;
            public readonly int RefCount;
            public Slot(Material material, int refCount) { Material = material; RefCount = refCount; }
        }

        private static readonly Dictionary<PanelParams, Slot> _live = new();
        private static readonly Stack<Material> _spare = new();
        private static Shader _shader;

        /// <summary>Number of distinct live parameter sets. Test-only observability.</summary>
        internal static int LiveMaterialCount => _live.Count;

        public static Material Acquire(in PanelParams p)
        {
            if (_live.TryGetValue(p, out var slot))
            {
                _live[p] = new Slot(slot.Material, slot.RefCount + 1);
                return slot.Material;
            }

            var mat = _spare.Count > 0 ? _spare.Pop() : CreateMaterial();
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
            if (slot.Material != null) _spare.Push(slot.Material);
        }

        private static Material CreateMaterial()
        {
            _shader ??= Resources.Load<Shader>(ShaderResourcePath);
            if (_shader == null)
                throw new InvalidOperationException(
                    $"PromptUGUI: shader not found at Resources/{ShaderResourcePath}. " +
                    "The package's Runtime/Resources folder is required for procedural panels.");
            return new Material(_shader)
            {
                name = "PromptUGUI/ProceduralPanel",
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
            mat.SetVector(RadiusId, p.Radius);
            mat.SetFloat(PillId, p.Pill ? 1f : 0f);
            mat.SetFloat(BorderWidthId, p.BorderWidth);
            mat.SetFloat(GlowSizeId, p.GlowSize);
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
        }

        private static void DestroyMaterial(Material mat)
        {
            if (mat == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(mat);
            else UnityEngine.Object.DestroyImmediate(mat);
        }
    }
}
