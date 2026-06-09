using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// Toast 的定位来源（spec §4）。预设 Top/Bottom/Center、坐标、控件引用、控件路径四选一。
    /// 解析在"显示时刻"进行，落到 toast 自己 Canvas 的本地坐标系。
    /// </summary>
    public readonly struct ToastPosition
    {
        internal enum Kind { Unspecified = 0, Top, Bottom, Center, Coord, Control, ControlPath }

        /// <summary>解析结果：基准位 + anchor/pivot + 堆叠方向（单位向量）。</summary>
        internal readonly struct Resolved
        {
            public readonly Vector2 BasePos, Anchor, Pivot, Dir;
            public Resolved(Vector2 basePos, Vector2 anchor, Vector2 pivot, Vector2 dir)
            { BasePos = basePos; Anchor = anchor; Pivot = pivot; Dir = dir; }
        }

        private readonly Kind _kind;
        private readonly Vector2 _coord;
        private readonly IControl _control;
        private readonly string _path;

        private ToastPosition(Kind k, Vector2 coord, IControl ctl, string path)
        { _kind = k; _coord = coord; _control = ctl; _path = path; }

        public static readonly ToastPosition Top = new(Kind.Top, default, null, null);
        public static readonly ToastPosition Bottom = new(Kind.Bottom, default, null, null);
        public static readonly ToastPosition Center = new(Kind.Center, default, null, null);

        public static ToastPosition At(Vector2 coords) => new(Kind.Coord, coords, null, null);
        public static ToastPosition At(IControl control) => new(Kind.Control, default, control, null);
        public static ToastPosition At(string controlPath) => new(Kind.ControlPath, default, null, controlPath);

        // Vector2 是 struct，隐式转换合法。IControl/string 各由 UI.Toast.Show 的专用重载承接
        // （C# 禁止到/从接口类型的转换运算符，CS0552）。
        public static implicit operator ToastPosition(Vector2 coords) => At(coords);

        internal bool IsUnspecified => _kind == Kind.Unspecified;

        /// <summary>同源 → 同组（互相顶）；异源独立。预设按 Kind、坐标按四舍五入、控件按引用/路径分组。</summary>
        internal object GroupKey() => _kind switch
        {
            Kind.Coord => new Vector2Int(Mathf.RoundToInt(_coord.x), Mathf.RoundToInt(_coord.y)),
            Kind.Control => (object)_control,
            Kind.ControlPath => _path,
            _ => _kind,   // Top/Bottom/Center/Unspecified
        };

        /// <summary>
        /// 解析到 toast Canvas 本地坐标。Control/ControlPath 未命中 → 返回 false（调用方退默认）。
        /// </summary>
        internal bool TryResolve(RectTransform toastCanvasRect, float edgeInset, out Resolved r)
        {
            switch (_kind)
            {
                case Kind.Top:
                    r = new Resolved(new Vector2(0f, -edgeInset), new(0.5f, 1f), new(0.5f, 1f), Vector2.down);
                    return true;
                case Kind.Bottom:
                    r = new Resolved(new Vector2(0f, edgeInset), new(0.5f, 0f), new(0.5f, 0f), Vector2.up);
                    return true;
                case Kind.Center:
                    r = new Resolved(Vector2.zero, new(0.5f, 0.5f), new(0.5f, 0.5f), Vector2.up);
                    return true;
                case Kind.Coord:
                    r = new Resolved(_coord, new(0.5f, 0.5f), new(0.5f, 0.5f), Vector2.up);
                    return true;
                case Kind.Control:
                case Kind.ControlPath:
                    if (TryResolveLocalPoint(toastCanvasRect, out var local))
                    {
                        r = new Resolved(local, new(0.5f, 0.5f), new(0.5f, 0.5f), Vector2.up);
                        return true;
                    }
                    r = default;
                    return false;
                default:
                    r = default;
                    return false;
            }
        }

        private bool TryResolveLocalPoint(RectTransform toastCanvasRect, out Vector2 local)
        {
            local = default;
            RectTransform target;
            if (_kind == Kind.Control) target = _control?.RectTransform;
            else if (!UI.TryResolvePath(_path, out target)) return false;
            if (target == null) return false;

            var srcCanvas = target.GetComponentInParent<Canvas>();
            Camera srcCam = srcCanvas != null ? srcCanvas.worldCamera : null;   // Overlay → null（正确）
            Vector3 worldCenter = target.TransformPoint(target.rect.center);
            Vector2 screenPt = RectTransformUtility.WorldToScreenPoint(srcCam, worldCenter);

            var toastCanvas = toastCanvasRect.GetComponentInParent<Canvas>();
            Camera toastCam = toastCanvas != null ? toastCanvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                toastCanvasRect, screenPt, toastCam, out local);
        }
    }
}
