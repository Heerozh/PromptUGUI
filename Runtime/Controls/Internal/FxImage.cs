using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The Image every <c>&lt;Image&gt;</c> and <c>&lt;Icon&gt;</c> is built on (spec 2026-09-02 §5.1).
    /// With nothing written it IS a plain <see cref="UnityEngine.UI.Image"/>: no material of its own
    /// (so uGUI draws it with <c>UI/Default</c> and batches it as before), the base mesh untouched,
    /// not one extra component. Ask for <c>blur</c> / <c>glow</c> — or a linear tint, or the disabled
    /// grey — and it takes over its own material the way <see cref="ProceduralPanel"/> does: shared
    /// per parameter set through <see cref="FxMaterialCache"/>, never one instance per graphic.
    ///
    /// <para><b>Why a subclass and not a <see cref="BaseMeshEffect"/>.</b> The quad has to be inflated
    /// BEFORE any other mesh effect runs — <c>GradientTint</c> normalises over the mesh it is handed
    /// and slices it at colour stops, <c>RotateFlipEffect</c> turns it about its centre — and a mesh
    /// effect can only guarantee that by being attached first, which would mean pre-attaching a
    /// disabled component to every Icon in the project. Post-processing inside
    /// <see cref="OnPopulateMesh"/> is ordered before every <c>IMeshModifier</c> by construction, and
    /// costs an unused Icon nothing.</para>
    ///
    /// <para><b>Why the material is folded rather than swapped.</b> <c>Graphic.material</c> is one
    /// slot, and <c>tint="linear"</c> and the disabled grey already fight over it by overwriting each
    /// other. Blur and glow would make that a three-way fight, so all four became parameters of one
    /// shader instead (<c>UI/ImageFx</c>) — which is also what lets an icon stay greyed while it
    /// glows.</para>
    /// </summary>
    // Graphic's own [RequireComponent(typeof(CanvasRenderer))] does NOT carry over to a subclass
    // added via AddComponent at runtime — see the same note on ProceduralPanel.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class FxImage : UnityEngine.UI.Image, ISelfGrayscale
    {
        private float _blur;
        private float _glow;
        private Color _glowColor = Color.white;
        private bool _glowColorExplicit;
        private bool _tintLinear;
        private bool _grayed;

        private FxParams _key;
        private bool _hasKey;
        private bool _paramsDirty = true;
        private float _lastPad;

        private static bool _warnedNonQuad;
        // Keyed on the texture itself rather than an id: GetInstanceID is obsolete-as-error on
        // Unity 6.7 and its replacement does not exist before it.
        private static readonly HashSet<Texture2D> _warnedNoMips = new();

        /// <summary>Blur radius in canvas units. Clamped at zero; 0 draws the sprite sharp.</summary>
        public float Blur
        {
            get => _blur;
            set
            {
                var v = Mathf.Max(0f, value);
                if (Mathf.Approximately(_blur, v)) return;
                _blur = v;
                MarkDirty();
            }
        }

        /// <summary>Outer glow reach in canvas units. Clamped at zero; 0 draws no glow.</summary>
        public float Glow
        {
            get => _glow;
            set
            {
                var v = Mathf.Max(0f, value);
                if (Mathf.Approximately(_glow, v)) return;
                _glow = v;
                MarkDirty();
            }
        }

        /// <summary>Paints the glow in one flat colour instead of the sprite's own blurred one.</summary>
        public void SetGlowColor(Color color)
        {
            if (_glowColorExplicit && _glowColor == color) return;
            _glowColor = color;
            _glowColorExplicit = true;
            MarkDirty();
        }

        /// <summary>
        /// The glow takes the sprite's own blurred colour at <paramref name="strength"/> (0–1):
        /// <c>glowColor="self/0.5"</c>. Full strength is the state a fresh instance is in.
        /// </summary>
        public void SetGlowSelf(float strength)
        {
            var s = Mathf.Clamp01(strength);
            if (!_glowColorExplicit && Mathf.Approximately(_glowColor.a, s)) return;
            _glowColorExplicit = false;
            // rgb is irrelevant while the glow is self-coloured (FxParams normalises it away); only
            // the alpha — the strength — travels to the shader.
            _glowColor = new Color(1f, 1f, 1f, s);
            MarkDirty();
        }

        /// <summary>Drops back to "the glow takes the sprite's own colour, at full strength", the
        /// state a fresh instance is in — how a Variant or a theme retracts a <c>glowColor</c>.</summary>
        public void ClearGlowColor() => SetGlowSelf(1f);

        /// <summary><c>tint="linear"</c>; driven by <see cref="ImageTint"/>.</summary>
        public bool TintLinear
        {
            get => _tintLinear;
            set
            {
                if (_tintLinear == value) return;
                _tintLinear = value;
                MarkDirty();
            }
        }

        /// <summary>The disabled look, folded into the composite so the glow greys with the body.
        /// Driven by <c>DisabledGrayscaleController</c>; see <see cref="ISelfGrayscale"/>.</summary>
        void ISelfGrayscale.SetDisabledGrayscale(bool value)
        {
            if (_grayed == value) return;
            _grayed = value;
            MarkDirty();
            // Flushed eagerly rather than at the next rebuild: the controller drives this from a
            // state subscription (never from inside a rebuild), and a control that goes disabled
            // has to look disabled in the same frame.
            FlushParams();
        }

        /// <summary>How far the drawn quad reaches past the sprite: the larger of the two radii.
        /// Layout never sees it (spec §4.2) — only the geometry grows.</summary>
        internal float Pad => Mathf.Max(_blur, _glow);

        /// <summary>
        /// Whether the mesh should be inflated at all. Only <c>type="simple"</c> qualifies: Sliced,
        /// Tiled and Filled meshes are not the single quad the sampling maths assumes, and a sprite
        /// with a 9-slice border becomes Sliced automatically unless the author wrote
        /// <c>type="simple"</c> (the control warns; lint catches the explicit spelling as
        /// <c>PUI-FX-TYPE</c>).
        /// </summary>
        internal bool HasGeometryFx => sprite != null && type == Type.Simple && Pad > 0f;

        /// <summary>Whether anything at all needs the fx shader.</summary>
        internal bool HasMaterialFx => HasGeometryFx || _tintLinear || _grayed;

        internal bool HasKeyForTests => _hasKey;
        internal FxParams KeyForTests => _key;
        internal void BuildMeshForTests(VertexHelper vh) => OnPopulateMesh(vh);

        /// <summary>Re-arms the warn-once diagnostics; called from <c>UI.ResetForTests</c>.</summary>
        internal static void ResetDiagnostics()
        {
            _warnedNonQuad = false;
            _warnedNoMips.Clear();
        }

        /// <summary>
        /// Whether the fx taps may sample <paramref name="tex"/>'s mip chain (spec §14.3): it has one,
        /// and it is not Point-filtered — Point samples its mips nearest as well, so a coarser level
        /// would only be blockier, never smoother.
        /// </summary>
        internal static bool CanSampleMips(Texture2D tex)
            => tex != null && tex.mipmapCount > 1 && tex.filterMode != FilterMode.Point;

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasChannels();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            EnsureCanvasChannels();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasChannels();
        }

        protected override void OnDestroy()
        {
            if (_hasKey)
            {
                FxMaterialCache.Release(_key);
                _hasKey = false;
            }
            base.OnDestroy();
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            base.OnPopulateMesh(toFill);

            var pad = HasGeometryFx ? Pad : 0f;
            _lastPad = pad;
            if (pad <= 0f) return;

            var tex = sprite.texture;
            var mipOk = CanSampleMips(tex);
            var textureSize = mipOk ? new Vector2(tex.width, tex.height) : Vector2.zero;

            if (!FxMesh.Inflate(toFill, pad, textureSize))
            {
                WarnOnce(ref _warnedNonQuad,
                    $"PromptUGUI: blur / glow on '{name}' needs the four-vertex quad of a Simple " +
                    $"sprite, but this graphic produced {toFill.currentVertCount} vertices " +
                    "(useSpriteMesh, or a mesh effect that ran first?). The effect is skipped.");
                return;
            }

            if (!mipOk && tex != null) WarnIfKernelLeavesGaps(toFill, tex, pad);
        }

        /// <summary>
        /// The precise half of the "needs mipmaps" diagnostic (spec §14.5; lint's <c>PUI-FX-RADIUS</c>
        /// is the coarse half, which cannot see the texture or the drawn size). Fires once per texture,
        /// only when the radius in TEXELS is past what the lod-0 kernel covers without gaps — a 10px
        /// glow on a sprite drawn at four times its size is fine, the same glow at 1:1 is not.
        /// </summary>
        private void WarnIfKernelLeavesGaps(VertexHelper vh, Texture2D tex, float pad)
        {
            var v = new UIVertex();
            vh.PopulateUIVertex(ref v, 0);
            // uv2.xy is uv per canvas unit; times the texture size that is texels per unit. (zw is
            // deliberately zero on this path — that is what keeps the fragment at lod 0.)
            var texelsPerUnit = Mathf.Max(v.uv2.x * tex.width, v.uv2.y * tex.height);
            if (!FxMesh.NeedsMips(pad, texelsPerUnit)) return;
            if (!_warnedNoMips.Add(tex)) return;

            var texels = pad * texelsPerUnit;
            var limitPx = 1f / (FxMesh.TapSpacing * texelsPerUnit);
            Debug.LogWarning(tex.filterMode == FilterMode.Point
                ? $"PromptUGUI: blur / glow of {pad:0.#}px on '{name}' is {texels:0.#} texels of the " +
                  $"Point-filtered texture '{tex.name}' — above ~{limitPx:0.#}px at this size the kernel " +
                  "draws ghost copies of thin strokes, and mipmaps cannot help a Point texture (they " +
                  $"are sampled nearest too). Keep the radius at or under {limitPx:0.#}px, or switch " +
                  "the texture to Bilinear with mipmaps."
                : $"PromptUGUI: blur / glow of {pad:0.#}px on '{name}' is {texels:0.#} texels of " +
                  $"'{tex.name}', which has no mipmaps — above ~{limitPx:0.#}px at this size the kernel " +
                  "draws ghost copies of thin strokes. Enable them (SpriteAtlas → Generate Mip Maps; " +
                  "TextureImporter → Generate Mipmaps); drawing with the plain kernel until then.");
        }

        protected override void UpdateMaterial()
        {
            FlushParams(fromRebuild: true);
            base.UpdateMaterial();
        }

        /// <summary>
        /// Resolves the current parameters into a shared material. Called once per canvas rebuild via
        /// <see cref="UpdateMaterial"/>, and eagerly from the controls' <c>OnAfterApply</c> so a
        /// freshly instantiated icon has its material before anything renders.
        /// </summary>
        /// <param name="fromRebuild">
        /// True when called from <see cref="UpdateMaterial"/>. Re-registering for a graphic rebuild
        /// from inside the rebuild loop makes uGUI complain, so the dirty flag is skipped there — the
        /// caller is already about to push the material through.
        /// </param>
        internal void FlushParams(bool fromRebuild = false)
        {
            if (!HasMaterialFx)
            {
                _paramsDirty = false;
                if (!_hasKey) return;

                FxMaterialCache.Release(_key);
                _hasKey = false;
                // Assign the backing field rather than the `material` property: the property setter
                // calls SetMaterialDirty, which would re-enter this during a canvas rebuild.
                m_Material = null;
                if (!fromRebuild) SetMaterialDirty();
                return;
            }

            if (!_paramsDirty && _hasKey) return;
            _paramsDirty = false;

            var next = BuildParams();
            if (_hasKey && next.Equals(_key)) return;

            // Acquire before release so a shared material never drops to zero refs and back.
            var mat = FxMaterialCache.Acquire(next);
            if (_hasKey) FxMaterialCache.Release(_key);
            _key = next;
            _hasKey = true;

            m_Material = mat;
            if (!fromRebuild) SetMaterialDirty();
        }

        private FxParams BuildParams()
        {
            // Radii only count while the geometry actually carries them: a Sliced mesh was never
            // inflated and has no atlas rect in uv1, so asking the shader to blur it would clip the
            // sprite away entirely. Letting them into the key would also split the cache into
            // entries that render identically.
            var geometry = HasGeometryFx;
            return new FxParams(
                geometry ? _blur : 0f,
                geometry ? _glow : 0f,
                _glowColor,
                !_glowColorExplicit,
                _tintLinear,
                _grayed);
        }

        /// <summary>
        /// Records that the parameters changed and rebuilds the mesh only when the radius that
        /// inflates it actually moved — a colour or tint change is material-only, so a Variant flip
        /// or a colour tween never touches the canvas mesh.
        /// </summary>
        private void MarkDirty()
        {
            _paramsDirty = true;

            var pad = HasGeometryFx ? Pad : 0f;
            if (!Mathf.Approximately(pad, _lastPad))
            {
                _lastPad = pad;
                if (pad > 0f) EnsureCanvasChannels();
                SetVerticesDirty();
            }

            SetMaterialDirty();
        }

        /// <summary>
        /// Re-evaluates everything that depends on state this component does not own — the sprite and
        /// the Image <c>type</c>, both of which decide whether the fx can apply at all. Called from
        /// the controls' <c>OnAfterApply</c>, once all attribute setters have run.
        /// </summary>
        internal void RefreshFxState() => MarkDirty();

        /// <summary>
        /// The sprite's atlas rect and uv scale ride TEXCOORD1 / TEXCOORD2, which uGUI strips from
        /// the canvas mesh unless the Canvas opts in. Without this the shader reads zeros, sees a
        /// degenerate rect and falls back to drawing the sprite plain.
        ///
        /// <para>Only opened once something actually needs them: a project that never writes blur or
        /// glow keeps its canvas vertices as small as they are today.</para>
        /// </summary>
        private void EnsureCanvasChannels()
        {
            if (!HasGeometryFx) return;
            var c = canvas;
            if (c == null) return;

            const AdditionalCanvasShaderChannels needed =
                AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            if ((c.additionalShaderChannels & needed) != needed)
                c.additionalShaderChannels |= needed;
        }

        private static void WarnOnce(ref bool flag, string message)
        {
            if (flag) return;
            flag = true;
            Debug.LogWarning(message);
        }
    }
}
