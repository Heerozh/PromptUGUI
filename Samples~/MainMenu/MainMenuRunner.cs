using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.MainMenu {
    /// <summary>
    /// 加载 Resources/UI/MainMenu.ui.xml 并打开 MainMenu Screen。
    /// Editor 内修改 .ui.xml 保存即自动 hot reload。
    ///
    /// 使用步骤：
    ///   1. 场景里建空 GameObject，挂本组件
    ///   2. 把 SolarSpriteSet.asset 拖到 Inspector 的 Sprite Sets 字段
    ///   3. Tools → PromptUGUI → Sprite → Sync Atlases (All Sets) 跑一次，
    ///      让 SolarSpriteSet 的 SpriteAtlas 包含 XML 引用到的 sprite
    ///   4. 按 Play
    /// </summary>
    public sealed class MainMenuRunner : MonoBehaviour {
        [SerializeField] SpriteSet[] spriteSets;

        async void Start() {
            UI.UseResourcesResolver("UI");

            if (spriteSets != null && spriteSets.Length > 0)
                SpriteResolverHelpers.UseSpriteSetResolver(spriteSets);

            await UI.LoadDocumentAsync("MainMenu.ui");
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
