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
        // 全部白底 sliced + #4A3322 暖深棕字；sprite 由 atlas tint 表现明暗。
        public static readonly Color DefaultBtnColor = Color.white;
        public static readonly Color DefaultControlBgColor = Color.white;
        public static readonly Color DefaultTrackColor = Color.white;
        public static readonly Color DefaultFillColor = Color.white;
        public static readonly Color DefaultHandleColor = Color.white;
        public static readonly Color DefaultPopupBgColor = Color.white;
        public static readonly Color DefaultContainerColor = new(1f, 1f, 1f, 0.392f);
        // 单一暖深棕色源（#4A3322），匹配种田风奶油皮肤；让 glyph / label / placeholder 三个语义角色共用同一基色
        // 单点修改：换主题色只动这一行
        private static readonly Color s_inkColor = new(0.290f, 0.200f, 0.133f, 1f);
        public static readonly Color DefaultGlyphColor = s_inkColor;
        public static readonly Color DefaultLabelColor = s_inkColor;
        public static readonly Color DefaultPlaceholderColor = new(s_inkColor.r, s_inkColor.g, s_inkColor.b, 0.5f);

        // pugui.pxl 像素图集中的精灵名（section 名 = sprite 名，参见 Runtime/Resources/PromptUGUI/Defaults/pugui.pxl）
        public const string SpriteRoundedRect = "pugui_9slice_round";
        public const string SpriteMaskRoundedRect = "pugui_9slice_mask";
        public const string SpriteCaret = "pugui_caret";
        public const string SpriteCheckmark = "pugui_checkmark";
        public const string SpriteInset = "pugui_9slice_inset";
        public const string SpritePressed = "pugui_9slice_pressed";
        public const string SpriteKnob = "pugui_knob";

        private const string DefaultSpritesPath = "PromptUGUI/Defaults/pugui";
        private static Dictionary<string, Sprite> _defaultSprites;
        private static bool _defaultHintsRegistered;

        /// <summary>pugui.pxl 的 tiled hint 自举：注册进 SpriteRenderHints。
        /// 无论默认皮肤经 GetDefaultSprite 还是经 UI.ResolveSprite 解析，DeriveType 都能正确判定，
        /// 故从这两处各调用一次（_defaultHintsRegistered 标志防重复注册）。
        /// 仅覆盖库自带默认皮肤；用户 pxl 的 tiled 注册仍由 ResolveSprite/BuildLookup 路径负责。</summary>
        private static void EnsureDefaultHintsRegistered()
        {
            if (_defaultHintsRegistered) return;
            _defaultHintsRegistered = true;
            var hintAssets = Resources.LoadAll<PromptUGUI.Application.PxlSpriteHints>(DefaultSpritesPath);
            for (var i = 0; i < hintAssets.Length; i++)
                PromptUGUI.Application.Internal.SpriteRenderHints.Register(hintAssets[i]);
        }

        public static Sprite GetDefaultSprite(string name)
        {
            if (_defaultSprites == null)
            {
                _defaultSprites = new Dictionary<string, Sprite>();
                var loaded = Resources.LoadAll<Sprite>(DefaultSpritesPath);
                foreach (var s in loaded)
                    if (s != null) _defaultSprites[s.name] = s;
                EnsureDefaultHintsRegistered();
            }
            return _defaultSprites.TryGetValue(name, out var sprite) ? sprite : null;
        }

        /// <summary>唯一的 Image.Type 推导点(spec pxl-tiled-hint §6):
        /// hint 标 tiled → Tiled；有 border → Sliced；否则 Simple。
        /// 内部调用 EnsureDefaultHintsRegistered 以兼容未经 GetDefaultSprite 加载的 sprite
        /// （如 Progress/ScrollList 经 UI.ResolveSprite 直接解析 pugui 资产的情形）。</summary>
        public static UnityImage.Type DeriveType(Sprite s)
        {
            EnsureDefaultHintsRegistered();
            if (s == null) return UnityImage.Type.Simple;
            if (PromptUGUI.Application.Internal.SpriteRenderHints.IsTiled(s)) return UnityImage.Type.Tiled;
            return s.border != Vector4.zero ? UnityImage.Type.Sliced : UnityImage.Type.Simple;
        }

        /// <summary>给 Image 应用 9-slice 圆角 sprite 兜底；调用者后续 sprite= 仍可 override。
        /// Tiled 而非 Sliced：钉木框边带有方向性纹理（青苔/木纹），平铺保形不拉糊；
        /// 带 border 的 Tiled = 四角固定 + 边/中心平铺（几何重复，不依赖 wrap mode，进图集安全）。
        /// Image.Type 由 DeriveType(sprite) 推导 —— 依赖 pugui.pxl tiled: true hint 登记。</summary>
        public static void ApplyDefaultSlicedSprite(UnityImage img)
        {
            if (img == null || img.sprite != null) return;
            var s = GetDefaultSprite(SpriteRoundedRect);
            if (s == null) return;
            img.sprite = s;
            img.type = DeriveType(s);
        }

        /// <summary>凹形容器（输入框/滑轨/列表底）的 9-slice 兜底；Image.Type 由 DeriveType 推导
        /// (inset 无 tiled hint → 有 border → Sliced)。</summary>
        public static void ApplyDefaultInsetSprite(UnityImage img)
        {
            if (img == null || img.sprite != null) return;
            var s = GetDefaultSprite(SpriteInset);
            if (s == null) return;
            img.sprite = s;
            img.type = DeriveType(s);
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

        /// <summary>null sprite 不动；否则 type = DeriveType(sprite)
        /// (hint tiled → Tiled, border → Sliced, else Simple)。</summary>
        public static void AutoSlice(UnityImage img)
        {
            if (img == null || img.sprite == null) return;
            img.type = DeriveType(img.sprite);
        }

        /// <summary>
        /// Viewport mask 三态（spec 2026-06-11-list-popup-skin-mask §2.3）：
        /// value == null → 默认 sprite + stencil Mask（OnAttached 初始形态）；
        /// value == ""   → RectMask2D 直角裁剪（stencil Mask + Image 关 enabled）；
        /// 其他          → 指定 sprite + stencil Mask（UI.ResolveSprite 失败路径同 sprite=）。
        /// lazy-add + enabled 开关，不 Destroy —— Variant ReSolve 可在三态间任意来回切，
        /// 也避免 PlayMode 下 Destroy 延迟销毁导致同帧切换读到待销毁组件。
        /// </summary>
        public static void ApplyViewportMask(RectTransform viewport, string value, string defaultSpriteName)
        {
            var go = viewport.gameObject;
            var img = go.GetComponent<UnityImage>();
            var mask = go.GetComponent<Mask>();
            var rectMask = go.GetComponent<RectMask2D>();

            if (value != null && value.Length == 0)
            {
                if (mask != null) mask.enabled = false;
                if (img != null) img.enabled = false;
                if (rectMask == null) rectMask = go.AddComponent<RectMask2D>();
                rectMask.enabled = true;
                return;
            }

            if (rectMask != null) rectMask.enabled = false;
            if (img == null) img = go.AddComponent<UnityImage>();
            img.enabled = true;
            // alpha=1 关键：alpha<1 触发 UI/Default shader 的 alpha-discard，把 stencil 写飞 (4af322b)。
            img.color = Color.white;
            img.sprite = value == null
                ? GetDefaultSprite(defaultSpriteName)
                : PromptUGUI.Application.UI.ResolveSprite(value);
            AutoSlice(img);
            if (mask == null) mask = go.AddComponent<Mask>();
            mask.enabled = true;
            mask.showMaskGraphic = false;
        }

        internal static void ResetDefaultSpriteCacheForTests()
        {
            _defaultSprites = null;
            _defaultHintsRegistered = false;
        }

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
