using PromptUGUI.Registry;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls {
    public sealed class Text : Control {
        TMP_Text _tmp;

        public override void OnAttached() {
            _tmp = GameObject.GetComponent<TMP_Text>()
                   ?? GameObject.AddComponent<TextMeshProUGUI>();
        }

        [UIAttr("text")]
        public string TextValue {
            set => _tmp.text = value ?? "";
        }

        [UIAttr]
        public int Size {
            set => _tmp.fontSize = value;
        }

        [UIAttr]
        public string Color {
            set {
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _tmp.color = c;
            }
        }

        [UIAttr]
        public string Align {
            set {
                _tmp.alignment = value switch {
                    "center" => TextAlignmentOptions.Center,
                    "right"  => TextAlignmentOptions.Right,
                    _        => TextAlignmentOptions.Left,
                };
            }
        }

        [UIAttr]
        public bool Wrap {
            set => _tmp.enableWordWrapping = value;
        }

        [UIAttr]
        public bool RaycastTarget {
            set => _tmp.raycastTarget = value;
        }
    }
}
