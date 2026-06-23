# CenteredSlideBox 实时数据 / 反应式 items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `CenteredSlideBox.Open` 能承载长期、实时变化的卡片界面——卡片集合可反应式增删（`Observable<IReadOnlyList<T>>`），卡内字段可在 `bind` 里订阅实时流并 `.AddTo(card)` 绑卡片生命周期。

**Architecture:** 三块解耦改动：(1) 给 `Control` 加按卡片的订阅袋 + R3 扩展 `.AddTo(IControl)`（对称 `.AddTo(screen)`）；(2) `Carousel.BindItems` 增 `key` 选择器，成员变化全量重建后按身份保住居中卡，一次 emit ≤ 一次 `OnCurrentChanged`；(3) `CenteredSlideBox.Open` 新增 `Observable<IReadOnlyList<T>>` 反应式重载，binder 维护"最新列表"供确认/点击读取、每 emit 刷新按钮禁用态。静态重载与请求对象 `Items` 字段全部保留 → 零破坏。

**Tech Stack:** Unity 6 / C# 9.0、R3（Cysharp Observable/Subject/Do/AddTo）、Unity `Awaitable`（禁 `System.Threading.Task`）、NUnit + Unity MCP 跑测。

## Global Constraints

- **禁用 .NET Threading**（WebGL）：不用 `Task` 返回值，用 `Awaitable`；TCS 用 `AwaitableCompletionSource`。本计划只用 R3 `Observable`/`Subject`，合规。
- **C# 9.0**：不用 primary constructor、collection expression `[]`、`[field: SerializeField]`。local function、tuple 解构可用。
- **无新增 `[UIAttr]`**：`key` 是 C# 形参，不是 XML 属性 → 不需 `[Preserve]`、不需 regenerate XSD、不改任何 `.ui.xml`（故无需跑 UIXmlLint）。
- **测试经 Unity MCP**，非 batch 模式。EditMode/PlayMode 测试类触碰 `UI` 必须在 `[SetUp]`/`[TearDown]` 调 `UI.ResetForTests()`。
- **每次写代码后 lint**：仓库根 `cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx`（whitespace 安全、不需 Unity 引用）。**禁** `dotnet format analyzers --severity info`。
- **不提交 main**：已在 `feat/centered-slidebox-reactive-items` 分支。每任务一次 commit。
- **MCP 跑测节奏**：每次源码改动后先 `refresh_unity(compile="request", mode="force")`，再 `read_console(types=["error"])` 确认无编译错，再 `run_tests`（异步返回 `job_id`，轮询 `get_test_job` 读通过数）。**禁** `execute_menu_item("Assets/Reimport All")`。
- 关联 spec：`docs~/superpowers/specs/2026-06-23-centered-slidebox-reactive-items-design.md`（决策码 RI-D1~D13）。

---

## Task 1: Control 订阅袋 + `.AddTo(IControl)`

补上唯一缺失的卡片级生命周期原语。对所有 Control 通用（RI-D2/D5/D6）。

**Files:**
- Modify: `Runtime/Controls/Control.cs`（加 `_subscriptions` 字段、`Track`、`DisposeSubscriptionsRecursive`、改 `Dispose`）
- Modify: `Runtime/Application/Disposables.cs`（加 `AddTo(this T, IControl)`）
- Test: `Tests/EditMode/Controls/ControlSubscriptionTests.cs`（新建）

**Interfaces:**
- Produces:
  - `void PromptUGUI.Controls.Control.Track(System.IDisposable d)` — 把 disposable 加入该 Control 的订阅袋
  - `T PromptUGUI.Application.DisposableExtensions.AddTo<T>(this T disposable, PromptUGUI.Controls.IControl control) where T : IDisposable` — 返回原 disposable
  - `Control.Dispose()` 现在先释放订阅袋（自身 + 递归 `_children` 兜底）再销毁 GameObject，幂等

- [ ] **Step 1: 写失败测试** —— 新建 `Tests/EditMode/Controls/ControlSubscriptionTests.cs`

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ControlSubscriptionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // 极简追踪 disposable：被 Dispose 时翻 flag。
        private sealed class Flag : System.IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        private static IScreen Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [Test]
        public void AddTo_Control_Disposes_On_Control_Dispose()
        {
            var screen = Open("<Frame id='f'/>");
            var f = screen.Get<Frame>("f");
            var flag = new Flag().AddTo(f);
            Assert.IsFalse(flag.Disposed);
            f.Dispose();
            Assert.IsTrue(flag.Disposed, "control 的订阅袋在 Dispose 时释放被跟踪的订阅");
        }

        [Test]
        public void Control_Dispose_Recursively_Disposes_Child_Subscriptions()
        {
            var screen = Open("<Frame id='outer'><Text id='inner'/></Frame>");
            var outer = screen.Get<Frame>("outer");
            var inner = screen.Get<Text>("inner");
            var flag = new Flag().AddTo(inner);
            outer.Dispose();
            Assert.IsTrue(flag.Disposed, "销毁父节点递归释放子节点的订阅袋");
        }

        [Test]
        public void Control_Double_Dispose_Is_Idempotent()
        {
            var screen = Open("<Frame id='f'/>");
            var f = screen.Get<Frame>("f");
            new Flag().AddTo(f);
            f.Dispose();
            Assert.DoesNotThrow(() => f.Dispose(), "二次 Dispose 不抛");
        }
    }
}
```

- [ ] **Step 2: 跑测确认失败（编译失败：`AddTo(IControl)` 不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `'Flag' does not contain ... AddTo` / `no overload for method 'AddTo' takes ... Frame`（即 `AddTo(IControl)` 未定义）。

- [ ] **Step 3: 实现** —— `Runtime/Application/Disposables.cs` 追加 `AddTo(IControl)` 重载（紧挨现有两个 `AddTo`）

```csharp
        public static T AddTo<T>(this T disposable, Controls.IControl control) where T : IDisposable
        {
            ((Controls.Control)control).Track(disposable);
            return disposable;
        }
```

- [ ] **Step 4: 实现** —— `Runtime/Controls/Control.cs`：加订阅袋字段（放在 `private readonly List<IControl> _children = new();` 那一行附近），并新增 `Track` / `DisposeSubscriptionsRecursive`，最后**替换** `Dispose()`。

加字段（与 `_children` 同区）：
```csharp
        private System.Collections.Generic.List<System.IDisposable> _subscriptions;
```

新增方法（放在文件末尾 `Dispose` 之前）：
```csharp
        /// <summary>把 R3 订阅绑到本 Control 生命周期（卡片重建 / 关窗时随 Dispose 释放）。对称 Screen.Track。</summary>
        public void Track(System.IDisposable d)
            => (_subscriptions ??= new System.Collections.Generic.List<System.IDisposable>()).Add(d);

        // 释放自身订阅袋 + 递归子树兜底：动态卡子树的内层 Control 不会被单独 Dispose（只销毁根 GO 级联），
        // 故 .AddTo(innerControl) 必须靠这条递归，否则泄漏。只碰订阅袋，不额外销毁 GO（GO 由根 Destroy 级联）。
        private void DisposeSubscriptionsRecursive()
        {
            if (_subscriptions != null)
            {
                for (int i = _subscriptions.Count - 1; i >= 0; i--) _subscriptions[i]?.Dispose();
                _subscriptions.Clear();
                _subscriptions = null;
            }
            foreach (var c in _children)
                if (c is Control cc) cc.DisposeSubscriptionsRecursive();
        }
```

替换 `Dispose()`（原方法只销毁 GO；先退订再销毁，幂等守卫保留）：
```csharp
        public virtual void Dispose()
        {
            // 先退订（自身 + 子树）——teardown 可能读 GO；避免往半销毁 GO fire。
            DisposeSubscriptionsRecursive();
            if (HostGameObject == null) return;
            // 与 Screen.Close 一致：EditMode 用 DestroyImmediate。
            if (UnityEngine.Application.isPlaying) Object.Destroy(HostGameObject);
            else Object.DestroyImmediate(HostGameObject);
        }
```

- [ ] **Step 5: 跑测确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ControlSubscriptionTests"])
# 轮询 get_test_job(job_id) 直到完成
```
Expected: 3 个测试 PASS，0 fail。

- [ ] **Step 6: 回归 + lint**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
# 轮询；确认无回归（尤其 Carousel/ScrollList/TabBar 既有测试）
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
```
Expected: 全绿；lint 无残留改动（或只做了空白规整）。

- [ ] **Step 7: 提交**

```bash
git add Runtime/Controls/Control.cs Runtime/Application/Disposables.cs \
        Tests/EditMode/Controls/ControlSubscriptionTests.cs Tests/EditMode/Controls/ControlSubscriptionTests.cs.meta
git commit -m "feat: Control 订阅袋 + .AddTo(IControl) 卡片级生命周期原语

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Carousel.BindItems `key` + 身份保持

成员变化全量重建后按身份保住居中卡；一次 emit ≤ 一次 `OnCurrentChanged`（RI-D9/D10/D11）。

**Files:**
- Modify: `Runtime/Controls/Carousel.cs`（`BindItems` 加 `key` 形参、`Rebuild` 加 `desiredIndex`、新增 `ComputeDesiredIndex`）
- Modify: `Runtime/Controls/Internal/CarouselView.cs`（`OnItemsRebuilt` 加可选 `int? desiredIndex`）
- Test: `Tests/EditMode/Controls/CarouselBindItemsTests.cs`（追加测试）

**Interfaces:**
- Consumes: `T.AddTo(IControl)`（Task 1）
- Produces:
  - `IDisposable Carousel.BindItems<T>(Observable<IReadOnlyList<T>> source, Action<IControl, T> bind, Func<T, object> key = null)`
  - `IDisposable Carousel.BindItems<T, TSlot>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind, Func<T, object> key = null) where TSlot : class, IControl`
  - `void CarouselView.OnItemsRebuilt(int? desiredIndex = null)`

- [ ] **Step 1: 写失败测试** —— 在 `Tests/EditMode/Controls/CarouselBindItemsTests.cs` 的类**内部**追加（沿用文件已有的 `Open(innerXml)` 帮手）：

```csharp
        // —— 身份保持 / 卡片级订阅（Task 2）——
        private sealed class Item { public string Id; }
        private sealed class Flag : System.IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

        [Test]
        public void Rebuild_Preserves_Centered_Item_By_Key()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var a = new Item { Id = "a" }; var b = new Item { Id = "b" }; var c = new Item { Id = "c" };
            var subject = new Subject<IReadOnlyList<Item>>();
            using var sub = car.BindItems(subject, (IControl card, Item it) => { }, key: x => x.Id);
            subject.OnNext(new[] { a, b, c });
            car.GoTo(1, animated: false);                 // 居中 b（index 1）
            Assert.AreEqual(1, car.Current);
            subject.OnNext(new[] { a, c, b });            // b 移到 index 2
            Assert.AreEqual(2, car.Current, "居中项按 key 跟随到新 index");
        }

        [Test]
        public void Rebuild_Preserves_Centered_Item_By_Reference_When_No_Key()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var a = new Item { Id = "a" }; var b = new Item { Id = "b" }; var c = new Item { Id = "c" };
            var subject = new Subject<IReadOnlyList<Item>>();
            using var sub = car.BindItems(subject, (IControl card, Item it) => { });   // 无 key
            subject.OnNext(new[] { a, b, c });
            car.GoTo(2, animated: false);                 // 居中 c
            subject.OnNext(new[] { c, a, b });            // c 移到 index 0
            Assert.AreEqual(0, car.Current, "无 key 时按引用相等跟随");
        }

        [Test]
        public void Rebuild_Removed_Centered_Item_Clamps()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var a = new Item { Id = "a" }; var b = new Item { Id = "b" }; var c = new Item { Id = "c" };
            var subject = new Subject<IReadOnlyList<Item>>();
            using var sub = car.BindItems(subject, (IControl card, Item it) => { }, key: x => x.Id);
            subject.OnNext(new[] { a, b, c });
            car.GoTo(2, animated: false);                 // 居中 c（index 2）
            subject.OnNext(new[] { a, b });               // c 被删；剩 2 张
            Assert.AreEqual(1, car.Current, "被删的居中项就近夹到末位");
        }

        [Test]
        public void Rebuild_Emits_OnCurrentChanged_At_Most_Once()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var a = new Item { Id = "a" }; var b = new Item { Id = "b" }; var c = new Item { Id = "c" };
            var subject = new Subject<IReadOnlyList<Item>>();
            using var sub0 = car.BindItems(subject, (IControl card, Item it) => { }, key: x => x.Id);
            subject.OnNext(new[] { a, b, c });
            car.GoTo(1, animated: false);                 // 居中 b
            int count = 0;
            using var sub = car.OnCurrentChanged.Subscribe(_ => count++);
            subject.OnNext(new[] { a, c, b });            // b → index 2：应恰好 fire 一次
            Assert.AreEqual(1, count, "一次 emit 至多一次 OnCurrentChanged");
        }

        [Test]
        public void Card_Subscription_Disposed_On_Rebuild()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var subject = new Subject<IReadOnlyList<string>>();
            Flag flag = null;
            using var sub = car.BindItems(subject,
                (IControl card, string s) => { if (flag == null) flag = new Flag().AddTo(card); });
            subject.OnNext(new[] { "a" });                // 建 1 卡，flag 绑首卡
            Assert.IsFalse(flag.Disposed);
            subject.OnNext(new[] { "x", "y" });           // 重建 → 旧卡 Dispose → flag 释放
            Assert.IsTrue(flag.Disposed, "重建释放旧卡跟踪的订阅（无泄漏）");
        }
```

- [ ] **Step 2: 跑测确认失败（编译失败：`BindItems` 无 `key` 形参）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错 `BindItems ... no parameter named 'key'`（前 4 个测试），`Card_Subscription_Disposed_On_Rebuild` 逻辑依赖 Task 1（已就绪）。

- [ ] **Step 3: 实现** —— `Runtime/Controls/Carousel.cs`：**替换** `BindItems`/`Rebuild` 两段（原文件 159-188 行），并加 `ComputeDesiredIndex`。

```csharp
        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<IControl, T> bind,
            Func<T, object> key = null)
            => BindItems<T, IControl>(source, bind, key);

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind,
            Func<T, object> key = null) where TSlot : class, IControl
        {
            _itemsSub?.Dispose();
            IReadOnlyList<T> prev = null;
            _itemsSub = source.Subscribe(items =>
            {
                items ??= System.Array.Empty<T>();
                int? desired = ComputeDesiredIndex(prev, items, key);
                Rebuild(items, bind, desired);
                prev = items;
            });
            return _itemsSub;
        }

        // 重建前：按 key（无 key 则按默认相等/引用）在新列表找回"上一帧的居中项"。命中 → 其新 index；
        // 不命中（被删 / 引用对不上 / key 返回 null）→ null，由 OnItemsRebuilt 走就近夹位。
        private int? ComputeDesiredIndex<T>(IReadOnlyList<T> prev, IReadOnlyList<T> next, Func<T, object> key)
        {
            if (prev == null) return null;
            int cur = _view.CurrentIndex;
            if (cur < 0 || cur >= prev.Count) return null;
            var centered = prev[cur];
            if (key != null)
            {
                var ck = key(centered);
                if (ck == null) return null;                          // null key → 不做身份保持（避免 null==null 误命中）
                for (int i = 0; i < next.Count; i++)
                    if (Equals(ck, key(next[i]))) return i;
            }
            else
            {
                for (int i = 0; i < next.Count; i++)
                    if (EqualityComparer<T>.Default.Equals(centered, next[i])) return i;
            }
            return null;
        }

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind, int? desiredIndex)
            where TSlot : class, IControl
        {
            if (_factory == null) _factory = ResolveFactory(_itemTemplate);
            _view.ClearCards();
            for (int i = 0; i < items.Count; i++)
            {
                var node = _factory(_strip);
                if (node is TSlot typed) bind(typed, items[i]);
                else throw new InvalidCastException(
                    $"itemTemplate='{_itemTemplate}' instantiated {node.GetType().Name}, " +
                    $"but BindItems expected {typeof(TSlot).Name}");
                _view.AddCard(node);
            }
            _view.OnItemsRebuilt(desiredIndex);
        }
```

- [ ] **Step 4: 实现** —— `Runtime/Controls/Internal/CarouselView.cs`：**替换** `OnItemsRebuilt`（原文件 143-154 行），加可选 `desiredIndex` 分支。

```csharp
        // BindItems 重建完：定位当前页（给定 desiredIndex 用它、否则保旧页夹位），重建指示点，重排，重启自动播放计时。
        public void OnItemsRebuilt(int? desiredIndex = null)
        {
            int prev = _current;
            if (_cards.Count == 0) { _current = -1; _scroll = 0f; }
            else if (desiredIndex.HasValue)
                _current = Mathf.Clamp(desiredIndex.Value, 0, _cards.Count - 1);
            else
                _current = Mathf.Clamp(_current < 0 ? 0 : _current, 0, _cards.Count - 1);
            RebuildIndicator();
            RelayoutNow();
            StartAutoplayIfNeeded();
            // 仅当提交页真正变化时 fire（空→-1 / 夹位 / 身份跟随），与 GoTo 的 change-guarded 一致 → 一次 emit ≤ 一次。
            if (_current != prev) OnCurrent?.Invoke(_current);
        }
```

> 注：`Carousel.OnAfterApply` 调的是 `_view.RelayoutNow()`，不是 `OnItemsRebuilt`；`Rebuild` 是唯一调用方。可选默认参数 `= null` 保旧行为。

- [ ] **Step 5: 跑测确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["CarouselBindItemsTests"])
# 轮询 get_test_job
```
Expected: 全部 PASS（含原有 `BindItems_Rebuild_That_Clamps_Current_Fires_OnCurrentChanged` —— 首次 emit `prev==null` → desiredIndex 为 null → 走夹位，行为不变）。

- [ ] **Step 6: 回归 + lint**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
```
Expected: 全绿。

- [ ] **Step 7: 提交**

```bash
git add Runtime/Controls/Carousel.cs Runtime/Controls/Internal/CarouselView.cs \
        Tests/EditMode/Controls/CarouselBindItemsTests.cs
git commit -m "feat: Carousel.BindItems 增 key 选择器 + 成员变化按身份保持居中卡

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: CenteredSlideBox 反应式 Open + binder 改造

`Observable<IReadOnlyList<T>>` 重载 + `key`；binder 维护最新列表、确认/点击读最新项、每 emit 刷新按钮禁用态（RI-D7/D12/D13）。

**Files:**
- Modify: `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`（整文件替换：binder 改签名 + 重排、请求加 `ItemsSource`/`Key`、facade 加反应式重载）
- Test: `Tests/EditMode/Modals/CenteredSlideBoxTests.cs`（追加反应式测试）
- Test: `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs`（追加 1 个反应式 play 测试）

**Interfaces:**
- Consumes: `Carousel.BindItems(..., key)`（Task 2）、`.AddTo(IControl)`（Task 1）
- Produces:
  - `Awaitable<T> CenteredSlideBox.Open<T>(Observable<IReadOnlyList<T>> items, Action<IControl,T> bind, string title=null, string confirmLabel=null, ModalMode mode=Popup, Action<IScreen> configure=null, Func<T,object> key=null, CancellationToken ct=default) where T:class`
  - `Awaitable<SlideSelection<T>> CenteredSlideBox.Open<T>(Observable<IReadOnlyList<T>> items, Action<IControl,T> bind, IEnumerable<(string,string)> buttons, string title=null, ModalMode mode=Popup, Action<IScreen> configure=null, Func<T,object> key=null, CancellationToken ct=default) where T:class`
  - 请求类新增字段 `Observable<IReadOnlyList<T>> ItemsSource` + `Func<T,object> Key`（保留 `Items`）

- [ ] **Step 1: 写失败测试** —— 在 `Tests/EditMode/Modals/CenteredSlideBoxTests.cs` 类内追加（沿用文件已有的 `ThreeLevels()` / `Cards()` / `CardButton(i)` / `Lv`）：

```csharp
        // —— 反应式 items（Task 3）——
        [Test]
        public void Reactive_Open_Renders_On_First_Emit()
        {
            var subject = new R3.Subject<IReadOnlyList<Lv>>();
            CenteredSlideBox.Open(subject, (c, l) => { });
            Assert.AreEqual(0, Cards().Count, "首发前无卡");
            subject.OnNext(ThreeLevels());
            Assert.AreEqual(3, Cards().Count, "首 emit 渲染卡片");
        }

        [Test]
        public void Reactive_Membership_Change_Rebuilds_Cards()
        {
            var subject = new R3.Subject<IReadOnlyList<Lv>>();
            CenteredSlideBox.Open(subject, (c, l) => { });
            subject.OnNext(ThreeLevels());
            Assert.AreEqual(3, Cards().Count);
            subject.OnNext(new List<Lv> { new Lv { Id = "x", Name = "X" } });
            Assert.AreEqual(1, Cards().Count, "成员变化触发重建");
        }

        [Test]
        public void Reactive_Confirm_Returns_Centered_Item_After_Reorder()
        {
            var subject = new R3.Subject<IReadOnlyList<Lv>>();
            var task = CenteredSlideBox.Open(subject, (c, l) => { }, key: o => o.Id);
            var first = ThreeLevels();                       // a,b,c
            subject.OnNext(first);
            Cards().GoTo(1, animated: false);               // 居中 b
            var reordered = new List<Lv> { first[0], first[2], first[1] };  // a,c,b → b 到 index 2
            subject.OnNext(reordered);
            Assert.AreEqual(2, Cards().Current, "身份保持：b 跟随到新 index");
            UI.Modal.TopScreen.Get<PBtn>("button0").SimulateClick();
            Assert.AreSame(first[1], task.GetAwaiter().GetResult(), "确认返回最新列表里的居中项 b");
        }

        [Test]
        public void Reactive_Empty_Emit_Disables_Then_NonEmpty_Enables()
        {
            var subject = new R3.Subject<IReadOnlyList<Lv>>();
            CenteredSlideBox.Open(subject, (c, l) => { });
            subject.OnNext(new List<Lv>());                 // 空 → 禁用
            Assert.IsFalse(UI.Modal.TopScreen.Get<PBtn>("button0").Interactable);
            subject.OnNext(ThreeLevels());                  // 非空 → 启用
            Assert.IsTrue(UI.Modal.TopScreen.Get<PBtn>("button0").Interactable);
        }

        [Test]
        public void Reactive_Multi_Button_Returns_Item_And_Key()
        {
            var subject = new R3.Subject<IReadOnlyList<Lv>>();
            var task = CenteredSlideBox.Open(subject, (c, l) => { },
                buttons: new[] { ("A", "a"), ("B", "b") }, key: o => o.Id);
            var items = ThreeLevels();
            subject.OnNext(items);
            Cards().GoTo(1, animated: false);
            UI.Modal.TopScreen.Get<PBtn>("button1").SimulateClick();
            var sel = task.GetAwaiter().GetResult();
            Assert.AreSame(items[1], sel.Item);
            Assert.AreEqual("b", sel.Button);
        }

        [Test]
        public void Reactive_Null_Items_Throws_ArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                CenteredSlideBox.Open((R3.Observable<IReadOnlyList<Lv>>)null, (c, l) => { }));
        }
```

并在 `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs` 类内追加 1 个 play 测试（验证运行时多轮重建不残留、确认正常）：

```csharp
        [Test]
        public void Reactive_Rebuild_Then_Confirm_NoCrash()
        {
            var subject = new R3.Subject<IReadOnlyList<Lv>>();
            var task = CenteredSlideBox.Open(subject, (c, l) => { }, key: o => o.Id);
            subject.OnNext(new List<Lv> { new Lv { Id = "a" }, new Lv { Id = "b" } });
            subject.OnNext(new List<Lv> { new Lv { Id = "a" }, new Lv { Id = "b" }, new Lv { Id = "c" } });
            UI.Modal.TopScreen.Get<Carousel>("cards").GoTo(2, animated: false);
            UI.Modal.TopScreen.Get<PBtn>("button0").SimulateClick();
            var picked = task.GetAwaiter().GetResult();
            Assert.AreEqual("c", picked.Id);
        }
```

- [ ] **Step 2: 跑测确认失败（编译失败：`Open(Observable, ...)` 重载不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错 `no overload for 'Open' ... Subject<...>`。

- [ ] **Step 3: 实现** —— **整文件替换** `Runtime/Application/Modals/CenteredSlideBoxRequest.cs` 为：

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
        // buttons 至少 1 个（两个 request 各自保证）；itemsSource 由 request 归一化为非空 Observable。
        public static void Bind<T>(
            IScreen screen, Observable<IReadOnlyList<T>> itemsSource, Action<IControl, T> bindCard,
            string title, IReadOnlyList<(string label, string key)> buttons, Func<T, object> key,
            string xmlSrcForError, Action<T, string> onConfirm, Action onCancel) where T : class
        {
            if (buttons == null || buttons.Count == 0)        // facade 已挡空；直接 new request 的兜底（CSB-D14）
                throw new ArgumentException("CenteredSlideBox requires at least one button.", nameof(buttons));
            if (itemsSource == null)
                throw new ArgumentNullException(nameof(itemsSource));

            // —— title ——
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = title;

            // —— 取消三通道（CSB-D9）——
            screen.Get<Btn>("close").OnClick.Subscribe(_ => onCancel()).AddTo(screen);
            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => onCancel()).AddTo(screen);

            bool autoConfirm = buttons.Count == 1;                  // CSB-D16
            string soleKey = autoConfirm ? buttons[0].key : null;

            IReadOnlyList<T> latest = Array.Empty<T>();
            int idx = 0;
            var car = screen.Get<Carousel>("cards");

            // —— 探测皮肤按钮槽（CSB-D17）：必须在 BindItems 之前，.Do 首发会同步刷新按钮禁用态 ——
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
            var visibleButtons = new List<Btn>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i >= buttons.Count) { slot.GameObject.SetActive(false); continue; }
                var (label, btnKey) = buttons[i];
                if (!string.IsNullOrEmpty(label)) slot.Text = label;   // null/空 → 保留皮肤默认（button0 的 "OK"）
                visibleButtons.Add(slot);
                slot.OnClick.Subscribe(_ =>
                {
                    int cur = car.Current;
                    if (cur >= 0 && cur < latest.Count) onConfirm(latest[cur], btnKey);
                }).AddTo(screen);
            }
            void RefreshButtonsEnabled(bool on) { foreach (var b in visibleButtons) b.Interactable = on; }

            // —— carousel + 卡：.Do（上游先于下游 Rebuild）维护 latest / 重置 idx / 刷新按钮态（RI-D12/D13）；
            //    BindItems 带 key 做身份保持（RI-D9~D11）——
            var src = itemsSource.Do(list =>
            {
                latest = list ?? Array.Empty<T>();
                idx = 0;                                            // ★ 每 emit 归零，否则跨重建累加致索引错乱
                RefreshButtonsEnabled(latest.Count > 0);            // 空→disable，非空→enable（CSB-D11 反应式版）
            });
            car.BindItems(src, (IControl card, T item) =>
            {
                int i = idx++;
                bindCard?.Invoke(card, item);
                AttachCardClick(card, i, car, () => latest, onConfirm, autoConfirm, soleKey);
            }, key).AddTo(screen);
        }

        // 每张卡：透明 raycast catcher + PuiButton。click（非拖动）→ 居中导航或（仅单按钮）确认。
        private static void AttachCardClick<T>(IControl card, int i, Carousel car,
            Func<IReadOnlyList<T>> getLatest,
            Action<T, string> onConfirm, bool autoConfirm, string soleKey) where T : class
        {
            var go = card.GameObject;
            var img = go.GetComponent<UnityImage>() ?? go.AddComponent<UnityImage>();
            img.color = new UnityEngine.Color(0f, 0f, 0f, 0f);   // 透明，仅 raycast
            img.raycastTarget = true;
            // 卡 GO 每次 BindItems 重建都是全新的（旧的由 ClearCards 销毁），故无条件 AddComponent 安全。
            var btn = go.AddComponent<PuiButton>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                var items = getLatest();
                if (car.Current == i)
                {
                    // 单按钮：点居中卡=确认；多按钮：无操作（CSB-D16）。
                    if (autoConfirm && i >= 0 && i < items.Count) onConfirm(items[i], soleKey);
                }
                else car.GoTo(i, animated: true);                // 点侧卡=居中
            });
        }
    }

    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;                       // 静态（保留，向后兼容）
        public Observable<IReadOnlyList<T>> ItemsSource;     // 反应式（优先）
        public Func<T, object> Key;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;                 // 单个按钮的 label（空→皮肤默认 "OK"）
        public string XmlSrcOverride;               // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out T result) { result = null; return true; }

        public override void Bind(IScreen screen, Action<T> close)
            => CenteredSlideBoxBinder.Bind(screen,
                   ItemsSource ?? Observable.Return<IReadOnlyList<T>>(Items ?? Array.Empty<T>()),
                   BindCard, Title, new[] { (ConfirmLabel, (string)null) }, Key, XmlSrc,   // 1 个隐式按钮；key 忽略
                   onConfirm: (item, _) => close(item),
                   onCancel: () => close(null));
    }

    // 多按钮：返回 SlideSelection<T>（选中卡 + 按钮 key）。
    public sealed class CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>> where T : class
    {
        public IReadOnlyList<T> Items;
        public Observable<IReadOnlyList<T>> ItemsSource;
        public Func<T, object> Key;
        public Action<IControl, T> BindCard;
        public string Title;
        public IReadOnlyList<(string label, string key)> Buttons;   // facade 保证非空
        public string XmlSrcOverride;

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out SlideSelection<T> result) { result = default; return true; }

        public override void Bind(IScreen screen, Action<SlideSelection<T>> close)
            => CenteredSlideBoxBinder.Bind(screen,
                   ItemsSource ?? Observable.Return<IReadOnlyList<T>>(Items ?? Array.Empty<T>()),
                   BindCard, Title, Buttons, Key, XmlSrc,
                   onConfirm: (item, key) => close(new SlideSelection<T>(item, key)),
                   onCancel: () => close(default));
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        // 单按钮 · 静态 → 返回选中对象 / null（向后兼容）。
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

        // 单按钮 · 反应式。
        public static UnityEngine.Awaitable<T> Open<T>(
            Observable<IReadOnlyList<T>> items,
            Action<IControl, T> bind,
            string title = null,
            string confirmLabel = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            Func<T, object> key = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            return UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
            {
                ItemsSource = items,
                Key = key,
                BindCard = bind,
                Title = title,
                ConfirmLabel = confirmLabel,
                Configure = configure,
            }, mode, ct);
        }

        // 多按钮 · 静态 → 返回 (选中对象, 按钮 key)。buttons 必填且非空。
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

        // 多按钮 · 反应式。
        public static UnityEngine.Awaitable<SlideSelection<T>> Open<T>(
            Observable<IReadOnlyList<T>> items,
            Action<IControl, T> bind,
            IEnumerable<(string label, string key)> buttons,
            string title = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            Func<T, object> key = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var list = new List<(string label, string key)>(
                buttons ?? throw new ArgumentNullException(nameof(buttons)));
            if (list.Count == 0)
                throw new ArgumentException("buttons must be non-empty", nameof(buttons));
            return UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<T>
            {
                ItemsSource = items,
                Key = key,
                BindCard = bind,
                Title = title,
                Buttons = list,
                Configure = configure,
            }, mode, ct);
        }
    }
}
```

- [ ] **Step 4: 跑测确认通过（EditMode 模态 + PlayMode）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force")
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["CenteredSlideBoxTests"])
# 轮询；再跑 play：
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["CenteredSlideBoxPlayTests"])
# 轮询 get_test_job
```
Expected: 新增反应式测试 + **所有原有 CenteredSlideBox 测试**（用 `Items=` 静态字段，经 `ItemsSource ?? Observable.Return(Items)` 归一化）全 PASS。

- [ ] **Step 5: 全量回归 + lint**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
```
Expected: 全绿。

- [ ] **Step 6: 提交**

```bash
git add Runtime/Application/Modals/CenteredSlideBoxRequest.cs \
        Tests/EditMode/Modals/CenteredSlideBoxTests.cs Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs
git commit -m "feat: CenteredSlideBox.Open 反应式 items 重载 + 按身份保持确认

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: 文档 / SKILL 更新

CLAUDE.md 强制：功能变更必须同 PR 更新对应 skill（英文）。

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `.claude/skills/authoring-promptugui-xml/reference/controls-carousel.md`

**Interfaces:** Consumes 全部前序任务的最终 API 形态（文档化，无新代码）。

- [ ] **Step 1: scripting skill —— 新增 `.AddTo(card)` 生命周期原语**

在 "Events & subscriptions" 段（讲 `.AddTo(screen)` 处）后补一小节：

```markdown
### Per-control subscription lifetime (`.AddTo(control)`)

`.AddTo(screen)` ties a subscription to **Screen** lifetime. For per-item live
data inside a `BindItems` / modal-card binder, tie it to the **card** instead so
it's disposed when that card is rebuilt (list membership change) or the screen
closes — whichever comes first:

\```csharp
list.BindItems(items, (slot, item) => {
    var label = slot.Get<Text>("label");                 // cache the handle
    item.Count.Subscribe(n => label.TextValue = $"x{n}") // R3 field on the item VM
        .AddTo(slot);                                    // ← card lifetime, NOT screen
});
\```

`.AddTo(control)` works on any `IControl` (mirrors `.AddTo(screen)`). Disposing a
control disposes its tracked subscriptions, recursively including child controls.
Using `.AddTo(screen)` for per-card subscriptions leaks across list rebuilds
(old cards are destroyed but their subscriptions keep firing into dead controls
until the screen closes).
```

- [ ] **Step 2: scripting skill —— CenteredSlideBox 反应式 Open + key + 实时示例**

在 CenteredSlideBox 段（`var lv = await CenteredSlideBox.Open(...)` 附近 + API surface）补充反应式重载与 `key`，并加实时下单示例：

```markdown
// Reactive items: the card set changes live (orders appear/disappear) AND each
// card's fields tick. Pass Observable<IReadOnlyList<T>> for membership; subscribe
// per-card fields inside `bind` with .AddTo(card). `key` keeps the centred card
// by identity across membership changes (so confirm submits the object the user sees).
var picked = await CenteredSlideBox.Open(
    items: liveOrders,                       // Observable<IReadOnlyList<OrderVM>>
    bind: (card, vm) => {
        var price = card.Get<Text>("price"); // cache; don't Get every tick
        vm.Price.Subscribe(p => price.TextValue = p.ToString("C")).AddTo(card);
    },
    title: UI.Tr("Select order"),
    key: o => o.Id);                         // identity for centred-card preservation
```

并在 API surface 的 `CenteredSlideBox` 块补两个反应式重载签名（`Observable<IReadOnlyList<T>> items` + `Func<T,object> key = null`，单/多按钮各一），注明：静态重载保留；`key` 仅反应式重载有（静态永不重建）；成员变化全量重建（非 keyed diff，高频变化应改用 ScrollList）。

- [ ] **Step 3: scripting skill —— cheatsheet**

`DATA PUSH` 段在 `Carousel.BindItems(...)` 行后补 `, key: o=>o.Id (反应式身份保持)`；`EVENTS (R3)` 段 `.AddTo(screen)` 行后补一行 `.AddTo(control)  per-card/per-control lifetime (BindItems 内订阅用它)`；`MODAL` 段 CenteredSlideBox 注释补 "items 可传 Observable<IReadOnlyList<T>> + key 身份保持"。

- [ ] **Step 4: carousel reference —— BindItems key 参数**

在 `.claude/skills/authoring-promptugui-xml/reference/controls-carousel.md` 的 `BindItems` 说明处补：

```markdown
`BindItems` 第三个可选参 `Func<T,object> key`（如 `key: x => x.Id`）：源 Observable
重新 emit（成员增删/换序）时，重建后按 key 把"上一帧居中的那张卡"重新居中；命中不到
（被删/无 key 引用对不上）则就近夹位。一次 emit 至多触发一次 `OnCurrentChanged`。
卡内实时字段在 `bind` 里订阅并 `.AddTo(card)`（重建/关窗自动退订）。
```

- [ ] **Step 5: 校对 + 提交**

通读两份 skill 改动，确认与 Task 1-3 的最终签名一致（`AddTo(IControl)`、`Open(Observable<IReadOnlyList<T>>, ..., Func<T,object> key=null, ct)`、`BindItems(..., Func<T,object> key=null)`）。

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md \
        .claude/skills/authoring-promptugui-xml/reference/controls-carousel.md
git commit -m "docs: skill 更新 .AddTo(card) / CenteredSlideBox 反应式 items / Carousel key

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 完成标准

- EditMode `PromptUGUI.Tests.EditMode` + PlayMode `PromptUGUI.Tests.PlayMode` 全绿，含全部新增测试与零回归。
- `cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx` 无残留。
- 两份 skill 已反映新 API。
- 4 次 commit 均在 `feat/centered-slidebox-reactive-items`，main 无改动。
- 静态 `Open(IReadOnlyList<T>, ...)` 与请求对象 `Items` 字段签名不变 → 现有调用方零影响。
