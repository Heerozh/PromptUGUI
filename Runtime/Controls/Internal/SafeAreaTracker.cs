using System;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    [DisallowMultipleComponent]
    internal sealed class SafeAreaTracker : MonoBehaviour
    {
        // 仅测试注入。生产代码不应触碰这些字段。
        internal static Func<Rect> SafeAreaOverride;
        internal static Func<Vector2> ScreenSizeOverride;
        // v2: design-px 单位的换算系数；不注入则走真 canvas.scaleFactor。
        internal static Func<float> ScaleFactorOverride;

        private RectTransform _rt;
        private Canvas _canvas;
        private bool _warnedNoCanvas;

        // v2: 由 SafeArea.OnAfterApply (即 ApplyCommon 刚写完纯 design margin 之后) 调用
        // CaptureDesignMargin 抓拍 offsetMin/Max,作为 design margin 的真值来源。
        // Update poll 重新 max-blend 时复用这个抓拍,避免被前一次自己的 blended 输出污染。
        private Vector2 _designOffsetMin;
        private Vector2 _designOffsetMax;
        private bool _hasDesignMargin;

        private Rect _lastSafe;
        private Vector2 _lastScreenSize;
        private float _lastScaleFactor;
        private bool _hasApplied;

        private void OnEnable()
        {
            _rt = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
            Apply();
        }

        private void Update()
        {
            // 跟 Unity 官方 SafeArea 示例对齐:每帧 poll,只在 safeArea / 分辨率 / scaleFactor
            // 真的变了时写。不订阅 OnRectTransformDimensionsChange —— 那条路径会跟
            // ApplyCommon / RectTransform setter 内部反向求解形成写入循环（已观测,
            // 见 SafeAreaTests.Tracker_does_not_subscribe_to_rect_transform_dimensions_change）。
            var safe = ResolveSafeArea();
            var screenSize = ResolveScreenSize();
            var sf = ResolveScaleFactor();

            if (!_hasApplied
                || safe != _lastSafe
                || screenSize != _lastScreenSize
                || !Mathf.Approximately(sf, _lastScaleFactor))
            {
                Apply();
            }
        }

        // v2 入口:SafeArea.OnAfterApply 调用。snapshot 当前 RectTransform 的 offsetMin/Max
        // 作为"纯 design margin"——它们刚由 ApplyCommon 根据 <SafeArea margin="..."> 写入。
        // 之后 Apply 用这个抓拍 + device inset 取 max 写回最终 offsets。
        internal void CaptureDesignMargin(RectTransform rt)
        {
            _designOffsetMin = rt.offsetMin;
            _designOffsetMax = rt.offsetMax;
            _hasDesignMargin = true;
        }

        internal void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) return;

            var safe = ResolveSafeArea();
            var screenSize = ResolveScreenSize();
            if (screenSize.x <= 0f || screenSize.y <= 0f) return;
            var sf = ResolveScaleFactor();

            _lastSafe = safe;
            _lastScreenSize = screenSize;
            _lastScaleFactor = sf;
            _hasApplied = true;

            // device-px safe-area → 4 个 inset 距屏幕各边的距离,再除 scaleFactor 拿到 design px
            var insetL = safe.xMin / sf;
            var insetR = (screenSize.x - safe.xMax) / sf;
            var insetB = safe.yMin / sf;
            var insetT = (screenSize.y - safe.yMax) / sf;

            // 设计 margin: ApplyCommon 写出来的 offsetMin/Max 等价于 (l, b) / (-r, -t)。
            // _hasDesignMargin=false 时（OnEnable 先于第一次 OnAfterApply 调 Apply 的同帧）按 0 取,
            // 此时跟 v0"<SafeArea/> 无 margin"行为完全等价:SafeArea 正好 fit safe area。
            var desL = _hasDesignMargin ? _designOffsetMin.x : 0f;
            var desB = _hasDesignMargin ? _designOffsetMin.y : 0f;
            var desR = _hasDesignMargin ? -_designOffsetMax.x : 0f;
            var desT = _hasDesignMargin ? -_designOffsetMax.y : 0f;

            var finL = Mathf.Max(desL, insetL);
            var finR = Mathf.Max(desR, insetR);
            var finB = Mathf.Max(desB, insetB);
            var finT = Mathf.Max(desT, insetT);

            _rt.anchorMin = new Vector2(0f, 0f);
            _rt.anchorMax = new Vector2(1f, 1f);
            _rt.offsetMin = new Vector2(finL, finB);
            _rt.offsetMax = new Vector2(-finR, -finT);
        }

        private Rect ResolveSafeArea() =>
            SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;

        private Vector2 ResolveScreenSize() =>
            ScreenSizeOverride != null
                ? ScreenSizeOverride()
                : new Vector2(Screen.width, Screen.height);

        private float ResolveScaleFactor()
        {
            if (ScaleFactorOverride != null) return ScaleFactorOverride();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                if (!_warnedNoCanvas)
                {
                    Debug.LogWarning(
                        "[SafeAreaTracker] no Canvas in parent chain; using 1:1 device→design scale " +
                        "(this should only happen in headless tests or detached GameObjects).");
                    _warnedNoCanvas = true;
                }
                return 1f;
            }
            return _canvas.scaleFactor;
        }
    }
}
