# CenteredSlideBox 居中选择器模态 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增第 4 个内置模态 `CenteredSlideBox.Open<T>(items, bind, …) → Awaitable<T>`——把 `fill="false"` peek Carousel 包成「居中卡片选择器」弹窗：泛型数据 + bind 填卡，A+C 选中（点侧卡居中 / 点居中卡确认 / 确认按钮），取消三通道（× / 背景 / ESC）→ `null`，返回选中对象。

**Architecture:** `CenteredSlideBoxRequest<T> : ModalRequest<T>`（照 `MarkdownBoxRequest`）持 items/bind/title/confirmLabel；`Bind(screen, close)` 里 `car.BindItems(Observable.Return(items), …)` 建卡、每卡挂透明 raycast + `PuiButton`（click→居中/确认）、确认按钮→`close(items[car.Current])`、×/背景→`close(null)`；`TryEscape→null`。卡片 `<Template name="Card">` 写在内置 `CenteredSlideBox.ui.xml` **自己文档里**，Carousel 用 v1 `itemTemplate` 解析（同文档，无跨文档）。**Carousel/BuiltinTags/lint/XSD 零改动。**

**Tech Stack:** Unity 6 uGUI, C# 9, R3 (`Observable.Return` / `.Subscribe` / `.AddTo`), Unity `Awaitable`, NUnit + Unity Test Framework, Unity MCP 跑测。

**Spec:** `docs~/superpowers/specs/2026-06-15-centered-slide-box-design.md`（CSB-D1..D11）。建立在 peek Carousel（同分支前序工作）。

**关键约定（务必遵守）：**
- **同分支 `feat/carousel-peek-mode`**（spec 已提交在上面）。**禁止向 main 提交**。
- 每写完 `.cs` → `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认无编译错误，再跑测。
- 跑测：`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["CenteredSlideBoxTests"])` → 轮询 `mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=40)`。**无 `filter` 参数**，用 `group_names`。
- 模态 EditMode 测试**不依赖 Resources**：用 `UI.SourceResolver` 注入假 XML + 把 `CenteredSlideBox.XmlSrc` 指到测试 key（照 `Tests/EditMode/Modals/InputBoxTests.cs`）。Resources 里的真 XML 仅供生产 + UIXmlLint。
- 所有模态测试 `[SetUp]`/`[TearDown]` 调 `UI.ResetForTests()`。
- 每个 Task 末尾 commit；message 末尾加 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。
- **已核实事实（照此实现，别质疑）：**
  - `ModalRequest<TResult>`：`abstract string XmlSrc`、`Action<IScreen> Configure`、`abstract void Bind(IScreen, Action<TResult> close)`、`virtual bool TryEscape(out TResult)`。`UI.Modal.OpenAsync<TResult>(ModalRequest<TResult> req, ModalMode mode, CancellationToken ct) → Awaitable<TResult>`。
  - `IControl` 暴露 `GameObject` / `RectTransform` / `Interactable` / `Get<T>(id)`（`IControl.cs:4-17`）。
  - `Carousel.BindItems<T>(Observable<IReadOnlyList<T>> source, Action<IControl,T> bind)` —— **收 Observable，不是裸 list**；裸 list 用 `R3.Observable.Return((IReadOnlyList<T>)items)` 包。`Carousel.Current`(get)、`GoTo(int, bool animated)` 都有；EditMode 下 `GoTo(animated:true)` 走 instant 分支。
  - `PuiButton`（`Runtime/Controls/Internal/PuiButton.cs`，internal `: Button`）有 `onClick`（uGUI UnityEvent）+ `targetGraphic`；同 Carousel dot 的点击装配。
  - `Btn.SimulateClick()`（internal 测试 seam）；backdrop 点击测试用 `ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler)`（照 `MarkdownBoxTests.Backdrop_pointer_down_closes`）；ESC 用 `TopScreen.RootGameObject.GetComponent<ModalEscapeListener>().FireForTests()`。
  - **backdrop 必须与 panel 同级**（兄弟，backdrop 在前/底层、panel 在后/顶层），**不能嵌套**——否则点 panel 冒泡到 backdrop 的 OnPointerDown 会误关（照 InputBox/MarkdownBox 测试 XML 结构）。

**参考实现（照抄模式）：** `Runtime/Application/Modals/MarkdownBoxRequest.cs`（request + facade + 背景/×/ESC）、`Runtime/Application/Modals/MessageBoxRequest.cs`（Bind + 按钮）、`Tests/EditMode/Modals/InputBoxTests.cs` + `MarkdownBoxTests.cs`（测试夹具）。

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `Runtime/Application/Modals/CenteredSlideBoxRequest.cs` | `CenteredSlideBoxRequest<T> : ModalRequest<T>` + `CenteredSlideBox` facade + `AttachCardClick` | 新建 |
| `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`(+.meta) | backdrop + panel(title/close/Carousel/confirm) + `<Template name="Card">` | 新建 |
| `Tests/EditMode/Modals/CenteredSlideBoxTests.cs` | EditMode：确认/点卡/取消/边界/bind | 新建 |
| `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs` | PlayMode 烟雾 | 新建 |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | `CenteredSlideBox.Open` 用法 | 改 |

---

## Task 1: 模态骨架 + 确认按钮返回居中项

**Files:**
- Create: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`
- Create: `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`
- Test: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`（新建）

- [ ] **Step 1: 写失败测试**（新建 `CenteredSlideBoxTests.cs`）

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PBtn = PromptUGUI.Controls.Btn;

namespace PromptUGUI.Tests.Modals
{
    public class CenteredSlideBoxTests
    {
        private sealed class Lv { public string Id; public string Name; }

        // 测试用模态 XML：backdrop 与 panel **同级**；卡模板写在同文档。
        private const string SlideBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/SlideBox1'>
    <Image id='backdrop' anchor='stretch' color='#000000A0'/>
    <Frame id='panel' anchor='center' size='600x400'>
      <Text id='title' anchor='top-stretch' height='40' align='center'/>
      <Btn  id='close' anchor='top-right' size='32x32'>x</Btn>
      <Carousel id='cards' anchor='stretch' margin='48,8,64,8'
                fill='false' interval='0' itemTemplate='Card'/>
      <Btn  id='confirm' anchor='bottom-center' size='140x40'>OK</Btn>
    </Frame>
    <Template name='Card'>
      <Frame size='160x200'>
        <Image id='cover' anchor='stretch'/>
        <Text  id='name'  anchor='bottom-stretch' height='24' align='center'/>
      </Frame>
    </Template>
  </Screen>
</PromptUGUI>";

        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string> { ["test/SlideBox1"] = SlideBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            CenteredSlideBox.XmlSrc = "test/SlideBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static List<Lv> ThreeLevels() => new()
        {
            new Lv { Id = "a", Name = "Alpha" },
            new Lv { Id = "b", Name = "Bravo" },
            new Lv { Id = "c", Name = "Charlie" },
        };

        private static Carousel Cards() => UI.Modal.TopScreen.Get<Carousel>("cards");

        [Test]
        public void Confirm_Returns_Centered_Item()
        {
            var items = ThreeLevels();
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            {
                Items = items,
                BindCard = (card, lv) => { },
            });
            Cards().GoTo(1, animated: false);                  // 把第 1 项居中
            UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();
            Assert.AreSame(items[1], task.GetAwaiter().GetResult(),
                "confirm returns the centered item");
        }
    }
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])`
预期：编译失败（`CenteredSlideBoxRequest` / `CenteredSlideBox` 不存在）。

- [ ] **Step 3: 实现** —— 新建 `CenteredSlideBoxRequest.cs`

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Application.Modals
{
    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;
        public string XmlSrcOverride;                       // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override void Bind(IScreen screen, Action<T> close)
        {
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            // 取消通道：×（背景 / ESC 在后续 Task 接）
            screen.Get<Btn>("close").OnClick.Subscribe(_ => close(null)).AddTo(screen);

            var car = screen.Get<Carousel>("cards");
            int idx = 0;
            car.BindItems(Observable.Return((IReadOnlyList<T>)Items), (IControl card, T item) =>
            {
                int i = idx++;
                BindCard?.Invoke(card, item);
                // 卡片点击在 Task 3 接；这里先只建卡
            }).AddTo(screen);

            var ok = screen.Get<Btn>("confirm");
            if (!string.IsNullOrEmpty(ConfirmLabel)) ok.Text = ConfirmLabel;
            ok.OnClick.Subscribe(_ =>
            {
                int cur = car.Current;
                if (cur >= 0 && cur < Items.Count) close(Items[cur]);
            }).AddTo(screen);
        }
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        public static UnityEngine.Awaitable<T> Open<T>(
            IReadOnlyList<T> items,
            Action<IControl, T> bind,
            string title = null,
            string confirmLabel = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
            => UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
            {
                Items = items,
                BindCard = bind,
                Title = title,
                ConfirmLabel = confirmLabel,
                Configure = configure,
            }, mode, ct);
    }
}
```

新建 `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`（生产版；backdrop 与 panel **同级**）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="CenteredSlideBox">
    <Image id="backdrop" anchor="stretch" color="#000000A0"/>
    <Frame id="panel" anchor="center" size="720x460">
      <Text id="title" anchor="top-stretch" height="48" align="center" tr="false"/>
      <Btn  id="close" anchor="top-right" size="36x36" margin="6,6,_,_">x</Btn>
      <Carousel id="cards" anchor="stretch" margin="56,16,72,16"
                fill="false" interval="0" loop="true"
                spacing="24" edgeScale="0.82" edgeAlpha="0.45"
                itemTemplate="Card"
                dots="bottom-center" dotColor="#666666" dotSelectedColor="#ffffff"/>
      <Btn id="confirm" anchor="bottom-center" size="160x48" margin="_,_,12,_">OK</Btn>
    </Frame>
    <Template name="Card">
      <Frame size="240x320">
        <Image id="cover" anchor="stretch"/>
        <Text  id="name"  anchor="bottom-stretch" height="40" align="center" tr="false"/>
      </Frame>
    </Template>
  </Screen>
</PromptUGUI>
```

- [ ] **Step 4: 跑测确认通过**

`refresh_unity` → `read_console(types=["error"])`（无错）
`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])` → PASS

- [ ] **Step 5: 跑 UIXmlLint（生产 XML 吃自己的 peek 狗粮）+ commit**

```bash
cd .lint && dotnet run --project UIXmlLint -- ../Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml ; cd ..
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml.meta Tests/EditMode/Modals/CenteredSlideBoxTests.cs
git commit -m "$(printf 'feat(modal): CenteredSlideBox skeleton — confirm returns centered item\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

> `.meta`：Unity 首次导入新资源会生成 `.ui.xml.meta`，刷新后一并 `git add`（若刷新后才出现，本步 add 它）。

---

## Task 2: 取消三通道（× / 背景 / ESC → null）

**Files:**
- Modify: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`
- Test: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`

- [ ] **Step 1: 写失败测试**（追加到 `CenteredSlideBoxTests.cs`；补 usings）

类顶部补：

```csharp
using PromptUGUI.Controls;
using UnityEngine.EventSystems;
using PImage = PromptUGUI.Controls.Image;
```

测试：

```csharp
[Test]
public void Click_Close_Returns_Null()
{
    var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = ThreeLevels(), BindCard = (c, l) => { } });
    UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
    Assert.IsNull(task.GetAwaiter().GetResult());
}

[Test]
public void Backdrop_PointerDown_Returns_Null()
{
    var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = ThreeLevels(), BindCard = (c, l) => { } });
    var backdrop = UI.Modal.TopScreen.Get<PImage>("backdrop");
    ExecuteEvents.Execute(backdrop.GameObject,
        new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler);
    Assert.IsNull(task.GetAwaiter().GetResult());
}

[Test]
public void Escape_Via_Listener_Returns_Null_And_Closes()
{
    var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = ThreeLevels(), BindCard = (c, l) => { } });
    var listener = UI.Modal.TopScreen.RootGameObject.GetComponent<ModalEscapeListener>();
    Assert.IsNotNull(listener);
    listener.FireForTests();
    Assert.IsNull(task.GetAwaiter().GetResult());
    Assert.IsFalse(UI.Modal.IsAnyOpen);
}

[Test]
public void TryEscape_Returns_Null_And_True()
{
    var req = new CenteredSlideBoxRequest<Lv> { Items = ThreeLevels() };
    Assert.IsTrue(req.TryEscape(out var r));
    Assert.IsNull(r);
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])`
预期：`Backdrop_*` FAIL（背景未接 → 点它不关）、`Escape_*` / `TryEscape_*` FAIL（`TryEscape` 默认 return false → ESC 不关、out r 默认）。`Click_Close_*` 已 PASS（Task 1 接了 ×）。

- [ ] **Step 3: 实现** —— `CenteredSlideBoxRequest.cs`

在 `Bind` 里 `close` 的 × 订阅旁，加背景订阅：

```csharp
            screen.Get<Btn>("close").OnClick.Subscribe(_ => close(null)).AddTo(screen);
            screen.Get<UnityImageControl>("backdrop")    // 见下：用 PromptUGUI.Controls.Image
                .OnPointerDown.Subscribe(_ => close(null)).AddTo(screen);
```

（实际写法：`screen.Get<PromptUGUI.Controls.Image>("backdrop").OnPointerDown.Subscribe(_ => close(null)).AddTo(screen);`——`PromptUGUI.Controls.Image` 有 `OnPointerDown`，同 MarkdownBox。）

并在类里加 `TryEscape` override：

```csharp
        public override bool TryEscape(out T result) { result = null; return true; }
```

- [ ] **Step 4: 跑测确认通过**

`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])` → 全 PASS

- [ ] **Step 5: commit**

```bash
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs Tests/EditMode/Modals/CenteredSlideBoxTests.cs
git commit -m "$(printf 'feat(modal): CenteredSlideBox cancel via close/backdrop/ESC -> null\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: A+C 卡片点击（点居中卡确认 / 点侧卡居中）

**Files:**
- Modify: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`
- Test: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`

- [ ] **Step 1: 写失败测试**（追加；补 `using PromptUGUI.Controls.Internal;`）

辅助 + 测试：

```csharp
// 取卡 i 的 PuiButton（AttachCardClick 挂在卡根）。
private static PromptUGUI.Controls.Internal.PuiButton CardButton(int i)
    => (PromptUGUI.Controls.Internal.PuiButton)
       UI.Modal.TopScreen.Get<Carousel>("cards").GameObject
         .transform.Find("Viewport/Strip").GetChild(i)
         .GetComponent<PromptUGUI.Controls.Internal.PuiButton>();

[Test]
public void Tap_Centered_Card_Returns_It()
{
    var items = ThreeLevels();
    var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = items, BindCard = (c, l) => { } });
    // current 默认 0；点第 0 张（居中）→ 确认返回它
    CardButton(0).onClick.Invoke();
    Assert.AreSame(items[0], task.GetAwaiter().GetResult());
}

[Test]
public void Tap_Side_Card_Centers_It_Without_Returning()
{
    var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = ThreeLevels(), BindCard = (c, l) => { } });
    var car = Cards();
    Assert.AreEqual(0, car.Current);
    CardButton(2).onClick.Invoke();        // 点侧卡 2 → 居中它
    Assert.AreEqual(2, car.Current, "side-card tap centers it");
    Assert.IsTrue(UI.Modal.IsAnyOpen, "side-card tap must NOT confirm/close");
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])`
预期：`Tap_*` FAIL（`CardButton(i)` 为 null —— 还没给卡挂 PuiButton；`GetComponent` 返回 null → 空引用/断言失败）。

- [ ] **Step 3: 实现** —— 给卡装配点击

在 `Bind` 的 BindItems 回调里，`BindCard?.Invoke` 之后加 `AttachCardClick(card, i, car, close);`：

```csharp
            car.BindItems(Observable.Return((IReadOnlyList<T>)Items), (IControl card, T item) =>
            {
                int i = idx++;
                BindCard?.Invoke(card, item);
                AttachCardClick(card, i, car, close);
            }).AddTo(screen);
```

并加方法（A+C；透明 raycast + PuiButton，click 语义、拖动冒泡给 CarouselView）：

```csharp
        // 每张卡挂透明 raycast catcher + PuiButton：click(非拖动) → 居中或确认。
        // 点居中卡 = 确认；点侧卡 = GoTo 居中。拖动不被 PuiButton 处理 → 冒泡给 CarouselView。
        private void AttachCardClick(IControl card, int i, Carousel car, Action<T> close)
        {
            var go = card.GameObject;
            var img = go.GetComponent<UnityImage>() ?? go.AddComponent<UnityImage>();
            img.color = new Color(0f, 0f, 0f, 0f);   // 透明，仅 raycast
            img.raycastTarget = true;
            var btn = go.AddComponent<PuiButton>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (car.Current == i) close(Items[i]);     // 点居中卡 = 确认
                else car.GoTo(i, animated: true);          // 点侧卡 = 居中
            });
        }
```

- [ ] **Step 4: 跑测确认通过**

`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])` → 全 PASS（含 Task 1/2 的）

- [ ] **Step 5: commit**

```bash
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs Tests/EditMode/Modals/CenteredSlideBoxTests.cs
git commit -m "$(printf 'feat(modal): CenteredSlideBox A+C card tap (center/confirm)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: chrome 细节（title 隐藏 / confirmLabel / 空列表禁确认 / bind 填槽 / configure）

**Files:**
- Modify: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`
- Test: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`

- [ ] **Step 1: 写失败测试**（追加；补 `using PText = PromptUGUI.Controls.Text;` 和 `using PromptUGUI.Controls;` 若缺）

```csharp
[Test]
public void Null_Title_Hides_Title_Node()
{
    UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = ThreeLevels(), BindCard = (c, l) => { }, Title = null });
    Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
}

[Test]
public void ConfirmLabel_Overrides_Button_Text()
{
    UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = ThreeLevels(), BindCard = (c, l) => { }, ConfirmLabel = "开始" });
    Assert.AreEqual("开始", UI.Modal.TopScreen.Get<PBtn>("confirm").Text);
}

[Test]
public void Empty_Items_Disables_Confirm()
{
    UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
        { Items = new List<Lv>(), BindCard = (c, l) => { } });
    Assert.IsFalse(UI.Modal.TopScreen.Get<PBtn>("confirm").Interactable);
}

[Test]
public void Bind_Fills_Card_Slots()
{
    var items = ThreeLevels();
    UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
    {
        Items = items,
        BindCard = (card, lv) => card.Get<PText>("name").TextValue = lv.Name,
    });
    var card0 = (RectTransform)Cards().GameObject.transform.Find("Viewport/Strip").GetChild(0);
    Assert.AreEqual("Alpha", card0.GetComponentInChildren<TMPro.TMP_Text>().text);
}

[Test]
public void Configure_Runs_After_Bind()
{
    var ran = false;
    UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
    {
        Items = ThreeLevels(), BindCard = (c, l) => { },
        Configure = _ => ran = true,
    });
    Assert.IsTrue(ran, "Configure hook runs (invoked by the modal pump after Bind)");
}
```

（补 `using TMPro;`。）

- [ ] **Step 2: 跑测确认失败**

预期：`Empty_Items_Disables_Confirm` FAIL（空列表没禁确认）；其余可能已过（title 隐藏 + confirmLabel 在 Task 1 已实现；`Configure` 由基类管线跑，应已过；`Bind_Fills_Card_Slots` 应已过）。**只为没过的写实现**——若全过说明 Task 1 已覆盖，仅 `Empty_*` 要补。

- [ ] **Step 3: 实现** —— 空列表禁确认

在 `Bind` 里 `var ok = screen.Get<Btn>("confirm");` 之后、订阅之前加：

```csharp
            if (Items == null || Items.Count == 0) ok.Interactable = false;
```

（title 隐藏、confirmLabel、bind 填槽、configure 已在 Task 1 / 基类管线中实现；本步只补空列表门。）

- [ ] **Step 4: 跑测确认通过**

`run_tests(EditMode, group_names=["CenteredSlideBoxTests"])` → 全 PASS

- [ ] **Step 5: commit**

```bash
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs Tests/EditMode/Modals/CenteredSlideBoxTests.cs
git commit -m "$(printf 'feat(modal): CenteredSlideBox chrome — title/confirmLabel/empty-disable/bind\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: PlayMode 烟雾

**Files:**
- Test: `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs`（新建）

- [ ] **Step 1: 写测试**（PlayMode；同步驱动，不需真定时）

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PBtn = PromptUGUI.Controls.Btn;

namespace PromptUGUI.Tests.PlayMode.Modals
{
    public class CenteredSlideBoxPlayTests
    {
        private sealed class Lv { public string Id; public string Name; }

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/SlideBoxP'>
    <Image id='backdrop' anchor='stretch' color='#000000A0'/>
    <Frame id='panel' anchor='center' size='600x400'>
      <Text id='title' anchor='top-stretch' height='40' align='center'/>
      <Btn  id='close' anchor='top-right' size='32x32'>x</Btn>
      <Carousel id='cards' anchor='stretch' margin='48,8,64,8'
                fill='false' interval='0' itemTemplate='Card'/>
      <Btn  id='confirm' anchor='bottom-center' size='140x40'>OK</Btn>
    </Frame>
    <Template name='Card'>
      <Frame size='160x200'><Image id='cover' anchor='stretch'/>
        <Text id='name' anchor='bottom-stretch' height='24'/></Frame>
    </Template>
  </Screen>
</PromptUGUI>";

        [SetUp] public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["test/SlideBoxP"] = Xml };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            CenteredSlideBox.XmlSrc = "test/SlideBoxP";
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Open_GoTo_Confirm_Returns_Item_NoCrash()
        {
            var items = new List<Lv> { new() { Id = "a" }, new() { Id = "b" }, new() { Id = "c" } };
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
                { Items = items, BindCard = (c, l) => { } });
            UI.Modal.TopScreen.Get<Carousel>("cards").GoTo(2, animated: false);
            UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();
            Assert.AreSame(items[2], task.GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: 跑测**

`run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["CenteredSlideBoxPlayTests"], init_timeout=120000)` → 轮询 → PASS

- [ ] **Step 3: commit**

```bash
git add Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs
git commit -m "$(printf 'test(modal): CenteredSlideBox PlayMode smoke\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 6: C# SKILL 文档

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: 在模态小节加 `CenteredSlideBox`**（找到 InputBox/MarkdownBox 的模态段，照风格加；匹配文件现有语言）

加一段：

```markdown
### CenteredSlideBox（居中选择器模态）

居中卡片选择弹窗（内部是 `fill="false"` peek Carousel）。泛型数据 + bind 填卡（同 `Carousel.BindItems`），await 返回**选中的对象**（取消 = `null`）。

​```csharp
record Level(string Id, string Name, Sprite Cover);

var picked = await CenteredSlideBox.Open(
    levels,
    bind: (card, lv) => { card.Get<Text>("name").TextValue = lv.Name;
                          card.Get<Image>("cover").Sprite   = lv.Cover; },
    title: "选择关卡");
if (picked != null) StartLevel(picked);   // 要 id 就 picked.Id
​```

- 选中交互：拖动 / dots 浏览；**点侧卡居中**、**点居中卡确认**、**确认按钮**确认居中卡；**× / 点背景 / ESC** 取消 → `null`。
- 卡片槽位由内置 `CenteredSlideBox.ui.xml` 的 `<Template name="Card">` 决定（默认 `cover`(Image) + `name`(Text)）；bind 填你用到的槽。
- 换一种卡片样式：`CenteredSlideBox.XmlSrc = "你的.ui";`（保持 id 契约 `backdrop/panel/title/close/confirm/cards` + 卡槽）；或抄 facade 传不同 `XmlSrcOverride`。
- `where T : class`（取消用 `null` 哨兵）；`title`/`confirmLabel`/`mode`/`configure`/`ct` 同其它模态。
```

- [ ] **Step 2: commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "$(printf 'docs: document CenteredSlideBox.Open in C# skill\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## 最终验证（全量）

- [ ] `refresh_unity(mode="force", scope="all")` → `read_console(types=["error"])` 无错
- [ ] `run_tests(EditMode, assembly_names=["PromptUGUI.Tests.EditMode"])` 全绿（含 `CenteredSlideBoxTests` + 既有 carousel-peek 全套）
- [ ] `run_tests(EditMode, assembly_names=["PromptUGUI.Tests.EditorOnly"])` 全绿
- [ ] `run_tests(PlayMode, assembly_names=["PromptUGUI.Tests.PlayMode"])` 全绿（含 `CenteredSlideBoxPlayTests`）
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 干净
- [ ] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 无 error（生产 CenteredSlideBox.ui.xml 含 fill=false + 带 size 卡 → 验证 peek lint 门控）
- [ ] 视觉 QA（用户）：宿主工程 `CenteredSlideBox.Open` 关卡选择——居中放大/淡出、拖动、点侧卡居中、点居中卡/确认返回、×/背景/ESC 取消

---

## Self-Review（写计划时已核对）

- **Spec 覆盖**：CSB-D1 模态形态→T1；D2 泛型+bind→T1（`BindItems(Observable.Return)`）；D3 返回 T/null→T1（confirm）+T2（cancel）；D4 卡模板进 XmlSrc 同文档→T1（XML + Carousel itemTemplate）；D5 cover/name 槽→T4（bind 填槽测试）；D6 换皮 XmlSrc/XmlSrcOverride→T1（字段）+T6（文档）；D7 A+C→T3；D8 透明 raycast+PuiButton→T3；D9 取消三通道→T2；D10 title/confirmLabel/configure→T1+T4；D11 空列表禁确认→T4。
- **类型一致**：`CenteredSlideBoxRequest<T> where T:class`（Items/BindCard/Title/ConfirmLabel/XmlSrcOverride/XmlSrc/Bind/TryEscape/AttachCardClick）+ facade `CenteredSlideBox.Open<T>`/`XmlSrc`——各 Task 引用一致。`BindItems` 用 `Observable.Return`、backdrop 用 `PromptUGUI.Controls.Image.OnPointerDown`、card 用 `PuiButton.onClick`——均已核实。
- **占位**：无 TBD；T4 Step 2 的「若已过仅补 Empty_*」是真实 TDD 观察（title/confirmLabel/configure/bind 在 T1+基类已实现），非占位。
- **结构**：backdrop 与 panel 同级（防误关）已在 T1 XML + 约定块强调。
