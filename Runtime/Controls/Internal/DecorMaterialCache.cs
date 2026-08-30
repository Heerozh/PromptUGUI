using System;
using System.Collections.Generic;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The material-side parameter set of one <see cref="DecorPanel"/>. Deliberately excludes both
    /// the instance's size and its slot: size rides the vertex stream, and the slot is folded into
    /// the vertex coordinates (see <see cref="DecorPanel.OnPopulateMesh"/>). That is what lets the
    /// four corner brackets of one <c>&lt;Decor&gt;</c> share a single material and batch into one
    /// draw call instead of four.
    /// </summary>
    internal readonly struct DecorParams : IEquatable<DecorParams>
    {
        public readonly Color FillTop;
        public readonly Color FillBottom;
        /// <summary>Gradient stop positions, 0..1 from the top edge (spec 2026-08-30). 0/1 is the
        /// full-height ramp, so decor that never asked for stops keys exactly as it did before.</summary>
        public readonly float FillStopTop;
        public readonly float FillStopBottom;
        /// <summary>Power the ramp is raised to, from a colour hint; 1 = the plain linear ramp.</summary>
        public readonly float FillCurve;
        public readonly Color GlowColor;
        public readonly DecorKind Kind;
        public readonly float Thickness;
        public readonly float GlowSize;

        public DecorParams(Color fillTop, Color fillBottom,
                           float fillStopTop, float fillStopBottom, float fillCurve,
                           Color glowColor, DecorKind kind, float thickness, float glowSize)
        {
            FillTop = fillTop;
            FillBottom = fillBottom;
            FillStopTop = fillStopTop;
            FillStopBottom = fillStopBottom;
            FillCurve = fillCurve;
            GlowColor = glowColor;
            Kind = kind;
            Thickness = thickness;
            GlowSize = glowSize;
        }

        public bool Equals(DecorParams o) =>
            FillTop == o.FillTop && FillBottom == o.FillBottom
            && FillStopTop == o.FillStopTop && FillStopBottom == o.FillStopBottom
            && FillCurve == o.FillCurve
            && GlowColor == o.GlowColor
            && Kind == o.Kind && Thickness == o.Thickness && GlowSize == o.GlowSize;

        public override bool Equals(object o) => o is DecorParams p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = FillTop.GetHashCode();
                h = (h * 397) ^ FillBottom.GetHashCode();
                h = (h * 397) ^ FillStopTop.GetHashCode();
                h = (h * 397) ^ FillStopBottom.GetHashCode();
                h = (h * 397) ^ FillCurve.GetHashCode();
                h = (h * 397) ^ GlowColor.GetHashCode();
                h = (h * 397) ^ (int)Kind;
                h = (h * 397) ^ Thickness.GetHashCode();
                h = (h * 397) ^ GlowSize.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// Hands out one shared <see cref="Material"/> per distinct <see cref="DecorParams"/>, with the
    /// same refcount + spare-stack shape as <see cref="ProceduralMaterialCache"/> — see that type
    /// for why <c>CanvasRenderer</c> leaves per-instance materials as the only option and why the
    /// released ones are pooled rather than destroyed.
    /// </summary>
    internal static class DecorMaterialCache
    {
        internal const string ShaderResourcePath = "PromptUGUI/Material/UI-Decor";

        private static readonly int FillTopId = Shader.PropertyToID("_FillTop");
        private static readonly int FillBottomId = Shader.PropertyToID("_FillBottom");
        private static readonly int FillStopsId = Shader.PropertyToID("_FillStops");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int KindId = Shader.PropertyToID("_Kind");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");

        private readonly struct Slot
        {
            public readonly Material Material;
            public readonly int RefCount;
            public Slot(Material material, int refCount) { Material = material; RefCount = refCount; }
        }

        private static readonly Dictionary<DecorParams, Slot> _live = new();
        private static readonly Stack<Material> _spare = new();
        private static Shader _shader;

        /// <summary>Number of distinct live parameter sets. Test-only observability.</summary>
        internal static int LiveMaterialCount => _live.Count;

        public static Material Acquire(in DecorParams p)
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

        public static void Release(in DecorParams p)
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
            var shader = _shader ??= Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
                throw new InvalidOperationException(
                    $"PromptUGUI: shader not found at Resources/{ShaderResourcePath}. " +
                    "The package's Runtime/Resources folder is required for <Decor>.");
            return new Material(shader)
            {
                name = "PromptUGUI/Decor",
                // Runtime-created and never authored into a scene; without this it would be
                // offered up for serialization and leak into the user's assets.
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static void Configure(Material mat, in DecorParams p)
        {
            mat.SetColor(FillTopId, p.FillTop);
            mat.SetColor(FillBottomId, p.FillBottom);
            mat.SetVector(FillStopsId, new Vector4(p.FillStopTop, p.FillStopBottom, p.FillCurve, 0f));
            mat.SetColor(GlowColorId, p.GlowColor);
            mat.SetFloat(KindId, (float)p.Kind);
            mat.SetFloat(ThicknessId, p.Thickness);
            mat.SetFloat(GlowSizeId, p.GlowSize);
        }
    }
}
