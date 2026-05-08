using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.MainMenu {
    /// <summary>
    /// 加载 Resources/UI/MainMenu.ui.xml 并打开 MainMenu Screen。
    /// Editor 内修改这个 .ui.xml 保存即自动 hot reload（M4.5 起）。
    /// 使用步骤：场景里建空 GameObject，挂本组件，按 Play。
    /// 不再需要 Inspector 拖文件。
    /// </summary>
    public sealed class MainMenuRunner : MonoBehaviour {
        void Start() {
            BuiltinPrimitives.Register(UI.Registry);
            UI.UseResourcesResolver("UI");
            UI.LoadDocumentFromSrc("MainMenu");
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
