using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// 引导挖洞遮罩(spec §5.2):洞外四块环形带渲染遮罩色并拦截 raycast,
    /// 洞内不渲染、IsRaycastLocationValid 返回 false → 点击穿透到下层真实控件。
    /// 不用 shader/stencil,WebGL 安全。
    /// </summary>
    // Graphic 只 RequireComponent(RectTransform);CanvasRenderer 由各具体 graphic(Image/Text…)
    // 各自声明,自定义 Graphic 子类必须自己加,否则 AddComponent 不补 CanvasRenderer →
    // GraphicRaycaster 每帧读 canvasRenderer.cull 抛 MissingComponentException。
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class SpotlightMask : MaskableGraphic, ICanvasRaycastFilter
    {
        private Rect? _hole;   // 本地坐标(pivot 居中)

        /// <summary>null = 无洞(整屏遮罩,纯说明页/等待目标期)。</summary>
        public void SetHole(Rect? holeInLocalSpace)
        {
            _hole = holeInLocalSpace;
            SetVerticesDirty();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPoint, eventCamera, out var local))
                return false;
            return HitTest(local);
        }

        // 命中测试用未夹紧的 _hole(网格用夹紧版):洞恒为 目标 rect+padding,与本 rect 同属
        // overlay 画布、必然相交,故不存在"完全在 rect 外"的洞 → 二者不会分歧。
        private bool HitTest(Vector2 local) => !(_hole.HasValue && _hole.Value.Contains(local));

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (_hole == null) { AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax); return; }

            // 洞夹紧到自身 rect,四块环形带:上、下、左、右(左右带只到洞的上下沿)
            var h = _hole.Value;
            float hx0 = Mathf.Max(h.xMin, r.xMin), hx1 = Mathf.Min(h.xMax, r.xMax);
            float hy0 = Mathf.Max(h.yMin, r.yMin), hy1 = Mathf.Min(h.yMax, r.yMax);
            if (hx1 <= hx0 || hy1 <= hy0) { AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax); return; }

            AddQuad(vh, r.xMin, hy1, r.xMax, r.yMax);   // 上
            AddQuad(vh, r.xMin, r.yMin, r.xMax, hy0);   // 下
            AddQuad(vh, r.xMin, hy0, hx0, hy1);         // 左
            AddQuad(vh, hx1, hy0, r.xMax, hy1);         // 右
        }

        private void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            if (x1 <= x0 || y1 <= y0) return;   // 退化带跳过
            int i = vh.currentVertCount;
            var c = color;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        // —— 测试钩子 —— //
        internal Rect? HoleForTests => _hole;

        internal bool HitTestForTests(Vector2 local) => HitTest(local);

        internal int PopulateMeshVertexCountForTests()
        {
            using var vh = new VertexHelper();
            OnPopulateMesh(vh);
            return vh.currentVertCount;
        }
    }
}
