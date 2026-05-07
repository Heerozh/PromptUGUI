using PromptUGUI.Controls;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine.UI;

namespace PromptUGUI.Samples.MainMenu {
    public sealed class DangerButton : Control {
        Button _btn;
        TMP_Text _label;

        public override void OnAttached() {
            _btn = GameObject.GetComponent<Button>();
            _label = GameObject.GetComponentInChildren<TMP_Text>();
        }

        [UIAttr("text")]
        public string TextValue {
            set => _label.text = value;
        }

        public Observable<Unit> OnClick => _btn.OnClickAsObservable();
    }
}
