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

            ApplyLibraryWorkarounds(screen);
            BindThemeSwitcher(screen);
            BindFormPage(screen);
            BindDisplayPage(screen);
            BindListPage(screen);
            BindModalPage(screen);

            screen.Get<Btn>("tutorialBtn").OnClick
                  .Subscribe(_ => RunTutorial(screen)).AddTo(screen);
        }

        /// <summary>
        /// 两处库层面的缺口，在修好之前只能在这里绕过。两个都跟 &lt;Style&gt; 系统无关 ——
        /// 是本 demo 想做扁平外观时撞上的既有问题。
        ///
        /// **① &lt;Tab&gt; 的 width / height 不生效。**
        /// TabBar 建的 Horizontal/VerticalLayoutGroup 用的是 Unity 默认值
        /// <c>childControlWidth/Height = false</c>，这种模式下 LayoutGroup 只**摆位置**、
        /// 不**改尺寸**，所以 XML 写的 width/height 只影响间距分配，每个 Tab 的实际
        /// RectTransform 一直是默认的 100×100（撑穿轨道、互相重叠）。
        /// 打开 childControl* 后 XML 写的值才真正落到 rect 上。
        ///
        /// **② 内部图层还带默认像素皮。**
        /// Slider 的滑轨/滑块、Dropdown 的箭头、Toggle 的对勾由控件 OnAttached 写死，
        /// 没有 XML 属性可改（XML 的 sprite= 只作用于控件最外层那张背景 Image）。
        /// skill 建议「subclass 并 override OnAttached」，但这些控件全是 sealed 的，
        /// 那条路走不通；只能按 GameObject 名字（来自 ProceduralBuilders）遍历改。
        /// </summary>
        static void ApplyLibraryWorkarounds(IScreen screen)
        {
            var root = ((Control)screen.Get<Slider>("masterVol")).GameObject.transform.root;

            // ① 让 <Tab width/height> 真的生效。TabBar 每次切换 direction 会重建
            // LayoutGroup（横竖屏切换时），所以横竖屏来回切之后需要再调一次。
            var tabLayout = screen.Get<TabBar>("tabs").GameObject
                                  .GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
            if (tabLayout != null)
            {
                tabLayout.childControlWidth = true;
                tabLayout.childControlHeight = true;
            }

            // ② 拉平内部图层
            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                switch (img.gameObject.name)
                {
                    case "Background":   // Slider 轨道
                        img.sprite = null;
                        img.color = UI.Theme.Resolve("bg-bottom/0.6");
                        break;
                    case "Fill":         // Slider 已填充段
                        img.sprite = null;
                        img.color = UI.Theme.Resolve("accent");
                        break;
                    case "Handle":       // Slider 滑块 + ScrollList/Dropdown 的滚动条滑块
                        img.sprite = null;
                        img.color = UI.Theme.Resolve("ink-dim");
                        break;
                    case "Checkmark":    // Toggle 对勾：保留字形，只改色
                        img.color = UI.Theme.Resolve("accent");
                        break;
                    case "Arrow":        // Dropdown 下拉箭头
                        // 直接隐藏。它是**有颜色**的像素三角，而 Image.color 是乘法，
                        // 染不成灰；去掉 sprite 又会变成一个实心方块。真实项目应该
                        // 换成自己的 chevron sprite 或图标字体。
                        img.enabled = false;
                        break;
                }
            }
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
