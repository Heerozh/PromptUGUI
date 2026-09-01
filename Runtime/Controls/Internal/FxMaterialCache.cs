using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The full material-side parameter set of one <see cref="FxImage"/> (spec 2026-09-02 §4.1).
    /// Deliberately excludes the sprite, its atlas rectangle and the element's size — those ride the
    /// vertex stream — so every icon wearing the same style hashes to the same key no matter which
    /// picture it draws or how big it is.
    /// </summary>
    /// <remarks>
    /// Two keys that would draw identical pixels are normalised into one by the constructor: an
    /// explicit <c>GlowColor</c> is dropped while <see cref="GlowSelf"/> is on (the colour cannot
    /// show), and the whole glow block is dropped when <see cref="Glow"/> is zero. Letting them
    /// through would split the cache into entries that render the same — two materials, two draw
    /// calls, no visible difference — the same trap <c>PanelParams</c> avoids by zeroing an opaque
    /// panel's glass block.
    /// </remarks>
    internal readonly struct FxParams : IEquatable<FxParams>
    {
        /// <summary>Blur radius in canvas units (design px); 0 = the sprite is drawn sharp.</summary>
        public readonly float Blur;
        /// <summary>Outer glow reach in canvas units; 0 = no glow.</summary>
        public readonly float Glow;
        /// <summary>Glow tint; only meaningful while <see cref="GlowSelf"/> is false.</summary>
        public readonly Color GlowColor;
        /// <summary>The author wrote no <c>glowColor</c>: the glow takes the sprite's own blurred
        /// colour, so a coloured icon glows in its colour (spec §4.3).</summary>
        public readonly bool GlowSelf;
        /// <summary><c>tint="linear"</c> — Linear Light instead of the default multiply.</summary>
        public readonly bool TintLinear;
        /// <summary>The disabled look, applied to the composite (body and glow alike).</summary>
        public readonly bool Desaturate;

        public FxParams(float blur, float glow, Color glowColor, bool glowSelf,
                        bool tintLinear, bool desaturate)
        {
            Blur = Mathf.Max(0f, blur);
            Glow = Mathf.Max(0f, glow);
            var hasGlow = Glow > 0f;
            GlowSelf = !hasGlow || glowSelf;
            GlowColor = hasGlow && !glowSelf ? glowColor : Color.white;
            TintLinear = tintLinear;
            Desaturate = desaturate;
        }

        public bool Equals(FxParams o) =>
            Blur == o.Blur && Glow == o.Glow && GlowSelf == o.GlowSelf
            && GlowColor == o.GlowColor
            && TintLinear == o.TintLinear && Desaturate == o.Desaturate;

        public override bool Equals(object o) => o is FxParams p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Blur.GetHashCode();
                h = (h * 397) ^ Glow.GetHashCode();
                h = (h * 397) ^ GlowColor.GetHashCode();
                h = (h * 397) ^ GlowSelf.GetHashCode();
                h = (h * 397) ^ TintLinear.GetHashCode();
                h = (h * 397) ^ Desaturate.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// Hands out one shared <see cref="Material"/> per distinct <see cref="FxParams"/>, exactly as
    /// <see cref="ProceduralMaterialCache"/> does for panels and for the same reason:
    /// <c>CanvasRenderer</c> ignores <c>MaterialPropertyBlock</c>, so per-instance parameters can
    /// only live in per-instance materials — and every distinct material is a batch break. Icons
    /// naturally agree on their parameters (a row of <c>class="rare"</c> icons is one parameter set),
    /// so keying on the parameters collapses them back into one material and lets uGUI batch them.
    ///
    /// <para>Released materials are parked on a spare stack rather than destroyed, so an icon whose
    /// glow is tweened frame-by-frame walks through unique keys without ever allocating or destroying
    /// a Material. Steady-state allocation is zero (slots are structs).</para>
    /// </summary>
    internal static class FxMaterialCache
    {
        internal const string ShaderResourcePath = "PromptUGUI/Material/UI-ImageFx";
        internal const string MaterialName = "PromptUGUI/ImageFx";

        private static readonly int BlurId = Shader.PropertyToID("_Blur");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowSelfId = Shader.PropertyToID("_GlowSelf");
        private static readonly int TintLinearId = Shader.PropertyToID("_TintLinear");
        private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");

        private readonly struct Slot
        {
            public readonly Material Material;
            public readonly int RefCount;
            public Slot(Material material, int refCount) { Material = material; RefCount = refCount; }
        }

        private static readonly Dictionary<FxParams, Slot> _live = new();
        private static readonly Stack<Material> _spare = new();
        private static Shader _shader;

        /// <summary>Number of distinct live parameter sets. Test-only observability.</summary>
        internal static int LiveMaterialCount => _live.Count;

        /// <summary>Materials parked for reuse. Test-only observability.</summary>
        internal static int SpareCount => _spare.Count;

        public static Material Acquire(in FxParams p)
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

        public static void Release(in FxParams p)
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
                    "The package's Runtime/Resources folder is required for blur / glow.");

            return new Material(shader)
            {
                name = MaterialName,
                // Runtime-created and never authored into a scene; without this it would be
                // offered up for serialization and leak into the user's assets.
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static void Configure(Material mat, in FxParams p)
        {
            mat.SetFloat(BlurId, p.Blur);
            mat.SetFloat(GlowId, p.Glow);
            mat.SetColor(GlowColorId, p.GlowColor);
            mat.SetFloat(GlowSelfId, p.GlowSelf ? 1f : 0f);
            mat.SetFloat(TintLinearId, p.TintLinear ? 1f : 0f);
            mat.SetFloat(DesaturateId, p.Desaturate ? 1f : 0f);
        }

        /// <summary>
        /// Destroys every pooled material. Called from <c>UI.ResetForTests</c> after open Screens are
        /// closed, so EditMode runs don't accumulate HideAndDontSave objects across tests.
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
