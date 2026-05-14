# Pointer Event Triggers (`hover-enter` / `hover-exit` / `press`) 设计

**日期**：2026-05-14
**状态**：设计阶段（待 review，未进入实施）
**作用域**：扩展 `<Trigger>` / `<Animation>` 的 `on=` DSL，新增 3 类 pointer-event 触发器（hover-enter / hover-exit / press），事件源支持 `<Btn>` 和 `<Image>` 两种控件。
**依赖**：
- [`2026-05-14-litmotion-animations-design.md`](2026-05-14-litmotion-animations-design.md) §2 ANIM-D4/D5/D6（现有 `on=` DSL + click 子树查找规则）
- `Runtime/Controls/Trigger.cs` + `Runtime/Controls/Internal/TriggerSpec.cs` + `Runtime/Controls/Internal/TriggerSourceResolver.cs`（这次都要改）

---

## 1. 背景与目标

v1 的 `<Trigger>` 只支持 `on="click"`（Btn 内置 `Button.onClick` 流）。游戏 UI 常见的反馈需求里，hover 起色 / hover 退色 / 长按动画 这一类需要 pointer-event 级别的 trigger：

- 按钮 hover 起：缩放放大或微 glow → 需要 `on="hover-enter"`
- 按钮 hover 退：复位 → 需要 `on="hover-exit"`
- 按钮 / 图标按下瞬间反馈：scale 0.95 → 需要 `on="press"`
- 装饰性 Image 上挂 hover 提示动画 → 需要 Image 作为事件源

uGUI 的 `IPointerEnterHandler` / `IPointerExitHandler` / `IPointerDownHandler` 是这三类事件的官方接口，触摸 / 鼠标 / 笔输入都走同一套，跨平台一致。

### 1.1 用例

```xml
<!-- hover 起：放大微缩 -->
<Animation type="bounce" on="hover-enter" duration="0.15s">
  <Btn id="play">Play</Btn>
</Animation>

<!-- 按下瞬间反馈：scale 0.95 -->
<Animation scale="1:0.95" on="press" duration="0.08s">
  <Btn id="ok">OK</Btn>
</Animation>

<!-- Image 作为事件源 -->
<Animation type="fadein" on="hover-enter@portrait" duration="0.2s">
  <Image id="portrait" sprite="ui/hero.png"/>
  <Text>Hero Name</Text>
</Animation>

<!-- 命名 hook + C# 反应 -->
<Trigger id="card-hover" on="hover-enter@card">
  <Image id="card" sprite="ui/card.png"/>
</Trigger>
```

### 1.2 目标

1. 扩 `on=` DSL 三个新值：`hover-enter` / `hover-exit` / `press`（每个都支持 `@<id>` 形式）。
2. 事件源支持 **`<Btn>` + `<Image>`**：`<Btn>` 已经默认 `raycastTarget=true`（由内部 `Image` 提供），`<Image>` 也是默认 true。其他控件（`<Icon>` 硬编码 raycastTarget=false / `<Text>` / `<Frame>`）作为事件源会报错。
3. 触发后行为完全复用现有 `Trigger.Fire()` / `Animation.OnTriggerFired()` 链路 —— 不引入新的 trigger 类型分支，只引入新的事件源订阅路径。
4. `on="click"` 不扩展到 Image —— `Button.onClick` 内置的交互语义（drag-cancel / disabled state 抑制）是按钮场景的契约，不要让 Image 假装是 Button。需要 Image 上的"按下"用 `press`。

### 1.3 显式不做

- ❌ **`press-hold` / `release`**：v1 只覆盖瞬时事件（enter / exit / down）。"按住期间持续"是状态而非事件，需要不同的 trigger 模型，v2 再说
- ❌ **`drag-start` / `drag` / `drag-end`**：drag 是更复杂的 `IDragHandler` 流，跨度太大，v2 独立设计
- ❌ **`<Icon>` 作为事件源**：Icon 硬编码 `raycastTarget=false`，spec 不会临时改这个；作者要 Icon 上的 hover 反馈，用 `<Image>` 替代或用 `<Btn>` 包装
- ❌ **`<Text>` / `<Frame>` 作为事件源**：Text 默认 `raycastTarget=false`，Frame 没 Graphic 收不到 raycast；这俩作为 `@id` 引用直接报错
- ❌ **raycastTarget 检查**：作者把 Image 的 `raycastTarget` 显式设成 `false` 又作为 hover trigger 源是合法 XML，运行时事件不来，**不报错**（parse 期没法知道 ApplyCommon 之后 raycastTarget 是什么值）。这条 SKILL.md 标 caveat
- ❌ **`on="click"` 扩展到 Image**：保留 Btn-only，理由见 §1.2

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| PE-D1 | 新增 on= 值 | `hover-enter` / `hover-exit` / `press` 各自 + `@<id>` 后缀 = 6 个新合法 on= 值 | 跟现有 `click` / `click@<id>` 同结构；用"动作名"而非 Unity API 名（pointer-enter）保持 `click` 的友好度 |
| PE-D2 | `press` 语义 | = `IPointerDownHandler.OnPointerDown`（按下瞬间），不包括松开和长按状态 | 跟其他两个保持"事件而非状态"一致；松开 / 长按是 v2 范围 |
| PE-D3 | 事件源控件范围 | `<Btn>` + `<Image>`；其他（Icon/Text/Frame/Toggle/Slider/Dropdown/ScrollList/InputField/SafeArea/Trigger/Animation）作为 `@id` 引用 → 运行时 InvalidOperationException with 类型提示 | Btn/Image 是显式的"可视化 + 可点击"组合；其他控件要么有自己的交互语义（Toggle/Slider 等）要么不该接 pointer events |
| PE-D4 | `on="click"` 是否扩到 Image | 否 | click 走 `Button.onClick`（内置 drag-cancel / disabled state 抑制），Image 没这套语义；想要 Image 按下用 `press` |
| PE-D5 | 实现路径 | 新增 `internal sealed class PointerEventRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler`，懒加载到 Btn / Image 的根 GameObject 上 | 集中转发，避免 Btn / Image 各自实现一遍 IPointer* 接口；单点 dispose |
| PE-D6 | Btn 和 Image 暴露 streams 的方式 | 新增 `internal interface IPointerEventSource { Observable<Unit> OnPointerEnter / OnPointerExit / OnPointerDown }`；Btn + Image 实现接口，getter 内部 lazy-add `PointerEventRelay` 并委托 | TriggerSourceResolver 返回 `IPointerEventSource` 统一类型；Btn / Image 类型一致；未来若 Toggle 等也要加，实现接口即可 |
| PE-D7 | TriggerSourceResolver 新增方法 | 新增 `FindPointerSource(Trigger trigger, string sourceId) → IPointerEventSource`（独立于 `FindBtn`） | 不污染现有 `FindBtn` 的语义（FindBtn 只为 click 用）；新方法的查找规则完全平行：`sourceId==null` → 子树里唯一 Btn 或 Image；多个 → 报错；`sourceId != null` → ScopedIds 查找 + 类型校验 |
| PE-D8 | TriggerSpec.Parse 扩展 | 增加 3 个 bare value + 3 个 `<name>@<id>` value 的 case，全部复用 `TriggerSpec.Kind` 字段（新值进 `TriggerKind` 枚举）和 `TriggerSpec.SourceId` 字段，**不新增字段** | 跟现有 click@<id> 同模式；3 个新 Kind 值跟 Click 共享同一 SourceId 字段语义 |
| PE-D9 | Trigger.InitTriggerSubscription 扩展 | 加 3 个 case 到 switch，每个 case 调用 `SubscribePointer(kind)`，内部走 FindPointerSource + 订阅对应 Observable | 不引入 Trigger 子类；既有 Open/Loop/Click/Manual 分支不变 |
| PE-D10 | TriggerKind 枚举扩展 | 加 3 个值：`HoverEnter`, `HoverExit`, `Press` | 同枚举集中所有 trigger 类型；TriggerSpec.Kind 字段语义不变 |
| PE-D11 | 错误消息 | `@id` 引用非 Btn / 非 Image 控件 → InvalidOperationException 提示"id 'X' is a Y, not supported as pointer source. Use Btn or Image."；空 / 多目标用现有的"no Btn or Image found"/"ambiguous"消息 | 友好定位错误源头 |
| PE-D12 | raycastTarget 校验 | 不做；运行时不报错 | parse 期看不到 ApplyCommon 之后的属性值；不值得为这个加运行时探测。SKILL.md 标 caveat |
| PE-D13 | Image 暴露 streams 时的 raycastTarget 副作用 | 不主动改 raycastTarget；保持 Image 当前默认（true）+ 作者控制 | 不让 SKILL.md "<Image> 默认 raycastTarget=true" 这个事实意外被某些代码路径反转 |
| PE-D14 | PointerEventRelay 文件位置 | `Runtime/Controls/Internal/PointerEventRelay.cs` | 跟 TriggerSourceResolver / ScopedIds resolver 等内部辅助一致 |
| PE-D15 | IPointerEventSource 接口位置 | `Runtime/Controls/Internal/IPointerEventSource.cs` | internal 接口，跟 PointerEventRelay 同一目录 |
| PE-D16 | 单元测试 | EditMode（parse + resolver）+ PlayMode（Btn 上 hover-enter 真触发；Image 上 hover-enter 真触发；press 在 Btn 上；@id 跨 Btn/Image 引用错误） | 跟现有 TriggerTests / AnimationPlayTests 分布一致 |
| PE-D17 | SKILL.md 同步 | authoring-promptugui-xml/SKILL.md 加 3 个新 on= 值到 trigger 表 + 事件源范围 + raycastTarget caveat；scripting-promptugui-csharp/SKILL.md 不动（C# 没新 API，全 XML 触发） | 跟 v1 同步策略一致 |

---

## 3. 改动面

### 3.1 新文件 `Runtime/Controls/Internal/IPointerEventSource.cs`

```csharp
using R3;

namespace PromptUGUI.Controls.Internal
{
    internal interface IPointerEventSource
    {
        Observable<Unit> OnPointerEnter { get; }
        Observable<Unit> OnPointerExit  { get; }
        Observable<Unit> OnPointerDown  { get; }
    }
}
```

### 3.2 新文件 `Runtime/Controls/Internal/PointerEventRelay.cs`

```csharp
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    internal sealed class PointerEventRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private readonly Subject<Unit> _enter = new();
        private readonly Subject<Unit> _exit  = new();
        private readonly Subject<Unit> _down  = new();

        public Observable<Unit> OnPointerEnter => _enter;
        public Observable<Unit> OnPointerExit  => _exit;
        public Observable<Unit> OnPointerDown  => _down;

        void IPointerEnterHandler.OnPointerEnter(PointerEventData e) => _enter.OnNext(Unit.Default);
        void IPointerExitHandler.OnPointerExit(PointerEventData e)   => _exit.OnNext(Unit.Default);
        void IPointerDownHandler.OnPointerDown(PointerEventData e)   => _down.OnNext(Unit.Default);

        private void OnDestroy()
        {
            _enter.Dispose();
            _exit.Dispose();
            _down.Dispose();
        }
    }
}
```

### 3.3 修改 `Runtime/Controls/Btn.cs`

加 `IPointerEventSource` 实现：

```csharp
public sealed class Btn : Control, IPointerEventSource
{
    // ... existing fields ...
    private Internal.PointerEventRelay _pointerRelay;

    private Internal.PointerEventRelay EnsureRelay()
        => _pointerRelay ??= GameObject.AddComponent<Internal.PointerEventRelay>();

    public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
    public Observable<Unit> OnPointerExit  => EnsureRelay().OnPointerExit;
    public Observable<Unit> OnPointerDown  => EnsureRelay().OnPointerDown;

    // ... rest unchanged ...
}
```

### 3.4 修改 `Runtime/Controls/Image.cs`

同 Btn.cs 加 `IPointerEventSource` 实现 + lazy `PointerEventRelay`。

### 3.5 修改 `Runtime/Controls/Internal/TriggerSpec.cs`

`TriggerKind` 加 3 个值：

```csharp
internal enum TriggerKind { Open, Loop, Click, Manual, HoverEnter, HoverExit, Press }
```

`TriggerSpec.Parse` 扩展（在 click@ 分支之前加 3 个对称分支）：

```csharp
public static TriggerSpec Parse(string value)
{
    if (string.IsNullOrEmpty(value)) return new TriggerSpec { Kind = TriggerKind.Open };
    switch (value)
    {
        case "open":        return new TriggerSpec { Kind = TriggerKind.Open };
        case "loop":        return new TriggerSpec { Kind = TriggerKind.Loop };
        case "manual":      return new TriggerSpec { Kind = TriggerKind.Manual };
        case "click":       return new TriggerSpec { Kind = TriggerKind.Click };
        case "hover-enter": return new TriggerSpec { Kind = TriggerKind.HoverEnter };
        case "hover-exit":  return new TriggerSpec { Kind = TriggerKind.HoverExit };
        case "press":       return new TriggerSpec { Kind = TriggerKind.Press };
    }
    return ParseWithId(value);  // 处理 click@id / hover-enter@id / hover-exit@id / press@id
}

private static TriggerSpec ParseWithId(string value)
{
    foreach (var (prefix, kind) in s_prefixedKinds)
    {
        if (value.StartsWith(prefix))
        {
            var id = value.Substring(prefix.Length);
            if (string.IsNullOrEmpty(id) || id.Contains('@'))
                throw new ArgumentException(...);
            return new TriggerSpec { Kind = kind, SourceId = id };
        }
    }
    throw new ArgumentException(...);
}

private static readonly (string prefix, TriggerKind kind)[] s_prefixedKinds = {
    ("click@",       TriggerKind.Click),
    ("hover-enter@", TriggerKind.HoverEnter),
    ("hover-exit@",  TriggerKind.HoverExit),
    ("press@",       TriggerKind.Press),
};
```

### 3.6 修改 `Runtime/Controls/Internal/TriggerSourceResolver.cs`

新增 `FindPointerSource` 方法，跟 `FindBtn` 并列：

```csharp
public static IPointerEventSource FindPointerSource(Trigger trigger, string sourceId)
{
    if (!string.IsNullOrEmpty(sourceId))
    {
        if (!trigger.ScopedIds.TryGetValue(sourceId, out var ctrl))
            throw new InvalidOperationException(
                $"<Trigger on=\"...@{sourceId}\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                $"id '{sourceId}' not found in trigger subtree scope");
        return ctrl as IPointerEventSource ?? throw new InvalidOperationException(
            $"<Trigger on=\"...@{sourceId}\">: id '{sourceId}' is a " +
            $"{ctrl.GetType().Name}, not supported as pointer event source. Use <Btn> or <Image>.");
    }

    var found = new List<IPointerEventSource>();
    CollectPointerSources(trigger, found);
    if (found.Count == 0)
        throw new InvalidOperationException(
            $"<Trigger> in '{trigger.Id ?? trigger.GameObject.name}': " +
            "no <Btn> or <Image> found in subtree. Add one or use @<id>.");
    if (found.Count > 1)
        throw new InvalidOperationException(
            $"<Trigger> in '{trigger.Id ?? trigger.GameObject.name}': " +
            $"ambiguous — found {found.Count} pointer-event-source descendants. " +
            "Use on=\"...@<id>\" to disambiguate.");
    return found[0];
}

private static void CollectPointerSources(IControl c, List<IPointerEventSource> outList)
{
    foreach (var child in c.Children)
    {
        if (child is IPointerEventSource src)
        {
            outList.Add(src);
            // 跟 CollectBtns 一致：source 是 leaf，不再下钻其子节点
        }
        else if (child is Control cc)
        {
            CollectPointerSources(cc, outList);
        }
    }
}
```

### 3.7 修改 `Runtime/Controls/Trigger.cs`

`InitTriggerSubscription` 加 3 个 case：

```csharp
protected virtual void InitTriggerSubscription()
{
    switch (_spec.Kind)
    {
        case TriggerKind.Open:
        case TriggerKind.Loop:
            Fire();
            break;
        case TriggerKind.Click:
            SubscribeClick();
            break;
        case TriggerKind.HoverEnter:
        case TriggerKind.HoverExit:
        case TriggerKind.Press:
            SubscribePointer(_spec.Kind);
            break;
        case TriggerKind.Manual:
            break;
    }
}

private void SubscribePointer(TriggerKind kind)
{
    var src = Internal.TriggerSourceResolver.FindPointerSource(this, _spec.SourceId);
    var stream = kind switch
    {
        TriggerKind.HoverEnter => src.OnPointerEnter,
        TriggerKind.HoverExit  => src.OnPointerExit,
        TriggerKind.Press      => src.OnPointerDown,
        _ => throw new InvalidOperationException("unreachable"),
    };
    _sourceSub = stream.Subscribe(_ => Fire());
}
```

### 3.8 修改 `.claude/skills/authoring-promptugui-xml/SKILL.md`

在 `<Trigger>` `on=` 值表里新增 3 行（hover-enter / hover-exit / press），各自标明：

- 触发：`IPointerEnterHandler` / `IPointerExitHandler` / `IPointerDownHandler`（不在 SKILL.md 直接写接口名，写"指针进入子树事件源 / 离开 / 按下"）
- 可作事件源的控件：`<Btn>` 和 `<Image>`
- `@<id>` 形式：跟 `click@<id>` 一致

加 caveat 段：

- `<Image>` 默认 `raycastTarget=true`，能收 pointer events。手动设 `raycastTarget="false"` 后再作为 hover/press trigger 源 → 运行时事件不触发，无报错。
- `<Icon>` 硬编码 `raycastTarget=false`，**不能**作为 hover/press 事件源。作者要 Icon 上的反馈，外面包 `<Btn>` 或换成 `<Image>`。
- `<Text>` 默认 `raycastTarget=false`，同上。
- `<Frame>` 没有 Graphic 收不到 raycast，同上。
- `press` 是按下瞬间（pointer-down），不是长按状态。松开 / 长按 v2 再加。

---

## 4. 测试范围

### 4.1 EditMode

`Tests/EditMode/Controls/TriggerSpecTests.cs` 追加：

- `Hover_enter_parses` / `Hover_exit_parses` / `Press_parses`（bare）
- `Hover_enter_with_id_parses` / `Hover_exit_with_id_parses` / `Press_with_id_parses`
- 非法形式：`hover-enter@` 空 id / `press@a@b` 双 @ → throws

`Tests/EditMode/Controls/TriggerTests.cs` 追加 4 个：

- `Pointer_trigger_subtree_unique_Btn_resolves` — 子树唯一 Btn，hover-enter 解析成功（不模拟事件触发）
- `Pointer_trigger_subtree_unique_Image_resolves` — 子树唯一 Image，press 解析成功
- `Pointer_trigger_subtree_multiple_sources_no_id_throws` — 子树有 Btn + Image 两个，无 `@id` → throws
- `Pointer_trigger_at_id_pointing_to_Text_throws` — `@id` 指向 Text 类型 → throws "not supported as pointer event source"

### 4.2 PlayMode

`Tests/PlayMode/Controls/PointerTriggerPlayTests.cs`（新文件）：

模拟 PointerEvent 通过 `UnityEngine.EventSystems.ExecuteEvents.Execute<...>(targetGo, pointerEventData, ExecuteEvents.pointerEnterHandler)`：

- `HoverEnter_fires_on_Btn_pointer_enter`
- `HoverExit_fires_on_Btn_pointer_exit`
- `Press_fires_on_Btn_pointer_down`
- `HoverEnter_fires_on_Image_pointer_enter`
- `Press_fires_on_Image_pointer_down`
- `Press_triggers_Animation_scale` —— `<Animation scale="1:0.95" on="press">` PointerDown 后 scale 真的变化

每个测试：建一个 EventSystem，构 PointerEventData，ExecuteEvents.Execute 模拟事件，断言 OnFire 次数 / Animation 状态变化。

### 4.3 XSD 生成器

`Tests/EditMode/Editor/XsdGeneratorTests.cs` 现有 `on=` 属性是 string-typed，新值不会触发 XSD 结构变化。XSD 测试无新增需求。

---

## 5. 风险 / 开放问题

1. **PointerEventRelay 跟 Btn 内置 Button 的事件传播**：Unity Button 自身实现 `IPointerEnterHandler` / `IPointerExitHandler` / `IPointerDownHandler`（用于状态切换）。同一 GameObject 上挂额外的 `PointerEventRelay`（也实现这些接口）时，Unity 的 EventSystem 会把事件 dispatch 给**所有**实现接口的 component。所以 Btn 的 Button 自身状态切换 + Relay 都会触发，互不干扰。**风险：低**。实施前用一个 PlayMode 测试确认（同 GameObject 上 Button + Relay 都收到 pointer enter）。

2. **R3 Subject 内部线程安全**：PointerEvent 由 Unity 主线程 dispatch，Subject.OnNext 在主线程触发；订阅者通常也在主线程。**无并发风险**。

3. **PointerEventRelay 在 EditMode 测试里的行为**：`MonoBehaviour.OnPointerEnter` 是 Unity 调用的；EditMode 没 Unity 运行时事件系统。EditMode 测试只测 parse + resolver 路径（不模拟事件触发），PlayMode 测试覆盖真触发。这是已经设计中的拆法，再次确认。

4. **`<Image>` 已经有 `raycastTarget` 属性可以被作者设为 false**：本设计不主动改这个默认。如果作者关掉了又作为事件源，pointer 事件本身就不会 dispatch 到 GameObject，相当于 trigger 永远不 fire。无错误，但也"看起来什么都没发生"。SKILL.md caveat 已经覆盖。

5. **Animation 内部的 `_offsetProxy` 是否拦截 pointer events**：`_offsetProxy` 是个空 RectTransform（无 Graphic），所以本身不参与 raycast。Animation 包住的 Btn / Image 子节点正常接收事件。**无风险**。

6. **多个 Trigger 共享一个 source**：`<Animation type="bounce" on="hover-enter@btn">` 和 `<Trigger id="hook" on="hover-enter@btn">` 都引用同一个 Btn。两个都会订阅 Relay 的 OnPointerEnter 流，独立 fire。R3 Subject 多订阅是天然支持的。**无风险**。

---

## 6. 实施顺序（写 plan 时细化）

1. `IPointerEventSource` 接口 + `PointerEventRelay` 组件（含 Disposable）
2. `Btn` 和 `Image` 分别实现 `IPointerEventSource`（lazy relay）+ smoke test 确认双 Button-Relay 不冲突
3. `TriggerKind` 枚举扩展 + `TriggerSpec.Parse` 新分支 + EditMode parse 测试
4. `TriggerSourceResolver.FindPointerSource` + EditMode 测试（4 个错误路径）
5. `Trigger.InitTriggerSubscription` 加 3 个 case + `SubscribePointer` 助手 + EditMode + PlayMode 测试
6. SKILL.md 新增表行 + caveat 段
7. 跑全套测试确认无回归
