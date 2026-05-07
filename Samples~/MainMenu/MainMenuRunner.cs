using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.MainMenu {
    /// <summary>
    /// 把 MainMenu.ui.xml 加载并打开。无需 prefab——按钮由 <Btn> 原语 + <Template> 组合而来。
    /// 使用步骤：场景里建空 GameObject，挂本组件，把 MainMenu.ui.xml 拖到 _xml 字段，按 Play。
    /// </summary>
    public sealed class MainMenuRunner : MonoBehaviour {
        [SerializeField] TextAsset _xml;

        void Start() {
            BuiltinPrimitives.Register(UI.Registry);

            UI.LoadDocument("main", _xml.text);
            var screen = UI.Open("MainMenu");

            screen.Get<Btn>("playBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] play clicked")).AddTo(screen);
            screen.Get<Btn>("settingsBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] settings clicked")).AddTo(screen);
            screen.Get<Btn>("quitBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] quit clicked")).AddTo(screen);
        }
    }
}
