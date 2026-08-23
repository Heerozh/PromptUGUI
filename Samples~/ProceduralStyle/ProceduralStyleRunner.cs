using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.ProceduralStyle
{
    /// <summary>
    /// Common Controls Demo 的程序化样式改版：同一批控件，视觉全部来自
    /// <c>&lt;Style&gt;</c> + <c>&lt;Frame&gt;</c> 的 SDF 绘制（圆角 / 描边 / 渐变 / 辉光），
    /// 一张图素、一个 9-slice 都没有。
    ///
    /// 换皮肤只改 XML：<c>ProceduralStyle.ui.xml</c> 顶部那一行
    /// <c>&lt;Import src="UI/Skin-Flat"/&gt;</c> 指向另一份实现了同样 12 个
    /// <c>&lt;Style&gt;</c> 名字的文件即可 —— 版面、控件、本文件的绑定代码一行都不用动。
    ///
    /// 使用步骤：场景里建空 GameObject 挂本组件，按 Play。
    /// 不需要 SpriteSet（本 demo 不引用任何 sprite）。
    /// </summary>
    public sealed class ProceduralStyleRunner : MonoBehaviour
    {
        // 切语言时会重发的脉冲：立即发一次 + 每次 UI.Locale.Changed 再发一次。
        // ReSolve 只重译 XML 里声明的 text=，不会重跑 C# 绑定 lambda，所以动态绑定里的
        // UI.Tr(...) 必须自己挂到这个流上。
        static Observable<Unit> LocaleTicks =>
            Observable.FromEvent(h => UI.Locale.Changed += h, h => UI.Locale.Changed -= h)
                      .Prepend(Unit.Default);

        async void Start()
        {
            UI.UseResourcesResolver("UI");
            UI.UseGamepadNavigation();

            await UI.LoadDocumentAsync("ProceduralStyle.ui");
            UI.Theme.Set("night");   // Skin-Flat 注册了 night + day 两套 token
            var screen = UI.Open("ProceduralStyle");

            BindThemeSwitcher(screen);
            BindFormPage(screen);
            BindDisplayPage(screen);
            BindListPage(screen);
            BindModalPage(screen);

            screen.Get<Btn>("tutorialBtn").OnClick
                  .Subscribe(_ => RunTutorial(screen)).AddTo(screen);
        }

        // 顶栏下拉：夜间 / 白天 → UI.Theme.Set。
        // 注意这里换的只是 token 值：<Style> 里写的是 "surface/0.9" 这种引用，
        // 所以圆角、描边宽度、辉光半径全都不变，只有颜色随主题走。
        static void BindThemeSwitcher(IScreen screen)
        {
            var theme = screen.Get<Dropdown>("theme");
            theme.BindOptions(LocaleTicks.Select(_ => (IEnumerable<string>)
                new[] { UI.Tr("Night"), UI.Tr("Day") })).AddTo(screen);
            theme.OnSelected.Subscribe(i => UI.Theme.Set(i == 0 ? "night" : "day")).AddTo(screen);
        }

        // ① 表单输入
        static void BindFormPage(IScreen screen)
        {
            screen.Get<InputField>("username").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] username = {v}")).AddTo(screen);

            screen.Get<Toggle>("muteAudio").OnValueChanged
                  .Subscribe(b => Debug.Log($"[Sample] mute = {b}")).AddTo(screen);

            screen.Get<Slider>("masterVol").OnValueChanged
                  .Subscribe(v => Debug.Log($"[Sample] master vol = {v:F2}")).AddTo(screen);

            var quality = screen.Get<Dropdown>("quality");
            quality.BindOptions(LocaleTicks.Select(_ => (IEnumerable<string>)
                new[] { UI.Tr("Low"), UI.Tr("Medium"), UI.Tr("High"), UI.Tr("Ultra") })).AddTo(screen);
            quality.OnSelected.Subscribe(i => Debug.Log($"[Sample] quality = {i}")).AddTo(screen);

            screen.Get<Btn>("saveBtn").OnClick
                  .Subscribe(_ => UI.Toast.Show(UI.Tr("Saved"))).AddTo(screen);

            var username = screen.Get<InputField>("username");
            var vol = screen.Get<Slider>("masterVol");
            var mute = screen.Get<Toggle>("muteAudio");
            screen.Get<Btn>("resetBtn").OnClick.Subscribe(_ =>
            {
                username.TextValue = "";
                vol.Value = 0.8f;
                mute.IsOn = false;
            }).AddTo(screen);
        }

        // ② 展示反馈
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

        // ③ 列表轮播：卡片的形由 class="card" 给，色由这里按条目改写 —— 演示
        // 「Style 管形体、C# 管数据驱动的那一部分」的分工。
        static void BindListPage(IScreen screen)
        {
            screen.Get<Carousel>("banner").BindItems(
                LocaleTicks.Select(_ => (IReadOnlyList<(string title, string color)>)new[]
                {
                    (UI.Tr("Not a single sprite"), "accent/0.28"),
                    (UI.Tr("Corners and borders are shader-drawn"), "accent-2/0.28"),
                    (UI.Tr("Re-skin by changing one Import"), "danger/0.28"),
                }),
                (IControl card, (string title, string color) item) =>
                {
                    card.Get<Text>("title").TextValue = item.title;
                    card.Get<Frame>("bg").Color = item.color;
                }).AddTo(screen);

            screen.Get<ScrollList>("list").BindItems(
                LocaleTicks.Select(_ => (IReadOnlyList<string>)new[]
                {
                    UI.Tr("VSync"), UI.Tr("Anti-Aliasing"), UI.Tr("Shadows"), UI.Tr("Texture Quality"),
                    UI.Tr("Particles"), UI.Tr("Reflections"), UI.Tr("Post Processing"), UI.Tr("Bloom"),
                    UI.Tr("Motion Blur"), UI.Tr("Depth of Field")
                }),
                (IControl slot, string text) => slot.Get<Text>("label").TextValue = text).AddTo(screen);
        }

        // ⑤ 模态提示
        static void BindModalPage(IScreen screen)
        {
            screen.Get<Btn>("msgBoxBtn").OnClick.Subscribe(async _ =>
            {
                var r = await MessageBox.Open(UI.Tr("Save your changes?"), MsgBtn.OK | MsgBtn.Cancel, title: UI.Tr("Confirm"));
                UI.Toast.Show(string.Format(UI.Tr("MessageBox returned {0}"), r));
            }).AddTo(screen);

            screen.Get<Btn>("inputBoxBtn").OnClick.Subscribe(async _ =>
            {
                var s = await InputBox.Open(UI.Tr("What is your name?"), placeholder: UI.Tr("e.g. Link"));
                UI.Toast.Show(s == null ? UI.Tr("Cancelled") : string.Format(UI.Tr("Hello, {0}!"), s));
            }).AddTo(screen);

            screen.Get<Btn>("mdBoxBtn").OnClick.Subscribe(async _ =>
            {
                if (UI.Markdown.Renderer == null)   // 没装 Markdig 时退化成纯文本
                {
                    await MessageBox.Open(
                        UI.Tr("Markdig not detected — rich Markdown is unavailable.\nInstall Markdig.Signed via NuGetForUnity and try again."),
                        MsgBtn.OK, title: UI.Tr("MarkdownBox"));
                    return;
                }

                await MarkdownBox.Open(
                    UI.Tr("# Rich text\n\n**bold** / *italic* / [links](https://example.com)\n\n- first item\n- second item"),
                    title: UI.Tr("MarkdownBox"));
            }).AddTo(screen);

            screen.Get<Btn>("loadingBtn").OnClick.Subscribe(async _ =>
            {
                var h = Loading.Open(UI.Tr("Loading…"));
                await Awaitable.WaitForSecondsAsync(2f);
                h.Close();
            }).AddTo(screen);

            screen.Get<Btn>("toastBtn").OnClick
                  .Subscribe(_ => UI.Toast.Show(UI.Tr("This is a toast!"))).AddTo(screen);
        }

        // 新手引导：跨页脚本。不注册 UseProgressStore —— 每次点按钮都从头跑，可反复体验。
        static async void RunTutorial(IScreen screen)
        {
            if (UI.Tutorial.IsActive) return;   // 嵌套 Run 会抛 InvalidOperationException

            var username = screen.Get<InputField>("username");
            var vol = screen.Get<Slider>("masterVol");

            await UI.Tutorial.Run("procedural-style-intro", async t =>
            {
                // 非激活页的控件路径解析不到 —— 第一步先把表单页带出来
                await t.Step("ProceduralStyle/tabForm", text: UI.Tr("Start on the Form tab"));
                await t.Step("ProceduralStyle/username", text: UI.Tr("Type your name here"),
                             advance: Advance.When(() => !string.IsNullOrEmpty(username.TextValue)));
                var v0 = vol.Value;
                await t.Step("ProceduralStyle/masterVol", text: UI.Tr("Drag the slider to set the volume"),
                             advance: Advance.When(() => Mathf.Abs(vol.Value - v0) > 0.01f));
                await t.Step("ProceduralStyle/tabStyle", text: UI.Tr("Open the Styles tab to see where the shapes come from"));
                await t.Step("ProceduralStyle/tabModal", text: UI.Tr("Now switch to the Modals tab"));
                await t.Step("ProceduralStyle/toastBtn", text: UI.Tr("Tap here to fire a toast"));
                await t.Step(null, text: UI.Tr("Tour complete — explore away!"), advance: Advance.TapAnywhere);
            });
        }

        // 128x32 HSV 横向渐变，演示 RawImage 显示运行时生成的 Texture
        static Texture2D MakeGradientTexture()
        {
            const int w = 128, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    tex.SetPixel(x, y, Color.HSVToRGB(x / (float)w, 0.55f, 1f));
            tex.Apply();
            return tex;
        }
    }
}
