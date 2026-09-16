using System;
using System.Collections.Generic;
using PromptUGUI.Application;
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
        /// <summary>Fill and outer glow, each a full gradient (spec 2026-09-17 §6.2). A solid slot
        /// is a one-stop spec, so decor that never asked for a gradient keys exactly as it did before.
        /// The ramp runs in the decoration's canonical frame — the same one the shape is defined
        /// in — so mirrored instances (four brackets) mirror it too and keep sharing one material.</summary>
        public readonly ColorSpec Fill;
        public readonly ColorSpec Glow;
        public readonly DecorKind Kind;
        public readonly float Thickness;
        public readonly float GlowSize;
        /// <summary>Exposure of everything the decor paints (spec 2026-09-12); 1 = unchanged.</summary>
        public readonly float Intensity;

        public DecorParams(in ColorSpec fill, in ColorSpec glow, DecorKind kind, float thickness, float glowSize,
                           float intensity = 1f)
        {
            Fill = fill;
            Glow = glow;
            Kind = kind;
            Thickness = thickness;
            GlowSize = glowSize;
            Intensity = intensity;
        }

        public bool Equals(DecorParams o) =>
            Fill == o.Fill && Glow == o.Glow
            && Kind == o.Kind && Thickness == o.Thickness && GlowSize == o.GlowSize
            && Intensity == o.Intensity;

        public override bool Equals(object o) => o is DecorParams p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Fill.GetHashCode();
                h = (h * 397) ^ Glow.GetHashCode();
                h = (h * 397) ^ (int)Kind;
                h = (h * 397) ^ Thickness.GetHashCode();
                h = (h * 397) ^ GlowSize.GetHashCode();
                h = (h * 397) ^ Intensity.GetHashCode();
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

        private static readonly int KindId = Shader.PropertyToID("_Kind");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

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
            GradientUniforms.Write(mat, GradientUniforms.Fill, p.Fill);
            GradientUniforms.Write(mat, GradientUniforms.Glow, p.Glow);
            mat.SetFloat(KindId, (float)p.Kind);
            mat.SetFloat(ThicknessId, p.Thickness);
            mat.SetFloat(GlowSizeId, p.GlowSize);
            mat.SetFloat(IntensityId, p.Intensity);
        }
    }
}
