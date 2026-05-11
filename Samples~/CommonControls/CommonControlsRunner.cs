using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.CommonControls
{
    /// <summary>
    /// 演示 Toggle / Slider / Dropdown / ScrollList 四个常用控件的 XML 写法 + R3 数据流。
    /// 使用步骤：
    ///   1. 场景里建空 GameObject，挂本组件
    ///   2. 按 Play
    /// </summary>
    public sealed class CommonControlsRunner : MonoBehaviour
    {
        async void Start()
        {
            UI.UseResourcesResolver("UI");
            await UI.LoadDocumentAsync("Settings.ui");
            var screen = UI.Open("Settings");

            screen.Get<PromptUGUI.Controls.InputField>("username").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] username = {v}")).AddTo(screen);

            screen.Get<Toggle>("muteAudio").OnValueChanged
                  .Subscribe(b => Debug.Log($"[Sample] mute = {b}")).AddTo(screen);

            screen.Get<Slider>("masterVol").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] master vol = {v:F2}")).AddTo(screen);

            var quality = screen.Get<Dropdown>("quality");
            quality.BindOptions(Observable.Return<IEnumerable<string>>(
                new[] { "Low", "Medium", "High", "Ultra" }));
            quality.OnSelected.Subscribe(i => Debug.Log($"[Sample] quality = {i}")).AddTo(screen);

            var list = screen.Get<ScrollList>("list");
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[]
                {
                    "VSync", "Anti-Aliasing", "Shadows", "Texture Quality",
                    "Particles", "Reflections", "Post Processing", "Bloom",
                    "Motion Blur", "Depth of Field"
                }),
                (IControl slot, string text) =>
                {
                    slot.Get<Text>("label").TextValue = text;
                });
        }
    }
}
