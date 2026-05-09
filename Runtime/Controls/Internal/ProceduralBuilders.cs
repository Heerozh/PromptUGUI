using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    internal static class ProceduralBuilders
    {
        // 默认配色：让无 sprite/无 color 的控件依然可视（不是纯白叠白）。
        // 作者写 color="..." / sprite="..." 时 UIAttr setter 会 override 这些值。
        public static readonly Color DefaultBtnColor       = new(0.231f, 0.510f, 0.965f, 1f);  // #3B82F6
        public static readonly Color DefaultControlBgColor = new(0.267f, 0.267f, 0.267f, 1f);  // #444444
        public static readonly Color DefaultTrackColor     = new(0.200f, 0.200f, 0.200f, 1f);  // #333333
        public static readonly Color DefaultFillColor      = new(0.231f, 0.510f, 0.965f, 1f);  // #3B82F6
        public static readonly Color DefaultHandleColor    = new(1.000f, 1.000f, 1.000f, 1f);  // #FFFFFF
        public static readonly Color DefaultPopupBgColor   = new(0.227f, 0.227f, 0.227f, 1f);  // #3A3A3A
        public static readonly Color DefaultContainerColor = new(0.165f, 0.165f, 0.165f, 1f);  // #2A2A2A
        public static readonly Color DefaultGlyphColor     = new(1.000f, 1.000f, 1.000f, 1f);  // #FFFFFF

        // pugui.png 像素图集中的精灵名（参见 Runtime/Resources/PromptUGUI/Defaults/pugui.png.meta）
        public const string SpriteRoundedRect = "pugui_9slice_round";
        public const string SpriteCaret       = "pugui_caret";
        public const string SpriteCheckmark   = "pugui_checkmark";

        private const string DefaultSpritesPath = "PromptUGUI/Defaults/pugui";
        private static Dictionary<string, Sprite> _defaultSprites;

        public static Sprite GetDefaultSprite(string name)
        {
            if (_defaultSprites == null)
            {
                _defaultSprites = new Dictionary<string, Sprite>();
                var loaded = Resources.LoadAll<Sprite>(DefaultSpritesPath);
                foreach (var s in loaded)
                    if (s != null) _defaultSprites[s.name] = s;
            }
            return _defaultSprites.TryGetValue(name, out var sprite) ? sprite : null;
        }

        /// <summary>给 Image 应用 9-slice 圆角 sprite 兜底；调用者后续 sprite= 仍可 override。</summary>
        public static void ApplyDefaultSlicedSprite(UnityImage img)
        {
            if (img == null || img.sprite != null) return;
            var s = GetDefaultSprite(SpriteRoundedRect);
            if (s == null) return;
            img.sprite = s;
            img.type = UnityImage.Type.Sliced;
        }

        /// <summary>给 Image 应用 simple sprite 兜底（caret / checkmark 等无边界形状）。</summary>
        public static void ApplyDefaultSimpleSprite(UnityImage img, string spriteName)
        {
            if (img == null || img.sprite != null) return;
            var s = GetDefaultSprite(spriteName);
            if (s == null) return;
            img.sprite = s;
            img.type = UnityImage.Type.Simple;
            img.preserveAspect = true;
        }

        internal static void ResetDefaultSpriteCacheForTests() => _defaultSprites = null;

        public static RectTransform AddChild(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static UnityImage AddImage(RectTransform parent, string name, bool raycast = true)
        {
            var rt = AddChild(parent, name);
            var img = rt.gameObject.AddComponent<UnityImage>();
            img.raycastTarget = raycast;
            return img;
        }

        public static TMP_Text AddText(RectTransform parent, string name)
        {
            var rt = AddChild(parent, name);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
