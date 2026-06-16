# CenteredSlideBox 多按钮 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给内置模态 `CenteredSlideBox` 加多个自定义动作按钮，多按钮返回具名 `SlideSelection<T>`（选中卡 + 点击按钮 key），按钮 ≥2 时去掉「点居中卡=确认」捷径；单按钮 API 完全不变。

**Architecture:** 抽出共享静态 `CenteredSlideBoxBinder`（所有 Bind 逻辑 + 按钮槽探测 + 超量报错），由两个瘦 request 委托：`CenteredSlideBoxRequest<T> : ModalRequest<T>`（单按钮，原样返回 `T`）与新 `CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>>`（多按钮）。binder 用 `onConfirm(item,key)`/`onCancel()` 回调抽掉结果类型差异。默认皮肤底部单按钮换成 `<HStack>` + `button0..button4` 槽位行。

**Tech Stack:** Unity 6 / uGUI / R3 (Cysharp Observable) / Unity `Awaitable`（非 Task）/ NUnit (EditMode + PlayMode) / Unity MCP 跑测试 / `dotnet format` + UIXmlLint 跑 lint。

**Spec:** [`docs~/superpowers/specs/2026-06-16-centered-slide-box-multi-button-design.md`](../specs/2026-06-16-centered-slide-box-multi-button-design.md)（CSB-D12..D18）。

**前置约束:**
- 已在分支 `feat/centered-slide-box-multi-button`（**禁止提交 main**）。
- 每改 C#/XML 后：先 `refresh_unity`，再 `read_console` 查编译错误，**再** `run_tests`。`run_tests` 异步→拿 `job_id`→轮询 `get_test_job`。
- 若 Unity MCP 不可用：尝试重连或让用户重启 MCP（见 CLAUDE.md）。

**Unity MCP 工具加载（每个新 session 先做一次）:**
```
ToolSearch(query="select:mcp__UnityMCP__run_tests,mcp__UnityMCP__get_test_job,mcp__UnityMCP__refresh_unity,mcp__UnityMCP__read_console", max_results=4)
```

---

### Task 1: `SlideSelection<T>` 返回结构体

**Files:**
- Create: `Runtime/Application/Modals/SlideSelection.cs` (+ `.meta` — Unity 生成)
- Test: `Tests/EditMode/Modals/SlideSelectionTests.cs` (+ `.meta`)

- [ ] **Step 1: 写失败测试**

Create `Tests/EditMode/Modals/SlideSelectionTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class SlideSelectionTests
    {
        private sealed class Item { public string Id; }

        [Test]
        public void Holds_Item_And_Button()
        {
            var it = new Item { Id = "x" };
            var sel = new SlideSelection<Item>(it, "play");
            Assert.AreSame(it, sel.Item);
            Assert.AreEqual("play", sel.Button);
            Assert.IsFalse(sel.Cancelled);
        }

        [Test]
        public void Default_Is_Cancelled()
        {
            SlideSelection<Item> sel = default;
            Assert.IsNull(sel.Item);
            Assert.IsNull(sel.Button);
            Assert.IsTrue(sel.Cancelled);
        }

        [Test]
        public void Deconstructs()
        {
            var it = new Item { Id = "x" };
            var (item, button) = new SlideSelection<Item>(it, "hard");
            Assert.AreSame(it, item);
            Assert.AreEqual("hard", button);
        }

        [Test]
        public void Cancelled_Tracks_Button_Null_Only()
        {
            // 单按钮路径内部会出现 (item, null)；Cancelled 以 Button==null 为准（item 设了也算）
            var sel = new SlideSelection<Item>(new Item(), null);
            Assert.IsTrue(sel.Cancelled);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败（编译红：类型不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `The type or namespace name 'SlideSelection<>' could not be found`。

- [ ] **Step 3: 写实现**

Create `Runtime/Application/Modals/SlideSelection.cs`:

```csharp
namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// 多按钮 <c>CenteredSlideBox.Open</c> 的返回值：选中的卡 + 点击的按钮 key。
    /// 取消（×/背景/ESC）→ <c>default</c>：Item=null, Button=null, Cancelled=true。
    /// </summary>
    public readonly struct SlideSelection<T> where T : class
    {
        public readonly T Item;         // 选中的对象；取消时 null
        public readonly string Button;  // 点的按钮 key；取消时 null

        public SlideSelection(T item, string button) { Item = item; Button = button; }

        public bool Cancelled => Button == null;
        public void Deconstruct(out T item, out string button) { item = Item; button = Button; }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["SlideSelectionTests"])
mcp__UnityMCP__get_test_job(job_id="<返回的 id>")
```
Expected: 4/4 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Application/Modals/SlideSelection.cs Runtime/Application/Modals/SlideSelection.cs.meta \
        Tests/EditMode/Modals/SlideSelectionTests.cs Tests/EditMode/Modals/SlideSelectionTests.cs.meta
git commit -m "feat: SlideSelection<T> 多按钮返回结构体 (CSB-D12)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: 抽共享 binder + 单按钮委托 + 默认皮肤按钮行（重构，回归网兜底）

把现有 `CenteredSlideBoxRequest<T>` 的 Bind 逻辑搬进共享 `CenteredSlideBoxBinder`（顺带实现多按钮槽探测/超量报错/autoConfirm 条件化——这些分支由 Task 3 的多按钮测试验证）。单按钮 request 改为委托 binder，**基类 / 字段 / 返回 `T` 全不变**。默认皮肤 `confirm` → `<HStack>` + `button0..button4`。现有单按钮测试只改 `confirm`→`button0` id，作为重构回归网。

**Files:**
- Modify: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`（整体重写为下方内容）
- Modify: `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`（按钮行）
- Modify: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`（XML const + 4 处 id）
- Modify: `Tests/EditMode/Modals/CenteredSlideBoxDefaultSkinTests.cs`（3 处 id）

- [ ] **Step 1: 重写 `CenteredSlideBoxRequest.cs`（binder + 单按钮 request + facade）**

把整个文件替换为：

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Application.Modals
{
    // 共享 Bind 逻辑：单/多按钮 request 都委托这里。onConfirm/onCancel 抽掉结果类型差异。
    internal static class CenteredSlideBoxBinder
    {
        // buttons 至少 1 个（两个 request 各自保证）。
        public static void Bind<T>(
            IScreen screen, IReadOnlyList<T> items, Action<IControl, T> bindCard, string title,
            IReadOnlyList<(string label, string key)> buttons, string xmlSrcForError,
            Action<T, string> onConfirm, Action onCancel) where T : class
        {
            // —— title ——
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = title;

            // —— 取消三通道（CSB-D9）——
            screen.Get<Btn>("close").OnClick.Subscribe(_ => onCancel()).AddTo(screen);
            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => onCancel()).AddTo(screen);

            items ??= Array.Empty<T>();
            bool autoConfirm = buttons.Count == 1;                  // CSB-D16
            string soleKey = autoConfirm ? buttons[0].key : null;

            // —— carousel + 卡 ——
            var car = screen.Get<Carousel>("cards");
            int idx = 0;
            car.BindItems(
                Observable.Return(items),
                (IControl card, T item) =>
                {
                    int i = idx++;
                    bindCard?.Invoke(card, item);
                    AttachCardClick(card, i, car, items, onConfirm, autoConfirm, soleKey);
                }).AddTo(screen);

            // —— 探测皮肤按钮槽（CSB-D17）——
            var slots = new List<Btn>();
            for (int i = 0; ; i++)
            {
                try { slots.Add(screen.Get<Btn>($"button{i}")); }
                catch (KeyNotFoundException) { break; }
            }
            if (buttons.Count > slots.Count)
                throw new InvalidOperationException(
                    $"CenteredSlideBox skin '{xmlSrcForError}' provides {slots.Count} button slot(s) but " +
                    $"{buttons.Count} buttons were passed; override XmlSrc with more 'button{{i}}' slots.");

            // —— 映射 buttons[i] → slot i，隐藏多余槽 ——
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i >= buttons.Count) { slot.GameObject.SetActive(false); continue; }
                var (label, key) = buttons[i];
                if (!string.IsNullOrEmpty(label)) slot.Text = label;   // null/空 → 保留皮肤默认（button0 的 "OK"）
                if (items.Count == 0) slot.Interactable = false;       // CSB-D11
                slot.OnClick.Subscribe(_ =>
                {
                    int cur = car.Current;
                    if (cur >= 0 && cur < items.Count) onConfirm(items[cur], key);
                }).AddTo(screen);
            }
        }

        // 每张卡：透明 raycast catcher + PuiButton。click（非拖动）→ 居中导航或（仅单按钮）确认。
        private static void AttachCardClick<T>(IControl card, int i, Carousel car, IReadOnlyList<T> items,
            Action<T, string> onConfirm, bool autoConfirm, string soleKey) where T : class
        {
            var go = card.GameObject;
            var img = go.GetComponent<UnityImage>() ?? go.AddComponent<UnityImage>();
            img.color = new UnityEngine.Color(0f, 0f, 0f, 0f);   // 透明，仅 raycast
            img.raycastTarget = true;
            var btn = go.AddComponent<PuiButton>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (car.Current == i) { if (autoConfirm) onConfirm(items[i], soleKey); }   // 单按钮：点居中卡=确认；多按钮：无操作
                else car.GoTo(i, animated: true);                                          // 点侧卡=居中
            });
        }
    }

    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;                 // 单个按钮的 label（空→皮肤默认 "OK"）
        public string XmlSrcOverride;               // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out T result) { result = null; return true; }

        public override void Bind(IScreen screen, Action<T> close)
            => CenteredSlideBoxBinder.Bind(screen, Items, BindCard, Title,
                   new[] { (ConfirmLabel, (string)null) }, XmlSrc,     // 1 个隐式按钮；key 忽略
                   onConfirm: (item, _) => close(item),
                   onCancel: () => close(null));
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        // 单按钮 → 返回选中对象 / null（向后兼容，非 async 直传）。
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

- [ ] **Step 2: 改默认皮肤按钮行**

In `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`, replace:

```xml
      <Btn id="confirm" anchor="bottom-center" size="160x48" margin="_,_,12,_">OK</Btn>
```

with:

```xml
      <HStack id="buttons" anchor="bottom-center" height="48" margin="_,_,16,_" spacing="16">
        <Btn id="button0" size="160x48">OK</Btn>
        <Btn id="button1" size="160x48"/>
        <Btn id="button2" size="160x48"/>
        <Btn id="button3" size="160x48"/>
        <Btn id="button4" size="160x48"/>
      </HStack>
```

- [ ] **Step 3: 改现有 EditMode 测试 XML const + id（`CenteredSlideBoxTests.cs`）**

In `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`, replace in the `SlideBoxXml` const:

```xml
      <Btn  id='confirm' anchor='bottom-center' size='140x40'>OK</Btn>
```

with（**3 槽**，供多按钮 + 超量测试用）:

```xml
      <HStack id='buttons' anchor='bottom-center' height='40' spacing='8'>
        <Btn id='button0' size='140x40'>OK</Btn>
        <Btn id='button1' size='140x40'/>
        <Btn id='button2' size='140x40'/>
      </HStack>
```

然后把这 4 处 `Get<PBtn>("confirm")` 改成 `Get<PBtn>("button0")`：
- `Confirm_Returns_Centered_Item`（`UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();`）
- `ConfirmLabel_Overrides_Button_Text`（`UI.Modal.TopScreen.Get<PBtn>("confirm").GameObject.GetComponentInChildren<TMP_Text>().text`）
- `Empty_Items_Disables_Confirm`（`UI.Modal.TopScreen.Get<PBtn>("confirm").Interactable`）
- `Single_Item_Confirm_Returns_It`（`UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();`）

- [ ] **Step 4: 改 `CenteredSlideBoxDefaultSkinTests.cs` 的 3 处 id**

In `Tests/EditMode/Modals/CenteredSlideBoxDefaultSkinTests.cs`, 把 3 处 `Get<PBtn>("confirm")` 改成 `Get<PBtn>("button0")`（`Open_With_Default_Skin_Loads_And_Confirms` / `Default_Skin_Pixel_Mode_Honors_Reference_Not_Error_Fallback` / `Default_Skin_Does_Not_Loop` 各一处）。

- [ ] **Step 5: 刷新 + 查编译 + 跑回归（两套）确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["CenteredSlideBoxTests"])
mcp__UnityMCP__get_test_job(job_id="<id>")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["CenteredSlideBoxDefaultSkinTests"])
mcp__UnityMCP__get_test_job(job_id="<id>")
```
Expected: 两套全 PASS（`CenteredSlideBoxTests` 13 + `CenteredSlideBoxDefaultSkinTests` 3）。单按钮行为零变化 = 重构正确。

- [ ] **Step 6: lint XML + 提交**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml
```
Expected: exit 0（无 layout-group 非法属性等）。

```bash
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs \
        Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml \
        Tests/EditMode/Modals/CenteredSlideBoxTests.cs \
        Tests/EditMode/Modals/CenteredSlideBoxDefaultSkinTests.cs
git commit -m "refactor: CenteredSlideBox 抽共享 binder + 按钮行皮肤 (CSB-D15/D18)

confirm->button0..button4 HStack; 单按钮 request 委托 binder, 返回 T 不变.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: 多按钮 request + facade 重载（TDD）

**Files:**
- Modify: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`（加 `CenteredSlideBoxMultiRequest<T>` + facade ② 重载）
- Modify: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`（加多按钮用例）

- [ ] **Step 1: 写失败测试（编译红：`CenteredSlideBoxMultiRequest` / facade ② 不存在）**

在 `CenteredSlideBoxTests.cs` 的 class 内追加（用 `private static List<Lv> ThreeLevels()`、`Cards()`、`CardButton(int)` 现成 helper）:

```csharp
        [Test]
        public void Multi_Button_Click_Returns_Item_And_Key()
        {
            var items = ThreeLevels();
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<Lv>
            {
                Items = items, BindCard = (c, l) => { },
                Buttons = new[] { ("A", "a"), ("B", "b") },
            });
            Cards().GoTo(1, animated: false);
            UI.Modal.TopScreen.Get<PBtn>("button1").SimulateClick();
            var sel = task.GetAwaiter().GetResult();
            Assert.AreSame(items[1], sel.Item);
            Assert.AreEqual("b", sel.Button);
        }

        [Test]
        public void Multi_Button_Tap_Centered_Card_Is_NoOp()
        {
            UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<Lv>
            {
                Items = ThreeLevels(), BindCard = (c, l) => { },
                Buttons = new[] { ("A", "a"), ("B", "b") },
            });
            var car = Cards();
            Assert.AreEqual(0, car.Current);
            CardButton(0).onClick.Invoke();                 // 多按钮点居中卡 → 无操作
            Assert.AreEqual(0, car.Current);
            Assert.IsTrue(UI.Modal.IsAnyOpen, "多按钮时点居中卡不该确认/关闭");
        }

        [Test]
        public void Multi_Button_Cancel_Returns_Cancelled()
        {
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<Lv>
            {
                Items = ThreeLevels(), BindCard = (c, l) => { },
                Buttons = new[] { ("A", "a"), ("B", "b") },
            });
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            var sel = task.GetAwaiter().GetResult();
            Assert.IsTrue(sel.Cancelled);
            Assert.IsNull(sel.Item);
            Assert.IsNull(sel.Button);
        }

        [Test]
        public void Multi_Button_Hides_Unused_Slots()
        {
            UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<Lv>
            {
                Items = ThreeLevels(), BindCard = (c, l) => { },
                Buttons = new[] { ("A", "a"), ("B", "b") },   // 2 个 → button2 隐藏
            });
            var top = UI.Modal.TopScreen;
            Assert.IsTrue(top.Get<PBtn>("button0").GameObject.activeSelf);
            Assert.IsTrue(top.Get<PBtn>("button1").GameObject.activeSelf);
            Assert.IsFalse(top.Get<PBtn>("button2").GameObject.activeSelf);
            Assert.AreEqual("A", top.Get<PBtn>("button0").GameObject.GetComponentInChildren<TMP_Text>().text);
            Assert.AreEqual("B", top.Get<PBtn>("button1").GameObject.GetComponentInChildren<TMP_Text>().text);
        }

        [Test]
        public void Multi_Button_Over_Count_Throws()
        {
            // 测试 XML 只有 3 槽；传 4 个 → binder 抛 InvalidOperationException（faulted awaitable，见 spec §7）
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<Lv>
            {
                Items = ThreeLevels(), BindCard = (c, l) => { },
                Buttons = new[] { ("A", "a"), ("B", "b"), ("C", "c"), ("D", "d") },
            });
            Assert.Throws<System.InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void Multi_Button_Empty_List_Throws_ArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() =>
                CenteredSlideBox.Open(ThreeLevels(), (c, l) => { },
                    buttons: System.Array.Empty<(string, string)>()));
        }

        [Test]
        public void Multi_Button_Facade_Returns_Key()
        {
            var items = ThreeLevels();
            var task = CenteredSlideBox.Open(items, (c, l) => { },
                buttons: new[] { ("A", "a"), ("B", "b") });
            UI.Modal.TopScreen.Get<PBtn>("button0").SimulateClick();
            var sel = task.GetAwaiter().GetResult();
            Assert.AreSame(items[0], sel.Item);
            Assert.AreEqual("a", sel.Button);
        }
```

- [ ] **Step 2: 跑确认红（编译失败）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `'CenteredSlideBoxMultiRequest<>' could not be found` + facade ② 重载缺失（`Open(..., buttons:)`）。

- [ ] **Step 3: 实现多按钮 request + facade 重载**

In `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`, 在 `CenteredSlideBoxRequest<T>` 类之后、`public static class CenteredSlideBox` 之前插入：

```csharp
    // 多按钮：返回 SlideSelection<T>（选中卡 + 按钮 key）。
    public sealed class CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public IReadOnlyList<(string label, string key)> Buttons;   // facade 保证非空
        public string XmlSrcOverride;

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out SlideSelection<T> result) { result = default; return true; }

        public override void Bind(IScreen screen, Action<SlideSelection<T>> close)
            => CenteredSlideBoxBinder.Bind(screen, Items, BindCard, Title, Buttons, XmlSrc,
                   onConfirm: (item, key) => close(new SlideSelection<T>(item, key)),
                   onCancel: () => close(default));
    }
```

并在 `public static class CenteredSlideBox` 内（单按钮 `Open` 之后）追加 facade ② 重载：

```csharp
        // 多按钮 → 返回 (选中对象, 按钮 key)。buttons 必填且非空。
        public static UnityEngine.Awaitable<SlideSelection<T>> Open<T>(
            IReadOnlyList<T> items,
            Action<IControl, T> bind,
            IEnumerable<(string label, string key)> buttons,
            string title = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
        {
            var list = new List<(string label, string key)>(
                buttons ?? throw new ArgumentNullException(nameof(buttons)));
            if (list.Count == 0)
                throw new ArgumentException("buttons must be non-empty", nameof(buttons));
            return UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<T>
            {
                Items = items,
                BindCard = bind,
                Title = title,
                Buttons = list,
                Configure = configure,
            }, mode, ct);
        }
```

- [ ] **Step 4: 跑确认全绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["CenteredSlideBoxTests"])
mcp__UnityMCP__get_test_job(job_id="<id>")
```
Expected: `CenteredSlideBoxTests` 全 PASS（13 旧 + 7 新 = 20）。

> 若 `Multi_Button_Over_Count_Throws` 报「未抛 InvalidOperationException」：检查 `UI.Modal` 是否把 RunBind 异常包了一层（spec §7 说应为 faulted awaitable）。若实际是被包成别的异常类型，把断言改成 `Assert.Throws<System.Exception>` 并 `StringAssert.Contains("button slot", ex.Message)`。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs Tests/EditMode/Modals/CenteredSlideBoxTests.cs
git commit -m "feat: CenteredSlideBox 多按钮重载 + CenteredSlideBoxMultiRequest (CSB-D12..D17)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: PlayMode 烟雾（单按钮 id 改 + 多按钮）

**Files:**
- Modify: `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs`

- [ ] **Step 1: 改 XML const + 现有单按钮 id，加多按钮烟雾**

In `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs`：

(a) `Xml` const 里把
```xml
      <Btn  id='confirm' anchor='bottom-center' size='140x40'>OK</Btn>
```
换成
```xml
      <HStack id='buttons' anchor='bottom-center' height='40' spacing='8'>
        <Btn id='button0' size='140x40'>OK</Btn>
        <Btn id='button1' size='140x40'/>
        <Btn id='button2' size='140x40'/>
      </HStack>
```

(b) `Open_GoTo_Confirm_Returns_Item_NoCrash` 里 `Get<PBtn>("confirm")` → `Get<PBtn>("button0")`。

(c) 追加多按钮烟雾：
```csharp
        [Test]
        public void Multi_Button_GoTo_Click_Returns_Item_And_Key_NoCrash()
        {
            var items = new List<Lv> { new Lv { Id = "a" }, new Lv { Id = "b" }, new Lv { Id = "c" } };
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<Lv>
            {
                Items = items, BindCard = (c, l) => { },
                Buttons = new[] { ("Go", "go"), ("Hard", "hard") },
            });
            UI.Modal.TopScreen.Get<Carousel>("cards").GoTo(2, animated: false);
            UI.Modal.TopScreen.Get<PBtn>("button1").SimulateClick();
            var sel = task.GetAwaiter().GetResult();
            Assert.AreSame(items[2], sel.Item);
            Assert.AreEqual("hard", sel.Button);
        }
```

- [ ] **Step 2: 跑 PlayMode 确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["CenteredSlideBoxPlayTests"])
mcp__UnityMCP__get_test_job(job_id="<id>")
```
Expected: 2/2 PASS。
> PlayMode runner 在本机偶有退化（见 memory `project_unity_mcp_test_gotchas`）；若 runner 卡死/初始化失败，记录现象、让用户重启 Unity 后重跑，不要当作代码失败。

- [ ] **Step 3: 提交**

```bash
git add Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs
git commit -m "test: CenteredSlideBox 多按钮 PlayMode 烟雾 + button0 id

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: 更新 C# SKILL（公开 API 变更，CLAUDE.md 强制）

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: 找到模态小节里的 `CenteredSlideBox.Open` 说明**

```bash
grep -n "CenteredSlideBox" .claude/skills/scripting-promptugui-csharp/SKILL.md
```

- [ ] **Step 2: 在现有 `CenteredSlideBox.Open<T>` 说明旁补多按钮重载**

补充以下要点（贴合该文件既有风格，英文）：
- 多按钮重载签名：`CenteredSlideBox.Open<T>(items, bind, IEnumerable<(string label,string key)> buttons, title=null, …) → Awaitable<SlideSelection<T>>`。
- `SlideSelection<T>`：`.Item`（选中对象，取消=null）/ `.Button`（按钮 key，取消=null）/ `.Cancelled` / 支持解构 `var (item, key) = await …`。
- `label` 走 i18n、`key` 稳定用于分支（别拿 label 当 key）。
- 行为：按钮 ≥2 时**点居中卡=无操作**（去掉单按钮的自动确认捷径）；点侧卡仍居中；取消三通道（×/背景/ESC）→ `Cancelled`。
- 按钮槽契约：默认皮肤提供 `button0..button4`（5 槽）；传超过皮肤槽数 → `InvalidOperationException`，换 `XmlSrc` 加 `button{i}` 槽可扩展；空 `buttons` → `ArgumentException`。
- 换皮 id 契约从 `confirm` 改为 `button0..`（迁移说明）。
- 示例：
  ```csharp
  var (level, action) = await CenteredSlideBox.Open(
      levels, BindCard,
      buttons: new[] { ("立即进入游戏", "play"), ("进入更高难度", "hard") },
      title: "选择关卡");
  if (action == null) return;            // 取消
  if (action == "play") StartLevel(level);
  ```

- [ ] **Step 3: 提交**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): CenteredSlideBox 多按钮重载 + SlideSelection + 按钮槽契约

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: 全量验证 + lint + format

**Files:** 无（仅验证）

- [ ] **Step 1: 全量 EditMode（不只 group，跑整个 assembly 防漏）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__get_test_job(job_id="<id>")
```
Expected: 全 PASS（基线 1841 + 本特性新增；无回归）。

- [ ] **Step 2: EditorOnly + PlayMode**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__get_test_job(job_id="<id>")
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__get_test_job(job_id="<id>")
```
Expected: EditorOnly 全 PASS；PlayMode 全 PASS（runner 退化时按 Task 4 Step 2 注记处理）。

- [ ] **Step 3: UIXmlLint 全目录 + dotnet format 校验**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
cd .lint && dotnet restore PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: UIXmlLint exit 0；`dotnet format --verify-no-changes` 无改动（如有，跑 `dotnet format whitespace/style PromptUGUI.Lint.slnx` 修后重验、并把修动作并入相关 commit）。

- [ ] **Step 4: 自检 spec 覆盖**

对照 spec §8 测试清单，确认每条都有对应通过的测试；§7 边界表每行都有覆盖（空 buttons / 超量 / 隐藏槽 / 空 items / 多按钮点居中卡 / 取消）。无缺口即完成。

- [ ] **Step 5: 收尾**

按 `superpowers:finishing-a-development-branch` 决定合并/PR 方式（**不要直接合 main**；按仓库惯例走 PR）。视觉 QA（默认皮肤多按钮行布局、单按钮退化视觉）留用户在宿主工程确认。

---

## 自检（writing-plans self-review）

- **Spec 覆盖**：CSB-D12（Task 1）/ D13（Task 3 facade 签名）/ D14（Task 3 必填+非空两测试）/ D15（Task 2 binder + 两 request）/ D16（Task 3 `Multi_Button_Tap_Centered_Card_Is_NoOp` + 单按钮 autoConfirm 回归）/ D17（Task 3 超量 + 隐藏槽）/ D18（Task 2 皮肤）。全覆盖。
- **类型一致**：`SlideSelection<T>`（`.Item`/`.Button`/`.Cancelled`/`Deconstruct`）、`CenteredSlideBoxBinder.Bind<T>(…, onConfirm, onCancel)`、`CenteredSlideBoxRequest<T> : ModalRequest<T>`（含 `ConfirmLabel`）、`CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>>`（含 `Buttons`）跨任务签名一致。
- **无占位符**：每个代码步骤含完整代码；每个测试步骤含完整断言；MCP/shell 命令具体可跑。
- **id 契约**：默认皮肤 5 槽（button0..4）、测试 XML 3 槽（button0..2）——超量测试传 4 个对 3 槽，断言一致。
