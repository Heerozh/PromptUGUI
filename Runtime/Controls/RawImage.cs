using UnityEngine;
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
            set => _raw.texture = value;
        }
    }
}
