using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    internal static class ProceduralBuilders
    {
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
