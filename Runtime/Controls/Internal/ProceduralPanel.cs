using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Draws a rounded rectangle — fill (optionally a vertical gradient), inner border, inner glow
    /// and outer glow — straight from a signed distance field, with no sprite involved. With
    /// <c>glass="true"</c> the fill becomes a blurred sample of the captured backdrop plus edge
    /// refraction and lighting; the shape, border and glow are the same SDF either way. Lazily
    /// attached by <see cref="Frame"/> the first time the author writes one of the procedural visual
    /// attributes; a Frame without them stays the pure RectTransform container it has always been.
    ///
    /// Performance shape (this is the whole point of the split between vertex and material data):
    /// <list type="bullet">
    /// <item>Colour / radius / border / glow-colour changes touch only the material, so a Variant
    /// flip or a colour tween never rebuilds the canvas mesh.</item>
    /// <item>Attribute writes only flag the material dirty; the parameters are resolved once per
    /// canvas rebuild (see <see cref="FlushParams"/>), so applying seventeen attributes at
    /// instantiation costs one material lookup, not seventeen.</item>
    /// <item>Geometry is dirtied only when the glow radius (which inflates the quad) or overall
    /// visibility changes.</item>
    /// <item>A fully transparent panel emits no geometry at all — zero overdraw, which is the
    /// binding constraint on mobile UI.</item>
    /// <item><c>raycastTarget</c> is off by default and the owner decides: a Frame turns it on
    /// only for an authored <c>raycastTarget="true"</c>, a <see cref="ProceduralSurface"/> hands it
    /// the hit role of the Image it retires — both through <see cref="SetRaycastTarget"/>, never the
    /// bare property, so the decision survives a deferred <c>Awake</c>. Everything else stays out of
    /// the raycast list the EventSystem walks every pointer event (spec 2026-09-15 §3). A panel that
    /// draws nothing can still be a hit target — a transparent catcher with no geometry.</item>
    /// </list>
    /// </summary>
    // Graphic's own [RequireComponent(typeof(CanvasRenderer))] does NOT carry over to a subclass
    // added via AddComponent at runtime — without this the panel throws MissingComponentException
    // the first time uGUI rebuilds it, and draws nothing. Note that EditMode tests never run a
    // canvas rebuild, so only a real render (or CanvasRebuildTests) catches its absence.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ProceduralPanel : MaskableGraphic, ISelfGrayscale
    {
        private ColorSpec _fill = ColorSpec.Solid(Color.clear);
        private ColorSpec _borderColor = ColorSpec.Solid(Color.white);
        private ColorSpec _glowColor = ColorSpec.Solid(Color.white);
        private bool _glowColorExplicit;
        // No "explicit" twin: unlike the outer glow this one does not fall back to the fill, so
        // white IS the default rather than a stand-in for one (spec 2026-08-28 §5.4).
        private ColorSpec _innerGlowColor = ColorSpec.Solid(Color.white);
        private RadiusSpec _radius = RadiusSpec.Zero;
        private float _borderWidth;
        private float _glowSize;
        private float _innerGlowSize;
        // Exposure (spec 2026-09-12). 1 is "unlit" — the default has to be the identity, or every
        // panel in the library would light up.
        private float _intensity = IntensityAttrParser.Default;
        // The noise-fog layer (spec 2026-09-17 haze). Size 0 is "no fog"; the colour defaults to
        // white for the inner glow's reason (fog in the fill's own colour is invisible on an opaque
        // fill), and never follows the fill.
        private float _hazeSize;
        private ColorSpec _hazeColor = ColorSpec.Solid(Color.white);
        private float _hazeDrift;

        private bool _glass;
        private float _frost = GlassAttrParser.DefaultFrost;
        private float _depth = GlassAttrParser.DefaultDepth;
        private float _dispersion = GlassAttrParser.DefaultDispersion;
        private float _lightAngle = GlassAttrParser.DefaultLightAngle;
        private float _lightIntensity = GlassAttrParser.DefaultLightIntensity;
        private float _saturation = GlassAttrParser.DefaultSaturation;
        private float _noise = GlassAttrParser.DefaultNoise;

        private PanelParams _key;
        private bool _hasKey;
        private bool _paramsDirty = true;
        private bool _lastVisible;
        private float _lastGlowGeometry;
        private bool _countedAsGlass;
        private bool _suppressed;
        private bool _maskSource;
        private bool _grayed;

        private static bool _warnedFeedbackLoop;

        /// <summary>
        /// Set while this panel is a member of a <see cref="GlassGroupPanel"/>: the group draws the
        /// fused shape, so this panel contributes parameters but no geometry of its own.
        /// </summary>
        internal GlassGroupPanel Group { get; set; }

        private bool _raycastDecided;

        /// <summary>
        /// The owner's decision on the hit role (Frame.RaycastTarget for an authored
        /// <c>raycastTarget="true"</c>, ProceduralSurface for a control's retired hit layer). Goes
        /// through here rather than the bare property because Awake is NOT "at creation": a panel
        /// built under an inactive parent (a ScrollList row bound while its tab page is hidden) gets
        /// its Awake only when the parent is shown — after the owner already decided — and the
        /// click-through default in Awake used to clobber that, leaving a procedural Slider / Btn /
        /// catcher with no hit area at all (ssw_re_client 岗位页的滑块, 2026-09-16).
        /// </summary>
        internal void SetRaycastTarget(bool on)
        {
            _raycastDecided = true;
            raycastTarget = on;
        }

        protected override void Awake()
        {
            base.Awake();
            // Click-through until the owner says otherwise. uGUI's default is true, which is the
            // wrong default for a library where hit-testing is declared rather than painted. Only
            // while nobody has decided yet: see SetRaycastTarget for why Awake can run late.
            if (!_raycastDecided) raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasChannels();
            SyncGlassCount();
            // A block hidden by a Variant drops out of its weld group's fused shape and back in
            // again — the group has to re-pack either way.
            if (Group != null) Group.MarkMembersDirty();
            FlushParams();
        }

        protected override void OnDisable()
        {
            SyncGlassCount();
            if (Group != null) Group.MarkMembersDirty();
            base.OnDisable();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            EnsureCanvasChannels();
            WarnOnBackdropFeedbackLoop();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasChannels();
        }

        /// <summary>Re-arms the warn-once diagnostics; see <c>GlassRuntime</c>.</summary>
        internal static void ResetDiagnostics() => _warnedFeedbackLoop = false;

        protected override void OnDestroy()
        {
            // Leave the weld group before disappearing, so it never walks a destroyed member.
            var group = Group;
            Group = null;
            if (group != null) group.MarkMembersDirty();

            if (_countedAsGlass)
            {
                _countedAsGlass = false;
                GlassRuntime.PanelDeactivated();
            }
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

        public void SetFill(in ColorSpec fill)
        {
            _fill = fill;
            MarkDirty();
        }

        public void SetRadius(RadiusSpec radius)
        {
            _radius = radius;
            MarkDirty();
        }

        public void SetBorderWidth(float width)
        {
            _borderWidth = Mathf.Max(0f, width);
            MarkDirty();
        }

        public void SetBorderColor(Color color) => SetBorderColor(ColorSpec.Solid(color));

        public void SetBorderColor(in ColorSpec color)
        {
            _borderColor = color;
            MarkDirty();
        }

        public void SetGlowSize(float size)
        {
            _glowSize = Mathf.Max(0f, size);
            MarkDirty();
        }

        public void SetGlowColor(Color color) => SetGlowColor(ColorSpec.Solid(color));

        public void SetGlowColor(in ColorSpec color)
        {
            _glowColor = color;
            _glowColorExplicit = true;
            MarkDirty();
        }

        public void SetInnerGlowSize(float size)
        {
            _innerGlowSize = Mathf.Max(0f, size);
            MarkDirty();
        }

        public void SetInnerGlowColor(Color color) => SetInnerGlowColor(ColorSpec.Solid(color));

        public void SetInnerGlowColor(in ColorSpec color)
        {
            _innerGlowColor = color;
            MarkDirty();
        }

        /// <summary>
        /// Exposure of everything the panel paints; 1 = unchanged. Material-only: the curve
        /// brightens what is already drawn, so the geometry never changes for it.
        /// </summary>
        public void SetIntensity(float intensity)
        {
            _intensity = Mathf.Max(IntensityAttrParser.Min, intensity);
            MarkDirty();
        }

        /// <summary>Feature size of the fog's patches, px; 0 = no fog. Material-only, never geometry.</summary>
        public void SetHazeSize(float size)
        {
            _hazeSize = Mathf.Max(0f, size);
            MarkDirty();
        }

        /// <summary>The fog colour — a full gradient, which is also its directional mask (H-D2).</summary>
        public void SetHazeColor(in ColorSpec color)
        {
            _hazeColor = color;
            MarkDirty();
        }

        /// <summary>Flow speed in px per unscaled second; 0 = still.</summary>
        public void SetHazeDrift(float drift)
        {
            _hazeDrift = Mathf.Max(0f, drift);
            MarkDirty();
        }

        public void SetGlass(bool glass)
        {
            if (_glass == glass) return;
            _glass = glass;
            SyncGlassCount();
            WarnOnBackdropFeedbackLoop();
            // Membership in a weld group is decided by this flag, and ReSolve applies the container
            // before its children — so the group has already synced against the old value and only
            // finds out if we tell it.
            NotifyWeldGroup();
            MarkDirty();
        }

        /// <summary>
        /// Tells the weld group covering this panel that its membership may have changed. When the
        /// panel is already a member the group is known; when it is turning glass ON for the first
        /// time it is not, so the group is looked up among the parent's children — that is where
        /// <see cref="GlassGroupPanel.Attach"/> puts it.
        /// </summary>
        private void NotifyWeldGroup()
        {
            var group = Group;
            if (group == null)
            {
                var parent = transform.parent;
                if (parent == null) return;
                for (var i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i).TryGetComponent<GlassGroupPanel>(out var found))
                    {
                        group = found;
                        break;
                    }
            }
            if (group != null) group.RequestMemberRescan();
        }

        public void SetFrost(float v) { _frost = v; MarkDirty(); }
        public void SetDepth(float v) { _depth = v; MarkDirty(); }
        public void SetDispersion(float v) { _dispersion = v; MarkDirty(); }
        public void SetLightAngle(float v) { _lightAngle = v; MarkDirty(); }
        public void SetLightIntensity(float v) { _lightIntensity = v; MarkDirty(); }
        public void SetSaturation(float v) { _saturation = v; MarkDirty(); }
        public void SetNoise(float v) { _noise = v; MarkDirty(); }

        internal bool IsGlass => _glass;

        /// <summary>
        /// The fill as last authored — what the Frame / surface painted. Read by
        /// <see cref="ColorApplier.Peek"/>: on a panel the colour lives in the material and
        /// <c>Graphic.color</c> is only the multiplier (white at rest), so a state reactor that
        /// peeked <c>Graphic.color</c> for its fallback base would paint white into the fill.
        /// </summary>
        internal ColorSpec Fill => _fill;

        /// <summary>
        /// The glass values as written, regardless of whether glass mode is on. A weld container is
        /// not itself glass but carries the group-level parameters, so the group reads them here —
        /// <see cref="CurrentParams"/> deliberately zeroes them outside glass mode to protect the
        /// material cache.
        /// </summary>
        internal GlassParams RawGlassParams => new(_frost, _depth, _dispersion, _lightAngle,
                                                   _lightIntensity, _saturation, _noise);

        // Test observability — EditMode asserts read the resolved parameters without needing a
        // canvas rebuild to have run.
        internal PanelParams CurrentParams => BuildParams();
        internal bool IsPanelVisible => ComputeVisible();
        internal bool IsSuppressed => _suppressed;

        /// <summary>Runs the geometry pass on demand — EditMode has no canvas rebuild loop.</summary>
        internal void BuildMeshForTests(VertexHelper vh) => OnPopulateMesh(vh);

        // ---- cloning ----

        /// <summary>
        /// Takes over another panel's authored parameters. Everything here is a private,
        /// non-serialized field, so <c>Object.Instantiate</c> hands a clone a blank panel — the
        /// <c>TMP_Dropdown</c> popup, cloned from its Template on every Show, is exactly that case
        /// (2026-09-12 spec §5.5). Runtime state (grayed / mask-source / suppressed / weld group)
        /// is deliberately not copied: the clone's own drivers set those.
        /// </summary>
        internal void CopyStateFrom(ProceduralPanel source)
        {
            if (source == null || source == this) return;
            _fill = source._fill;
            _borderColor = source._borderColor;
            _glowColor = source._glowColor;
            _glowColorExplicit = source._glowColorExplicit;
            _innerGlowColor = source._innerGlowColor;
            _radius = source._radius;
            _borderWidth = source._borderWidth;
            _glowSize = source._glowSize;
            _innerGlowSize = source._innerGlowSize;
            _intensity = source._intensity;
            _hazeSize = source._hazeSize;
            _hazeColor = source._hazeColor;
            _hazeDrift = source._hazeDrift;
            _frost = source._frost;
            _depth = source._depth;
            _dispersion = source._dispersion;
            _lightAngle = source._lightAngle;
            _lightIntensity = source._lightIntensity;
            _saturation = source._saturation;
            _noise = source._noise;
            // Through the setter: glass membership is counted and the weld group notified.
            SetGlass(source._glass);
            MarkDirty();
            FlushParams();
        }

        /// <summary>
        /// Copies every panel under <paramref name="source"/> onto the panel at the same hierarchy
        /// path under <paramref name="clone"/>. The clone is an <c>Instantiate</c> of the source, so
        /// the two trees have identical shape and sibling order.
        /// </summary>
        internal static void CopyStateToClone(Transform source, Transform clone)
        {
            if (source == null || clone == null) return;
            foreach (var panel in source.GetComponentsInChildren<ProceduralPanel>(true))
            {
                var target = Mirror(panel.transform, source, clone);
                if (target != null && target.TryGetComponent<ProceduralPanel>(out var twin))
                    twin.CopyStateFrom(panel);
            }
        }

        private static Transform Mirror(Transform node, Transform sourceRoot, Transform cloneRoot)
        {
            if (node == sourceRoot) return cloneRoot;
            var parent = node.parent;
            if (parent == null) return null;
            var mirroredParent = Mirror(parent, sourceRoot, cloneRoot);
            if (mirroredParent == null) return null;
            var index = node.GetSiblingIndex();
            return index < mirroredParent.childCount ? mirroredParent.GetChild(index) : null;
        }

        // ---- rendering ----

        private PanelParams BuildParams()
        {
            // An unset glowColor follows the fill so `glow="12"` alone reads as "this shape glows",
            // not "this shape gets a white halo".
            // It takes the WHOLE fill ramp — direction, stops, curves — at full alpha, so a gradient fill
            // glows in its own gradient (spec 2026-09-17 LG-D4).
            var glow = _glowColorExplicit
                ? _glowColor
                : (_fill.AnyVisible ? _fill.Opaque() : ColorSpec.Solid(Color.white));

            // Glass values are zeroed when the mode is off: they change nothing about how an opaque
            // panel draws, so letting them into the key would split the material cache into entries
            // that render identically — two draw calls where there should be one.
            GlassParams glassParams = _glass
                ? new GlassParams(_frost, _depth, _dispersion, _lightAngle,
                                  _lightIntensity, _saturation, _noise)
                : GlassParams.None;

            var fill = _fill;
            var border = _borderColor;
            var innerGlow = _innerGlowColor;
            // Glass paints the backdrop, which is not light the surface emits, so exposure has no
            // meaning there (spec 2026-09-12 §5.4); and a disabled control has to read as inert —
            // greyed AND lit is a white-hot grey panel that reads as switched on (§5.5). Both are
            // folded into the key rather than the shader so panels differing only in a value that
            // cannot show keep sharing one material.
            var intensity = _glass || _grayed ? IntensityAttrParser.Default : _intensity;
            // Fog over glass would need its own compositing rule and a second shader (H-D4), so it
            // is zeroed there; and while the size is 0 the colour and the drift are canonicalised so
            // a stray hazeColor= never splits the cache. Disabled fog stops flowing (H-D5): grey but
            // still moving reads as alive, and a disabled control has to read as inert.
            var hazeSize = _glass ? 0f : _hazeSize;
            var haze = hazeSize > 0f ? _hazeColor : ColorSpec.Solid(Color.white);
            var hazeDrift = hazeSize > 0f && !_grayed ? _hazeDrift : 0f;
            if (_grayed)
            {
                // Disabled greying has to happen HERE, inside the parameters, not by swapping the
                // material for UI-Grayscale the way DisabledGrayscaleController does for a plain
                // Image: this material carries the shape, the border and the glass, and replacing it
                // erases all three. It would also be a losing fight — FlushParams writes the cached
                // material back on the next parameter change and would undo the greying in turn.
                fill = fill.Desaturate();
                border = border.Desaturate();
                glow = glow.Desaturate();
                innerGlow = innerGlow.Desaturate();
                if (hazeSize > 0f) haze = haze.Desaturate();
                if (_glass)
                    // Grey glass AND thin glass: dropping saturation alone still reads as a live,
                    // refracting pane. A disabled control should look inert, so the bevel goes too.
                    glassParams = new GlassParams(_frost, _depth * DisabledGlassDepth, _dispersion,
                                                  _lightAngle, _lightIntensity * DisabledGlassLight,
                                                  0f, _noise);
            }

            return new PanelParams(fill, border, glow, innerGlow, _radius,
                                   _borderWidth, _glowSize, _innerGlowSize, _glass, glassParams,
                                   intensity, haze, hazeSize, hazeDrift);
        }

        /// <summary>
        /// Hands drawing over to a <see cref="GlassGroupPanel"/> (or takes it back). A suppressed
        /// panel emits no geometry and holds no material — it exists only to carry its parameters
        /// into the group, while staying a normal RectTransform for layout and children.
        /// </summary>
        /// <summary>
        /// The default disabled look for a procedural surface: desaturated, and for glass also
        /// thinner and less lit. Driven by <c>DisabledGrayscaleController</c>, which greys plain
        /// Graphics by material swap — a swap this panel cannot survive (see <see cref="BuildParams"/>).
        /// </summary>
        public void SetDisabledGrayscale(bool value)
        {
            if (_grayed == value) return;
            _grayed = value;
            MarkDirty();
            FlushParams();
        }

        private const float DisabledGlassDepth = 0.35f;
        private const float DisabledGlassLight = 0.35f;

        /// <summary>
        /// Marks the panel as the mask source of a stencil <see cref="UnityEngine.UI.Mask"/> on the
        /// same GameObject. Driven by <c>Frame.ReconcileMask</c>; see <see cref="ComputeVisible"/>
        /// for why it matters.
        /// </summary>
        internal void SetMaskSource(bool value)
        {
            if (_maskSource == value) return;
            _maskSource = value;
            _lastVisible = ComputeVisible();
            SetVerticesDirty();
        }

        internal void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed) return;
            _suppressed = suppressed;

            if (suppressed && _hasKey)
            {
                ProceduralMaterialCache.Release(_key);
                _hasKey = false;
                m_Material = null;
            }
            _paramsDirty = true;
            _lastVisible = ComputeVisible();
            SetVerticesDirty();
            SetMaterialDirty();
        }

        private bool ComputeVisible()
        {
            if (_suppressed) return false;
            // A stencil mask source has to emit geometry even when it paints nothing: the stencil is
            // written by its fragments, so culling it for being invisible clips every child away.
            // That is precisely the invisible-rounded-clipper form (mask="self" showMask="false"
            // with no fill), which is the most useful one.
            if (_maskSource) return true;
            // The blurred backdrop is itself the visual, so a glass panel with no fill still draws.
            if (_glass) return true;
            if (_fill.AnyVisible) return true;
            if (_borderWidth > 0f && _borderColor.AnyVisible) return true;
            if (_glowSize > 0f) return true;
            // A ring of light with no fill behind it is a legitimate look, same standing as the
            // border-only hollow box above.
            if (_innerGlowSize > 0f && _innerGlowColor.AnyVisible) return true;
            // A patch of light in an otherwise empty rect, same standing as the ring above.
            if (_hazeSize > 0f && _hazeColor.AnyVisible) return true;
            return false;
        }

        private void SyncGlassCount()
        {
            var shouldCount = _glass && isActiveAndEnabled;
            if (shouldCount == _countedAsGlass) return;
            _countedAsGlass = shouldCount;
            if (shouldCount) GlassRuntime.PanelActivated();
            else GlassRuntime.PanelDeactivated();
        }

        /// <summary>
        /// Records that the parameters changed, without touching the material. Resolving is deferred
        /// to <see cref="FlushParams"/> so a run of attribute writes — instantiation applies up to
        /// seventeen of them — collapses into a single cache lookup.
        /// </summary>
        private void MarkDirty()
        {
            _paramsDirty = true;
            if (Group != null) Group.MarkMembersDirty();

            var visible = ComputeVisible();
            if (visible != _lastVisible || !Mathf.Approximately(_glowSize, _lastGlowGeometry))
            {
                _lastVisible = visible;
                _lastGlowGeometry = _glowSize;
                SetVerticesDirty();
            }

            SetMaterialDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            // The group packs every member's rect into its uniforms, so a member that moves or
            // resizes has to re-publish. Its own geometry is rect-relative and needs nothing.
            if (Group != null) Group.MarkMembersDirty();
        }

        /// <summary>
        /// Resolves the current parameters into a shared material. Called once per canvas rebuild,
        /// and eagerly by <c>Frame.OnAfterApply</c> so freshly instantiated panels have their
        /// material before anything renders.
        /// </summary>
        /// <param name="fromRebuild">
        /// True when called from <see cref="UpdateMaterial"/>. Re-registering for a graphic rebuild
        /// from inside the rebuild loop makes uGUI complain, so the dirty flag is skipped there —
        /// the caller is already about to push the material through.
        /// </param>
        internal void FlushParams(bool fromRebuild = false)
        {
            if (_suppressed) return;
            if (!_paramsDirty && _hasKey) return;
            _paramsDirty = false;

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
            if (!fromRebuild) SetMaterialDirty();
        }

        protected override void UpdateMaterial()
        {
            FlushParams(fromRebuild: true);
            base.UpdateMaterial();
        }

        /// <summary>
        /// A glass panel on a canvas rendered <em>by</em> the capture camera samples a backdrop that
        /// already contains itself — the feedback smears over a few frames. Not an error (a
        /// multi-camera rig can legitimately capture a different camera), so it warns once.
        /// </summary>
        private void WarnOnBackdropFeedbackLoop()
        {
            if (!_glass || _warnedFeedbackLoop) return;
            var c = canvas;
            if (c == null || c.renderMode == RenderMode.ScreenSpaceOverlay) return;
            var capture = GlassRuntime.Camera != null ? GlassRuntime.Camera : Camera.main;
            if (capture == null || c.worldCamera != capture) return;

            _warnedFeedbackLoop = true;
            UILog.Warn(this,
                $"PromptUGUI: glass Frame '{name}' is on a canvas rendered by the same camera the " +
                "glass backdrop is captured from, so it will sample a blurred copy of itself and " +
                "smear over successive frames. Put glass Screens on an Overlay canvas (the default) " +
                "and give UI that should appear blurred behind them CanvasMode.Camera, or point " +
                "UI.Glass.Camera at a different camera.");
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
