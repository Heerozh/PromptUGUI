# CenteredSlideBox 实时数据 / 反应式 items 设计

**日期**: 2026-06-23
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `Runtime/Controls/Control.cs` —— 新增按 Control 的订阅生命周期：惰性 `_subscriptions` 袋 + `public void Track(IDisposable)`，`Dispose()` 释放它（先订阅、后销毁 GO，递归 `_children` 兜底，幂等）
2. `Runtime/Application/Disposables.cs` —— 新增 R3 扩展 `AddTo(this T, IControl)`（对称现有 `AddTo(IScreen)`）
3. `Runtime/Controls/Carousel.cs` —— `BindItems` 增加可选 `Func<T,object> key` 形参；emit 重建后按身份保持居中卡
4. `Runtime/Controls/Internal/CarouselView.cs` —— `OnItemsRebuilt` 支持"重建后定位到指定 index"，避免一次 emit 内重复 fire `OnCurrentChanged`
5. `Runtime/Application/Modals/CenteredSlideBoxRequest.cs` —— `Open` 新增 `Observable<IReadOnlyList<T>>` 反应式重载（单/多按钮各一）+ `key`；`CenteredSlideBoxBinder` 改为维护"最新列表"，确认/点击读最新项；空列表→按钮 disable 改为每 emit 更新；旧静态重载保留、委托反应式实现
6. `scripting-promptugui-csharp/SKILL.md` —— 文档化 `.AddTo(card)` 原语、反应式 `Open` + `key`、"bind 内订阅实时字段"模式、cheatsheet、CenteredSlideBox API surface
7. `authoring-promptugui-xml/reference/controls-carousel.md` —— `BindItems` 的 `key` 参数

**依赖**: 无新增包。复用：R3（`Observable` / `Subject` / `.AddTo` / `.Do`）、现有 `Screen.Track` / `AddTo(IScreen)` 范式、`Carousel.BindItems` / `CarouselView` 翻页与重建机制、`CenteredSlideBoxBinder` 现有按钮探测 / 取消三通道逻辑。

---

## 1. 背景

`CenteredSlideBox.Open` 现在是一次性快照型 API：

```csharp
// CenteredSlideBoxRequest.cs:39
car.BindItems(Observable.Return(items), bindCard);   // emit 一次即 complete，永不重建
```

- `items` 是**静态** `IReadOnlyList<T>`；
- `bind` 是 `Action<IControl, T>`，调用方在里面 `card.Get<Text>("name").TextValue = ...` 填卡槽。

真实需求：用户用 CenteredSlideBox 打开一个**长期存在、数据实时变化**的复杂界面（典型如"实时下单"——几张可滑动的卡片，卡内价格/状态/库存不停跳，且卡片集合本身也会增删）。当前 API 无法承载，存在两个正交的更新粒度都没解：

| 粒度 | 含义 | 现状 |
|---|---|---|
| **A. 列表成员变化** | 卡片增 / 删 / 换序 | ❌ `Open` 只收静态 `IReadOnlyList<T>`，传不进 `Observable` |
| **B. 卡内字段更新** | 某张卡的价格 / 状态字段跳动，卡本身不增删 | ⚠️ 今天**碰巧能做**（卡片永不重建 → `bind` 内订阅 + `.AddTo(screen)` 可工作），但无文档、且一旦支持 A 就会泄漏 |

**B 的关键现状**：因为底层是 `Observable.Return`，卡片建出来后永不重建，所以在 `bind` 里订阅实时流并 `.AddTo(screen)` **目前就能正常工作**。问题在于：

1. **没有"按卡片"的生命周期**。`Control` 没有 disposable 袋，`Control.Dispose()` 只销毁 GameObject、不释放任何 R3 订阅。唯一能绑的生命周期是 `screen`（`.AddTo(screen)`）。一旦支持 A（列表会重建），旧卡被 `ClearCards` 销毁，但其 `.AddTo(screen)` 订阅会存活到关窗、继续往已销毁控件写值 → 泄漏 + 无效写。
2. **模式无文档**。用户正是因为不确定才来问"bind 里订阅上就行了？"——答案基本是"对"，但要补一个干净的卡片级生命周期才安全。

需要的不是一套声明式数据绑定层（那违背本库"XML 只描述结构、C# 显式接线"的核心哲学），而是：**补上唯一缺失的原语——按 Control 的订阅生命周期 `.AddTo(card)`（对称 `.AddTo(screen)`），再让 `items` 能反应式、并在成员变化时按身份保住居中卡。**

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| RI-D1 | 更新粒度 | 拆成 A（成员，`Observable<IReadOnlyList<T>>`）+ B（字段，`bind` 内订阅 `T` 自带的 R3 字段）两条正交通道 | 二者生命周期、频率截然不同：成员低频、整列表重建；字段高频、原地更新。混在一起（如整列表重发来表达字段跳动）对高频字段是灾难 |
| RI-D2 | 卡片级生命周期 | 给 `Control` 加订阅袋 + `Track`，新增 R3 扩展 `.AddTo(card)`，对称 `.AddTo(screen)` | 最贴合既有范式、最易发现；`bind` 签名 `Action<IControl,T>` **不变**（`card` 就是 `IControl`），调用方只是把 `.AddTo(screen)` 换成 `.AddTo(card)`；零破坏 |
| RI-D3 | 备选：第三参 `Action<IControl,T,CompositeDisposable>` | **否决** | 要改签名 / 加重载、与 `.AddTo(screen)` 不对称；`.AddTo(card)` 同样达意且零签名churn |
| RI-D4 | 备选：文档化 `.AddTo(card.GameObject)`（R3 自带 GameObject 触发器） | **否决** | 每卡多挂一个 `ObservableDestroyTrigger` MonoBehaviour、销毁时机随 Unity 帧末、与 `.AddTo(screen)` 不对称、不可发现 |
| RI-D5 | `Control.Dispose()` 顺序 | **先**释放订阅袋（自身 + 递归 `_children` 兜底），**再**销毁 `HostGameObject`；幂等（释放后置 null） | 订阅 teardown 可能要读 GO；先退订避免往半销毁 GO fire；递归 `_children` 让 `.AddTo(innerControl)` 也安全（不止根卡），消除脚枪 |
| RI-D6 | 通用性 | `.AddTo(card)` 原语做在 `Control` 上，对**所有** Control 通用，不止模态卡片 | 同一机制顺带让 `ScrollList`/`Carousel`/`TabBar` 的 `BindItems` per-row 订阅可安全 `.AddTo(slot)`，修复"列表重建时 `.AddTo(screen)` 订阅泄漏"这一既有潜在 bug |
| RI-D7 | 反应式 API 形态 | `Open` **新增** `Observable<IReadOnlyList<T>>` 重载（单/多按钮各一），旧静态重载保留、内部包 `Observable.Return(items)` 委托过去 | 向后兼容零破坏；调用方按首参类型自然重载分派 |
| RI-D8 | 数据形态 | `T`（视图模型）自带 R3 字段（如 `ReactiveProperty<decimal> Price`）；成员走 `Observable<IReadOnlyList<T>>`，字段走 `bind` 内订阅 | R3 惯用法；与本库"C# 显式接线"一致；列表流管成员、字段流管跳动，职责分离 |
| RI-D9 | 成员变化时居中卡 | **按身份保持**：重建后重新居中"同一对象"；被删则就近夹位 | 防止静默重排/删除后点确认提交错对象（确认正确性，非纯 UI）；用户已排除"保留卡片全状态"（keyed diff），仅保居中身份足够 |
| RI-D10 | 身份判定 | 可选 `Func<T,object> key`；不传 → 引用相等；都不命中 → 沿用就近夹位 | `key`（如 `o => o.Id`）覆盖"每 emit 重建视图模型"场景；引用相等覆盖"复用同实例"场景；夹位是安全兜底。`object` 会装箱值类型 key，但成员变化低频、可忽略 |
| RI-D11 | 身份保持归属层 | 放 **Carousel.BindItems**（它拥有 `_current` 分页状态），而非 binder | 复用：任何反应式 Carousel 都受益；状态就在 `CarouselView` |
| RI-D12 | 确认 / 点击读哪份列表 | binder 用 `source.Do(l => _latest = l)` 维护**最新列表**，确认读 `_latest[car.Current]`、每卡点击读 `_latest[i]` | 反应式下捕获的静态 `items` 会过期；单订阅 + `.Do` 副作用零额外开销 |
| RI-D13 | 空列表 → 按钮 disable | 由现在一次性（CSB-D11）改为**每 emit 更新**：`_latest.Count==0` → 确认按钮 disable，非空 → enable | 反应式下列表可在首发前为空、或 emit 成空集 |

---

## 3. 核心机制：按 Control 的订阅生命周期 `.AddTo(card)`

### 3.1 `Control` 的订阅袋（RI-D2 / RI-D5）

```csharp
// Runtime/Controls/Control.cs
private List<IDisposable> _subscriptions;   // 惰性：未用过的 Control 恒为 null，零内存代价

public void Track(IDisposable d) => (_subscriptions ??= new List<IDisposable>()).Add(d);

public virtual void Dispose()
{
    // 先退订（自身 + 递归子树兜底）——teardown 可能读 GO；避免往半销毁 GO fire
    DisposeSubscriptionsRecursive();
    if (HostGameObject == null) return;     // 既有幂等守卫
    if (UnityEngine.Application.isPlaying) Object.Destroy(HostGameObject);
    else Object.DestroyImmediate(HostGameObject);
}

private void DisposeSubscriptionsRecursive()
{
    if (_subscriptions != null)
    {
        for (int i = _subscriptions.Count - 1; i >= 0; i--) _subscriptions[i]?.Dispose();
        _subscriptions.Clear();
        _subscriptions = null;
    }
    foreach (var c in _children)            // _children 已由 AddChild 维护
        if (c is Control cc) cc.DisposeSubscriptionsRecursive();
}
```

- **幸福路径**：`bind` 拿到的 `card`（卡片根 IControl）上 `.AddTo(card)`，根 `Dispose()` 释放整卡订阅。
- **兜底**：`.AddTo(card.Get<Text>("price"))`（绑到内层控件）也安全——根 Dispose 递归释放 `_children` 的袋。注：动态卡子树的内层控件不会被单独 `Dispose()`（只销毁根 GO 级联），故必须靠这条递归，否则内层 AddTo 会泄漏。
- **幂等**：`Dispose()` 二次调用——`_subscriptions` 已置 null、`HostGameObject==null` 守卫返回，安全（与现有 double-dispose 安全契约一致）。
- 子类（`Carousel`/`ScrollList`/`TabBar`/...）的 `override Dispose()` 末尾都 `base.Dispose()` → 自动获得退订。

### 3.2 R3 扩展 `.AddTo(IControl)`（RI-D2）

```csharp
// Runtime/Application/Disposables.cs —— 紧挨现有 AddTo(Screen)/AddTo(IScreen)
public static T AddTo<T>(this T disposable, Controls.IControl control) where T : IDisposable
{
    ((Controls.Control)control).Track(disposable);
    return disposable;
}
```

- 与 `AddTo(IScreen)`（`((Screen)screen).Track(...)`）写法对称。
- 是 `IControl` 上的全新重载，不与 R3 自带 `AddTo(GameObject/Component/ICollection/CancellationToken)` 冲突。

### 3.3 生命周期语义

| 订阅绑到 | 释放时机 |
|---|---|
| `.AddTo(screen)` | 关窗 / `UI.Close` / `UnloadAll`（既有） |
| `.AddTo(card)` | **卡片重建（成员变化）或关窗**——以先到者为准 |

---

## 4. Carousel.BindItems：key 选择器 + 身份保持

### 4.1 API（RI-D9/D10/D11）

```csharp
// Runtime/Controls/Carousel.cs —— 形参追加，旧调用点（key 默认 null）零改动
public IDisposable BindItems<T>(
    Observable<IReadOnlyList<T>> source, Action<IControl, T> bind,
    Func<T, object> key = null);

public IDisposable BindItems<T, TSlot>(
    Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind,
    Func<T, object> key = null) where TSlot : class, IControl;
```

### 4.2 行为

每次 `source` emit：

1. **重建前**：若上一份列表非空且当前居中 index 合法，记下居中项 `prevCentered = prev[CurrentIndex]`。
2. `Rebuild(items, bind)`：`ClearCards()`（销毁旧卡 → 触发各卡根 `Dispose()` → 释放 `.AddTo(card)` 订阅）→ 逐项建新卡 + 调 `bind`。
3. **重建后定位**：在 `items` 中找回 `prevCentered`（有 `key` → 比 `key`；否则比引用相等）。命中 → 居中其新 index；不命中（被删/无 key 引用对不上）→ 沿用 `OnItemsRebuilt` 现有就近夹位。
4. `prev = items`，供下次 emit 用。

### 4.3 避免一次 emit 内重复 fire `OnCurrentChanged`（RI-D11）

现状 `Rebuild → OnItemsRebuilt` 会先夹位 `_current` 并可能 fire 一次 `OnCurrent`，若随后再 `GoTo(target)` 定位则可能二次 fire。解决：把"目标 index"在重建时一次性传入定位。

实现取向：`CarouselView.OnItemsRebuilt` 增可选 `int? desiredIndex` 形参——给定且合法则直接定位到它、只在与旧 `_current` 不同才 fire 一次；不给定走原夹位逻辑。`Carousel.BindItems` 把第 3 步算出的新 index 作为 `desiredIndex` 下传。**净效果：一次 emit 至多一次 `OnCurrentChanged`。**

---

## 5. CenteredSlideBox.Open：反应式重载

### 5.1 新增 / 保留的重载（RI-D7）

```csharp
// === 反应式（新增） ===
// 单按钮 → Awaitable<T>
public static Awaitable<T> Open<T>(
    Observable<IReadOnlyList<T>> items, Action<IControl, T> bind,
    string title = null, string confirmLabel = null, ModalMode mode = ModalMode.Popup,
    Action<IScreen> configure = null, Func<T, object> key = null,
    CancellationToken ct = default) where T : class;

// 多按钮 → Awaitable<SlideSelection<T>>
public static Awaitable<SlideSelection<T>> Open<T>(
    Observable<IReadOnlyList<T>> items, Action<IControl, T> bind,
    IEnumerable<(string label, string key)> buttons,
    string title = null, ModalMode mode = ModalMode.Popup,
    Action<IScreen> configure = null, Func<T, object> key = null,
    CancellationToken ct = default) where T : class;

// === 静态（保留，向后兼容；内部 Observable.Return(items) 委托反应式） ===
// 既有两个静态重载签名不变，无 key 形参（静态永不重建，key 无意义）
```

> 命名澄清：多按钮的 `buttons` 里每个元素的 `key` 是"按钮分支判别符"（既有语义），与本设计新增的 `Func<T,object> key`（身份选择器）是两个无关概念。代码中前者是 `(label, key)` 元组字段，后者是形参 `key`。

请求对象 `CenteredSlideBoxRequest<T>` / `CenteredSlideBoxMultiRequest<T>` 字段调整（**保留** `Items`、**新增** `ItemsSource` + `Key`——这两个 request 类是 public 且被现有测试直接构造，重命名会破坏公共 API / 测试，故新增而非替换）：
- 保留 `IReadOnlyList<T> Items`（静态调用方继续用）；
- 新增 `Observable<IReadOnlyList<T>> ItemsSource`（反应式调用方用，优先）；
- 新增 `Func<T, object> Key`。
- `Bind` 归一化：`var source = ItemsSource ?? Observable.Return<IReadOnlyList<T>>(Items ?? Array.Empty<T>());`。

### 5.2 `CenteredSlideBoxBinder` 改造（RI-D12/D13）

```csharp
public static void Bind<T>(
    IScreen screen, Observable<IReadOnlyList<T>> itemsSource, Action<IControl,T> bindCard,
    string title, IReadOnlyList<(string label, string key)> buttons, Func<T,object> key,
    string xmlSrcForError, Action<T,string> onConfirm, Action onCancel) where T : class
{
    // title / 取消三通道（close + backdrop）—— 不变

    IReadOnlyList<T> latest = Array.Empty<T>();
    int idx = 0;                                    // 每 emit 内的卡片构建序号
    var car = screen.Get<Carousel>("cards");

    // .Do 在下游 Rebuild 之前跑（同一 OnNext 上游先于下游）：维护最新列表、重置构建序号、
    // 每 emit 刷新按钮 disable 态（RI-D12/D13）。
    var src = itemsSource.Do(list =>
    {
        latest = list ?? Array.Empty<T>();
        idx = 0;                                    // ★ 必须每 emit 归零——否则跨重建累加致索引错乱
        RefreshButtonsEnabled(latest.Count > 0);    // 空 → disable，非空 → enable
    });

    car.BindItems(src, (IControl cardCtl, T item) =>
    {
        int i = idx++;                              // 该卡在本次 emit / latest 中的下标
        bindCard?.Invoke(cardCtl, item);
        AttachCardClick(cardCtl, i, car, () => latest, onConfirm, autoConfirm, soleKey);
    }, key).AddTo(screen);

    // 按钮槽探测 / 映射 buttons[i] → slot i —— 逻辑不变；
    // 确认点击体改为读 latest：
    //   int cur = car.Current; if (cur in range(latest)) onConfirm(latest[cur], btnKey);
}
```

- **`AttachCardClick`** 由捕获静态 `items` 改为持构建下标 `i` + `Func<IReadOnlyList<T>> getLatest`；"点居中卡=确认（单按钮）/点侧卡=居中"判定用 `car.Current == i`（卡片随每 emit 重建、与 `latest` 一一对应，故构建下标 `i` 即该卡在 `latest` 的下标），确认体读 `getLatest()[i]`。
- **空列表**：首发前 `latest` 为空 → 按钮 disable；emit 空集同理。非空 emit 自动 enable。
- **确认正确性**：身份保持（§4）确保 `car.Current` 始终指向用户视觉上居中的那张卡在 `latest` 中的真实项 → `onConfirm(latest[car.Current], …)` 提交的就是用户看到的对象。

---

## 6. 用法（将写进 scripting skill）

```csharp
// 视图模型：成员身份用稳定 Id，字段用 R3
sealed class OrderVM {
    public string Id;
    public ReactiveProperty<decimal> Price;
    public ReactiveProperty<int>     Stock;
}

// liveOrders: Observable<IReadOnlyList<OrderVM>> —— 成员增删换序由它推
var picked = await CenteredSlideBox.Open(
    items: liveOrders,
    bind: (card, vm) => {
        var price = card.Get<Text>("price");          // 缓存句柄：别每 tick 走 Get 字典
        var stock = card.Get<Text>("stock");
        vm.Price.Subscribe(p => price.TextValue = p.ToString("C")).AddTo(card);  // 绑卡生命周期
        vm.Stock.Subscribe(s => stock.TextValue = $"x{s}").AddTo(card);          // 重建/关窗自动退订
    },
    title: UI.Tr("选择订单"),
    key: o => o.Id);                                  // 成员变化按 Id 保住居中卡
if (picked != null) Trade.Place(picked);
```

要点（写进文档）：
- 成员变化 → `Observable<IReadOnlyList<T>>`；字段跳动 → `T` 自带 R3 字段 + `bind` 内订阅 `.AddTo(card)`。
- **永远 `.AddTo(card)` 而非 `.AddTo(screen)`** 给卡内订阅——否则列表重建时旧订阅泄漏。
- 在 `bind` 入口缓存 `card.Get<T>(...)` 句柄，避免每次 tick 重复字典查找。

---

## 7. 测试（先红后绿，经 Unity MCP）

**EditMode**（`PromptUGUI.Tests.EditMode`）：
- `Control` 订阅袋：`Track` + `Dispose` 释放并退订；递归释放 `_children`（内层 `.AddTo` 也退订）；double-dispose 幂等。
- `AddTo(IControl)` 扩展：返回原 disposable；Control Dispose 后已 disposed。
- Carousel 身份保持：居中项被删 → 就近夹位；被移序 → 跟随到新 index；无 `key` 用引用相等命中；一次 emit 至多一次 `OnCurrentChanged`。
- 反应式 `Open`：emit 新列表 → 卡更新；空列表 → 确认按钮 disable、非空 → enable；身份保持后确认读到正确项；静态重载向后兼容（仍工作、行为不变）。

**PlayMode**（`PromptUGUI.Tests.PlayMode`，`Modals/CenteredSlideBoxPlayTests.cs` 同址）：
- 多轮成员重建后，旧卡的字段订阅计数归零（无泄漏、无往已销毁控件写）。
- 关窗 → 全部 `.AddTo(card)` / `.AddTo(screen)` 订阅释放。

---

## 8. 边界 / 错误处理

- `itemsSource` 为 `null`：静态重载不会产生（总包 `Observable.Return`）；反应式重载传 `null` → `ArgumentNullException`（与多按钮 `buttons` 空校验同风格）。
- 首发前（`Observable` 尚未 emit）：无卡片、确认按钮 disable；正常，等首 emit。
- `key` 返回 `null` 或抛异常：`null` 视为不命中 → 夹位；抛异常按既有 binder 异常路径（取消该 modal 的 await，与 `configure` 抛错一致）。
- emit 频率：成员变化预期低频。高频成员变化（每帧增删）超出本设计（用户已排除 keyed diff）——文档注明此时应换 `ScrollList` 或自管。

---

## 9. Out of Scope

- **卡片复用 / keyed diff**（成员变化时复用未变卡、其订阅原地存活、不闪烁、不重置滚动）——用户已明确排除；成员变化走全量重建。
- **声明式 XML 数据绑定**（`<Text text="{{vm.price}}">`）——违背本库哲学。
- **`ScrollList` / `TabBar` 的身份保持 `key`**——本次只给 `Carousel`（CenteredSlideBox 依赖它）。但 `.AddTo(slot)` 因做在 `Control` 上，三者 per-row 订阅**自动**受益（修复潜在泄漏）。后续若需可同样给它们加 `key`。

---

## 10. 跟现有 spec / SKILL 的整合点

- **主 spec** `2026-05-07-promptugui-description-language-design.md`：CenteredSlideBox / Carousel 章节追加反应式 `items` + `key` + `.AddTo(card)` 说明（沿用其 §编号引用惯例）。
- **`scripting-promptugui-csharp/SKILL.md`**：`.AddTo(card)` 原语（生命周期表）、反应式 `Open` 重载 + `key`、"bind 内订阅实时字段"模式 + §6 示例、cheatsheet（`DATA PUSH` / `MODAL` 段）、CenteredSlideBox API surface。
- **`authoring-promptugui-xml/reference/controls-carousel.md`**：`BindItems` 的 `key` 形参。
- **addressables skill**：无关，不动。
- **XSD**：无新增 `[UIAttr]`（`key` 是 C# 形参，非 XML 属性），不需 regenerate。

---

## 11. 风险与回滚

- **风险：`Control.Dispose()` 递归释放 `_children` 改了销毁路径**。缓解：先释放订阅再销毁 GO、保留既有 `HostGameObject==null` 幂等守卫；递归只碰订阅袋、不额外销毁 GO（GO 仍由根 Destroy 级联）。EditMode 全量回归（尤其 ScrollList/Carousel/TabBar 重建、Variant ReSolve）。
- **风险：身份保持引入的 `GoTo`/定位与 `OnItemsRebuilt` 既有夹位双触发**。缓解：用 `desiredIndex` 一次性定位（§4.3），单元测试钉死"一次 emit ≤ 一次 OnCurrentChanged"。
- **回滚**：三块解耦——(1) Control 订阅袋 + `.AddTo(IControl)`、(2) Carousel `key` 身份保持、(3) CenteredSlideBox 反应式重载。可独立 revert：去掉 (3) 退回静态、去掉 (2) 退回夹位、去掉 (1) 仅失 `.AddTo(card)`（`.AddTo(screen)` 仍在）。静态重载签名不变 → 现有调用方零影响。
