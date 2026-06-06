using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityRawImage = UnityEngine.UI.RawImage;

namespace PromptUGUI.Controls
{
    public sealed class RawImage : Control, IPointerEventSource
    {
        private UnityRawImage _raw;
        private RectMask2D _rectMask;
        private Mask _stencilMask;
        private string _pendingMaskPadding;
        private bool? _pendingShowMask;

        public override void OnAttached()
        {
            _raw = GameObject.GetComponent<UnityRawImage>()
                   ?? GameObject.AddComponent<UnityRawImage>();
        }

        private PointerEventRelay _pointerRelay;

        private PointerEventRelay EnsureRelay()
            => _pointerRelay ??= GameObject.AddComponent<PointerEventRelay>();

        public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
        public Observable<Unit> OnPointerExit => EnsureRelay().OnPointerExit;
        public Observable<Unit> OnPointerDown => EnsureRelay().OnPointerDown;

        /// <summary>
        /// 显示的 texture。由 C# 设置（下载图 / RenderTexture 等），无对应 XML 属性。
        /// 赋值时若已进入 contain/cover 适配模式会重算纵横比（Task 3 接上）。
        /// </summary>
        public UnityEngine.Texture Texture
        {
            get => _raw.texture;
            set { _raw.texture = value; RecomputeAspect(); }
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _raw.color = UI.Theme.Resolve(value);
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_raw, value);
        }

        private AspectRatioFitter _fitter;

        private AspectRatioFitter EnsureFitter()
            => _fitter ??= GameObject.AddComponent<AspectRatioFitter>();

        // contain/cover 下按当前 texture 宽高刷新 ARF 比例；无 fitter / 无 texture 时 no-op。
        private void RecomputeAspect()
        {
            if (_fitter != null && _fitter.enabled
                && _raw.texture != null && _raw.texture.height > 0)
                _fitter.aspectRatio = (float)_raw.texture.width / _raw.texture.height;
        }

        [UIAttr, Preserve]
        public string Type
        {
            set
            {
                switch (value)
                {
                    case "contain":
                    case "cover":
                        var f = EnsureFitter();
                        f.enabled = true;
                        f.aspectMode = value == "cover"
                            ? AspectRatioFitter.AspectMode.EnvelopeParent
                            : AspectRatioFitter.AspectMode.FitInParent;
                        RecomputeAspect();   // texture 已设（ReSolve 路径）则即时重算
                        break;
                    case null:
                    case "":
                        if (_fitter != null) _fitter.enabled = false;
                        break;
                    default:
                        Debug.LogWarning(
                            $"PromptUGUI: <RawImage type=\"{value}\"> only supports 'contain' / 'cover' " +
                            "(simple/sliced/tiled/filled are sprite-only <Image> modes); ignoring.");
                        if (_fitter != null) _fitter.enabled = false;
                        break;
                }
            }
        }

        [UIAttr, Preserve]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                else if (value == "self")
                {
                    _stencilMask ??= GameObject.AddComponent<Mask>();
                    if (_pendingShowMask.HasValue)
                        _stencilMask.showMaskGraphic = _pendingShowMask.Value;
                }
            }
        }

        [UIAttr, Preserve]
        public string ShowMask
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _pendingShowMask = bool.Parse(value);
                if (_stencilMask != null)
                    _stencilMask.showMaskGraphic = _pendingShowMask.Value;
            }
        }

        [UIAttr, Preserve]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = MaskPaddingParser.Parse(value);
            }
        }

        public override Vector2? GetNativeSize()
        {
            if (_raw == null || _raw.texture == null) return null;
            // RawImage 无 pixelsPerUnit；texture 像素 1:1 映射 UI 单位（同 RawImage.SetNativeSize）。
            return new Vector2(_raw.texture.width, _raw.texture.height);
        }
    }
}
