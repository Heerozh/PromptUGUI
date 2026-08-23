using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Draws several glass <see cref="ProceduralPanel"/>s as one continuous pane, fused with an SDF
    /// smooth-min. Attached to the <see cref="Frame"/> that carries <c>weld</c>; its members are that
    /// Frame's direct glass children.
    ///
    /// <para>The members stay ordinary RectTransforms — they lay out, hold children and answer
    /// <c>Get&lt;T&gt;</c> exactly as before. Only their <em>drawing</em> moves here, so the seam
    /// between them can be a thickness step rather than a line.</para>
    ///
    /// <para>Performance: one draw call for the whole group instead of one per block, and member
    /// data is re-packed only when something actually moves or changes — a layout pass that leaves
    /// the rects alone costs nothing. The vertex buffer is rebuilt only when the union bounds move,
    /// not when a colour does.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class GlassGroupPanel : MaskableGraphic
    {
        /// <summary>
        /// Constant-buffer arrays are fixed length; the shader declares eight slots.
        /// Mirrored by <c>GlassRules.MaxWeldMembers</c> so the linter can say so up front.
        /// </summary>
        internal const int MaxMembers = 8;

        private const string ShaderResourcePath = "PromptUGUI/Material/UI-GlassGroup";

        private static readonly int WeldRectsId = Shader.PropertyToID("_WeldRects");
        private static readonly int WeldRadiiId = Shader.PropertyToID("_WeldRadii");
        private static readonly int WeldTintTopId = Shader.PropertyToID("_WeldTintTop");
        private static readonly int WeldTintBottomId = Shader.PropertyToID("_WeldTintBottom");
        private static readonly int WeldDepthsId = Shader.PropertyToID("_WeldDepths");
        private static readonly int WeldCountId = Shader.PropertyToID("_WeldCount");
        private static readonly int WeldId = Shader.PropertyToID("_Weld");
        private static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");
        private static readonly int BorderWidthId = Shader.PropertyToID("_BorderWidth");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowSizeId = Shader.PropertyToID("_GlowSize");
        private static readonly int GlassAId = Shader.PropertyToID("_GlassA");
        private static readonly int GlassBId = Shader.PropertyToID("_GlassB");

        private static Shader _shader;

        private readonly List<ProceduralPanel> _members = new();

        // Reused every repack — the arrays are always sent full length, so they never reallocate and
        // the group produces no per-frame garbage.
        private readonly Vector4[] _rects = new Vector4[MaxMembers];
        private readonly Vector4[] _radii = new Vector4[MaxMembers];
        private readonly Vector4[] _tintTop = new Vector4[MaxMembers];
        private readonly Vector4[] _tintBottom = new Vector4[MaxMembers];
        private readonly Vector4[] _depths = new Vector4[MaxMembers];

        private ProceduralPanel _container;
        private Material _material;
        private float _weld;
        private bool _membersDirty = true;
        private Rect _lastBounds;
        private int _activeCount;

        private static bool _warnedTooManyMembers;

        internal bool IsWelding => _weld > 0f && _members.Count >= 2;
        internal int MemberCount => _members.Count;
        internal Material MaterialForTests => _material;

        /// <summary>
        /// Creates the group on its own child object of the weld container.
        ///
        /// It cannot live on the container itself: <see cref="Graphic"/> is
        /// <c>[DisallowMultipleComponent]</c>, and the container already needs a
        /// <see cref="ProceduralPanel"/> to carry the group-level parameters — adding a second
        /// Graphic there silently returns null. The child stretches over the container and sits at
        /// sibling index 0, so the fused pane draws behind everything the blocks contain.
        /// </summary>
        internal static GlassGroupPanel Attach(GameObject container)
        {
            var go = new GameObject("GlassWeld", typeof(RectTransform)) { layer = container.layer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(container.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(0);
            return go.AddComponent<GlassGroupPanel>();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _membersDirty = true;
            FlushGroup();
        }

        protected override void OnDestroy()
        {
            ReleaseMembers();
            if (_material != null)
            {
                if (UnityEngine.Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
                _material = null;
            }
            base.OnDestroy();
        }

        internal void SetWeld(float weld)
        {
            if (Mathf.Approximately(_weld, weld)) return;
            _weld = Mathf.Max(0f, weld);
            _membersDirty = true;
            // Dropping weld to 0 hands drawing back right here rather than waiting for the next
            // SyncMembers — otherwise the blocks would stay invisible until something else
            // re-applied attributes.
            if (_weld <= 0f) ReleaseMembers();
            SetVerticesDirty();
            SetMaterialDirty();
        }

        /// <summary>
        /// Something about the membership, a member's rect or a member's parameters changed. Both
        /// dirty flags are raised here rather than inside <see cref="FlushGroup"/>: the flush runs
        /// from <see cref="UpdateMaterial"/>, i.e. from inside uGUI's rebuild loop, and registering
        /// for a rebuild from in there makes uGUI complain.
        /// </summary>
        internal void MarkMembersDirty()
        {
            _membersDirty = true;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        /// <summary>
        /// Re-scans the direct children for glass panels and hands their drawing over. Called from
        /// <c>Frame.OnAfterApply</c>, so it runs after the subtree is instantiated and again on every
        /// Variant ReSolve — which is what picks up blocks added or made glass by a variant.
        /// </summary>
        internal void SyncMembers(ProceduralPanel containerPanel)
        {
            _container = containerPanel;

            // The fused pane draws behind everything the blocks contain, which is only true while
            // this child holds sibling index 0. A Variant <Add at='start'> renumbers the container's
            // children knowing nothing about it, so the invariant is re-asserted rather than assumed.
            if (transform.GetSiblingIndex() != 0) transform.SetSiblingIndex(0);

            var previous = ListPool.Rent();
            previous.AddRange(_members);
            _members.Clear();

            var welding = _weld > 0f;
            var container = transform.parent;
            if (welding && container != null)
            {
                var overflow = false;
                for (var i = 0; i < container.childCount; i++)
                {
                    var child = container.GetChild(i);
                    if (child == transform) continue;
                    if (!child.TryGetComponent<ProceduralPanel>(out var panel)) continue;
                    if (!panel.IsGlass) continue;
                    if (_members.Count == MaxMembers) { overflow = true; break; }
                    _members.Add(panel);
                }

                if (overflow && !_warnedTooManyMembers)
                {
                    _warnedTooManyMembers = true;
                    Debug.LogWarning(
                        $"PromptUGUI: weld group '{name}' has more than {MaxMembers} glass children; " +
                        "the extra ones draw themselves instead of fusing. Split the group.", this);
                }
            }

            // Below two blocks a weld does nothing — leave the child drawing itself rather than
            // routing one panel through the group shader for no reason.
            var active = welding && _members.Count >= 2;
            if (!active) _members.Clear();

            // The container is a carrier only while the group actually fuses something. Suppressing
            // it unconditionally would mean a group that once existed permanently erases the
            // container's own panel — and the two ways to cancel a weld (the setter directly, or a
            // Variant going through OnAfterApply) would end in opposite states.
            if (_container != null) _container.SetSuppressed(active);

            foreach (var panel in previous)
            {
                // Unity objects read as null once destroyed; touching one throws and takes the whole
                // ReSolve pass down with it, not just this group.
                if (panel == null) continue;
                if (_members.Contains(panel)) continue;
                panel.Group = null;
                panel.SetSuppressed(false);
            }
            foreach (var panel in _members)
            {
                panel.Group = this;
                panel.SetSuppressed(true);
            }

            ListPool.Return(previous);
            _membersDirty = true;
            FlushGroup();
        }

        private void ReleaseMembers()
        {
            foreach (var panel in _members)
            {
                if (panel == null) continue;
                panel.Group = null;
                panel.SetSuppressed(false);
            }
            _members.Clear();
            if (_container != null) _container.SetSuppressed(false);
        }

        /// <summary>
        /// Re-scans membership after something outside the container changed a child's glass flag.
        ///
        /// <para>Needed because <c>ReSolve</c> re-applies attributes parent-before-children (it walks
        /// the node map in insertion order, unlike the post-order used when the Screen is first
        /// built): by the time a Variant turns a block's <c>glass</c> on or off, this group has
        /// already synced against the previous value. Without the block telling the group, a
        /// de-glassed block keeps rendering as fused glass — and a newly glass one stays outside the
        /// weld, leaving the seam the weld exists to remove — until some later ReSolve.</para>
        /// </summary>
        internal void RequestMemberRescan() => SyncMembers(_container);

        /// <summary>Re-arms the warn-once diagnostics; see <c>GlassRuntime</c>.</summary>
        internal static void ResetDiagnostics() => _warnedTooManyMembers = false;

        /// <summary>Re-packs member data into the material. Idempotent and cheap when nothing moved.</summary>
        internal void FlushGroup()
        {
            if (!_membersDirty) return;
            _membersDirty = false;

            if (!IsWelding)
            {
                _activeCount = 0;
                _lastBounds = default;
                return;
            }

            EnsureMaterial();

            var count = 0;
            var bounds = default(Rect);
            foreach (var member in _members)
            {
                // A block hidden by a Variant (or destroyed out from under us) must drop out of the
                // fused shape — otherwise the group keeps drawing glass where nothing is left.
                if (member == null || !member.isActiveAndEnabled) continue;

                var p = member.CurrentParams;
                GetLocalRect(member.rectTransform, rectTransform, out var center, out var half);

                _rects[count] = new Vector4(center.x, center.y, half.x, half.y);
                _radii[count] = ResolveRadius(p, half);
                _tintTop[count] = p.FillTop;
                _tintBottom[count] = p.FillBottom;
                _depths[count] = new Vector4(p.GlassParams.Depth, 0f, 0f, 0f);

                var r = new Rect(center.x - half.x, center.y - half.y, half.x * 2f, half.y * 2f);
                bounds = count == 0 ? r : Union(bounds, r);
                count++;
            }
            // Unused slots must still hold something the shader will never select; an empty rect at
            // the origin is harmless because the loop stops at _WeldCount.
            for (var i = count; i < MaxMembers; i++)
            {
                _rects[i] = Vector4.zero;
                _radii[i] = Vector4.zero;
                _tintTop[i] = Vector4.zero;
                _tintBottom[i] = Vector4.zero;
                _depths[i] = Vector4.zero;
            }

            _material.SetVectorArray(WeldRectsId, _rects);
            _material.SetVectorArray(WeldRadiiId, _radii);
            _material.SetVectorArray(WeldTintTopId, _tintTop);
            _material.SetVectorArray(WeldTintBottomId, _tintBottom);
            _material.SetVectorArray(WeldDepthsId, _depths);
            _material.SetInt(WeldCountId, count);
            _material.SetFloat(WeldId, _weld);

            ApplyGroupParams();

            _activeCount = count;
            _lastBounds = bounds;

            // Assign the backing field, not the `material` property: the property setter calls
            // SetMaterialDirty, which would re-enter this during a canvas rebuild. The dirty flags
            // were already raised by MarkMembersDirty.
            m_Material = _material;
        }

        /// <summary>
        /// Group-level parameters come from the container Frame, because physically they have to:
        /// two halves of one pane cannot be frosted differently or lit from different angles.
        /// </summary>
        private void ApplyGroupParams()
        {
            var g = _container != null ? _container.RawGlassParams : default;
            var frost = _container != null ? g.Frost : Parser.GlassAttrParser.DefaultFrost;
            var dispersion = _container != null ? g.Dispersion : Parser.GlassAttrParser.DefaultDispersion;
            var noise = _container != null ? g.Noise : Parser.GlassAttrParser.DefaultNoise;
            var angle = _container != null ? g.LightAngle : Parser.GlassAttrParser.DefaultLightAngle;
            var intensity = _container != null ? g.LightIntensity : Parser.GlassAttrParser.DefaultLightIntensity;
            var saturation = _container != null ? g.Saturation : Parser.GlassAttrParser.DefaultSaturation;

            _material.SetVector(GlassAId, new Vector4(frost, 0f, dispersion, noise));
            var rad = angle * Mathf.Deg2Rad;
            _material.SetVector(GlassBId,
                new Vector4(Mathf.Sin(rad), Mathf.Cos(rad), intensity, saturation));

            // Border and glow follow the fused outline, so they are the container's — a per-block
            // border would draw exactly the dividing line the weld exists to remove.
            var c = _container != null ? _container.CurrentParams : default;
            _material.SetColor(BorderColorId, _container != null ? c.BorderColor : Color.white);
            _material.SetFloat(BorderWidthId, _container != null ? c.BorderWidth : 0f);
            _material.SetColor(GlowColorId, _container != null ? c.GlowColor : Color.white);
            _material.SetFloat(GlowSizeId, _container != null ? c.GlowSize : 0f);
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;
            _shader ??= Resources.Load<Shader>(ShaderResourcePath);
            if (_shader == null)
                throw new InvalidOperationException(
                    $"PromptUGUI: shader not found at Resources/{ShaderResourcePath}.");
            // Per group rather than pooled: the uniforms carry live rects, so no two groups could
            // ever share one. Groups are counted in ones, not hundreds.
            _material = new Material(_shader)
            {
                name = "PromptUGUI/GlassGroup",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        protected override void UpdateMaterial()
        {
            FlushGroup();
            base.UpdateMaterial();
        }

        /// <summary>
        /// Catches members that moved without resizing. <c>OnRectTransformDimensionsChange</c> covers
        /// a member being resized, but a pure translation — a layout group re-flowing after a sibling
        /// appears, a position tween — fires nothing at all, and the fused shape would stay behind at
        /// the old coordinates until the next ReSolve.
        ///
        /// <para>The check is a handful of vector compares against the rects already packed last
        /// flush, and only groups run it (there are ones of those, not hundreds), so "costs nothing
        /// when nothing moved" stays true.</para>
        /// </summary>
        private void LateUpdate()
        {
            if (_membersDirty || !IsWelding) return;

            var i = 0;
            foreach (var member in _members)
            {
                if (member == null || !member.isActiveAndEnabled) continue;
                if (i >= _activeCount) { MarkMembersDirty(); return; }

                GetLocalRect(member.rectTransform, rectTransform, out var center, out var half);
                var packed = _rects[i];
                if (!Mathf.Approximately(packed.x, center.x) || !Mathf.Approximately(packed.y, center.y)
                    || !Mathf.Approximately(packed.z, half.x) || !Mathf.Approximately(packed.w, half.y))
                {
                    MarkMembersDirty();
                    return;
                }
                i++;
            }
            if (i != _activeCount) MarkMembersDirty();
        }

        private static Vector4 ResolveRadius(in PanelParams p, Vector2 half)
        {
            var shortest = Mathf.Min(half.x, half.y);
            // Unlike the single-panel shader, pill is resolved here: the group material is per-group
            // anyway, so there is no material sharing left to protect by deferring it to the GPU.
            var r = p.Pill ? new Vector4(shortest, shortest, shortest, shortest) : p.Radius;
            return new Vector4(
                Mathf.Clamp(r.x, 0f, shortest), Mathf.Clamp(r.y, 0f, shortest),
                Mathf.Clamp(r.z, 0f, shortest), Mathf.Clamp(r.w, 0f, shortest));
        }

        /// <summary>
        /// A block's rect expressed in the group's own local space. Goes through the transforms
        /// rather than adding up local positions: the container's pivot, the block's pivot and the
        /// group child's pivot need not agree, and hand-rolled arithmetic silently mislocates the
        /// blocks the moment one of them differs.
        /// </summary>
        private static void GetLocalRect(RectTransform member, RectTransform group,
                                         out Vector2 center, out Vector2 half)
        {
            var r = member.rect;
            half = r.size * 0.5f;
            var world = member.TransformPoint(r.center);
            center = group.InverseTransformPoint(world);
        }

        private static Rect Union(Rect a, Rect b)
        {
            var xMin = Mathf.Min(a.xMin, b.xMin);
            var yMin = Mathf.Min(a.yMin, b.yMin);
            return new Rect(xMin, yMin, Mathf.Max(a.xMax, b.xMax) - xMin,
                            Mathf.Max(a.yMax, b.yMax) - yMin);
        }

        internal void BuildMeshForTests(VertexHelper vh) => OnPopulateMesh(vh);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!IsWelding) return;

            FlushGroup();
            if (_activeCount == 0) return;
            var bounds = _lastBounds;
            if (bounds.width <= 0f || bounds.height <= 0f) return;

            var glow = _container != null ? _container.CurrentParams.GlowSize : 0f;
            var xMin = bounds.xMin - glow;
            var xMax = bounds.xMax + glow;
            var yMin = bounds.yMin - glow;
            var yMax = bounds.yMax + glow;

            var tint = (Color32)color;
            var v = UIVertex.simpleVert;
            v.color = tint;

            v.position = new Vector3(xMin, yMin);
            v.uv0 = new Vector4(xMin, yMin, 0f, 0f);
            vh.AddVert(v);

            v.position = new Vector3(xMin, yMax);
            v.uv0 = new Vector4(xMin, yMax, 0f, 0f);
            vh.AddVert(v);

            v.position = new Vector3(xMax, yMax);
            v.uv0 = new Vector4(xMax, yMax, 0f, 0f);
            vh.AddVert(v);

            v.position = new Vector3(xMax, yMin);
            v.uv0 = new Vector4(xMax, yMin, 0f, 0f);
            vh.AddVert(v);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        /// <summary>
        /// Tiny scratch-list pool: SyncMembers needs a snapshot of the previous membership, and it
        /// runs on every Variant ReSolve — allocating a List there would be garbage on a UI event.
        /// </summary>
        private static class ListPool
        {
            private static readonly Stack<List<ProceduralPanel>> _pool = new();

            public static List<ProceduralPanel> Rent()
                => _pool.Count > 0 ? _pool.Pop() : new List<ProceduralPanel>(MaxMembers);

            public static void Return(List<ProceduralPanel> list)
            {
                list.Clear();
                _pool.Push(list);
            }
        }
    }
}
