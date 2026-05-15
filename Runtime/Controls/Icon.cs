using PromptUGUI.Application;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Icon : Control
    {
        private UnityImage _img;

        public override void OnAttached()
        {
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<UnityImage>();
            _img.preserveAspect = true;
            _img.raycastTarget = false;
            _img.color = UnityEngine.Color.white;
        }

        [UIAttr]
        public string Name
        {
            set
            {
                if (string.IsNullOrEmpty(value)) { _img.sprite = null; return; }
                if (UI.SpriteResolver == null)
                {
                    Debug.LogError(
                        $"Icon '{value}': UI.SpriteResolver is not registered. " +
                        $"Call SpriteResolverHelpers.UseSpriteSetResolver(iconSets) " +
                        $"before opening Screens that contain <Icon>.");
                    _img.sprite = null;
                    return;
                }
                var sprite = UI.SpriteResolver(value);
                if (sprite == null)
                    Debug.LogError(
                        $"Icon '{value}': resolver returned null. " +
                        $"Check the icon name spelling, or run " +
                        $"Tools → PromptUGUI → Sync Icon Atlases (All Sets) to " +
                        $"include it in the SpriteSet's atlas.");
                _img.sprite = sprite;
            }
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _img.color = c;
            }
        }

        public override Vector2? GetNativeSize() =>
            _img != null && _img.sprite != null ? (Vector2?)_img.sprite.rect.size : null;
    }
}
