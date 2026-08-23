using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Draws a rounded rectangle — fill (optionally a vertical gradient), inner border and outer
    /// glow — straight from a signed distance field, with no sprite involved. Lazily attached by
    /// <see cref="Frame"/> the first time the author writes one of the procedural visual attributes;
    /// a Frame without them stays the pure RectTransform container it has always been.
    ///
    /// Performance shape (this is the whole point of the split between vertex and material data):
    /// <list type="bullet">
    /// <item>Colour / radius / border / glow-colour changes touch only the material, so a Variant
    /// flip or a colour tween never rebuilds the canvas mesh.</item>
    /// <item>Geometry is dirtied only when the glow radius (which inflates the quad) or overall
    /// visibility changes.</item>
    /// <item>A fully transparent panel emits no geometry at all — zero overdraw, which is the
    /// binding constraint on mobile UI.</item>
    /// <item><c>raycastTarget</c> is forced off: a Frame stays click-through, and the raycast list
    /// the EventSystem walks every pointer event stays short.</item>
    /// </list>
    /// </summary>
    // Graphic's own [RequireComponent(typeof(CanvasRenderer))] does NOT carry over to a subclass
    // added via AddComponent at runtime — without this the panel throws MissingComponentException
    // the first time uGUI rebuilds it, and draws nothing. Note that EditMode tests never run a
    // canvas rebuild, so only a real render (or CanvasRebuildTests) catches its absence.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ProceduralPanel : MaskableGraphic
    {
        private Color _fillTop = Color.clear;
        private Color _fillBottom = Color.clear;
        private Color _borderColor = Color.white;
        private Color _glowColor = Color.white;
        private bool _glowColorExplicit;
        private RadiusSpec _radius = RadiusSpec.Zero;
        private float _borderWidth;
        private float _glowSize;

        private PanelParams _key;
        private bool _hasKey;
        private bool _lastVisible;
        private float _lastGlowGeometry;

        protected override void Awake()
        {
            base.Awake();
            // A Frame is a container, not a hit target. Set on the component (not via the author's
            // XML) because there is no scenario where a procedural background should swallow clicks
            // that the plain Frame would have let through.
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasChannels();
            ApplyParams();
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
                ProceduralMaterialCache.Release(_key);
                _hasKey = false;
            }
            base.OnDestroy();
        }

        /// <summary>
        /// The half-size input rides TEXCOORD1, which uGUI strips from the canvas mesh unless the
        /// Canvas opts in. Without this the shader reads zeros and every panel collapses.
        /// </summary>
        private void EnsureCanvasChannels()
        {
            var c = canvas;
            if (c == null) return;
            if ((c.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) == 0)
                c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }

        // ---- authoring surface (called from Frame's [UIAttr] setters) ----

        public void SetFill(Color top, Color bottom)
        {
            _fillTop = top;
            _fillBottom = bottom;
            ApplyParams();
        }

        public void SetRadius(RadiusSpec radius)
        {
            _radius = radius;
            ApplyParams();
        }

        public void SetBorderWidth(float width)
        {
            _borderWidth = Mathf.Max(0f, width);
            ApplyParams();
        }

        public void SetBorderColor(Color color)
        {
            _borderColor = color;
            ApplyParams();
        }

        public void SetGlowSize(float size)
        {
            _glowSize = Mathf.Max(0f, size);
            ApplyParams();
        }

        public void SetGlowColor(Color color)
        {
            _glowColor = color;
            _glowColorExplicit = true;
            ApplyParams();
        }

        // Test observability — EditMode asserts read the resolved parameters without needing a
        // canvas rebuild to have run.
        internal PanelParams CurrentParams => BuildParams();
        internal bool IsPanelVisible => ComputeVisible();

        /// <summary>Runs the geometry pass on demand — EditMode has no canvas rebuild loop.</summary>
        internal void BuildMeshForTests(VertexHelper vh) => OnPopulateMesh(vh);

        // ---- rendering ----

        private PanelParams BuildParams()
        {
            // An unset glowColor follows the fill so `glow="12"` alone reads as "this shape glows",
            // not "this shape gets a white halo".
            var glow = _glowColorExplicit
                ? _glowColor
                : (_fillTop.a > 0f ? new Color(_fillTop.r, _fillTop.g, _fillTop.b, 1f) : Color.white);

            return new PanelParams(
                _fillTop, _fillBottom, _borderColor, glow,
                new Vector4(_radius.TopLeft, _radius.TopRight, _radius.BottomRight, _radius.BottomLeft),
                _radius.IsPill, _borderWidth, _glowSize);
        }

        private bool ComputeVisible()
        {
            if (_fillTop.a > 0f || _fillBottom.a > 0f) return true;
            if (_borderWidth > 0f && _borderColor.a > 0f) return true;
            if (_glowSize > 0f) return true;
            return false;
        }

        private void ApplyParams()
        {
            var visible = ComputeVisible();
            if (visible != _lastVisible || !Mathf.Approximately(_glowSize, _lastGlowGeometry))
            {
                _lastVisible = visible;
                _lastGlowGeometry = _glowSize;
                SetVerticesDirty();
            }

            var next = BuildParams();
            if (_hasKey && next.Equals(_key)) return;

            // Acquire before release so a shared material never drops to zero refs and back.
            var mat = ProceduralMaterialCache.Acquire(next);
            if (_hasKey) ProceduralMaterialCache.Release(_key);
            _key = next;
            _hasKey = true;

            // Assign the backing field rather than the `material` property: the property setter
            // calls SetMaterialDirty, which would re-enter this during a canvas rebuild.
            m_Material = mat;
            SetMaterialDirty();
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
            // exactly the layout rect so there is no transparent overdraw around every panel.
            var pad = _glowSize;
            var ex = hx + pad;
            var ey = hy + pad;

            var half = new Vector4(hx, hy, 0f, 0f);
            var tint = (Color32)color;

            var v = UIVertex.simpleVert;
            v.color = tint;
            v.uv1 = half;

            v.position = new Vector3(cx - ex, cy - ey);
            v.uv0 = new Vector4(-ex, -ey, 0f, 0f);
            vh.AddVert(v);

            v.position = new Vector3(cx - ex, cy + ey);
            v.uv0 = new Vector4(-ex, ey, 0f, 0f);
            vh.AddVert(v);

            v.position = new Vector3(cx + ex, cy + ey);
            v.uv0 = new Vector4(ex, ey, 0f, 0f);
            vh.AddVert(v);

            v.position = new Vector3(cx + ex, cy - ey);
            v.uv0 = new Vector4(ex, -ey, 0f, 0f);
            vh.AddVert(v);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
