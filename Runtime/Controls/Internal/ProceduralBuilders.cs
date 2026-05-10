using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    internal static class ProceduralBuilders
    {
        // 默认配色对齐 Unity 6 标准控件（菜单 GameObject → UI → … 创建出来的 prefab）
        // 全部白底 sliced + #323232 深字；sprite 由 atlas tint 表现明暗。
        public static readonly Color DefaultBtnColor = Color.white;
        public static readonly Color DefaultControlBgColor = Color.white;
        public static readonly Color DefaultTrackColor = Color.white;
        public static readonly Color DefaultFillColor = Color.white;
        public static readonly Color DefaultHandleColor = Color.white;
        public static readonly Color DefaultPopupBgColor = Color.white;
        public static readonly Color DefaultContainerColor = new(1f, 1f, 1f, 0.392f);
        // 单一深灰色源（#323232），让 glyph / label / placeholder 三个语义角色共用同一基色
        // 单点修改：换主题色只动这一行
        private static readonly Color s_darkGrey = new(0.196f, 0.196f, 0.196f, 1f);
        public static readonly Color DefaultGlyphColor = s_darkGrey;
        public static readonly Color DefaultLabelColor = s_darkGrey;
        public static readonly Color DefaultPlaceholderColor = new(s_darkGrey.r, s_darkGrey.g, s_darkGrey.b, 0.5f);

        // pugui.png 像素图集中的精灵名（参见 Runtime/Resources/PromptUGUI/Defaults/pugui.png.meta）
        public const string SpriteRoundedRect = "pugui_9slice_round";
        public const string SpriteMaskRoundedRect = "pugui_9slice_mask";
        public const string SpriteCaret = "pugui_caret";
        public const string SpriteCheckmark = "pugui_checkmark";

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

        /// <summary>
        /// 给 stencil Mask 用的 graphic 应用专门的 mask sprite (pugui_9slice_mask)。
        /// 这张 sprite 跟 round 不同：border=2、Simple type 整张拉伸，让 stencil 的圆角形状
        /// 跟 RT 大小成比例可见 (default Unity Scroll View 用单独 UIMask sprite 同样思路)。
        /// </summary>
        public static void ApplyDefaultMaskSprite(UnityImage img)
        {
            if (img == null || img.sprite != null) return;
            var s = GetDefaultSprite(SpriteMaskRoundedRect);
            if (s == null) return;
            img.sprite = s;
            img.type = UnityImage.Type.Sliced;
        }

        /// <summary>给 Image 应用 simple sprite 兜底（caret / checkmark 等无边界形状）。</summary>
        public static void ApplyDefaultSimpleSprite(UnityImage img, string spriteName, bool preserveAspect = false)
        {
            if (img == null || img.sprite != null) return;
            var s = GetDefaultSprite(spriteName);
            if (s == null) return;
            img.sprite = s;
            img.type = UnityImage.Type.Simple;
            img.preserveAspect = preserveAspect;
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
            tmp.color = DefaultLabelColor;
            tmp.fontSize = 14;
            return tmp;
        }
    }
}
