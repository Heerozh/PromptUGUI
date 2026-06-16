# `CenteredSlideBox` 多按钮设计

**日期**: 2026-06-16
**状态**: 设计阶段（待 review，未进入实施）
**建立在**: [`2026-06-15-centered-slide-box-design.md`](2026-06-15-centered-slide-box-design.md)（CSB-D1..D11，已合并入 main `fc00b26`）。本文是它的扩展，决策编号续到 `CSB-D12+`，沿用同一套模态机制（`ModalRequest<TResult>` / `UI.Modal.OpenAsync` / `Configure` / `TryEscape`）。

**一句话**: 给 `CenteredSlideBox` 加**多个自定义动作按钮**——例如关卡选择窗里「立即进入游戏」/「进入更高难度」。多按钮返回「选中的对象 **+** 点了哪个按钮」（具名 `SlideSelection<T>`）；按钮 ≥2 时自动关闭「点居中卡=确认」这个捷径。现有单按钮 `Open<T> → Awaitable<T>` 保持不变。

**动机**: 当前只有一个确认按钮，确认语义单一（"选好了"）。现实选择器常需要在同一个选中项上分出多条动作路径（进入 / 高难度进入 / 预览…）。这要求返回值能同时携带「选了哪张卡」和「触发了哪个动作」，单个值装不下——这是整个设计的支点。

**作用域**:
1. 新增 `Runtime/Application/Modals/SlideSelection.cs`：`public readonly struct SlideSelection<T>`（多按钮返回类型；对标 `MsgBtn` 是个独立具名类型）。
2. 改 `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`：
   - **新增**共享静态 `CenteredSlideBoxBinder`（把现有 Bind 逻辑搬进去，回调化）；
   - `CenteredSlideBoxRequest<T> : ModalRequest<T>` **基类不变**、改为委托 binder；
   - **新增** `CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>>`；
   - facade 加多按钮重载（单按钮重载签名/实现不变）。
3. 改 `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`：单个 `<Btn id="confirm">` → `<HStack id="buttons">` + 5 个槽 `button0..button4`。
4. 改 `.claude/skills/scripting-promptugui-csharp/SKILL.md`：补多按钮重载用法 + `SlideSelection<T>` + 按钮槽契约（公开 C# API 变更）。
5. 改测试：`CenteredSlideBoxTests` / `CenteredSlideBoxDefaultSkinTests` / `CenteredSlideBoxPlayTests`（`confirm`→`button0`，新增多按钮用例）。

**Carousel / BuiltinTags / lint / XSD：零改动**——仍是 C# 模态 API，不是新 XML 标签；按钮行用现有 `<HStack>`/`<Btn>`。

---

## 1. 决策一览（续 CSB-D11）

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| CSB-D12 | 多按钮返回类型 | 具名 `public readonly struct SlideSelection<T> { T Item; string Button; bool Cancelled; Deconstruct(…); }`，**不用匿名元组** | 对齐 `MsgBtn`「为返回值定义具名类型」的惯例；耐扩展（以后加字段不像元组那样改 arity 撞坏解构点）；自带 `Cancelled` 语义；`Deconstruct` 让 `var (lv, act) = await …` 解构写法不损失 |
| CSB-D13 | 按钮规格 | `IEnumerable<(string label, string key)>`，**`key` 用 `string`**。对齐 MessageBox 的 `CustomLabels` | `label` 走 i18n 会被翻译、`key` 稳定用于分支判断（不能拿 label 当 key）；`string` key 零仪式、和库「不强加类型、JSON 自己反序列化」一致。备选「泛型 `Open<T,TKey>` 用 enum」更类型安全但多一个泛型参数，YAGNI |
| CSB-D14 | 重载 / 向后兼容 | 保留单按钮 `Open<T>(…) → Awaitable<T>`；新增多按钮重载，`buttons` **必填**（消除 `Open(items,bind)` 两重载全可选参的歧义）+ **非空**（空列表 → facade 抛 `ArgumentException`） | 不破坏宿主已用的单按钮 API；`buttons` 必填让 C# 重载解析靠第三参类型（`string title` vs `IEnumerable<…>`）区分；选择器没有任何动作按钮 + 又无自动确认 = 不可用，fail-fast |
| CSB-D15 | 内部因式分解 | **两个瘦 request + 一个共享静态 `CenteredSlideBoxBinder`**：`CenteredSlideBoxRequest<T> : ModalRequest<T>`（单按钮，**基类原样不变**、返回 `T`）＋ 新 `CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>>`（多按钮）。两者 `Bind` 都委托 `CenteredSlideBoxBinder.Bind(…, onConfirm:(item,key)=>…, onCancel:()=>…)`，用回调抽掉结果类型差异 | DRY（共用取消三通道/carousel/卡点击/槽探测），又保单按钮 facade **非 async 直传、返回 `T`**——避开「统一成 `ModalRequest<SlideSelection<T>>` + 单按钮 facade async 解包」在 EditMode 下 awaitable 续体不同步恢复（`GetResult()` 挂起）的风险，且现有单按钮 request 测试**零结果形状改动**（只改 `confirm`→`button0` id） |
| CSB-D16 | 自动确认条件化 | `autoConfirm = (buttons.Count == 1)`。单按钮：点居中卡 = 确认（≈现状）；多按钮：点居中卡 = **无操作**，点侧卡照旧居中，必须点某个按钮才关 | 用户明确要求：多按钮时「点居中卡=确认」语义歧义（确认哪条动作？），自动去掉；单按钮保留这个捷径 |
| CSB-D17 | 按钮槽探测 + 超量硬报错 | binder 从 `button0` 起 `Get<Btn>` 探到 `KeyNotFoundException`，数出皮肤槽数 N（**不在代码写死 cap**）。`buttons.Count > N` → 抛 `InvalidOperationException`（消息含 N + `XmlSrcOverride` 提示）；`< N` → 多余槽 `SetActive(false)` 隐藏 | 传超量是开发期写错，fail-fast 比静默丢弃强；槽数由皮肤决定 → 换皮放 `button0..button9` 自动支持 10 个，代码零改。同 `MessageBoxRequest` 探测可选 `icon` 槽的 `try/catch KeyNotFoundException` 套路 |
| CSB-D18 | 默认皮肤按钮行 | 单个 `<Btn id="confirm">` → 底部 `<HStack id="buttons">` + 5 个槽 `button0..button4`（对齐 MessageBox 的 5 槽慷慨度）。单按钮退化：只显 `button0`、居中、文案默认 `OK`，视觉 ≈现状 | 一个选择器动作行现实 2~3 个按钮，5 槽绰绰有余；要更多换皮。**迁移**：内置皮肤 id `confirm`→`button0`（见 §6 兼容性） |

---

## 2. 公开 API

### 2.1 返回类型 `SlideSelection<T>`（新文件）

```csharp
namespace PromptUGUI.Application.Modals
{
    /// 多按钮 CenteredSlideBox 的返回值：选中的卡 + 点击的按钮 key。
    /// 取消（×/背景/ESC）→ default(SlideSelection&lt;T&gt;)：Item=null, Button=null, Cancelled=true。
    public readonly struct SlideSelection<T> where T : class
    {
        public readonly T Item;        // 选中的对象；取消时 null
        public readonly string Button; // 点的按钮 key；取消时 null

        public SlideSelection(T item, string button) { Item = item; Button = button; }

        public bool Cancelled => Button == null;
        public void Deconstruct(out T item, out string button) { item = Item; button = Button; }
    }
}
```

### 2.2 facade（两个重载）

```csharp
public static class CenteredSlideBox
{
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

    // ① 单按钮（向后兼容，CSB-D14/D15）→ 返回选中对象 / null。签名 + 实现都不变（非 async 直传）。
    public static UnityEngine.Awaitable<T> Open<T>(
        IReadOnlyList<T> items, Action<IControl, T> bind,
        string title = null, string confirmLabel = null,
        ModalMode mode = ModalMode.Popup, Action<IScreen> configure = null,
        CancellationToken ct = default) where T : class
        => UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
        {
            Items = items, BindCard = bind, Title = title,
            ConfirmLabel = confirmLabel, Configure = configure,
        }, mode, ct);

    // ② 多按钮（CSB-D12/D13/D14）→ 返回 (选中对象, 按钮 key)。buttons 必填且非空。
    public static UnityEngine.Awaitable<SlideSelection<T>> Open<T>(
        IReadOnlyList<T> items, Action<IControl, T> bind,
        IEnumerable<(string label, string key)> buttons,
        string title = null,
        ModalMode mode = ModalMode.Popup, Action<IScreen> configure = null,
        CancellationToken ct = default) where T : class
    {
        var list = new List<(string label, string key)>(buttons ?? throw new ArgumentNullException(nameof(buttons)));
        if (list.Count == 0) throw new ArgumentException("buttons must be non-empty", nameof(buttons));
        return UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<T>
        {
            Items = items, BindCard = bind, Title = title,
            Buttons = list, Configure = configure,
        }, mode, ct);
    }
}
```

### 2.3 调用（用户的关卡选择例子）

```csharp
record Level(string Id, string Name, Sprite Cover);

var (level, action) = await CenteredSlideBox.Open(
    levels,
    bind: (card, lv) => { card.Get<Text>("name").TextValue = lv.Name;
                          card.Get<Image>("cover").Sprite   = lv.Cover; },
    buttons: new[] { ("立即进入游戏", "play"), ("进入更高难度", "hard") },
    title: "选择关卡");

if (action == null)        return;               // 取消（或用 result.Cancelled）
if (action == "play")      StartLevel(level);
else if (action == "hard") StartLevel(level, hard: true);
```

单按钮老用法**完全不变**（仍返回 `T`）：

```csharp
var picked = await CenteredSlideBox.Open(levels, BindCard, title: "选择关卡");
if (picked != null) StartLevel(picked);
```

---

## 3. 内部：共享 binder + 两个瘦 request（CSB-D15）

所有 Bind 逻辑（title / 取消三通道 / carousel 绑定 / 卡点击装配 / 按钮槽探测 / 超量报错）只写一份在静态 `CenteredSlideBoxBinder` 里，用 `onConfirm(item, key)` / `onCancel()` 回调抽掉「返回 `T` 还是 `SlideSelection<T>`」的差异：

```csharp
internal static class CenteredSlideBoxBinder
{
    // buttons 至少 1 个（两个 request 各自保证）。onConfirm/onCancel 由 request 注入以适配其 TResult。
    public static void Bind<T>(
        IScreen screen, IReadOnlyList<T> items, Action<IControl, T> bindCard, string title,
        IReadOnlyList<(string label, string key)> buttons, string xmlSrcForError,
        Action<T, string> onConfirm, Action onCancel) where T : class
    {
        var titleCtl = screen.Get<Text>("title");
        if (string.IsNullOrEmpty(title)) titleCtl.GameObject.SetActive(false);
        else titleCtl.TextValue = title;

        screen.Get<Btn>("close").OnClick.Subscribe(_ => onCancel()).AddTo(screen);          // 取消三通道（CSB-D9）
        screen.Get<Image>("backdrop").OnPointerDown.Subscribe(_ => onCancel()).AddTo(screen);

        items ??= System.Array.Empty<T>();
        bool autoConfirm = buttons.Count == 1;                       // CSB-D16
        string soleKey = autoConfirm ? buttons[0].key : null;

        var car = screen.Get<Carousel>("cards");
        int idx = 0;
        car.BindItems(Observable.Return(items), (card, item) =>
        {
            int i = idx++;
            bindCard?.Invoke(card, item);
            AttachCardClick(card, i, car, items, onConfirm, autoConfirm, soleKey);          // CSB-D7/D8/D16
        }).AddTo(screen);

        var slots = new List<Btn>();                                 // 探测皮肤槽（CSB-D17）
        for (int i = 0; ; i++)
        {
            try { slots.Add(screen.Get<Btn>($"button{i}")); }
            catch (System.Collections.Generic.KeyNotFoundException) { break; }
        }
        if (buttons.Count > slots.Count)
            throw new InvalidOperationException(
                $"CenteredSlideBox skin '{xmlSrcForError}' provides {slots.Count} button slot(s) but " +
                $"{buttons.Count} buttons were passed; override XmlSrc with more 'button{{i}}' slots.");

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (i >= buttons.Count) { slot.GameObject.SetActive(false); continue; }         // 隐藏多余槽
            var (label, key) = buttons[i];
            if (!string.IsNullOrEmpty(label)) slot.Text = label;     // null/空 → 保留皮肤默认（button0 的 "OK"）
            if (items.Count == 0) slot.Interactable = false;         // CSB-D11
            slot.OnClick.Subscribe(_ =>
            {
                int cur = car.Current;
                if (cur >= 0 && cur < items.Count) onConfirm(items[cur], key);
            }).AddTo(screen);
        }
    }

    static void AttachCardClick<T>(IControl card, int i, Carousel car, IReadOnlyList<T> items,
        Action<T, string> onConfirm, bool autoConfirm, string soleKey) where T : class
    {
        var go = card.GameObject;
        var img = go.GetComponent<UnityImage>() ?? go.AddComponent<UnityImage>();
        img.color = new UnityEngine.Color(0f, 0f, 0f, 0f);          // 透明，仅 raycast
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
```

两个 request 是薄适配层，只把各自的 `close` 映射成 `onConfirm`/`onCancel`：

```csharp
// 单按钮：原样保留 ModalRequest<T>，返回 T（现有测试零结果形状改动）
public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
{
    public IReadOnlyList<T> Items;
    public Action<IControl, T> BindCard;
    public string Title;
    public string ConfirmLabel;                 // 单个按钮的 label（空→皮肤默认 "OK"）
    public string XmlSrcOverride;

    public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;
    public override bool TryEscape(out T result) { result = null; return true; }

    public override void Bind(IScreen screen, Action<T> close)
        => CenteredSlideBoxBinder.Bind(screen, Items, BindCard, Title,
               new[] { (ConfirmLabel, (string)null) }, XmlSrc,        // 1 个隐式按钮；key 忽略
               onConfirm: (item, _) => close(item),
               onCancel: () => close(null));
}

// 多按钮：ModalRequest<SlideSelection<T>>
public sealed class CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>> where T : class
{
    public IReadOnlyList<T> Items;
    public Action<IControl, T> BindCard;
    public string Title;
    public IReadOnlyList<(string label, string key)> Buttons;     // facade 保证非空
    public string XmlSrcOverride;

    public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;
    public override bool TryEscape(out SlideSelection<T> result) { result = default; return true; }  // (null,null)=Cancelled

    public override void Bind(IScreen screen, Action<SlideSelection<T>> close)
        => CenteredSlideBoxBinder.Bind(screen, Items, BindCard, Title, Buttons, XmlSrc,
               onConfirm: (item, key) => close(new SlideSelection<T>(item, key)),
               onCancel: () => close(default));
}
```

> 与 CSB-D8 一致：卡根（默认 `<Frame>`）本无 Graphic，`AttachCardClick` 补一层透明 raycast `Image`；换皮卡根须是无 Graphic 容器。

---

## 4. 内置 `CenteredSlideBox.ui.xml`（仅按钮行变更）

`backdrop` / `panel` / `title` / `close` / `cards` 不变；底部单个 `<Btn id="confirm">` 换成按钮行：

```xml
<!-- 旧：<Btn id="confirm" anchor="bottom-center" size="160x48" margin="_,_,12,_">OK</Btn> -->
<HStack id="buttons" anchor="bottom-center" height="48" margin="_,_,16,_" spacing="16">
  <Btn id="button0" size="160x48">OK</Btn>
  <Btn id="button1" size="160x48"/>
  <Btn id="button2" size="160x48"/>
  <Btn id="button3" size="160x48"/>
  <Btn id="button4" size="160x48"/>
</HStack>
```

- **居中、hug 内容宽**：`anchor="bottom-center"`（X 非 stretch → 宽由内容定），HStack 抱紧子按钮、隐藏槽 `SetActive(false)` 后在布局里折叠，整行随可见按钮数自动重新居中。
- **固定 `size="160x48"`**：HStack 子节点固定尺寸经 LayoutElement min/preferred 固定（同 fixed-size stack child 既有行为），视觉一致。
- 单按钮：只 `button0` 可见 → 居中的单个 160×48 按钮，≈旧 `confirm` 视觉。
- > ⚠️ 精确 hug/居中行为（HStack 在 `bottom-center` 下是否需要额外尺寸属性）在 TDD 里以 live uGUI 矩形断言验证；设计意图固定为「底部居中、内容 hug、隐藏折叠」。

**id 契约更新**：原 `confirm` → `button0..button4`（连续，从 0 起）。换皮 XML 想要 K 个动作按钮就提供 `button0..button{K-1}`。

---

## 5. 交互流（CSB-D7/D8/D9/D16）

```
开窗 → BindItems 建卡（每卡挂透明 raycast + PuiButton）→ 居中默认页(current=0)
拖动 / 点 dots → 翻页，焦点卡放大不淡（peek）
点侧卡   → car.GoTo(i) 居中它（动画），不关窗

单按钮(autoConfirm)：点居中卡 → onConfirm(Items[i], soleKey) ；button0 → onConfirm(居中项, key)
多按钮             ：点居中卡 → 无操作              ；button{i} → onConfirm(居中项, buttons[i].key)

× / 点背景 / ESC → onCancel() → await 返回取消
```

- **单按钮 request**：`onConfirm:(item,_)=>close(item)` / `onCancel:()=>close(null)` → 返回 `T`，取消 = `null`（契约同 CSB-D3，零变化）。
- **多按钮 request**：`onConfirm:(item,key)=>close(new SlideSelection<T>(item,key))` / `onCancel:()=>close(default)` → 返回 `SlideSelection<T>`，取消 = `Cancelled`。

---

## 6. 兼容性 / 迁移

| 面 | 影响 |
|---|---|
| 公开 facade `Open<T>(…) → Awaitable<T>`（单按钮） | **签名 + 实现都不变**（仍非 async 直传）；返回值/取消语义一致 |
| 公开 facade 多按钮 | **新增重载**，不影响旧调用 |
| `public CenteredSlideBoxRequest<T>`（单按钮 request） | **基类不变**（仍 `ModalRequest<T>`、仍返回 `T`）；唯一内部变化是 Bind 改为委托 binder + 字段从「直接逻辑」变成传 binder。直接 `new` 它的代码（测试/命名变体）**零结果形状改动** |
| `public CenteredSlideBoxMultiRequest<T>`（新增） | 全新类型，多按钮 facade / 命名变体用 |
| 内置皮肤 id `confirm` → `button0` | **皮肤契约变更**。用默认皮肤者无感；自定义 `XmlSrcOverride` 皮肤需把 `id="confirm"` 改成 `id="button0"`（要多按钮再加 `button1..`）。SKILL 文档同步更新槽位契约 |

> `confirm` 不保留别名兜底——保持探测逻辑单一（纯 `button{i}` 序列）。若宿主已有自定义皮肤依赖 `confirm`，在 plan 阶段确认后可加一行 `button0` 缺失时回退 `confirm` 的兼容垫片；默认不加。

---

## 7. 边界 / 错误处理

| 场景 | 处理 |
|---|---|
| 多按钮 `buttons` 空 | facade 抛 `ArgumentException`（CSB-D14），开窗前 |
| `buttons.Count` > 皮肤槽数 | binder 抛 `InvalidOperationException`（CSB-D17）。注意：抛在 `RunBind` 内 → `UI.Modal` 的 `MaterializePump` `try/catch` 把它转成 **faulted awaitable**（不是同步冒泡）；`task.GetAwaiter().GetResult()` 时重抛 |
| `buttons.Count` < 皮肤槽数 | 多余槽隐藏（`SetActive(false)`） |
| `items` 空 | carousel 空；**所有可见按钮** `Interactable=false`（灰）；只能取消 |
| `items` 1 张 | 居中、无邻卡；点按钮/点居中卡（单按钮）返回该项 |
| 多按钮 + 点居中卡 | 无操作（CSB-D16） |
| 换皮缺 `button0` | 探测得 0 槽；若传了按钮 → 抛 `InvalidOperationException`（槽数 0）。无 `button0` 的皮肤即「无动作按钮」皮肤（仅取消）|
| `ct` 取消 | `UI.Modal.OpenAsync` 关窗 + Awaitable 取消（同其它模态） |

---

## 8. 测试

EditMode（沿用现有 `CenteredSlideBoxTests` 套路 + `UI.ResetForTests`；按钮文案读 `GameObject.GetComponentInChildren<TMP_Text>().text`，`Btn.Text` 是 set-only）:

- **现有单按钮用例**：**只改 `confirm`→`button0` id**（结果形状/`ConfirmLabel`/`TryEscape(out T)` 全不变）后全绿（确认返回居中项 / 点居中卡返回 / 取消三通道 / 空列表禁用 / confirmLabel 改文案 / 单项确认）。
- **多按钮 → 按钮 key 进返回**：`new CenteredSlideBoxMultiRequest<Lv>{ Buttons=[("A","a"),("B","b")] }`；`GoTo(k)`；点 `button1` SimulateClick；await == `Item==items[k] && Button=="b"`。
- **多按钮 → 点居中卡无操作**：2 按钮；`CardButton(0).onClick.Invoke()`；`UI.Modal.IsAnyOpen` 仍 true、`car.Current` 不变（区别单按钮的确认）。
- **多按钮取消**：×/背景/ESC → `result.Cancelled == true`（`Item==null && Button==null`）。
- **超量报错**：测试 XML 给 3 槽，传 4 个按钮 → `Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult())`（faulted awaitable，见 §7）。
- **空 buttons 列表**：多按钮 facade 传 `Array.Empty<(string,string)>()` → `Assert.Throws<ArgumentException>(…)`（facade 同步抛）。
- **隐藏槽**：传 2 个按钮（3 槽测试 XML）→ `button2` `activeSelf==false`、`button0/button1` 可见且文案正确。
- **单按钮自动确认仍在**：单按钮 request 点居中卡 → 返回对象（autoConfirm 未被多按钮逻辑误关）。
- **多按钮 facade happy-path**：`CenteredSlideBox.Open(items, bind, buttons:[("A","a"),("B","b")])` → 点 button0 → `Button=="a"`（验证 facade 非 async 直传、`GetResult` 可同步取）。

PlayMode：烟雾——多按钮开窗、拖一格、点第二个按钮，`SlideSelection.Button` 命中、不崩。

---

## 9. 文件结构

| 文件 | 动作 |
|---|---|
| `Runtime/Application/Modals/SlideSelection.cs`(+.meta) | 新建（`SlideSelection<T>` struct） |
| `Runtime/Application/Modals/CenteredSlideBoxRequest.cs` | 改：新增 `CenteredSlideBoxBinder`（搬入 Bind 逻辑 + 探测槽 + autoConfirm 条件化）；`CenteredSlideBoxRequest<T>` 改委托 binder（基类不变）；新增 `CenteredSlideBoxMultiRequest<T>`；facade 加多按钮重载 |
| `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml` | 改（`confirm` → `<HStack id="buttons">` + `button0..button4`） |
| `Tests/EditMode/Modals/CenteredSlideBoxTests.cs` | 改：XML const `confirm`→`button0`（HStack+button0..button2，3 槽）、4 处 `Get<Btn>("confirm")`→`"button0"`；加多按钮/超量/隐藏槽/无操作用例 |
| `Tests/EditMode/Modals/CenteredSlideBoxDefaultSkinTests.cs` | 改（3 处 `Get<Btn>("confirm")`→`"button0"`） |
| `Tests/PlayMode/Modals/CenteredSlideBoxPlayTests.cs` | 改（多按钮烟雾） |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 改（多按钮重载 + `SlideSelection<T>` + 按钮槽契约） |

---

## 10. Out of Scope

- **per-按钮 启用/禁用 / 样式**（如「高难度」仅满足条件才可点）：v1 所有按钮统一可点（空列表全禁）；细粒度走 `configure` 钩子自取 `screen.Get<Btn>("button1").Interactable=…`。
- **per-按钮 不绑选中项**（如「随机一关」忽略居中卡）：v1 所有按钮都带居中项；这类需求自定逻辑。
- **泛型 `TKey`**：v1 用 `string` key（CSB-D13）；要 enum 自己 `nameof`/`ToString`。
- **运行时动态实例化按钮（真·无上限）**：固定槽 + 探测 + 超量报错已覆盖现实场景（CSB-D17）。
- **按钮区位置/方向自定义**（底部固定）：换皮 XML 自理。

---

## 11. 整合点

- `scripting-promptugui-csharp/SKILL.md` 模态小节：现有 `CenteredSlideBox.Open<T>` 旁补多按钮重载（`buttons:[(label,key),…]` → `SlideSelection<T>`：`Item`+`Button`+`Cancelled`+解构；多按钮自动去掉点居中卡确认；按钮槽契约 `button0..`；超量报错）。
- XML / Addressables skill：无关（无新标签/属性）。
- 主 spec / XSD / lint：无新 XML 标签或属性，不动。
- 建立在 CSB-D1..D11（同特性），本文只增量。
