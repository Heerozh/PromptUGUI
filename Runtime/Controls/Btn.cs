using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls {
    /// <summary>
    /// 通用按钮原语：自带 Image (背景 + Button 的 raycast target) + Button + OnClick (R3)。
    /// 模板可以把它作为根（<Btn .../>）并在内部塞 Image/Text 等子节点组合出
    /// PrimaryButton / IconButton / 进度条按钮 等变体，不需要新的 prefab 或 C# 类。
    ///
    /// 注意：Btn 的子 Graphic 默认会拦截点击事件。如果不想被拦截，把子 Image/Text 的
    /// raycastTarget 设为 false（Text 通过 raycastTarget="false" 属性控制）。
    /// </summary>
    public sealed class Btn : Control {
        UnityImage _bg;
        Button _btn;
        readonly Subject<Unit> _click = new();

        public override void OnAttached() {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _btn = GameObject.GetComponent<Button>() ?? GameObject.AddComponent<Button>();
            _btn.targetGraphic = _bg;
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
        }

        [UIAttr]
        public string Color {
            set {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
            }
        }

        [UIAttr]
        public string Sprite {
            set {
                if (string.IsNullOrEmpty(value)) { _bg.sprite = null; return; }
                _bg.sprite = Resources.Load<Sprite>(value);
            }
        }

        public Observable<Unit> OnClick => _click;

        public override void Dispose() {
            _click.Dispose();
            base.Dispose();
        }
    }
}
