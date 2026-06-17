using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    // 把 TMP 文本的「对齐感知参考点」吸到整数设备像素网格——补 Canvas.pixelPerfect
    // 漏掉的 TMP 字形对齐。见 spec 2026-06-17-pixel-position-snap (PPS-D1…D7)。
    [DisallowMultipleComponent]
    internal sealed class PixelSnap : UIBehaviour
    {
        private RectTransform _rt;
        private TMP_Text _text;
        private Canvas _canvas;
        private Vector3 _baseLocalPos;
        private Vector3 _lastOffset;
        private bool _hasBase;

        protected override void Awake()
        {
            _rt = (RectTransform)transform;
            _text = GetComponent<TMP_Text>();
        }

        // 父链变化 → 下次 Snap 重新解析所属 Canvas。
        protected override void OnCanvasHierarchyChanged() => _canvas = null;
        protected override void OnTransformParentChanged() => _canvas = null;

        // 重新激活（Tab 页切换、对象池复用）后强制重取基线——隐藏期间 layout 可能已改 localPosition，
        // 避免用陈旧 _baseLocalPos 吸附。
        protected override void OnEnable()
        {
            _hasBase = false;
            // 在 willRenderCanvases（PostLateUpdate，晚于所有 LateUpdate）里吸附，确保跑在
            // ScrollRect 惯性移动 content（其自身 LateUpdate）与布局重建之后——否则吸附会用
            // 移动前的陈旧位置，惯性滚动时文字逐帧渲染偏离整数像素而发糊/斜（LateUpdate 间
            // 执行顺序不确定）。
            Canvas.willRenderCanvases += Snap;
        }

        protected override void OnDisable()
        {
            Canvas.willRenderCanvases -= Snap;
        }

        // 运行期自门控于 canvas.pixelPerfect（关掉它 = 同时关吸附，复用既有 opt-out，PPS-D7）。
        internal void Snap()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>(true);
            if (_text == null || _canvas == null
                || !_canvas.pixelPerfect
                || _canvas.renderMode == RenderMode.WorldSpace)
                return;

            // 非累积：吸附偏移是"瞬时视觉修正"，不属于元素的逻辑位置。每帧先还原逻辑
            // localPosition（layout/scroll 设定、不含上一帧偏移的值）再重新 fresh 吸附。
            // 滚动改的是父级 content.anchoredPosition、不动本子节点的 localPosition——故外部
            // 未改动时 localPosition 仍 == base+lastOffset，base 保持不变，逐帧修正不叠加，
            // 不会把文字从滚动内容上"钉住"剥离（慢速滚动卡死 bug）。外部（layout/resize/
            // ReSolve）改了 localPosition 时等式被打破 → 重新取基线（无陈旧残差）。
            if (!_hasBase
                || (_rt.localPosition - (_baseLocalPos + _lastOffset)).sqrMagnitude > 1e-6f)
            {
                _baseLocalPos = _rt.localPosition;
                _hasBase = true;
            }
            _rt.localPosition = _baseLocalPos;

            var localRef = ReferencePoint(_rt.rect, _text.horizontalAlignment, _text.verticalAlignment);
            SnapToPixelGrid(_rt, _canvas, localRef);
            _lastOffset = _rt.localPosition - _baseLocalPos;
        }

        // 参考点 = TMP 把文本块相对 rect 摆放所依据的边/中心（PPS-D4）。
        internal static Vector2 ReferencePoint(
            Rect rect, HorizontalAlignmentOptions h, VerticalAlignmentOptions v)
        {
            float x = h == HorizontalAlignmentOptions.Center ? rect.center.x
                    : h == HorizontalAlignmentOptions.Right ? rect.xMax
                    : rect.xMin;   // Left / Justified / Flush / Geometry
            float y = v == VerticalAlignmentOptions.Middle ? rect.center.y
                    : v == VerticalAlignmentOptions.Bottom ? rect.yMin
                    : rect.yMax;   // Top / Baseline / Capline / Geometry
            return new Vector2(x, y);
        }

        // 把 rt 平移，使 localRef（局部空间）落在整数设备像素上。模式无关：overlay 用
        // null 相机、camera 模式用 worldCamera。已实测幂等（PPS-D3）。
        internal static void SnapToPixelGrid(RectTransform rt, Canvas canvas, Vector2 localRef)
        {
            var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            var refWorld = rt.TransformPoint((Vector3)localRef);
            var before = RectTransformUtility.WorldToScreenPoint(cam, refWorld);
            var snap = new Vector2(Mathf.Round(before.x), Mathf.Round(before.y));
            if ((before - snap).sqrMagnitude < 1e-4f) return;          // 已对齐
            var canvasRect = (RectTransform)canvas.transform;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    canvasRect, snap, cam, out var snapWorld))
                rt.position += (snapWorld - refWorld);
        }
    }
}
