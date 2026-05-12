using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class HStack : Control
    {
        private HorizontalLayoutGroup _layout;

        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<HorizontalLayoutGroup>()
                      ?? GameObject.AddComponent<HorizontalLayoutGroup>();
            // 同 VStack：spec §6.5 路由前提。
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = false;
            _layout.childForceExpandHeight = false;
        }

        [UIAttr]
        public float Spacing
        {
            set => _layout.spacing = value;
        }

        [UIAttr]
        public string Padding
        {
            set
            {
                VStack.ParseTRBL(value, out var t, out var r, out var b, out var l);
                _layout.padding = new RectOffset(l, r, t, b);
            }
        }
    }
}
