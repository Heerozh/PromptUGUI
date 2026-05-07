using PromptUGUI.Controls;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine.UI;

namespace PromptUGUI.Samples.MainMenu {
    public sealed class PrimaryButton : Control {
        Button _btn;
        TMP_Text _label;
        readonly Subject<Unit> _click = new();

        public override void OnAttached() {
            _btn = GameObject.GetComponent<Button>();
            _label = GameObject.GetComponentInChildren<TMP_Text>();
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
        }

        [UIAttr("text")]
        public string TextValue {
            set => _label.text = value;
        }

        public Observable<Unit> OnClick => _click;

        public override void Dispose() {
            _click.Dispose();
            base.Dispose();
        }
    }
}
