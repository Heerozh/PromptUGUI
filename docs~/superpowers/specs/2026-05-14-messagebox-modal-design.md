# 通用模态对话框系统 + 内置 MessageBox 设计

**日期**：2026-05-14
**状态**：设计阶段（待 review，未进入实施）
**作用域**：新增 `PromptUGUI.Application.Modals` 命名空间，提供：(1) 通用模态对话框管理器 `UI.Modal`（队列 / sortingOrder / ESC 监听 / 生命周期）；(2) 抽象基类 `ModalRequest<TResult>` 让用户扩展自定义 modal 类型（含 slot 内容和自定义 result）；(3) 默认 modal 类型 `MessageBoxRequest` + 静态 wrapper `MessageBox.Open(text, buttons, icon, title)`；(4) 内置 `.ui.xml` 模板 + 用户可整体覆盖。
**依赖**：[`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §6（XML 描述语言）、§7（C# API）；`Runtime/Application/UI.cs`（Screen 加载流水线、`UI.Open`、`UI.Close`、`SourceResolver`、`ResetForTests`）；`Runtime/Application/Screen.cs`（Canvas / sortingOrder）；`Runtime/Application/AwaitableHelpers.cs`（`AwaitableCompletionSource<T>` 同步包装）

---

## 1. 背景与目标

PromptUGUI 当前没有模态对话框抽象。Unity 工程师常见需求：

```csharp
var r = await MessageBox.Open(UI.Tr("Save changes?"), Btn.Yes | Btn.No | Btn.Cancel);
if (r == Btn.Yes) await game.SaveAsync();
```

技术上模态对话框等价于：

1. 一个普通 Screen（PromptUGUI 既有流水线已经能产生 GameObject + Canvas + Raycaster）
2. **sortingOrder 高于所有普通 Screen**，盖在屏幕最上层
3. **一个全屏 Image 拦截 raycast**，阻止下层 Screen 收到点击
4. **TaskCompletionSource** 把 Screen 关闭事件接成 await 返回值
5. **全局队列**：同时只能有一个 modal 在屏，新请求 FIFO 排队

调研结果：
- 仓库内 grep `Modal` / `MessageBox` / `Dialog` / `Popup` 零命中（命中的 `Modal` 在 Dropdown 内部是局部变量名，不相关）
- `Runtime/Application/Screen.cs` 已经在 `Open()` 里创建 Canvas + CanvasScaler + GraphicRaycaster；`UI.CanvasConfigurator` 是 sortingOrder 设置的 hook
- `Runtime/Application/AwaitableHelpers.cs` 已经封装了把同步值包成 `Awaitable<T>` 的工具（`Completed(value)` / `Faulted<T>(ex)`）；新模态系统加一个 `AwaitableCompletionSource<TResult>` 持有未完成 task 即可
- `Runtime/Controls/Btn.cs` 的 `_click` 是 `Subject<Unit>`，已经支持订阅；EditMode 测试已通过 `InternalsVisibleTo` 能访问 internal 成员

### 设计原则

1. **吃自己狗粮**：modal 是一个普通 Screen，走完整 PromptUGUI 流水线（XML → IR → Templates → GameObject），CanvasScaler / IconResolver / Locale / Variant 都自动生效
2. **扩展点优先**：抽象基类 `ModalRequest<TResult>` 在 day 1 就开放，MessageBox 是其中**一个**实现而非唯一形态；后续 InputField pop-up / 道具 tooltip / 多按钮自定义 modal 都用同一套机制
3. **API 类型安全**：TResult 走泛型，不用 `object` / `Dictionary<string,object>` payload
4. **caller 端 API 最小化**：常见路径 `MessageBox.Open(text, Btn.OK)` 一行代码 + 一次 await；只有少数自定义场景才需要写 `ModalRequest<T>` 子类

### 非目标

- 不做"弱模态"（dim 背景但允许点穿）—— 全部 modal 都是强模态
- 不做开/关动画 —— 由 LitMotion-based 的 `<Animation>` 控件在 XML 里声明
- 不做内置「toast / 短暂通知」—— 那个非阻塞、不入队，是独立特性
- v1 不做 Editor 预览工具（Scene 视图里看 modal 长什么样）

---

## 2. 架构层次

四层结构，由内到外：

```
1. UI.Modal              静态门面：队列 / sortingOrder / ESC 监听 / 生命周期钩子
2. ModalRequest<TResult> 抽象基类：每种 modal 一个子类，描述 XmlSrc + Bind + TryEscape
3. MessageBoxRequest     内置子类（ModalRequest<Btn>），实现 OK/Cancel/Yes/No/Close 多按钮
4. MessageBox            静态便捷 wrapper（Open(text, buttons, icon, title) → Awaitable<Btn>）
```

**关键不变量**：每个 modal 本质就是一个普通 `Screen`，通过 `UI.LoadDocumentAsync` + `UI.Open` 走与 `MainMenu` / `Settings` 完全相同的路径。`UI.Modal` 额外做三件 Screen 不管的事：

- **sortingOrder 叠加**：第一个 modal 用 `UI.Modal.SortingOrderBase`（默认 1000），多 modal 叠加每个 +1，覆盖任何 `CanvasConfigurator` 给出的值
- **TaskCompletionSource 绑定**：把 close(result) 回调接到 `AwaitableCompletionSource<TResult>.SetResult`
- **队列 + ESC 监听**：FIFO pump；只给队首 modal 挂 `ModalEscapeListener`

---

## 3. C# API

### 3.1 命名空间和枚举

```csharp
namespace PromptUGUI.Application.Modals;

[Flags]
public enum Btn {
    None   = 0,
    OK     = 1,
    Cancel = 2,
    Yes    = 4,
    No     = 8,
    Close  = 16,
}
```

命名 `Btn` 与现有 `PromptUGUI.Controls.Btn`（一个 Control 类）同名。**有意区分**：用 `using PromptUGUI.Application.Modals;` 引入枚举的代码不需要再用 `Controls.Btn`，因为 modal 调用方关心的是 result enum；写 control 的代码用全名 `PromptUGUI.Controls.Btn`。

### 3.2 `ModalRequest<TResult>` 抽象基类

```csharp
public abstract class ModalRequest<TResult> {
    public abstract string XmlSrc { get; }                          // e.g. "PromptUGUI/Modals/MessageBox"
    public abstract void Bind(IScreen screen, Action<TResult> close);
    public virtual bool TryEscape(out TResult result) {             // ESC/Back; false = 不响应
        result = default;
        return false;
    }
}
```

**契约**：
- `XmlSrc` 是 Screen src key。`"PromptUGUI/"` 前缀走包内 Resources（见 §6.2）；其他前缀走 caller 的 `UI.SourceResolver`
- `Bind(screen, close)`：当 Screen 已 Open、id 已可 `Get<T>()` 时被调用。**必须**注册一条或多条触发 `close(result)` 的路径（点击按钮、InputField submit 等）；订阅必须 `.AddTo(screen)` 保证 Close 时清理
- `TryEscape(out result)`：ESC/Back 被按下时调用。返回 true → 用 `result` 当作 close 值；false → 忽略本次 ESC

### 3.3 `UI.Modal` 静态门面

```csharp
public static partial class UI {
    public static class Modal {
        public static UnityEngine.Awaitable<TResult> OpenAsync<TResult>(ModalRequest<TResult> request);
        public static void CloseAll();                              // 队列里全部 → OperationCanceledException
        public static int QueuedCount { get; }                      // 包含正在显示的那个
        public static bool IsAnyOpen { get; }
        public static int SortingOrderBase { get; set; } = 1000;    // 第一个 modal 用这个，叠加 +1
    }
}
```

`OpenAsync` 同步入队 + 立即返回 `Awaitable<TResult>`；caller `await` 时直到 close(result) 才唤醒。

### 3.4 内置 `MessageBoxRequest`

```csharp
public sealed class MessageBoxRequest : ModalRequest<Btn> {
    public string Text;
    public Btn Buttons = Btn.OK;
    public string Icon;                                             // <Icon name> 格式，如 "ui:warn"
    public string Title;
    public IReadOnlyList<(string label, Btn key)> CustomLabels;     // 可空；非空时覆盖默认按钮文字

    public override string XmlSrc => MessageBox.XmlSrc;
    public override void Bind(IScreen screen, Action<Btn> close) { /* §5.1 */ }
    public override bool TryEscape(out Btn result) {                // Cancel > No > Close > false
        if ((Buttons & Btn.Cancel) != 0) { result = Btn.Cancel; return true; }
        if ((Buttons & Btn.No)     != 0) { result = Btn.No;     return true; }
        if ((Buttons & Btn.Close)  != 0) { result = Btn.Close;  return true; }
        result = Btn.None; return false;
    }
}
```

仅 `OK` / `Yes` 时 ESC 不响应（避免误关无 cancel 路径的确认框）。

### 3.5 静态便捷 wrapper `MessageBox`

```csharp
public static class MessageBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox";

    public static UnityEngine.Awaitable<Btn> Open(
        string text, Btn buttons = Btn.OK, string icon = null, string title = null)
        => UI.Modal.OpenAsync(new MessageBoxRequest {
            Text = text, Buttons = buttons, Icon = icon, Title = title,
        });

    public static UnityEngine.Awaitable<Btn> Open(
        string text,
        IEnumerable<(string label, Btn key)> buttons,
        string icon = null, string title = null) {
        var list = buttons.ToArray();
        return UI.Modal.OpenAsync(new MessageBoxRequest {
            Text = text,
            CustomLabels = list,
            Buttons = list.Aggregate(Btn.None, (a, b) => a | b.key),
            Icon = icon, Title = title,
        });
    }
}
```

`MessageBox.XmlSrc` 是可写 property——赋值替换默认 XML 源；无需单独 `UseXml(src)` 方法。

### 3.6 调用例子

```csharp
// 默认 messagebox
var r = await MessageBox.Open(UI.Tr("Save changes?"), Btn.Yes | Btn.No | Btn.Cancel);
if (r == Btn.Yes) await game.SaveAsync();

// 自定义按钮文字
var r2 = await MessageBox.Open(UI.Tr("File not found."),
    new[] { (UI.Tr("Retry"), Btn.OK), (UI.Tr("Skip"), Btn.Cancel) });

// 用户扩展：name picker
public sealed class NamePickerRequest : ModalRequest<string> {
    public override string XmlSrc => "MyUI/Modals/NamePicker";
    public override void Bind(IScreen screen, Action<string> close) {
        screen.Get<Controls.Btn>("ok").OnClick.Subscribe(_ =>
            close(screen.Get<InputField>("input").Text)).AddTo(screen);
        screen.Get<Controls.Btn>("cancel").OnClick.Subscribe(_ => close(null)).AddTo(screen);
    }
    public override bool TryEscape(out string r) { r = null; return true; }
}
var name = await UI.Modal.OpenAsync(new NamePickerRequest());
```

---

## 4. 数据流和生命周期

### 4.1 一次 OpenAsync 的完整路径

```
caller: await UI.Modal.OpenAsync(request)
   │
   ▼
1. 内部 new AwaitableCompletionSource<TResult>，封装为 ModalEntry { Request, Tcs, ... }，
   入队 Queue<ModalEntry>
   │
   ▼
2. 如果队列长度从 0 → 1，立即 _ = PumpAsync()；否则 caller await 在 Tcs.Awaitable 上
   │
   ▼
3. PumpAsync 处理队首：
   a. screenName = entry.Request.XmlSrc；第一次使用调 UI.LoadDocumentAsync(screenName)
      （走包内 Resources 或 caller SourceResolver，见 §6.2），之后缓存在 _loadedSrcs set
   b. UI.Open(screenName) → IScreen
   c. canvas.sortingOrder = SortingOrderBase + (queue depth - 1)
   d. request.Bind(screen, CompleteCurrent)
   e. screen.RootGameObject.AddComponent<ModalEscapeListener>().OnEscape = OnEscapePressed
   │
   ▼
4. close(result) 触发 CompleteCurrent(result):
   a. UI.Close(screenName)
   b. entry.Tcs.SetResult(result) — caller await 返回
   c. dequeue；非空 → 回到 step 3
```

### 4.2 ESC / Android Back 监听

PromptUGUI 当前无 input system 依赖。新增 `ModalEscapeListener` MonoBehaviour 走**双轨条件编译**，匹配 Unity 自动定义的 `ENABLE_INPUT_SYSTEM` / `ENABLE_LEGACY_INPUT_MANAGER` 符号：

```csharp
internal sealed class ModalEscapeListener : MonoBehaviour {
#if ENABLE_INPUT_SYSTEM
    private UnityEngine.InputSystem.InputAction _action;
    private void OnEnable() {
        _action = new UnityEngine.InputSystem.InputAction(
            "PromptUGUI.Modal.Escape",
            UnityEngine.InputSystem.InputActionType.Button);
        _action.AddBinding("<Keyboard>/escape");
        _action.AddBinding("<Gamepad>/start");          // 手柄顺手覆盖
        _action.performed += OnPerformed;
        _action.Enable();
    }
    private void OnDisable() { _action?.Dispose(); }
    private void OnPerformed(UnityEngine.InputSystem.InputAction.CallbackContext _) =>
        OnEscape?.Invoke();
#elif ENABLE_LEGACY_INPUT_MANAGER
    private void Update() {
        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape)) OnEscape?.Invoke();
    }
#endif
    internal System.Action OnEscape;
    internal void FireForTests() => OnEscape?.Invoke();   // PlayMode 测试避开 input 模拟
}
```

- **不需要 InputActionAsset**：程序代码声明 `InputAction` + 字符串 binding
- **`<Keyboard>/escape` 自动覆盖 Android Back**：Unity Input System 把硬件 Back 映射到 escape
- **package.json 不加 `com.unity.inputsystem` 依赖**：让 caller 项目自己装；两个符号都没定义时静默不响应 ESC（启动时一次性 LogWarning）
- **只给队首 modal 挂 listener**：multi-modal 叠加时，下层 modal 没有 listener，所以只有最上面那个会响应 ESC（避免 ESC 一次关掉全部）

### 4.3 队列内并发 Open

`OpenAsync` 不是 async，同步入队 + 同步返回 `Awaitable<TResult>`。pump 是 fire-and-forget（`_ = PumpAsync()`），异常通过 `Tcs.SetException` 上抛 + `Debug.LogError` 兜底。pump 用 `_pumping` flag 串行化，无并发执行多个 entry。

### 4.4 异常路径

| 错误位置 | 处理 |
|---|---|
| `UI.LoadDocumentAsync` 失败（XML 解析 / Screen 名重复 / resolver 抛） | `entry.Tcs.SetException(e)`；dequeue；pump 下一个 |
| `request.Bind` 抛 | 先 `UI.Close(screenName)`；再 `Tcs.SetException(e)`；dequeue；pump 下一个 |
| `close(result)` 调多次 | 内部 `_currentResolved` flag；第二次起静默忽略（不抛、不打日志） |
| `XmlSrc` 不存在的 control id | `screen.Get<T>(id)` 抛 KeyNotFoundException → 走上面"Bind 抛"路径 |

**一个 modal 加载失败不会卡死队列**——catch + dequeue + pump 下一个是统一策略。

### 4.5 取消路径

三种触发源，行为细分两类：

**类 A：caller 主动 `UI.Modal.CloseAll()`** —— modal 自己负责 close Screen：

```csharp
public static void CloseAll() {
    foreach (var entry in _queue)
        entry.Tcs.SetException(new OperationCanceledException("Modal cancelled"));
    _queue.Clear();
    if (_currentScreenName != null) {
        UI.Close(_currentScreenName);    // 主动 close 当前 modal Screen
        _currentScreenName = null;
    }
    _pumping = false;
}
```

**类 B：`UI.ResetForTests` / `UI.UnloadAll` 拆除整个 UI 系统** —— modal 只 cancel Tcs，**不**调 `UI.Close`，让外层统一处理：

```csharp
internal static void CancelAllForTeardown() {
    foreach (var entry in _queue)
        entry.Tcs.SetException(new OperationCanceledException("Modal cancelled"));
    _queue.Clear();
    _currentScreenName = null;
    _pumping = false;
    // 不调 UI.Close — 由 UnloadAll 的 `foreach (s in _open) s.Close()` 统一处理
}
```

调用顺序：`UI.UnloadAll` / `UI.ResetForTests` 内部**先**调 `Modal.CancelAllForTeardown()`，**再**走原有的 `foreach (s in _open) s.Close()`。Modal Screen 也在 `_open` 里，跟普通 Screen 一起被 close —— 不会双关闭，因为 modal 已把 `_currentScreenName` 置 null。

### 4.6 sortingOrder 与现有 CanvasConfigurator 的关系

`CanvasConfigurator` 在 `Screen.Open()` 期间调（XML canvas= 应用之后），传 `(canvas, screenName)`。`UI.Modal` 在 `UI.Open` 返回之后再覆盖 `canvas.sortingOrder = SortingOrderBase + depth`。

→ caller 的 `CanvasConfigurator` 设的 sortingOrder 会被 Modal 覆盖。**这是有意的**：modal 必须叠在普通 Screen 之上。其他 Canvas 属性（worldCamera、scaler 调整）caller 的设置生效。

---

## 5. 默认 MessageBox XML 模板 + Bind 实现

### 5.1 模板结构

文件位置：`Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/MessageBox" reference="1920x1080">
    <!-- 遮罩：半透黑，全屏拦截 raycast -->
    <Image id="backdrop" anchor="stretch" color="#0000007F"/>

    <!-- 对话框本体 -->
    <Frame id="dialog" anchor="center" size="640x300">
      <Image anchor="stretch" color="#202020"/>
      <VStack anchor="stretch" margin="24" spacing="12">
        <HStack height="40" spacing="12">
          <Icon id="icon" width="40" height="40"/>
          <Text  id="title" fontSize="24"/>
        </HStack>
        <Text id="text" anchor="stretch" fontSize="18"/>
        <HStack height="44" spacing="8">
          <Btn id="ok">OK</Btn>
          <Btn id="cancel">Cancel</Btn>
          <Btn id="yes">Yes</Btn>
          <Btn id="no">No</Btn>
          <Btn id="close">Close</Btn>
        </HStack>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>
```

- 全 5 个按钮节点都列出；Bind 时按 flag SetActive(false) 不需要的
- 按钮显示顺序 = Btn enum 值升序（OK → Cancel → Yes → No → Close）。HStack 里 inactive child 不参与 layout，自动 collapse
- XML 里英文写死作为 msgid 兜底；项目通过 `.po` 翻译（"OK" / "Cancel" / "Yes" / "No" / "Close" 五个 key）
- `reference="1920x1080"` 是默认；用户整体覆盖 XML 时可以改

颜色、size、字号都是粗糙默认值——pixel-art 项目大概率要覆盖 XML 用自家边框 sprite 和字体。这正是「内置 + 可覆盖」的价值。

### 5.2 `MessageBoxRequest.Bind` 实现

```csharp
public override void Bind(IScreen screen, Action<Btn> close) {
    screen.Get<Text>("text").TextValue = Text;

    var titleCtl = screen.Get<Text>("title");
    if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
    else titleCtl.TextValue = Title;

    var iconCtl = screen.Get<Controls.Icon>("icon");
    if (string.IsNullOrEmpty(Icon)) iconCtl.GameObject.SetActive(false);
    else iconCtl.Name = Icon;

    BindBtn(screen, "ok",     Btn.OK,     close);
    BindBtn(screen, "cancel", Btn.Cancel, close);
    BindBtn(screen, "yes",    Btn.Yes,    close);
    BindBtn(screen, "no",     Btn.No,     close);
    BindBtn(screen, "close",  Btn.Close,  close);
}

private void BindBtn(IScreen s, string id, Btn flag, Action<Btn> close) {
    var btn = s.Get<Controls.Btn>(id);
    if ((Buttons & flag) == 0) { btn.GameObject.SetActive(false); return; }
    var custom = CustomLabels?.FirstOrDefault(t => t.key == flag);
    if (custom is { label: { Length: > 0 } label }) btn.Text = label;
    btn.OnClick.Subscribe(_ => close(flag)).AddTo(s);
}
```

---

## 6. 边界情况

### 6.1 ReSolve 是否会覆盖 Bind 期间的 SetActive(false)？

**风险**：`VariantStore.Changed` → `Screen.ReSolve` → `ControlAttributeApplier.Apply` 给所有节点重新写一遍 attribute。如果 `ApplyCommon` 把 `Hidden = false` 无条件写到 GameObject.SetActive，则 Bind 期间通过 `SetActive(false)` 隐藏的按钮会被 ReSolve 重新激活。

**验证策略**：plan 阶段先写一条 EditMode 测试，红色 → 决定补丁路径：

- 选项 A：`ApplyCommon` 不写 active 状态（只在 `hidden=true` attribute 显式存在时写）→ 最简
- 选项 B：Bind 改成通过 attribute（给节点写 `hidden=true`）→ 走 attribute 路径，跟 Variant ReSolve 协同
- 选项 C：Bind 改用 Variant flip（给每个按钮挂个 mbox.ok 之类的 Variant）→ 最复杂

**默认假设**：A 已经成立（看 `ControlAttributeApplier.cs` 实际行为）。plan 阶段先红再修。

### 6.2 内置 XML 加载路径

内置 `MessageBox.ui.xml` 是包内资源，**caller 不能假设他们的 `UI.SourceResolver` root 包含它**。

**方案**：`UI.Modal` 内部实现一个分流加载器，**不**侵入 `UI.SourceResolver`（避免污染 caller 的 resolver 状态）：

```csharp
internal static class ModalSourceLoader {
    public static async Awaitable<string> LoadAsync(string src) {
        if (src.StartsWith("PromptUGUI/")) {
            var req = Resources.LoadAsync<TextAsset>(src);
            await req;
            if (req.asset is not TextAsset ta)
                throw new InvalidOperationException(
                    $"Builtin modal XML missing at Resources/{src}.ui.xml");
            return ta.text;
        }
        if (UI.SourceResolver == null)
            throw new InvalidOperationException(
                $"UI.SourceResolver must be set to load non-builtin modal '{src}'");
        return await UI.SourceResolver(src);
    }
}
```

modal 加载流水线**不**走 `UI.LoadDocumentAsync`（那条路径绑死了 `UI.SourceResolver`），而是：

```csharp
var xml = await ModalSourceLoader.LoadAsync(request.XmlSrc);
UI.LoadDocument(request.XmlSrc, xml);    // 走 sync raw-XML 形式
```

权衡：modal Screen **不参与 hot reload**（`LoadDocument` 不进 DepGraph），与 §6.5 的"modal 期间禁用 hot reload"决策一致。下次 Open 时如果 XML 改了，会因 `_loadedSrcs` 缓存命中而不重新加载——所以 `_loadedSrcs` 在 Editor 下随 `UI.HotReload.NotifyAssetChanged`（针对 modal src）失效，重新加载。具体 invalidate 逻辑由 plan 决定。

### 6.3 Open 在 modal 关闭过程中又被调

caller `.Subscribe(_ => MessageBox.Open(...))` 紧接着开下一个 → 新 entry 入队 → pump 检测到 `_currentScreen` 还在销毁中 → 等一帧（`await Awaitable.NextFrameAsync()`）再继续。`_pumping` flag 串行化保证不并发。

### 6.4 多个 modal 叠加时 ESC 只对最上层生效

只给当前队首 modal Screen 挂 `ModalEscapeListener`。关闭时随 GameObject Destroy 一起清掉。新 modal 开时挂新 listener。

### 6.5 Hot reload

§6.2 决定 modal 走 `UI.LoadDocument(label, xml)` 同步路径，**不进 DepGraph**。因此 `UI.HotReload.NotifyAssetChanged` 走 `_depGraph.ScreensDependingOn(src)` 时不会命中 modal Screen，`ReloadAsync` 也不会被针对 modal Screen 调用——**hot reload 对 modal 天然不生效**，不需要额外卫语句。

为了让 Editor 期编辑 modal XML 在下次 Open 时生效，`UI.Modal` 内部维护一个 Editor-only 钩子：

```csharp
#if UNITY_EDITOR
internal static void InvalidateCacheForEditor(string src) {
    _loadedSrcs.Remove(src);   // 下次 Open 重新走 ModalSourceLoader.LoadAsync
    UI.UnloadDocument(src);    // 清掉 _docs，让 LoadDocument 不再 throw "already loaded"
}
#endif
```

`UI.UnloadDocument(name)` 是为本特性新增的内部 helper（清 `_docs[name]`），公开 API 范围由 plan 决定。`UIAssetPostprocessor` 检测 modal-src 改动时调用 `InvalidateCacheForEditor`。修改 modal XML 在当前 modal 关闭后**下次 Open 自动生效**，运行中的 modal 不受影响（v1 设计）。

---

## 7. 测试策略

### 7.1 测试分布

| asmdef | 用途 |
|---|---|
| `PromptUGUI.Tests.EditMode` | 队列语义、Bind 逻辑、TryEscape、close 多次幂等、异常路径、cancel 路径、ReSolve 不覆盖 SetActive — 无 EventSystem |
| `PromptUGUI.Tests.PlayMode` | `ModalEscapeListener.FireForTests`、sortingOrder 数值、`UI.Modal` end-to-end 一次 Open → Click → Result |

### 7.2 EditMode 关键测试

```csharp
[TestFixture]
public sealed class ModalQueueTests {
    [SetUp] public void SetUp() {
        UI.ResetForTests();
        UI.SourceResolver = src => src switch {
            "test/Box1" => AwaitableHelpers.Completed(MinimalMboxXml("test/Box1")),
            _ => AwaitableHelpers.Faulted<string>(new System.IO.FileNotFoundException(src)),
        };
        MessageBox.XmlSrc = "test/Box1";   // 测试期把内置 XML 也走 SourceResolver
    }
    [TearDown] public void TearDown() => UI.ResetForTests();

    [Test] public void Open_then_clickOk_returns_OK();
    [Test] public void Second_open_queues_until_first_closes();
    [Test] public void CloseAll_cancels_pending_tasks();
    [Test] public void TryEscape_with_only_OK_returns_false();
    [Test] public void TryEscape_priority_Cancel_over_No_over_Close();
    [Test] public void Custom_labels_override_default_text();
    [Test] public void Close_double_call_is_idempotent();
    [Test] public void Bind_exception_dequeues_and_pumps_next();
    [Test] public void UnloadAll_cancels_queue();
    [Test] public void Bind_SetActive_false_survives_VariantStore_Changed();   // §6.1 验证
    [Test] public void Title_or_Icon_null_hides_corresponding_node();
}
```

**模拟 Btn 点击**：给 `Controls.Btn` 加 `internal void SimulateClick() => _click.OnNext(Unit.Default);`。EditMode 测试已通过 `InternalsVisibleTo` 能访问。

### 7.3 PlayMode 关键测试

```csharp
[UnityTest] public IEnumerator Escape_closes_top_modal_only() {
    UI.ResetForTests();
    var es = new GameObject("EventSystem").AddComponent<EventSystem>();
    es.gameObject.AddComponent<StandaloneInputModule>();

    var t1 = MessageBox.Open("first",  Btn.OK | Btn.Cancel);
    var t2 = MessageBox.Open("second", Btn.OK | Btn.Cancel);
    yield return null;

    var topScreen = UI.Get("PromptUGUI/Modals/MessageBox");
    topScreen.RootGameObject.GetComponent<ModalEscapeListener>().FireForTests();
    yield return t1;

    Assert.AreEqual(Btn.Cancel, t1.GetAwaiter().GetResult());
    yield return null;
    Assert.IsTrue(UI.Modal.IsAnyOpen);              // second 自动 pump 出来
}

[UnityTest] public IEnumerator SortingOrder_stacks_correctly();
```

`FireForTests` 避开模拟 Input System 状态注入的脆弱性，但保留 listener 挂对了 Screen 上的覆盖。

### 7.4 不测的

- New Input System 包没装 / `ENABLE_INPUT_SYSTEM` 未定义时的实际 ESC 不响应 —— CI 默认 Both 跑测试，覆盖 New + Legacy 双分支即可
- 双 modal 叠 sortingOrder 的视觉验证（需要截图比对）—— 只测 `canvas.sortingOrder` 数值正确
- LitMotion-based 的开/关动画 —— 那是独立特性，modal 不强制

---

## 8. SKILL.md 影响

按 `CLAUDE.md` 的 trigger 规则：

- 新公开 C# API（`UI.Modal`、`ModalRequest<T>`、`MessageBoxRequest`、`MessageBox`、`PromptUGUI.Application.Modals.Btn`）→ **scripting-promptugui-csharp/SKILL.md 必须更新**
- 内置 `.ui.xml` 模板（`Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml`）→ XML 元素本身都是已有 built-in，无新 tag/attribute → **authoring-promptugui-xml/SKILL.md 不需要更新**
- 不涉及 `PROMPTUGUI_HAS_ADDRESSABLES` → **using-promptugui-addressables/SKILL.md 不需要更新**

C# skill 加一节 "Modal dialogs" 覆盖：`UI.Modal.OpenAsync` / `MessageBox.Open` / 自定义 `ModalRequest<T>` 子类的写法 / ESC 行为 / 覆盖默认 XML / sortingOrder 配置。

---

## 9. 实施顺序（plan 阶段细化）

1. **Btn enum + ModalRequest 抽象基类** — 纯类型定义
2. **AwaitableCompletionSource 入队 / 出队 / sortingOrder pump** — 不含 ESC，不含内置 MessageBox；用 fake XML 跑 EditMode 测试
3. **ESC 监听双轨** — ModalEscapeListener + FireForTests；PlayMode 测试
4. **MessageBoxRequest + 内置 XML + Resources 加载分流** — 完整 default path
5. **MessageBox 静态 wrapper + 自定义 label 重载**
6. **取消路径**（CloseAll / UnloadAll / ResetForTests 钩子）
7. **Hot reload 跳过 modal Screen**
8. **C# SKILL.md 更新**

每步完成后跑 lint + UnityMCP 编译检查 + 对应单元测试，红 → 绿 → 下一步。

---

## 10. 验收标准

- `await MessageBox.Open("Hello", Btn.OK)` 在 PlayMode demo 中弹出对话框，点 OK 后 task 返回 `Btn.OK`
- 队列里第二个 modal 在第一个关闭后自动弹
- `UI.Modal.CloseAll()` 让所有未决 `await` 抛 `OperationCanceledException`
- ESC 在 `OK | Cancel` modal 上返回 `Btn.Cancel`；在仅 `OK` modal 上不响应
- 自定义 `ModalRequest<string>` 子类 + `await UI.Modal.OpenAsync(...)` 能拿回 string result
- modal 期间下层 Screen 的按钮不响应点击（GraphicRaycaster + sortingOrder + 遮罩 Image）
- EditMode + PlayMode 测试全绿；`dotnet format --verify-no-changes --severity warn` 干净
