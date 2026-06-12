# CommonControls 示例扩充 + 新手引导演示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 CommonControls 示例扩充为 TabBar 四页的全控件橱窗（补齐 Grid / TabBar / Progress / Carousel / Markdown / RawImage / SafeArea / Animation / Show / 内置模态 / Toast），并加一段可反复触发的 UI.Tutorial 新手引导。

**Architecture:** 仅改 `Samples~/CommonControls/`（XML + Runner），零 Runtime/Editor 代码改动，无 SKILL 更新。XML 重命名为 `CommonControls.ui.xml`，TabBar `bind=` 切 4 页；Runner 按页拆分绑定方法；引导用 `UI.Tutorial.Run` 七步脚本，不注册 ProgressStore（每次从头跑）。

**Tech Stack:** PromptUGUI XML + C# (R3 / Awaitable)。验证 = UIXmlLint CLI + 临时导入宿主工程（`UnityProjects~/PromptUGUIDev/Assets/Samples/`）经 UnityMCP 编译检查。

**Spec:** `docs~/superpowers/specs/2026-06-12-common-controls-sample-expansion-design.md`

**关键事实（已在源码核实）：**

- `Samples~/` 不参与包编译（无 .meta、不在 .lint 解决方案里）——C# 验证只能走宿主工程临时导入；`dotnet format` 不覆盖它。
- 宿主工程在仓库内：`UnityProjects~/PromptUGUIDev/`（git 跟踪，但临时导入的 sample 副本**不提交**）。
- API 签名：`Btn.OnClick : Observable<Unit>`；`Slider.Value`(get/set)；`Toggle.IsOn`；`InputField.TextValue`；`Progress.Value`(setter, Clamp01)；`RawImage.Texture`(C# 属性)；`Image.Color`(string setter)；`Carousel.BindItems(Observable<IReadOnlyList<T>>, Action<IControl,T>)`（同 ScrollList）；`MessageBox.Open(...)→Awaitable<MsgBtn>`、`InputBox.Open(title,...)→Awaitable<string>`(cancel→null)、`MarkdownBox.Open(markdown, title:...)`、`Loading.Open(text)→LoadingHandle`+`h.Close()`（均在 `PromptUGUI.Application.Modals`）；`UI.Toast.Show(text)`；`UI.Tutorial.Run(id, async t => ...)` + `t.Step(target, text:, advance:)` + `Advance.When(Func<bool>)` + `UI.Tutorial.IsActive`（`TutorialMode`/`Advance` 在 `PromptUGUI.Application`）。
- XML 事实：Tab 是 TabBar 的 layout child（不可写 anchor/margin，width/height=格子尺寸）；`bind="frameId"` 选中显示/未选隐藏；Carousel 直接子卡不可写 anchor/margin/size（`PUI-CAROUSEL-CARD-SIZE`）；layout-group 子节点不可写 anchor/margin（`PUI-LAYOUT-*`）；`<Animation>` 包裹在 VStack 里是文档认可的 stagger 模式；`Grid` 属性=`columns`/`cellSize="WxH"`/`spacing`/`padding`；margin 顺序 = top,right,bottom,left。

---

### Task 1: 建分支 + 提交 spec/plan

**Files:**
- 无代码文件；git 操作

- [ ] **Step 1: 确认工作区干净并建分支**

```bash
cd /workspace-PromptUGUI
git status --short        # 预期只有 docs~ 下的 spec/plan 未跟踪
git checkout -b feat/common-controls-sample-expansion
```

- [ ] **Step 2: 提交 spec + plan**

```bash
git add docs~/superpowers/specs/2026-06-12-common-controls-sample-expansion-design.md \
        docs~/superpowers/plans/2026-06-12-common-controls-sample-expansion.md
git commit -m "docs: CommonControls 示例扩充 spec + plan"
```

---

### Task 2: 重写 `CommonControls.ui.xml`（改名 + TabBar 四页）

**Files:**
- Rename: `Samples~/CommonControls/Resources/UI/Settings.ui.xml` → `Samples~/CommonControls/Resources/UI/CommonControls.ui.xml`
- 内容整体替换

- [ ] **Step 1: git mv 改名**

```bash
cd /workspace-PromptUGUI
git mv "Samples~/CommonControls/Resources/UI/Settings.ui.xml" \
       "Samples~/CommonControls/Resources/UI/CommonControls.ui.xml"
```

- [ ] **Step 2: 写入新 XML（全文替换）**

`Samples~/CommonControls/Resources/UI/CommonControls.ui.xml`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">

  <!-- ScrollList 行模板 -->
  <Template name="OptionRow">
    <HStack height="32" spacing="8">
      <Text id="label" fontSize="20"/>
      <Frame width="0"/>
    </HStack>
  </Template>

  <!-- Carousel 卡片模板（bg 颜色 / 标题由 C# BindItems 填） -->
  <Template name="DemoCard">
    <Frame>
      <Image id="bg" anchor="stretch" color="#444444"/>
      <Text id="title" anchor="center" fontSize="22" raycastTarget="false"/>
    </Frame>
  </Template>

  <Screen name="CommonControls" reference="640x360" reference.portrait="360x640" scale-mode="pixel">
    <Image anchor="stretch" color="#202020"/>
    <SafeArea anchor="stretch">

      <!-- 顶部固定：标题 + 常驻新手引导按钮 -->
      <HStack anchor="top-stretch" height="44" padding="8" spacing="8">
        <Text fontSize="22">Common Controls Demo</Text>
        <Frame width="0"/>
        <Btn id="tutorialBtn" width="100" height="28" fontSize="16" color="#3B82F6"
             pressedModulate="#bbbbbb">新手引导</Btn>
      </HStack>

      <!-- TabBar 分页（bind= 切页；Tab 是 layout child，只写 width/height） -->
      <TabBar id="tabs" anchor="top-stretch" height="32" margin="44,8,0,8">
        <Tab id="tabForm"    width="84" text="表单输入" fontSize="15" color="#333333"
             selectedColor="#3B82F6" bind="pageForm" isOn="true"/>
        <Tab id="tabDisplay" width="84" text="展示反馈" fontSize="15" color="#333333"
             selectedColor="#3B82F6" bind="pageDisplay"/>
        <Tab id="tabList"    width="84" text="列表轮播" fontSize="15" color="#333333"
             selectedColor="#3B82F6" bind="pageList"/>
        <Tab id="tabModal"   width="84" text="模态提示" fontSize="15" color="#333333"
             selectedColor="#3B82F6" bind="pageModal"/>
      </TabBar>

      <!-- ① 表单输入（原 Settings 内容保留） -->
      <Frame id="pageForm" anchor="stretch" margin="84,8,8,8">
        <VStack anchor="stretch" spacing="12" padding="8">
          <InputField id="username" placeholder="Username" width="240" height="30"/>
          <Toggle id="muteAudio">静音</Toggle>
          <Slider id="masterVol" min="0" max="1" value="0.8"/>
          <Dropdown id="quality"/>
        </VStack>
      </Frame>

      <!-- ② 展示反馈 -->
      <Frame id="pageDisplay" anchor="stretch" margin="84,8,8,8">
        <VStack anchor="stretch" spacing="10" padding="8">
          <Progress id="progress" height="16" value="0.3" bgColor="#222222" fillColor="#3CC3F0"/>
          <HStack height="28" spacing="8">
            <Btn id="progMinus" width="64" fontSize="15" color="#555555" pressedModulate="#bbbbbb">-10%</Btn>
            <Btn id="progPlus"  width="64" fontSize="15" color="#555555" pressedModulate="#bbbbbb">+10%</Btn>
            <Frame width="0"/>
          </HStack>
          <!-- RawImage：纹理由 C# 生成后喂入 -->
          <RawImage id="gradient" height="40"/>
          <!-- Animation：点击按钮自身 pulse（VStack 内包 Animation 是文档认可的模式）
               + 状态可视化：hoverColor / pressedModulate / <Show on="state-hover"> 角标 -->
          <Animation type="pulse" on="click" duration="0.3s">
            <Btn id="pulseBtn" width="120" height="30" color="#3B82F6"
                 hoverColor="#5B9CF8" pressedModulate="#bbbbbb">
              <Text anchor="center" fontSize="15" raycastTarget="false">点我弹跳</Text>
              <Show on="state-hover">
                <Image anchor="top-right" size="10x10" color="#F59E0B" raycastTarget="false"/>
              </Show>
            </Btn>
          </Animation>
          <!-- Markdown：宿主未装 Markdig 时自动降级纯文本 -->
          <Markdown id="md" height="stretch">**Markdown** 控件：支持 *斜体* / `代码` / [链接](https://example.com)</Markdown>
        </VStack>
      </Frame>

      <!-- ③ 列表轮播 -->
      <Frame id="pageList" anchor="stretch" margin="84,8,8,8">
        <VStack anchor="stretch" spacing="10" padding="8">
          <Grid columns="6" cellSize="32x32" spacing="4" height="72">
            <Image color="#EF4444"/><Image color="#F59E0B"/><Image color="#10B981"/>
            <Image color="#3B82F6"/><Image color="#8B5CF6"/><Image color="#EC4899"/>
            <Image color="#F87171"/><Image color="#FBBF24"/><Image color="#34D399"/>
            <Image color="#60A5FA"/><Image color="#A78BFA"/><Image color="#F472B6"/>
          </Grid>
          <Carousel id="banner" height="90" itemTemplate="DemoCard"
                    interval="3" loop="true" transition="0.3"
                    dots="bottom-center" dotColor="#666666" dotSelectedColor="#FFFFFF"/>
          <ScrollList id="list" width="stretch" height="stretch" itemTemplate="OptionRow"
                      spacing="4" padding="8"/>
        </VStack>
      </Frame>

      <!-- ④ 模态提示 -->
      <Frame id="pageModal" anchor="stretch" margin="84,8,8,8">
        <VStack anchor="stretch" spacing="10" padding="8">
          <Btn id="msgBoxBtn"   height="30" fontSize="15" color="#3B82F6" pressedModulate="#bbbbbb">MessageBox</Btn>
          <Btn id="inputBoxBtn" height="30" fontSize="15" color="#3B82F6" pressedModulate="#bbbbbb">InputBox</Btn>
          <Btn id="mdBoxBtn"    height="30" fontSize="15" color="#3B82F6" pressedModulate="#bbbbbb">MarkdownBox</Btn>
          <Btn id="loadingBtn"  height="30" fontSize="15" color="#3B82F6" pressedModulate="#bbbbbb">Loading (2s)</Btn>
          <Btn id="toastBtn"    height="30" fontSize="15" color="#10B981" pressedModulate="#bbbbbb">Toast</Btn>
        </VStack>
      </Frame>

    </SafeArea>
  </Screen>
</PromptUGUI>
```

注意点（执行者按 lint 结果微调，但**不得**违反这些硬规则）：

- VStack/HStack/Grid/TabBar 的子节点**不写** `anchor`/`margin`
- Carousel 卡模板根 `<Frame>` 不写 anchor/margin/size
- `<Markdown height="stretch">`：若 lint/运行抱怨 stretch 不支持，改固定 `height="120"`
- 若 `<Animation>` 作为 VStack 子节点报 width/height 缺失类 warning，给 `<Animation ... width="120" height="30">` 加上与内部 Btn 一致的尺寸

- [ ] **Step 3: 跑 UIXmlLint**

```bash
cd /workspace-PromptUGUI
dotnet run --project .lint/UIXmlLint -- "Samples~/CommonControls/Resources/UI/CommonControls.ui.xml"
```

预期：exit 0，无 error（warning 逐条评估：layout 类 warning 必须修掉）。有 error → 修 XML 再跑，直到干净。

- [ ] **Step 4: Commit**

```bash
git add -A "Samples~/CommonControls/Resources/UI/"
git commit -m "feat(samples): CommonControls.ui.xml 重组为 TabBar 四页全控件橱窗"
```

---

### Task 3: 重写 `CommonControlsRunner.cs`

**Files:**
- Modify: `Samples~/CommonControls/CommonControlsRunner.cs`（全文替换）

- [ ] **Step 1: 写入新 Runner（全文替换）**

```csharp
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
    ///   2. 按 Play；点右上角「新手引导」体验引导（可反复触发）
    /// </summary>
    public sealed class CommonControlsRunner : MonoBehaviour
    {
        async void Start()
        {
            UI.UseResourcesResolver("UI");
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
        }

        // ② 展示反馈：Progress 按钮驱动；RawImage 喂 C# 生成的渐变纹理
        static void BindDisplayPage(IScreen screen)
        {
            var progress = screen.Get<Progress>("progress");
            var value = 0.3f;   // Progress 是只读显示控件，当前值由调用方持有
            screen.Get<Btn>("progMinus").OnClick
                  .Subscribe(_ => progress.Value = value = Mathf.Clamp01(value - 0.1f)).AddTo(screen);
            screen.Get<Btn>("progPlus").OnClick
                  .Subscribe(_ => progress.Value = value = Mathf.Clamp01(value + 0.1f)).AddTo(screen);

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

            var username = screen.Get<PromptUGUI.Controls.InputField>("username");
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
```

实现注意：

- `Slider.Value` 需要 getter（引导第 4 步读 `vol.Value`）。已核实 `Runtime/Controls/Slider.cs:88` `public float Value` 存在；若实际只有 setter，改成订阅 `OnValueChanged` 置位 bool 的写法：步骤前 `var moved = false; var sub = vol.OnValueChanged.Subscribe(_ => moved = true);`，`Advance.When(() => moved)`，步骤后 `sub.Dispose()`。
- `Image.Color` 是 string setter（`Runtime/Controls/Image.cs:46`），直接赋 `"#3B5BA5"`。
- R3 的 `Subscribe(async _ => ...)` 是 async void lambda 当 `Action<T>` 用——示例代码可接受。

- [ ] **Step 2: Commit（编译验证前先留存）**

```bash
cd /workspace-PromptUGUI
git add Samples~/CommonControls/CommonControlsRunner.cs
git commit -m "feat(samples): CommonControlsRunner 四页绑定 + UI.Tutorial 新手引导脚本"
```

---

### Task 4: 宿主工程编译验证（UnityMCP）

**Files:**
- 临时创建: `UnityProjects~/PromptUGUIDev/Assets/Samples/CommonControls/`（**不提交**）

`Samples~/` 不参与包编译，必须把 sample 复制进宿主 Assets 才能编译 + 视觉 QA。

- [ ] **Step 1: 复制 sample 进宿主**

```bash
mkdir -p "/workspace-PromptUGUI/UnityProjects~/PromptUGUIDev/Assets/Samples"
cp -r "/workspace-PromptUGUI/Samples~/CommonControls" \
      "/workspace-PromptUGUI/UnityProjects~/PromptUGUIDev/Assets/Samples/CommonControls"
```

- [ ] **Step 2: 加载 UnityMCP 工具并刷新编译**

先 `ToolSearch(query="select:mcp__UnityMCP__refresh_unity,mcp__UnityMCP__read_console,mcp__UnityMCP__run_tests,mcp__UnityMCP__get_test_job", max_results=4)`（注意必须用全名 select），然后：

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

预期：console 无 error。有编译错误 → 修 `Samples~/` 下源文件 → 重新 cp 覆盖宿主副本 → 再 refresh，直到干净。修复 commit 到 `Samples~/`（`git commit --amend` 或追加 fix commit）。

- [ ] **Step 3: 回归跑 EditMode 全量（保险，无 Runtime 改动预期全绿）**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 至完成。预期：全部 pass（当前基线 1641）。

- [ ] **Step 4: 确认宿主副本不入 git**

```bash
cd /workspace-PromptUGUI
git status --short UnityProjects~ | head    # 副本应为 untracked；保留在工作区给用户视觉 QA，不 add
```

---

### Task 5: package.json 描述更新 + 收尾

**Files:**
- Modify: `package.json`（samples 块 CommonControls 条目）

- [ ] **Step 1: 更新 sample description**

`package.json` 中：

```json
        {
            "displayName": "Common Controls Demo",
            "description": "TabBar 四页全控件橱窗：表单 / 展示反馈 / 列表轮播 / 模态提示 + UI.Tutorial 新手引导",
            "path": "Samples~/CommonControls"
        }
```

- [ ] **Step 2: Commit**

```bash
cd /workspace-PromptUGUI
git add package.json
git commit -m "docs(samples): 更新 CommonControls sample 描述"
```

- [ ] **Step 3: 汇报 + 等用户视觉 QA**

向用户说明：宿主工程 `Assets/Samples/CommonControls` 已就位，场景挂 `CommonControlsRunner` 按 Play 即可；四页逐页看 + 点「新手引导」走完七步。视觉 QA 通过后再走 finishing-a-development-branch（PR）。**不要主动 push / 开 PR / 合 main。**
