using LitMotion;
using PromptUGUI.Application.Tutorial;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Application.Navigation
{
    /// <summary>
    /// 光标 overlay 视图组件：跟踪当前 EventSystem selection，在 Directional 模式下显示并
    /// 滑动到目标控件边缘；Pointer 模式或 selection 不属于本屏时隐藏。
    /// </summary>
    internal sealed class FocusCursorView : UIBehaviour
    {
        private Screen _owner;
        private RectTransform _rt;       // 光标 overlay 根（被移动）
        private CanvasGroup _cg;
        private Canvas _canvas;
        private string _side = "left";
        private Vector2 _offset;
        private MotionHandle _slide;
        private bool _hasLast;
        private Vector2 _lastTarget;

        internal void Init(Screen owner, RectTransform overlay, ElementNode cursorNode)
        {
            _owner = owner;
            _rt = overlay;
            _cg = overlay.GetComponent<CanvasGroup>();
            _canvas = overlay.GetComponentInParent<Canvas>();
            if (cursorNode.Attributes.TryGetValue("side", out var s)) _side = s;
            if (cursorNode.Attributes.TryGetValue("offset", out var o)) _offset = ParseVec(o);
            _cg.alpha = 0f;
        }

        private void LateUpdate() => Tick();

        /// <summary>Test seam: drives one Tick without LateUpdate.</summary>
        internal void TickForTests() => Tick();

        // EventSystem.current is null in EditMode (no game loop). Mirror Screen.FindEventSystem().
        private static EventSystem FindEventSystem() =>
            EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        private void Tick()
        {
            var es = FindEventSystem();
            var sel = es != null ? es.currentSelectedGameObject : null;
            bool show = UI.Navigation.IsDirectional && sel != null && _owner.RootGameObject != null
                        && sel.transform.IsChildOf(_owner.RootGameObject.transform);
            if (!show) { _cg.alpha = 0f; _hasLast = false; return; }

            _cg.alpha = 1f;
            var targetRt = (RectTransform)sel.transform;
            var rect = TutorialOverlayView.WorldRectToLocal(targetRt, (RectTransform)_rt.parent);
            var p = EdgePoint(rect) + _offset;

            if (!_hasLast)
            {
                _rt.anchoredPosition = p;
                _hasLast = true;
                _lastTarget = p;
            }
            else if ((p - _lastTarget).sqrMagnitude > 0.01f)
            {
                _lastTarget = p;
                if (_slide.IsActive()) _slide.TryCancel();
                _slide = LMotion.Create(_rt.anchoredPosition, p, 0.12f)
                    .WithEase(Ease.OutCubic)
                    .Bind(_rt, static (v, rt) => { if (rt) rt.anchoredPosition = v; })
                    .AddTo(_rt.gameObject);
            }
            if (_canvas != null) PixelSnap.SnapToPixelGrid(_rt, _canvas, Vector2.zero);
        }

        private Vector2 EdgePoint(Rect r) => _side switch
        {
            "right" => new Vector2(r.xMax, r.center.y),
            "top" => new Vector2(r.center.x, r.yMax),
            "bottom" => new Vector2(r.center.x, r.yMin),
            _ => new Vector2(r.xMin, r.center.y),      // left (default)
        };

        private static Vector2 ParseVec(string s)
        {
            var p = s.Split(',');
            return p.Length == 2
                   && float.TryParse(p[0], out var x)
                   && float.TryParse(p[1], out var y)
                ? new Vector2(x, y)
                : Vector2.zero;
        }

        protected override void OnDestroy()
        {
            if (_slide.IsActive()) _slide.TryCancel();
            base.OnDestroy();
        }
    }
}
