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
    ///   4. 右上角下拉切「农场 / 玻璃」两套皮肤 —— 同一棵 XML 树，两套 <Style> 属性包
    /// </summary>
    public sealed class CommonControlsRunner : MonoBehaviour
    {
        [SerializeField] SpriteSet[] spriteSets;   // 拖 FarmSpriteSet.asset

        // 切语言时会重发的脉冲：立即发一次 + 每次 UI.Locale.Changed 再发一次。
        // 用它驱动所有"文字需随 locale 重译"的 BindOptions / BindItems —— ReSolve 只重译 XML 里
        // 声明的 text=，不会重跑 C# 绑定 lambda，所以动态绑定里的 UI.Tr(...) 必须自己挂到这个流上，
        // 否则 Observable.Return 只发一次、永远停在第一个 locale 的字符串。
        static Observable<Unit> LocaleTicks =>
            Observable.FromEvent(h => UI.Locale.Changed += h, h => UI.Locale.Changed -= h)
                      .Prepend(Unit.Default);

        async void Start()
        {
            UI.UseResourcesResolver("UI");
            // 手柄/键盘方向导航（opt-in）：装上设备检测 + 单 EventSystem，按方向键/摇杆即进 Directional
            // 模式并显示默认焦点光标。必须在 UI.Open 之前调用——初始焦点与光标 overlay 在 Open 时按
            // IsEnabled 建立。鼠标仍照常工作（Pointer 模式），方向输入才切换。
            UI.UseGamepadNavigation();
            // canvas="camera" 的 Screen 必须拿到相机，否则静默退回 Overlay —— 它照样显示，但不再
            // 被相机渲染，玻璃就永远采不到它。**无条件**赋值：Canvas.renderMode 的 getter 在
            // worldCamera 为空时会谎报成 Overlay，拿它做条件永远不命中；给真 Overlay 画布赋相机
            // 是无害的（被忽略）。
            UI.CanvasConfigurator = (canvas, _) => canvas.worldCamera = Camera.main;
            await UI.Locale.SetToSystemDefaultAsync("en");

            if (spriteSets != null && spriteSets.Length > 0)
                SpriteResolverHelpers.UseSpriteSetResolver(spriteSets);

            await UI.LoadDocumentAsync("CommonControls.ui");
            SetSkin(glass: false);   // 注册了 farm+glass 两套皮，AutoSet 不自动选，显式设默认
            UI.Open("Backdrop");     // 相机画布：玻璃能看见的只有相机渲染出来的那张图
            var screen = UI.Open("CommonControls");

            BindSkinSwitcher(screen);
            BindFormPage(screen);
            BindDisplayPage(screen);
            BindListPage(screen);
            BindModalPage(screen);

            screen.Get<Btn>("tutorialBtn").OnClick
                  .Subscribe(_ => RunTutorial(screen)).AddTo(screen);
        }

        // 右上角皮肤下拉：农场（像素木框）/ 玻璃（磨砂）。换的是整套 <Style> 属性包 —— sprite、
        // hidden、pressedOffset、glass 参数全在里面，不只是颜色。已打开的 Screen 自动 ReSolve
        // 重新应用属性，GameObject 一个都不重建（引用与 R3 订阅全部存活）。
        static void BindSkinSwitcher(IScreen screen)
        {
            var skin = screen.Get<Dropdown>("skin");
            skin.BindOptions(LocaleTicks.Select(_ => (IEnumerable<string>)
                new[] { UI.Tr("Farm"), UI.Tr("Glass") })).AddTo(screen);
            skin.OnSelected.Subscribe(i => SetSkin(glass: i == 1)).AddTo(screen);
        }

        // 一套皮 = 一个 <Theme>（颜色 token + <Style> 属性包）+ 一个同名 Variant。
        //
        // 换皮的部分**全在 Theme 里**：颜色、贴图、圆角、玻璃参数，一个属性都没有走 Variant。
        // 同名 Variant 只为下面这一件事存在。
        //
        // 为什么要两个开关：scale-mode 写在 <Screen> 上，而 <Screen> 不接受 class= —— 它不是控件，
        // 挂不住属性包，parser 直接报错。玻璃是高清程序化图形，跟 pixel 那种整数倍缩放天生打架
        // （圆角和模糊会被整块放大），所以这一项走 Variant：XML 里 scale-mode="pixel"
        // scale-mode.glass="auto"，两个开关同名，读起来是一件事。
        //
        // 玻璃需要 URP ≥ 17 + Render Graph + 一台采集相机（默认 Camera.main）。缺任何一样都会
        // 静默降级成一块半透明板：形状 / 描边 / 发光都在，只是不模糊。UI.Glass.IsActive 一眼分辨。
        static void SetSkin(bool glass)
        {
            UI.Variants.Set("glass", glass);
            UI.Theme.Set(glass ? "glass" : "farm");
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
            quality.BindOptions(LocaleTicks.Select(_ => (IEnumerable<string>)
                new[] { UI.Tr("Low"), UI.Tr("Medium"), UI.Tr("High"), UI.Tr("Ultra") })).AddTo(screen);
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
                LocaleTicks.Select(_ => (IReadOnlyList<(string title, string color)>)new[]
                {
                    (UI.Tr("欢迎使用 PromptUGUI"), "#F2B24C"),
                    (UI.Tr("XML 直接生成 uGUI"), "#8FCF6A"),
                    (UI.Tr("轮播卡自动播放"), "#F28C6A"),
                }),
                (IControl card, (string title, string color) item) =>
                {
                    card.Get<Text>("title").TextValue = item.title;
                    card.Get<Image>("bg").Color = item.color;
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

        // ④ 模态提示：四种内置模态 + Toast，结果用 Toast 回显
        static void BindModalPage(IScreen screen)
        {
            screen.Get<Btn>("msgBoxBtn").OnClick.Subscribe(async _ =>
            {
                var r = await MessageBox.Open(UI.Tr("要保存更改吗？"), MsgBtn.OK | MsgBtn.Cancel, title: UI.Tr("确认"));
                UI.Toast.Show(string.Format(UI.Tr("MessageBox 返回 {0}"), r));
            }).AddTo(screen);

            screen.Get<Btn>("inputBoxBtn").OnClick.Subscribe(async _ =>
            {
                var s = await InputBox.Open(UI.Tr("你的名字？"), placeholder: UI.Tr("e.g. Link"));
                UI.Toast.Show(s == null ? UI.Tr("已取消") : string.Format(UI.Tr("你好，{0}！"), s));
            }).AddTo(screen);

            screen.Get<Btn>("mdBoxBtn").OnClick.Subscribe(async _ =>
            {
                if (UI.Markdown.Renderer == null)   // 没装 Markdig（NuGetForUnity: Markdig.Signed）时会退化成纯文本
                {
                    await MessageBox.Open(
                        UI.Tr("未检测到 Markdig，Markdown 富文本功能不可用。\n请通过 NuGetForUnity 安装 Markdig.Signed 后重试。"),
                        MsgBtn.OK, title: UI.Tr("MarkdownBox"));
                    return;
                }

                await MarkdownBox.Open(
                    UI.Tr("# 富文本\n\n支持 **加粗** / *斜体* / [链接](https://example.com)\n\n- 列表项一\n- 列表项二"),
                    title: UI.Tr("MarkdownBox"));
            }).AddTo(screen);

            screen.Get<Btn>("loadingBtn").OnClick.Subscribe(async _ =>
            {
                var h = Loading.Open(UI.Tr("加载中…"));
                await Awaitable.WaitForSecondsAsync(2f);
                h.Close();
            }).AddTo(screen);

            screen.Get<Btn>("toastBtn").OnClick
                  .Subscribe(_ => UI.Toast.Show(UI.Tr("这是一条 Toast！"))).AddTo(screen);
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
                await t.Step("CommonControls/tabForm", text: UI.Tr("先切到「表单输入」页"));
                await t.Step("CommonControls/username", text: UI.Tr("在这里输入你的名字"),
                             advance: Advance.When(() => !string.IsNullOrEmpty(username.TextValue)));
                await t.Step("CommonControls/muteAudio", text: UI.Tr("勾选静音开关"));
                var v0 = vol.Value;
                await t.Step("CommonControls/masterVol", text: UI.Tr("拖动滑杆调整音量"),
                             advance: Advance.When(() => Mathf.Abs(vol.Value - v0) > 0.01f));
                await t.Step("CommonControls/tabModal", text: UI.Tr("切到「模态提示」页"));
                await t.Step("CommonControls/toastBtn", text: UI.Tr("点这里弹一条 Toast"));
                await t.Step(null, text: UI.Tr("引导完成，尽情探索吧！"), advance: Advance.TapAnywhere);
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
