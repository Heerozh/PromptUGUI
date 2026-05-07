using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls {
    public sealed class HStack : Control {
        HorizontalLayoutGroup _layout;

        public override void OnAttached() {
            _layout = GameObject.GetComponent<HorizontalLayoutGroup>()
                      ?? GameObject.AddComponent<HorizontalLayoutGroup>();
        }

        [UIAttr]
        public float Spacing {
            set => _layout.spacing = value;
        }

        [UIAttr]
        public string Padding {
            set {
                VStack.ParseTRBL(value, out int t, out int r, out int b, out int l);
                _layout.padding = new RectOffset(l, r, t, b);
            }
        }
    }
}
