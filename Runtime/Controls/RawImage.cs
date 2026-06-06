using PromptUGUI.Application;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;
using UnityRawImage = UnityEngine.UI.RawImage;

namespace PromptUGUI.Controls
{
    public sealed class RawImage : Control
    {
        private UnityRawImage _raw;

        public override void OnAttached()
        {
            _raw = GameObject.GetComponent<UnityRawImage>()
                   ?? GameObject.AddComponent<UnityRawImage>();
        }

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
    }
}
