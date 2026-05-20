# Modal 分层重构:Loading overlay 抽离 + dialog 栈化(Popup / Queued)设计

**日期**:2026-05-20
**状态**:设计阶段(待 review,未进入实施)
**作用域**:重构 `UI.Modal`,解决两个问题:(1) Loading 模态与其他 modal 无法共存(实为死锁);(2) modal 无法叠加显示(嵌套对话框需求)。具体:① 把 Loading 从 `ModalRequest` 体系中抽出,成为独立的下层 overlay 子系统;② dialog 系统从"单活跃 FIFO 队列"改为"显示栈 + 等待队列",新增 `ModalMode` { Popup(默认)、Queued } 由每次 `Open` 选择行为。
**修订对象**:本设计修订 [`2026-05-14-messagebox-modal-design.md`](2026-05-14-messagebox-modal-design.md)(队列语义)与 [`2026-05-16-loading-modal-design.md`](2026-05-16-loading-modal-design.md)(Loading 作为 modal)的对应部分。`ModalRequest<TResult>` / `ModalEscapeListener` / `ModalSourceLoader` / `MessageBoxRequest` 保留。

---

## 1. 背景与目标

### 1.1 现状

`UI.Modal` 是一个全局 FIFO 队列,`PumpAsync` 同时只显示一个 modal(显示队首 → `await` 它彻底 resolve → 轮下一个)。Loading 被塞进同一队列:`LoadingRequest : ModalRequest<Unit>`(假的 `Unit` result),并为此往 `IModalEntry` 加了 `ResolveExternally` / `SetWaker`、往 pump 加了 pre-show 跳过逻辑。

### 1.2 两个问题

**问题 A —— Loading 与 dialog 无法共存(死锁)。** 下面这段自然代码会卡死:

```csharp
var loading = Loading.Open("上传中...");
var bytes = await ReadFileAsync();
if (bytes.Length > Limit)
{
    var r = await MessageBox.Open("文件过大,继续?", MsgBtn.Yes | MsgBtn.No);  // ← 卡在这
    if (r == MsgBtn.No) { loading.Close(); return; }
}
await UploadAsync(bytes);
loading.Close();
```

`Loading.Open` 入队后 pump 停在 `await waiter.Awaitable`,`_pumping` 仍为 `true` → `MessageBox.Open` 入队但 `if (!_pumping)` 不成立、不启动新 pump → MessageBox 永不显示 → `await MessageBox.Open` 不返回 → `loading.Close()` 到不了 → pump 不前进。死锁。即使没到严格死锁,Loading 期间弹的 MessageBox 也总是排在 Loading 之后,无法盖在其上。

**问题 B —— modal 无法叠加。** 嵌套对话框需求:一个 MessageBox 上带"下次不再显示"复选框,勾选时要弹一个二次确认 MessageBox **盖在原 MessageBox 之上**。当前 FIFO 队列只会把确认框排到原 MessageBox 后面;原框不关,确认框不显示;原框 `await` 确认框时直接死锁。

### 1.3 目标

- Loading 与 dialog 可同时存在;Loading 永远在 dialog 之下。
- dialog 之间可叠加(`Popup`),也可保留"排队、互不打断"(`Queued`)。
- 行为由调用方在 `Open` 时显式选择,系统不做隐式判断(理由见 §4.1)。

### 1.4 非目标

- toast / 短暂通知 —— 仍是独立特性,非 modal(沿用 messagebox spec 非目标)。
- 多个并发 Loading 的合并 / refcount —— 每个 `Loading.Open` 各自一个 overlay(见 §5.3)。
- 自动判断一次 `Open` 是"嵌套"还是"独立" —— 不可靠(见 §4.1)。

---

## 2. 架构总览

三个 sortingOrder 层带,由下到上:

```
普通 Screen        CanvasConfigurator 给的值(低)
──────────────────────────────────────────────────
Loading overlay    Loading.SortingOrder 起(默认 500);多个并发 Loading 依次 +1
──────────────────────────────────────────────────
dialog 栈          UI.Modal.SortingOrderBase 起(默认 1000);栈每深一层 +1
```

`UI.Modal`(dialog)与 `Loading`(overlay)成为**两个独立子系统**,互不进入对方的栈/队列。两者都复用 `ModalSourceLoader`(内置 `PromptUGUI/` 资源 vs caller `SourceResolver` 分流)与"Screen 实例化 + Canvas overrideSorting"这段流水线。

---

## 3. dialog 栈

### 3.1 ModalMode

```csharp
namespace PromptUGUI.Application.Modals;

public enum ModalMode
{
    Popup  = 0,   // 默认:立刻压到栈顶显示
    Queued = 1,   // 等显示栈清空后作为新栈底显示;多个 Queued 之间 FIFO
}
```

`Popup = 0` → `default(ModalMode)` 与所有参数默认值都是 `Popup`。

### 3.2 数据结构

`UI.Modal` 内部,替换掉旧的单 `_current` / `_queue` / `_pumping`:

- `_displayStack`(`List<IModalEntry>`,自底向上)—— 当前在屏的 modal。栈顶 = 用户能交互的那个。
- `_waitingQueue`(`Queue<IModalEntry>`)—— 等待中的 `Queued` modal。

### 3.3 Open 规则

`OpenAsync(request, mode)`:

| mode | 显示栈状态 | 行为 |
|---|---|---|
| `Popup` | 任意 | 立刻实例化,压到栈顶,`sortingOrder = SortingOrderBase + (栈深 - 1)` |
| `Queued` | **空闲**(栈空 + 无正在实例化的条目 + `_waitingQueue` 空) | 等同 `Popup` —— 立刻作为栈底显示 |
| `Queued` | 非空闲 | 进 `_waitingQueue` |

栈空时 `Popup` 与 `Queued` 行为一致 —— mode 只在"已有 modal 在屏"时才产生区别。

### 3.4 Close 规则

modal 被关闭(`Bind` 的 close 回调 / ESC / `CloseAll`)时:

1. resolve 它的 awaitable;`Destroy` 它的 Screen;从 `_displayStack` 移除。
2. 新栈顶的 `ModalEscapeListener` 重新激活(见 §3.7)。
3. 若 `_displayStack` 变空且 `_waitingQueue` 非空 → 取队首 `Queued` modal,实例化为新栈底。

正常情况下只有栈顶 modal 会被用户输入关闭(下层被 backdrop 挡住),`CloseAll` / teardown 才会移除全部。

### 3.5 实例化串行化

XML 首次加载是异步的(`ModalSourceLoader.LoadAsync`)。连续多次 `Open` 必须按调用顺序落栈 —— 用一个内部 "materialize pump" 串行处理待实例化条目(沿用现有 `_pumping` 串行思路)。**关键区别**:新 pump 只负责"把 modal 放上栈"然后返回,**不再 `await` 到 resolve**;modal 一旦在栈上就一直活着,直到自己的 close 回调按 §3.4 处理。已被 `CloseAll` 取消的待实例化条目(`entry.Resolved == true`)出队时跳过。

### 3.6 嵌套场景(问题 B 的解)

```csharp
// 自定义 modal 的 Bind 里:复选框勾选 → 弹二次确认
chk.OnValueChanged.Subscribe(async on =>
{
    if (!on) return;
    var r = await MessageBox.Open("确认下次不再显示?", MsgBtn.Yes | MsgBtn.No,
                                  mode: ModalMode.Popup);
    if (r == MsgBtn.No) chk.Value = false;   // 撤销勾选
}).AddTo(screen);
```

`Popup` 把确认框压在原 modal 之上;答完弹掉,原 modal 露出。深层嵌套(A→B→C)同理。

### 3.7 ESC

`ModalEscapeListener` 只让**栈顶** modal 响应 ESC / Back。`Popup` 压栈时,旧栈顶的 listener 停止响应,新栈顶激活;栈顶关闭时,被露出的那个重新激活。

---

## 4. 为什么由调用方显式选择

### 4.1 系统无法可靠自动判断

一个新 `Open` 与当前 modal 的关系只有两种:嵌套(当前 modal 内交互触发)或独立(后台代码触发)。但 `Open()` 是个普通函数调用,看不出自己的触发源。`AsyncLocal` 之类的上下文追踪在 Unity `Awaitable` 上不可靠(`Awaitable` 不流转 `ExecutionContext`),且违反仓库"禁用 .Net Threading"约定。因此关系由调用方在 `Open` 时显式给出。

直觉佐证:modal 一显示,backdrop 就挡住下面所有输入 —— 用户能点的只有最顶上那个。所以"用户点击 + 期间有 modal 在屏"触发的 `Open` 必然是嵌套,后台代码触发的就是独立 —— 这个区别只有写代码的人知道。

### 4.2 默认值为何是 Popup

`Popup` 走错(本应 `Queued` 的独立 modal 用了默认)→ 只是被叠加打断一下,可见、可改。`Queued` 走错(嵌套确认框忘了 `Popup`)→ 确认框排到父框后面、永不显示,父框 `await` 它时直接死锁。把不会造成严重后果的那个设为默认。

---

## 5. Loading overlay 抽离

### 5.1 Loading 不再是 modal

Loading 没有 result、不接受用户输入、由代码主动关闭 —— 它不是对话框。它从 `ModalRequest` 体系移出,成为独立 overlay 子系统。

- `public sealed class LoadingRequest : ModalRequest<Unit>` —— **删除**。
- `IModalEntry.ResolveExternally` / `SetWaker`、`PumpAsync` 里为 Loading 加的两处 pre-show 跳过 —— **删除**(它们只为 Loading 存在,dialog 栈不需要)。

### 5.2 公开 API 不变

`Loading.Open(text)` → `LoadingHandle` → `handle.Close()`、`Loading.XmlSrc` —— 调用方 API 完全不变。新增 `Loading.SortingOrder`(可写,默认 500)。`Loading` / `LoadingHandle` 保留在 `PromptUGUI.Application.Modals` 命名空间(避免破坏现有 `using`,虽然 Loading 已不属于 modal)。

### 5.3 内部

新增内部 `LoadingOverlay` 管理器:`Loading.Open` 直接走 `ModalSourceLoader` 加载 + 实例化 Loading Screen,Canvas `overrideSorting = true`、`sortingOrder = Loading.SortingOrder + n`。`LoadingHandle.Close()` `Destroy` 对应 Screen。每个 `Loading.Open` 各自一个 overlay Screen(并发时叠放,`n` 递增);handle 与 overlay 一一对应,无 refcount。XML 缓存(`_loadedSrcs` 等价物)与 Editor 端 hot-reload invalidate 与 dialog 侧同构。

---

## 6. C# API 变更汇总

| 类别 | 内容 |
|---|---|
| 新增 | `enum ModalMode { Popup, Queued }` |
| 新增 | `Loading.SortingOrder { get; set; }`(默认 500) |
| 改 | `MessageBox.Open(...)` 两个重载末尾加 `ModalMode mode = ModalMode.Popup` |
| 改 | `UI.Modal.OpenAsync<T>(request, ModalMode mode = ModalMode.Popup)`(加默认参数,源码兼容) |
| 改(语义) | `UI.Modal.QueuedCount` = 显示栈 + 等待队列总数;`IsAnyOpen` = 显示栈非空 |
| 删 | `public sealed class LoadingRequest`(由内部 `LoadingOverlay` 取代;调用方走 `Loading.Open`,不受影响) |
| 删 | `IModalEntry.ResolveExternally` / `SetWaker`(internal,无外部影响) |
| 不变 | `Loading.Open` / `LoadingHandle` / `Loading.XmlSrc` / `MessageBox.XmlSrc` / `ModalRequest<T>` / `MsgBtn` / `UI.Modal.SortingOrderBase` / `CloseAll` |

---

## 7. 行为变更与兼容性

**这不是纯增量改动。** `Popup` 作默认 → 现有"一个 modal 在屏时再 `Open`"的代码,从 FIFO 排队变成叠加显示。依赖旧 FIFO 行为的调用点需显式加 `mode: ModalMode.Queued`。

- **库内**:只有 `MessageBox` / `Loading`,自身不连开多 modal,无内部破坏。
- **host 工程**:调用方自行评估并按需加 `Queued`。
- **测试**:`ModalQueueTests` 等假定 FIFO 默认的用例需相应改写(见 §8)。

权衡已定:`Popup` 走错只是被打断、`Queued` 走错会死锁,故安全的当默认(§4.2)。

---

## 8. 测试策略

EditMode(`PromptUGUI.Tests.EditMode`):

- `Popup` 默认:第二个 `Open` 叠在第一个之上;关栈顶露下层。
- `Queued`:栈非空时等待;栈空后按 FIFO 弹出;多个 `Queued` 顺序正确。
- 栈空时 `Popup` 与 `Queued` 行为一致。
- 嵌套链 A→B→C 的压栈 / 弹栈。
- `CloseAll` 取消整个显示栈 + 等待队列,全部 `await` 抛 `OperationCanceledException`。
- 旧 `ModalQueueTests` 中依赖 FIFO 默认的用例 → 改为显式 `mode: Queued` 或重写为栈语义。
- Loading:`Loading_and_MessageBox_coexist`(MessageBox 显示在 Loading 之上;关掉 MessageBox 后 Loading 仍在);§1.2 死锁场景现在跑通。
- 删除 `LoadingTests.Mixed_with_MessageBox_respects_FIFO_queue`(前提已不成立),替换为上一条。
- 新增 `LoadingOverlay` 的 Open / Close / 并发 / teardown 用例。

PlayMode(`PromptUGUI.Tests.PlayMode`):sortingOrder 分层数值(Loading 带 < dialog 带;栈深递增);ESC 只关栈顶。

---

## 9. SKILL.md 影响

`scripting-promptugui-csharp/SKILL.md` —— **必须更新**(公开 API + 行为变更):

- "Modal dialogs" 节:新增 `ModalMode` / `mode:` 参数说明;"subsequent `Open(...)` calls queue FIFO" 改为"默认 `Popup` 叠加,`Queued` 排队"。
- "Loading modal" 节:Loading 不再是 modal、不再共用 FIFO 队列,改为独立下层 overlay;"Shares the FIFO queue with MessageBox" 一句删除 / 改写;补 `Loading.SortingOrder`。
- cheatsheet(MODAL 区)同步。

`authoring-promptugui-xml` / `using-promptugui-addressables` —— 无 XML 元素 / 属性变更、不涉及 Addressables,**不更新**。

---

## 10. 实施顺序(plan 阶段细化)

1. **Loading 抽离** —— 新增 `LoadingOverlay`;`Loading` / `LoadingHandle` 改走它;删 `LoadingRequest`、`IModalEntry.ResolveExternally` / `SetWaker` 及 pump 里对应跳过。EditMode 测试。
2. **`ModalMode` + dialog 栈** —— `UI.Modal` 由 `_queue` 单 pump 改为 `_displayStack` + `_waitingQueue` + materialize pump。EditMode 测试。
3. **`MessageBox.Open` / `OpenAsync` 加 `mode` 参数。**
4. **teardown 接线** —— `UnloadAll` / `ResetForTests` 清 dialog 栈 + 等待队列 + Loading overlay;`UI.Modal.CloseAll` 不动 Loading,另加 `Loading.CloseAll`。
5. **ESC 在栈上** —— 只栈顶响应。
6. **`scripting-promptugui-csharp/SKILL.md` 更新。**

每步跑 lint + UnityMCP 编译 / 测试,红 → 绿 → 下一步。

---

## 11. 验收标准

- §1.2 死锁代码跑通:Loading 期间 `await MessageBox.Open` 正常弹出并返回。
- 嵌套确认框(§3.6)叠在父 modal 之上,答完露出父 modal。
- `mode: ModalMode.Queued` 的两个 modal 按 FIFO 依次显示。
- Loading overlay 始终在所有 dialog 之下。
- ESC 只关栈顶 dialog;Loading 不响应 ESC。
- `UI.Modal.CloseAll()` 让显示栈 + 等待队列的全部 `await` 抛 `OperationCanceledException`。
- EditMode + PlayMode 测试全绿;`dotnet format --verify-no-changes --severity warn` 干净;`UIXmlLint` 对 modal XML exit 0。
