using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Draws one <c>&lt;Decor&gt;</c> instance — a corner bracket, an edge tick or an emphasis line,
    /// plus an optional outer glow — straight from a signed distance field, with no sprite involved.
    ///
    /// <para><b>The slot rides the vertices, not the material.</b> Every shape is defined once in a
    /// canonical orientation (bracket hugging the top-left, tick pointing down) and this component
    /// folds the instance's slot into the local coordinates it writes into TEXCOORD0. A top-left and
    /// a bottom-right bracket therefore hash to the same <see cref="DecorParams"/>, share one
    /// material and batch into a single draw call — which is the whole reason a four-corner
    /// <c>&lt;Decor&gt;</c> costs one draw rather than four.</para>
    ///
    /// <para>Same split as <see cref="ProceduralPanel"/> otherwise: attribute writes only flag the
    /// material dirty and are resolved once per canvas rebuild; geometry is re-emitted only when the
    /// glow radius (which inflates the quad) or overall visibility changes; an instance that would
    /// paint nothing emits no geometry at all.</para>
    /// </summary>
    // Graphic's own [RequireComponent(typeof(CanvasRenderer))] does NOT carry over to a subclass
    // added via AddComponent at runtime — without this the panel throws MissingComponentException
    // the first time uGUI rebuilds it, and draws nothing.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class DecorPanel : MaskableGraphic
    {
        private DecorKind _kind = DecorKind.None;
        private DecorSlot _slot = DecorSlot.TopLeft;
        private Color _fillTop = Color.white;
        private Color _fillBottom = Color.white;
        private float _fillStopTop;
        private float _fillStopBottom = 1f;
        private Color _glowColor = Color.white;
        private bool _glowColorExplicit;
        private float _thickness = 2f;
        private float _glowSize;

        private DecorParams _key;
        private bool _hasKey;
        private bool _paramsDirty = true;
        private bool _lastVisible;
        private float _lastGlowGeometry;

        protected override void Awake()
        {
            base.Awake();
            // A decoration is never a hit target: it hangs over the host's edges, and swallowing
            // clicks there would make the host's own corners dead.
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasChannels();
            FlushParams();
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
                DecorMaterialCache.Release(_key);
                _hasKey = false;
            }
            base.OnDestroy();
        }

        /// <summary>
        /// The half-size input rides TEXCOORD1, which uGUI strips from the canvas mesh unless the
        /// Canvas opts in. Without this the shader reads zeros and every instance collapses.
        /// </summary>
        private void EnsureCanvasChannels()
        {
            var c = canvas;
            if (c == null) return;
            if ((c.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) == 0)
                c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }

        // ---- authoring surface (called from Decor's reconcile) ----

        public void SetKind(DecorKind kind) { _kind = kind; MarkDirty(); }

        /// <summary>Which corner / edge this instance sits on — folded into the mesh, not the material.</summary>
        public void SetSlot(DecorSlot slot)
        {
            if (_slot == slot) return;
            _slot = slot;
            SetVerticesDirty();
        }

        public void SetFill(in ColorSpec fill)
        {
            _fillTop = fill.Top;
            _fillBottom = fill.Bottom;
            _fillStopTop = fill.TopStop;
            _fillStopBottom = fill.BottomStop;
            MarkDirty();
        }

        public void SetThickness(float thickness)
        {
            _thickness = Mathf.Max(0f, thickness);
            MarkDirty();
        }

        public void SetGlowSize(float size)
        {
            _glowSize = Mathf.Max(0f, size);
            MarkDirty();
        }

        public void SetGlowColor(Color color)
        {
            _glowColor = color;
            _glowColorExplicit = true;
            MarkDirty();
        }

        /// <summary>Drops back to "glow follows the fill", the state a fresh instance is in.</summary>
        public void ClearGlowColor()
        {
            if (!_glowColorExplicit) return;
            _glowColorExplicit = false;
            MarkDirty();
        }

        internal DecorParams CurrentParams => BuildParams();
        internal bool IsPanelVisible => ComputeVisible();

        /// <summary>Test seam: EditMode never runs a canvas rebuild, so the mesh has to be asked for.</summary>
        internal void BuildMeshForTests(VertexHelper vh) => OnPopulateMesh(vh);

        private DecorParams BuildParams()
        {
            // Thickness only means something to the bracket; zeroing it elsewhere keeps two
            // instances that render identically from splitting the cache into two materials.
            var thickness = _kind == DecorKind.Bracket ? _thickness : 0f;
            var glow = _glowColorExplicit
                ? _glowColor
                : (_fillTop.a > 0f || _fillBottom.a > 0f ? _fillTop : Color.white);
            return new DecorParams(_fillTop, _fillBottom, _fillStopTop, _fillStopBottom,
                                   glow, _kind, thickness, _glowSize);
        }

        private bool ComputeVisible()
        {
            if (_kind == DecorKind.None) return false;
            if (_fillTop.a > 0f || _fillBottom.a > 0f) return true;
            return _glowSize > 0f;
        }

        private void MarkDirty()
        {
            _paramsDirty = true;

            var visible = ComputeVisible();
            if (visible != _lastVisible || !Mathf.Approximately(_glowSize, _lastGlowGeometry))
            {
                _lastVisible = visible;
                _lastGlowGeometry = _glowSize;
                SetVerticesDirty();
            }

            SetMaterialDirty();
        }

        /// <summary>
        /// Resolves the current parameters into a shared material. Called once per canvas rebuild,
        /// and eagerly by <c>Decor.OnAfterApply</c> so freshly built instances have their material
        /// before anything renders.
        /// </summary>
        internal void FlushParams(bool fromRebuild = false)
        {
            if (!_paramsDirty && _hasKey) return;
            _paramsDirty = false;

            var next = BuildParams();
            if (_hasKey && next.Equals(_key)) return;

            // Acquire before release so a shared material never drops to zero refs and back.
            var mat = DecorMaterialCache.Acquire(next);
            if (_hasKey) DecorMaterialCache.Release(_key);
            _key = next;
            _hasKey = true;

            // Assign the backing field rather than the `material` property: the property setter
            // calls SetMaterialDirty, which would re-enter this during a canvas rebuild.
            m_Material = mat;
            if (!fromRebuild) SetMaterialDirty();
        }

        protected override void UpdateMaterial()
        {
            FlushParams(fromRebuild: true);
            base.UpdateMaterial();
        }

        /// <summary>
        /// Maps a point in the instance's own rect frame into the shape's canonical frame. Corners
        /// mirror, the two vertical edges transpose — every case is a rigid reflection, so the SDF
        /// comes out congruent and only the material-free vertex data changes.
        /// </summary>
        private void CanonicalTransform(out bool swap, out float flipX, out float flipY)
        {
            swap = false;
            flipX = 1f;
            flipY = 1f;

            if (_kind == DecorKind.Line) return;

            switch (_slot)
            {
                // Bracket hugs the top-left canonically; the other three corners mirror into it.
                case DecorSlot.TopLeft: break;
                case DecorSlot.TopRight: flipX = -1f; break;
                case DecorSlot.BottomRight: flipX = -1f; flipY = -1f; break;
                case DecorSlot.BottomLeft: flipY = -1f; break;
                // Tick points down canonically, i.e. away from the host across the bottom edge.
                case DecorSlot.Bottom: break;
                case DecorSlot.Top: flipY = -1f; break;
                case DecorSlot.Left: swap = true; break;
                case DecorSlot.Right: swap = true; flipX = -1f; flipY = -1f; break;
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!ComputeVisible()) return;

            var r = GetPixelAdjustedRect();
            var hx = r.width * 0.5f;
            var hy = r.height * 0.5f;
            var cx = r.x + hx;
            var cy = r.y + hy;

            // Only a glow needs the quad to reach past the rect; without one the geometry stays
            // exactly the layout rect so there is no transparent overdraw around every instance.
            var pad = _glowSize;
            var ex = hx + pad;
            var ey = hy + pad;

            CanonicalTransform(out var swap, out var flipX, out var flipY);
            var half = swap ? new Vector4(hy, hx, 0f, 0f) : new Vector4(hx, hy, 0f, 0f);

            var v = UIVertex.simpleVert;
            v.color = (Color32)color;
            v.uv1 = half;

            AddCorner(vh, ref v, cx, cy, -ex, -ey, swap, flipX, flipY);
            AddCorner(vh, ref v, cx, cy, -ex, ey, swap, flipX, flipY);
            AddCorner(vh, ref v, cx, cy, ex, ey, swap, flipX, flipY);
            AddCorner(vh, ref v, cx, cy, ex, -ey, swap, flipX, flipY);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private static void AddCorner(VertexHelper vh, ref UIVertex v, float cx, float cy,
                                      float lx, float ly, bool swap, float flipX, float flipY)
        {
            v.position = new Vector3(cx + lx, cy + ly);
            v.uv0 = swap
                ? new Vector4(flipY * ly, flipX * lx, 0f, 0f)
                : new Vector4(flipX * lx, flipY * ly, 0f, 0f);
            vh.AddVert(v);
        }
    }
}
