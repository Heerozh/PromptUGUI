using PromptUGUI.Application;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.MainMenu {
    public sealed class MainMenuRunner : MonoBehaviour {
        [SerializeField] TextAsset _xml;
        [SerializeField] GameObject _primaryButtonPrefab;
        [SerializeField] GameObject _dangerButtonPrefab;

        void Start() {
            UI.Registry.Register<PrimaryButton>("PrimaryButton", _primaryButtonPrefab);
            UI.Registry.Register<DangerButton>("DangerButton", _dangerButtonPrefab);

            BuiltinPrimitives.Register(UI.Registry);

            UI.LoadDocument("main", _xml.text);
            var screen = UI.Open("MainMenu");

            screen.Get<PrimaryButton>("playBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] play clicked"))
                  .AddTo(screen);
            screen.Get<PrimaryButton>("settingsBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] settings clicked"))
                  .AddTo(screen);
            screen.Get<DangerButton>("quitBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] quit clicked"))
                  .AddTo(screen);
        }
    }
}
