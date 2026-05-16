## 内置 Loading 模态设计

**日期**：2026-05-16
**状态**：设计阶段（待 review，未进入实施）
**作用域**：在现有 modal 框架基础上新增 `LoadingRequest` / `Loading` / `LoadingHandle`：(1) 显示一个不接受用户输入的"加载中"模态；(2) 调用方拿到句柄，业务完成后主动调用 `handle.Close()` 关闭；(3) 视觉用纯 XML（三个 `<Animation fade>` 错相 yoyo），无新 C# 渲染脚本；(4) 用户可通过 `Loading.XmlSrc` 整体替换默认 XML（与 MessageBox 同模式）。
**依赖**：[`2026-05-14-messagebox-modal-design.md`](2026-05-14-messagebox-modal-design.md)（`UI.Modal` 队列泵 / `ModalRequest<TResult>` / `ModalEscapeListener` / `ModalSourceLoader` / `Screen.Open` sortingOrder 叠加）；[`2026-05-14-litmotion-animations-design.md`](2026-05-14-litmotion-animations-design.md)（`<Animation fade>` + `loop="yoyo"` + `delay`）

---

## 1. 背景与目标

MessageBox 解决了"等用户做选择"的模态。还有一类常见需求：**业务跑后台任务时挡住 UI、转个圈、任务完成后代码主动关掉**。典型用法：

```csharp
var loading = Loading.Open("加载中...");
try { await DoWork(); }
finally { loading.Close(); }
```

跟 MessageBox 的差异：
- **没有 TResult** —— modal 不靠用户输入关闭；调用方拿不到"按钮结果"
- **不可逃逸** —— ESC / Back 都不能关
- **打开是非阻塞的** —— `Loading.Open(...)` 同步返回句柄，调用方不 await modal 本身
- **关闭由外部触发** —— handle.Close() 是关闭路径，不是 `Bind` 里订阅按钮

复用现有 modal 框架（队列、sortingOrder 叠加、Screen 流水线、`ModalSourceLoader`），新增的只有 request 子类 + 公开 façade + handle 类型 + 一段 XML + pump 里一条 pre-show 跳过逻辑。

**内置 + 可覆盖**：跟 MessageBox 一致，`Loading.XmlSrc` 是可写 property——pixel-art 项目大概率要换自家边框 sprite / 字体 / 动画样式。默认 XML 是粗糙占位；用户赋 `Loading.XmlSrc = "MyGame/Modals/MyLoading"` 即整体替换（走 caller 的 `UI.SourceResolver`）。Bind 对自定义 XML 的契约见 §4。

### 非目标

- 进度条（已知进度 0..100%）—— 那是独立的 `<ProgressBar>` 控件，跟 modal 解耦
- 可取消（用户能主动按 Cancel 取消后台任务）—— 后续如有需要可以加 `LoadingRequest.Cancelable=true` + 一个 Cancel 按钮的变体，v1 不做
- 可关闭超时 —— 调用方自己用 `Task.WhenAny(work, Task.Delay(timeout))` 实现，框架不做
- 打开后改文案（`SetText`）—— YAGNI；v1 文案在 `Open(text)` 时定死

---

## 2. C# API

### 2.1 `LoadingRequest`

```csharp
namespace PromptUGUI.Application.Modals;

public sealed class LoadingRequest : ModalRequest<R3.Unit>
{
    public string Text;

    public override string XmlSrc => Loading.XmlSrc;

    public override void Bind(IScreen screen, Action<R3.Unit> close)
    {
        try
        {
            var textCtl = screen.Get<PromptUGUI.Controls.Text>("text");
            if (string.IsNullOrEmpty(Text)) textCtl.GameObject.SetActive(false);
            else textCtl.TextValue = Text;
        }
        catch (System.Collections.Generic.KeyNotFoundException) { /* text 元素可选 */ }
        // 不订阅 close —— Loading 没有按钮，关闭路径走 IModalEntry.ResolveExternally
    }

    // TryEscape 不重写 → 继承基类默认 return false → ESC 不响应
}
```

`R3.Unit` 作为 TResult 是因为 `ModalRequest<T>` 强制要 TResult；这个类型对调用方不可见（句柄不暴露 awaitable）。

### 2.2 `Loading` 静态门面

```csharp
public static class Loading
{
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";

    public static LoadingHandle Open(string text = null)
    {
        var entry = UI.Modal.EnqueueRequest(new LoadingRequest { Text = text });
        return new LoadingHandle(entry);
    }
}
```

`Loading.Open` **同步返回**——不 await modal pump，立即给调用方句柄。modal 实际显示是异步的（pump 在下一帧排到）；调用方不感知这层。

### 2.3 `LoadingHandle`

```csharp
public sealed class LoadingHandle
{
    private readonly IModalEntry _entry;
    private bool _closed;

    public bool IsClosed => _closed;

    internal LoadingHandle(IModalEntry entry) => _entry = entry;

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _entry.ResolveExternally();   // pump 内部分情况处理：pre-show 跳过 / 显示后关闭
    }
}
```

句柄不区分"已显示 / 还在队列"两种状态——`ResolveExternally` 把这层复杂度封进 `ModalEntry<TResult>`，详见 §3。

### 2.4 调用例子

```csharp
// 简单
var loading = Loading.Open("Loading...");
await SomeWork();
loading.Close();

// try/finally 保护异常路径
var loading = Loading.Open("Saving...");
try { await SaveAsync(); }
finally { loading.Close(); }

// 跟 MessageBox 串联
var loading = Loading.Open();
var data = await FetchAsync();
loading.Close();
if (data == null)
    await MessageBox.Open("Network error.", MsgBtn.OK);
```

---

## 3. 现有 modal 框架需要的小改

### 3.1 `IModalEntry` 加 `ResolveExternally` + `SetWaker`

```csharp
internal interface IModalEntry
{
    string XmlSrc { get; }
    void RunBind(IScreen screen, Action onClose);
    bool TryEscape(Action wakePump);
    void Cancel(Exception ex);
    bool Resolved { get; }
    void SetWaker(Action waker);     // ← 新增：pump 注入 waiter 唤醒回调
    void ResolveExternally();        // ← 新增：句柄触发的外部关闭路径
}
```

`ModalEntry<TResult>` 实现：

```csharp
private Action _waker;

public void SetWaker(Action waker) => _waker = waker;

public void ResolveExternally()
{
    if (Resolved) return;
    Resolved = true;
    _tcs.TrySetResult(default!);     // TResult=Unit 时是 Unit.Default；外部句柄不消费此值
    _waker?.Invoke();                // Bind 已跑过 → 唤醒 pump 走正常 Close 路径；
                                     // Bind 没跑过 → _waker=null，pump 下次轮到时靠 Resolved flag 跳过
}
```

### 3.2 `UI.Modal.PumpAsync` 加 pre-show 跳过 + 注入 waker

```csharp
while (_queue.Count > 0)
{
    var entry = _queue.Dequeue();
    if (entry.Resolved) continue;             // ← 新增 ①：Close() 在 dequeue 之前发生
    ...
    if (!_loadedSrcs.Contains(entry.XmlSrc))
    {
        var xml = await ModalSourceLoader.LoadAsync(entry.XmlSrc);
        LoadDocument(entry.XmlSrc, xml);
        _loadedSrcs.Add(entry.XmlSrc);
    }
    if (entry.Resolved) continue;             // ← 新增 ②：Close() 在上面那个 await 里发生

    var screen = Open(entry.XmlSrc);
    ...
    var waiter = _currentWaiter;
    captured.SetWaker(() => waiter.TrySetResult(true));   // ← 新增 ③
    captured.RunBind(screen, () => waiter.TrySetResult(true));
    ...
}
```

两处 `Resolved` 检查覆盖 pre-show 的两种时机：dequeue 之前 vs LoadAsync await 期间。`SetWaker` 在 `RunBind` 之前调，确保 `ResolveExternally` 在 Bind 后任何时刻调用都能唤醒 pump。

### 3.3 `UI.Modal.EnqueueRequest` 内部 helper

现有 `UI.Modal.OpenAsync<TResult>` 返回 `Awaitable<TResult>`。Loading 不需要 awaitable 而需要 entry，所以加一个 internal helper：

```csharp
internal static IModalEntry EnqueueRequest<TResult>(ModalRequest<TResult> request)
{
    if (request == null) throw new ArgumentNullException(nameof(request));
    var (entry, _) = ModalEntry<TResult>.Create(request);
    _queue.Enqueue(entry);
    if (!_pumping) _ = PumpAsync();
    return entry;
}
```

`OpenAsync<TResult>` 不动；两个入口都基于 `ModalEntry<TResult>.Create` 工厂方法，无重复入队逻辑。

---

## 4. XML 模板

文件：`Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/Loading.ui" reference="1920x1080" reference.portrait="1080x1920">
    <Image id="backdrop" anchor="stretch" color="#000000C0"/>

    <Frame id="dialog" anchor="center" size="320x160">
      <Image anchor="stretch" sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"/>
      <VStack anchor="stretch" margin="20" spacing="12">

        <!-- HStack 不带 anchor: VStack 是 layout group, PUI-LAYOUT-ANCHOR 禁止 child 写 anchor.
             VStack 默认 childAlignment=UpperCenter 会水平居中 width=80 的 HStack. -->
        <HStack height="20" spacing="10" width="80">
          <Animation fade="0.25:1" duration="0.5s" delay="0s"    easing="in-out-quad" on="loop">
            <Image width="14" height="14" color="white"/>
          </Animation>
          <Animation fade="0.25:1" duration="0.5s" delay="0.15s" easing="in-out-quad" on="loop">
            <Image width="14" height="14" color="white"/>
          </Animation>
          <Animation fade="0.25:1" duration="0.5s" delay="0.3s"  easing="in-out-quad" on="loop">
            <Image width="14" height="14" color="white"/>
          </Animation>
        </HStack>

        <Text id="text" width="stretch" height="stretch" fontSize="16"/>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>
```

要点：
- **遮罩 `#000000C0`** 比 MessageBox 的 `#000000FE` 稍透一点；纯 raycast 拦截即可
- **三点脉冲**：三个 `<Animation fade>` 各包一个白色色块，相位差 0.15s
- **`on="loop"`** 隐含 `loop="yoyo"`（详见 LitMotion 动画 spec §6.3 + `Animation.cs` OnAfterApply）；不写 `loop=` 走默认 yoyo，alpha 在 0.25↔1.0 来回淡
- **没有 Icon 元素**：跟 MessageBox 不同，加载模态不显示图标
- **Text 可选**：`LoadingRequest.Bind` 里空串 → `SetActive(false)`，HStack 自动 collapse 排版

### 4.1 自定义 XML 时的 id 契约

用户赋 `Loading.XmlSrc = "MyGame/Modals/MyLoading"` 替换默认模板。`LoadingRequest.Bind` 对自定义 XML 的契约：

| id        | 控件类型 | 必需？ | Bind 行为 |
|---|---|---|---|
| `text`    | `<Text>` | **可选** | 存在则赋 `Text` 字段；`Text` 空串则 `SetActive(false)`；不存在则跳过（catch `KeyNotFoundException`） |

其他元素（backdrop、dialog 容器、动画 dot）**没有 id 契约**——用户可以全删全换，自由设计自己的加载视觉（旋转 Icon / 进度条 / 多帧 sprite 动画等）。

`<Screen name="...">` 必须跟 `Loading.XmlSrc` 字符串完全一致（沿用现有 modal pipeline 的 src=name 契约，与 MessageBox 相同）。其余 PromptUGUI 标准约束（reference 分辨率、`PROMPTUGUI/` 前缀只对包内 Resources 生效等）照常。

---

## 5. 边界情况

### 5.1 `Close()` 在 modal 还在队列里时被调用

`LoadingHandle.Close()` 走 `_entry.ResolveExternally()`，标记 `Resolved=true`。pump 下次轮到时，§3.2 新增的 `if (entry.Resolved) continue;` 直接跳过 dequeue 后的 LoadDocument / Open 步骤——**modal 根本不会被实例化**，零视觉闪烁。

### 5.2 `Close()` 在 modal 关闭过程中再次被调

`_closed` flag 守门，第二次起静默 return；handle 是幂等的。

### 5.3 `Close()` 在 UI teardown 之后调

`UI.ResetForTests` / `UI.UnloadAll` 调 `Modal.CancelAllForTeardown` → `entry.Cancel(oce)` → `Resolved=true` + `_tcs.TrySetException`。之后 handle.Close() 走 `_entry.ResolveExternally()`，里头的 `if (Resolved) return;` 直接 no-op。安全幂等。

### 5.4 Loading 跟 MessageBox 混排

两者共用 `UI.Modal._queue`。`Loading.Open` → `MessageBox.Open` → ... → `loading.Close`，行为：Loading 先显示；用户看到 Loading；MessageBox 进队列等着；调用方调 `loading.Close()` → Loading Screen 关闭 → pump 转到 MessageBox。

逆向：先开 MessageBox，再开 Loading。MessageBox 在屏幕上等用户点击；Loading 排在后面。这种顺序业务上罕见但不被禁止——`UI.Modal` 不做 modal-type 间的优先级。

### 5.5 ESC 按下时的行为

`LoadingRequest` 不覆写 `TryEscape`，继承基类返回 false。pump 里 `ModalEscapeListener.OnEscape` 调 `entry.TryEscape(...)` 失败 → `wakePump` 不被调 → modal 不关闭。**用户按 ESC 没反应**——符合 "不接受任何用户输入"。

### 5.6 Hot reload

跟 MessageBox 一致（spec §6.5）：modal Screen 走 `LoadDocument(label, xml)` 同步路径，不进 DepGraph；`UIAssetPostprocessor` 检测到 `Loading.ui.xml` 改动时调 `UI.Modal.InvalidateCacheForEditor(Loading.XmlSrc)`，下次 Open 重新加载。运行中的 Loading 不受影响。

---

## 6. 测试策略

### 6.1 EditMode (`Tests/EditMode/Modals/LoadingTests.cs`)

延续 `ModalTestFixture` 模式：

```csharp
public sealed class LoadingTests : ModalTestFixture
{
    private const string LoadingTestXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading1'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame id='dialog' anchor='center' size='320x160'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='text' fontSize='16'/>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

    public override void SetUp()
    {
        base.SetUp();
        Files["test/Loading1"] = LoadingTestXml;
        Loading.XmlSrc = "test/Loading1";
    }

    [Test] public void Open_returns_handle_and_modal_queues();
    [Test] public void Close_after_open_dismisses_modal();
    [Test] public void Close_before_pump_skips_modal_instantiation();   // §5.1
    [Test] public void Close_is_idempotent();
    [Test] public void Text_null_hides_text_node();
    [Test] public void Text_nonempty_renders();
    [Test] public void Custom_xml_without_text_id_does_not_throw();     // §4.1 契约
    [Test] public void TryEscape_returns_false_so_listener_does_nothing();
    [Test] public void Mixed_with_MessageBox_respects_FIFO_queue();
    [Test] public void UnloadAll_cancels_loading_and_handle_close_after_is_noop();
}
```

### 6.2 PlayMode（如有必要）

视觉动画（`<Animation fade loop>`）的端到端验证已由 LitMotion 模块的 PlayMode 测试覆盖（spec `2026-05-14-litmotion-animations-design.md` §8.2），不需要在 Loading 这里再测一次。如果 Loading 模板里有 PlayMode 才能验证的东西（layout 像素值），延后到出现问题时再加。

---

## 7. SKILL.md 影响

按 `CLAUDE.md` trigger 规则：

- 新公开 C# API（`Loading`、`LoadingHandle`、`LoadingRequest`）→ **scripting-promptugui-csharp/SKILL.md 必须更新**：在 "Modal dialogs" 一节加一个 "Loading modal" 小节，包含 `Loading.Open(text)` / `handle.Close()` 用法 + try/finally 范式 + "ESC 不可关闭"
- 内置 `.ui.xml` 模板（`Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml`）使用的 XML 元素全是已有 built-in（`Screen` / `Frame` / `Image` / `Text` / `VStack` / `HStack` / `Animation`），无新 tag/attribute → **authoring-promptugui-xml/SKILL.md 不需要更新**
- 不涉及 `PROMPTUGUI_HAS_ADDRESSABLES` → **using-promptugui-addressables/SKILL.md 不需要更新**

---

## 8. 实施顺序（plan 阶段细化）

1. **`IModalEntry.ResolveExternally` + `ModalEntry<TResult>` 实现 + pump 跳过** —— 内部小改，加 EditMode 单测验证 pre-show resolved entry 被跳过
2. **`LoadingRequest` + `Loading` 门面 + `LoadingHandle`** —— 类型骨架；fake XML 走完整路径
3. **`Loading.ui.xml` 内置模板** —— 拷进 `Runtime/Resources/PromptUGUI/Modals/`，跟 MessageBox.ui.xml 平级
4. **EditMode 测试全套** —— §6.1 列表
5. **`scripting-promptugui-csharp/SKILL.md` 更新**

每步跑 lint + UnityMCP 编译检查 + 对应单测，红 → 绿 → 下一步。

---

## 9. 验收标准

- `var h = Loading.Open("Loading..."); await DoWork(); h.Close();` 在 PlayMode demo 中能弹出加载模态并正确关闭
- handle.Close() 在 modal 还在队列里时被调用，modal 不显示（pump 跳过）
- ESC 在 Loading 模态上**不**关闭 modal
- Loading 跟 MessageBox 混排时遵守 FIFO
- `dotnet format --verify-no-changes --severity warn` 干净
- `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml` exit 0
- EditMode 测试全绿
