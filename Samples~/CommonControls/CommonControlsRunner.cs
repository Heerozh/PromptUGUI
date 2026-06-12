using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.CommonControls
{
    /// <summary>
    /// 全控件橱窗：TabBar 四页演示所有内置控件 + 内置模态 / Toast / UI.Tutorial 新手引导。
    /// 使用步骤：
    ///   1. 场景里建空 GameObject，挂本组件
    ///   2. 把 FarmSpriteSet.asset 拖到 Inspector 的 Sprite Sets 字段
    ///   3. 按 Play；点右上角「新手引导」体验引导（可反复触发）
    /// </summary>
    public sealed class CommonControlsRunner : MonoBehaviour
    {
        [SerializeField] SpriteSet[] spriteSets;   // 拖 FarmSpriteSet.asset

        async void Start()
        {
            UI.UseResourcesResolver("UI");

            if (spriteSets != null && spriteSets.Length > 0)
                SpriteResolverHelpers.UseSpriteSetResolver(spriteSets);

            await UI.LoadDocumentAsync("CommonControls.ui");
            var screen = UI.Open("CommonControls");

            BindFormPage(screen);
            BindDisplayPage(screen);
            BindListPage(screen);
            BindModalPage(screen);

            screen.Get<Btn>("tutorialBtn").OnClick
                  .Subscribe(_ => RunTutorial(screen)).AddTo(screen);
        }

        // ① 表单输入：值变化全部打 log
        static void BindFormPage(IScreen screen)
        {
            screen.Get<InputField>("username").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] username = {v}")).AddTo(screen);

            screen.Get<Toggle>("muteAudio").OnValueChanged
                  .Subscribe(b => Debug.Log($"[Sample] mute = {b}")).AddTo(screen);

            screen.Get<Slider>("masterVol").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] master vol = {v:F2}")).AddTo(screen);

            var quality = screen.Get<Dropdown>("quality");
            quality.BindOptions(Observable.Return<IEnumerable<string>>(
                new[] { "Low", "Medium", "High", "Ultra" }));
            quality.OnSelected.Subscribe(i => Debug.Log($"[Sample] quality = {i}")).AddTo(screen);
        }

        // ② 展示反馈：Progress 按钮驱动；RawImage 喂 C# 生成的渐变纹理
        static void BindDisplayPage(IScreen screen)
        {
            var progress = screen.Get<Progress>("progress");
            var current = 0.3f;   // Progress 是只读显示控件，当前值由调用方持有
            screen.Get<Btn>("progMinus").OnClick.Subscribe(_ =>
            {
                current = Mathf.Clamp01(current - 0.1f);
                progress.Value = current;
            }).AddTo(screen);
            screen.Get<Btn>("progPlus").OnClick.Subscribe(_ =>
            {
                current = Mathf.Clamp01(current + 0.1f);
                progress.Value = current;
            }).AddTo(screen);

            screen.Get<RawImage>("gradient").Texture = MakeGradientTexture();
        }

        // ③ 列表轮播：Carousel / ScrollList 均走 BindItems
        static void BindListPage(IScreen screen)
        {
            screen.Get<Carousel>("banner").BindItems(
                Observable.Return<IReadOnlyList<(string title, string color)>>(new[]
                {
                    ("欢迎使用 PromptUGUI", "#3B5BA5"),
                    ("XML 直接生成 uGUI", "#7C4DA5"),
                    ("轮播卡自动播放", "#2F8F6B"),
                }),
                (IControl card, (string title, string color) item) =>
                {
                    card.Get<Text>("title").TextValue = item.title;
                    card.Get<Image>("bg").Color = item.color;
                });

            screen.Get<ScrollList>("list").BindItems(
                Observable.Return<IReadOnlyList<string>>(new[]
                {
                    "VSync", "Anti-Aliasing", "Shadows", "Texture Quality",
                    "Particles", "Reflections", "Post Processing", "Bloom",
                    "Motion Blur", "Depth of Field"
                }),
                (IControl slot, string text) => slot.Get<Text>("label").TextValue = text);
        }

        // ④ 模态提示：四种内置模态 + Toast，结果用 Toast 回显
        static void BindModalPage(IScreen screen)
        {
            screen.Get<Btn>("msgBoxBtn").OnClick.Subscribe(async _ =>
            {
                var r = await MessageBox.Open("要保存更改吗？", MsgBtn.OK | MsgBtn.Cancel, title: "确认");
                UI.Toast.Show($"MessageBox 返回 {r}");
            }).AddTo(screen);

            screen.Get<Btn>("inputBoxBtn").OnClick.Subscribe(async _ =>
            {
                var s = await InputBox.Open("你的名字？", placeholder: "e.g. Link");
                UI.Toast.Show(s == null ? "已取消" : $"你好，{s}！");
            }).AddTo(screen);

            screen.Get<Btn>("mdBoxBtn").OnClick.Subscribe(async _ =>
            {
                await MarkdownBox.Open(
                    "# 富文本\n\n支持 **加粗** / *斜体* / [链接](https://example.com)\n\n- 列表项一\n- 列表项二",
                    title: "MarkdownBox");
            }).AddTo(screen);

            screen.Get<Btn>("loadingBtn").OnClick.Subscribe(async _ =>
            {
                var h = Loading.Open("加载中…");
                await Awaitable.WaitForSecondsAsync(2f);
                h.Close();
            }).AddTo(screen);

            screen.Get<Btn>("toastBtn").OnClick
                  .Subscribe(_ => UI.Toast.Show("这是一条 Toast！")).AddTo(screen);
        }

        // 新手引导：七步跨页脚本。不注册 UseProgressStore —— 每次点按钮都从头跑，可反复体验。
        static async void RunTutorial(IScreen screen)
        {
            if (UI.Tutorial.IsActive) return;   // 嵌套 Run 会抛 InvalidOperationException

            var username = screen.Get<InputField>("username");
            var vol = screen.Get<Slider>("masterVol");

            await UI.Tutorial.Run("common-controls-intro", async t =>
            {
                // 非激活页的控件路径解析不到 —— 第一步先把表单页带出来
                await t.Step("CommonControls/tabForm", text: "先切到「表单输入」页");
                await t.Step("CommonControls/username", text: "在这里输入你的名字",
                             advance: Advance.When(() => !string.IsNullOrEmpty(username.TextValue)));
                await t.Step("CommonControls/muteAudio", text: "勾选静音开关");
                var v0 = vol.Value;
                await t.Step("CommonControls/masterVol", text: "拖动滑杆调整音量",
                             advance: Advance.When(() => Mathf.Abs(vol.Value - v0) > 0.01f));
                await t.Step("CommonControls/tabModal", text: "切到「模态提示」页");
                await t.Step("CommonControls/toastBtn", text: "点这里弹一条 Toast");
                await t.Step(null, text: "引导完成，尽情探索吧！", advance: Advance.TapAnywhere);
            });
        }

        // 128x32 HSV 横向渐变，演示 RawImage 显示运行时生成的 Texture
        static Texture2D MakeGradientTexture()
        {
            const int w = 128, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    tex.SetPixel(x, y, Color.HSVToRGB(x / (float)w, 0.6f, 1f));
            tex.Apply();
            return tex;
        }
    }
}
