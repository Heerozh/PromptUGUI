# Toast 提示文字系统设计

**日期**：2026-06-08
**状态**：设计阶段（待 review，未进入实施）
**作用域**：新增一个**独立于模态体系**的轻量 overlay 子系统——`Toast`：屏幕上短暂浮现、定时自淡出自销毁、可视觉堆叠的纯文本提示（游戏里常见的"飘字 / 提示信息"）。无边框、无焦点、不可点击、不挡输入。包含 (1) 静态门面 `UI.Toast.Show(...)`；(2) 定位值类型 `ToastPosition`（预设 / 坐标 / 控件引用 / 控件路径四种来源）+ 堆叠枚举 `ToastStackMode`；(3) 静态管理器 `ToastOverlay`（队列 + materialize + teardown，克隆 `LoadingOverlay` 结构）；(4) 每条提示一份 `ToastView : UIBehaviour`（自管淡入淡出 + 计时 + 位置回收）；(5) 纯逻辑单元 `ToastDuration`（时长公式）/ `ToastStack`（堆叠偏移）；(6) 内置 `Toast.ui.xml` 模板（默认裸 `<Text>`，可换肤）。
**依赖**：[`2026-05-16-loading-modal-design.md`](2026-05-16-loading-modal-design.md)（`LoadingOverlay` 的"独立子系统 + 自有 materialize pump + epoch teardown"结构原型，被逐字克隆）；`Runtime/Application/UI.cs`（`OpenModalScreen` / `CloseModalScreen` / `Get(screenName)` / `_open`）；`Runtime/Application/Modals/ModalDocCache.cs`（通用 XML 文档缓存，被复用）；`Runtime/Controls/Text.cs`（承载文本 + 图文混排 `<sprite>` + i18n + autosize + `GetNativeSize()` 精确测量）；`Runtime/Controls/IControl.cs`（`RectTransform` / `Get(idPath)`）。

---

## 1. 背景与目标

模态体系（MessageBox / InputBox / Loading）解决"打断式、需用户应答或由代码控制关闭"的弹窗。但有一类提示**根本不是模态**：

> "已保存"、"+10 金币"、"网络不稳定"、"背包已满"——浮现在屏幕某处，过一两秒自己淡出，不挡操作、不需要点。连着触发多次时会**依次堆叠**。

今天调用方只能自己拿 `Loading.Open` 凑（但 Loading 会挡屏、不自动关、不堆叠、带转圈），或手搓一个临时 Screen 自己管计时——重复且易错。

目标：把这套封装成开箱即用的 `UI.Toast.Show(...)`，与模态体系**平级但独立**。

```csharp
UI.Toast.Show("已保存");                                       // 底部居中、堆叠、按文本长度自动计时
UI.Toast.Show("+10 金币 <sprite name=\"coin\">", "Hud/rewardBtn"); // 显示在某控件位置（图文混排）
UI.Toast.Show("升级！", ToastPosition.Top, ToastStackMode.Sequential);
```

### 设计原则

1. **独立子系统，不碰模态**：Toast 不进 dialog 栈、不复用 `UI.Modal` 队列/ESC/sortingOrder 任何一行。它克隆 `LoadingOverlay` 的子系统骨架（独立 `_entries` + 自有 materialize pump + epoch teardown），但加上模态/Loading 都没有的**逐帧时序**（计时、淡入淡出、位置回收）。
2. **复用既有管线**：每条 toast 是一份普通 Screen（`UI.OpenModalScreen`/`CloseModalScreen`），XML 走 `ModalDocCache.EnsureLoaded`。文本承载在 `<Text>` 上——图文混排 `<sprite>`、i18n、autosize、`GetNativeSize()` 测量全部白嫖现成实现，零额外文本代码。
3. **caller 端 API 最小**：最常见一行 `UI.Toast.Show("...")`，位置/堆叠/时长/configure 全是可选项，缺省走可配置的全局默认（`DefaultPosition=Bottom` / `DefaultStackMode=Stacked`）。
4. **绝不崩游戏**：toast 是装饰性的。任何解析失败（控件路径失效、XML 缺 id、控件已销毁）→ 退回默认位置 / 静默降级 + 一条 `Debug.LogWarning`，**永不抛异常**冒泡到 gameplay。
5. **内置 + 可换肤**：`Toast.ui.xml` 是包内 Resources，默认裸文字无边框；`UI.Toast.XmlSrc` 可整体替换（加半透明圆角底 pill、阴影、改字号配色），与 Loading/MessageBox 的"内置 + 可覆盖"一致。
6. **纯逻辑可单测**：时长公式、堆叠偏移、位置解析、准入队列都抽成不依赖逐帧的纯单元（EditMode 测）；真正依赖时间推进的淡入淡出/自毁/回收走 PlayMode。

### 非目标

- **不做富交互**：toast 不可点击、无按钮、无回调结果（fire-and-forget，`Show` 返回 `void`）。需要可点的提示 → 用模态或自建 Screen。
- **不做进场/出场花式动画编排**：v1 只有"淡入 → 停留 → 淡出"。需要滑入/缩放/抖动 → 走 `<Animation>` 自定义模板（可叠在 toast XML 里），不进 C# API。
- **不跟随控件移动**：控件定位是**显示时刻的位置快照**。toast 浮现后控件再移动，toast 不跟。
- **不做全局 id 搜索**：控件路径必须 **screen 限定**（`"<screenName>/<idPath>"`），不支持只给裸 id 然后跨所有开着的 Screen 搜。
- **不改动模态体系**：MessageBox / InputBox / Loading / `UI.Modal` / `ModalDocCache` 行为零改动（仅**复用** `ModalDocCache.EnsureLoaded` 与 `UI.OpenModalScreen`，不改其实现）。

---

## 2. 架构定位

```
                       sortingOrder 层带（自底向上）
   ┌──────────────────────────────────────────────────────┐
   │  Toast        2000  ← 本特性（最高，提示盖在一切之上）  │
   │  Modal dialog 1000  ← UI.Modal.SortingOrderBase        │
   │  Loading       500  ← Loading.SortingOrder             │
   │  普通 Screen     0  ← 业务 UI                          │
   └──────────────────────────────────────────────────────┘
```

- **每条 toast 一份 Screen**（克隆 Loading：各自 `OpenModalScreen` → 各自 Canvas → 各自 `CanvasGroup`，互不干扰地独立淡入淡出）。
- toast Screen root 加 `CanvasGroup`：`blocksRaycasts=false` + `interactable=false`——**彻底不吃点击**（即使模板里放了 `<Image>`/`<Btn>` 也不挡输入）。`<Text>` 默认 `raycastTarget=false`，双保险。
- `Canvas.overrideSorting=true` + `sortingOrder=UI.Toast.SortingOrder`（默认 2000）。
- **逐帧驱动**：每条 toast 的 Screen root 挂一个 `ToastView : UIBehaviour`，自管 `淡入→停留→淡出` 状态机 + 位置 lerp（仿 `CarouselView` 的"per-instance UIBehaviour 连续刷新"先例）。`ToastOverlay`（静态）只管：加载/实例化、准入队列、堆叠分组与目标位计算、teardown。

子系统组件一览（§8 展开）：

| 组件 | 类型 | 职责 |
|---|---|---|
| `UI.Toast` | `UI` 的 partial 嵌套静态类（`UI.Toast.cs`） | 门面：`Show` 重载 + 全局旋钮 |
| `ToastPosition` | public readonly struct | 定位来源（4 种）+ 解析成 anchor/pivot/basePos/方向 |
| `ToastStackMode` | public enum | `Default / Stacked / Sequential` |
| `ToastOverlay` | internal static | 队列 + materialize pump + 分组堆叠布局 + teardown |
| `ToastView` | internal `UIBehaviour` | 单条 toast：淡入淡出状态机 + 计时 + 位置 lerp |
| `ToastDuration` | internal static（纯） | `Compute(text) → hold 秒` |
| `ToastStack` | internal static（纯） | 一组 toast 的高度序列 → 各自目标偏移 |

命名空间：新建 `PromptUGUI.Application.Toasts`（与 `PromptUGUI.Application.Modals` 平级）。`ToastPosition` / `ToastStackMode` 公开；其余 internal（测试 asmdef 已有 `InternalsVisibleTo`）。

---

## 3. C# API

### 3.1 门面 `UI.Toast`

```csharp
public static partial class UI {
    public static class Toast {
        // —— 全局旋钮（静态，可配置）——
        public static string XmlSrc { get; set; } = "PromptUGUI/Toast.ui";  // 带 .ui 后缀
        public static int    SortingOrder { get; set; } = 2000;             // 须高于 Modal(1000)
        public static ToastPosition  DefaultPosition  { get; set; } = ToastPosition.Bottom;
        public static ToastStackMode DefaultStackMode { get; set; } = ToastStackMode.Stacked;
        public static int    MaxVisible { get; set; } = 5;    // 同组超出即秒挤最老

        // 淡入淡出 + 间距 + 时长公式参数
        public static float FadeInSeconds  { get; set; } = 0.2f;
        public static float FadeOutSeconds { get; set; } = 0.4f;
        public static float Spacing        { get; set; } = 12f;   // 同组相邻 toast 间距(px@ref)
        public static float HoldBase       { get; set; } = 1.0f;
        public static float HoldPerChar    { get; set; } = 0.06f;
        public static float HoldMin        { get; set; } = 1.5f;
        public static float HoldMax        { get; set; } = 5.0f;
        public static float EdgeInset      { get; set; } = 120f;  // Top/Bottom 预设离屏边的内缩(px@ref)

        // —— canonical 重载 ——
        public static void Show(
            string text,
            ToastPosition  position = default,                 // default → DefaultPosition
            ToastStackMode mode     = ToastStackMode.Default,  // Default → DefaultStackMode
            float holdSeconds       = 0f,                      // 0 → 按文本长度自动
            Action<IScreen> configure = null);                 // 可选 post-bind 钩子，同 Loading

        // —— path 便捷重载（第二参为控件路径字符串）——
        public static void Show(
            string text,
            string controlPath,
            ToastStackMode mode = ToastStackMode.Default,
            float holdSeconds   = 0f,
            Action<IScreen> configure = null);

        // —— 控件引用便捷重载 ——
        // 用专用重载而非隐式转换：C# 禁止“到/从接口类型”的用户自定义转换运算符（CS0552），
        // 故 IControl 不能走隐式转换，只能由这个重载（或显式 ToastPosition.At(control)）承接。
        public static void Show(
            string text,
            IControl control,
            ToastStackMode mode = ToastStackMode.Default,
            float holdSeconds   = 0f,
            Action<IScreen> configure = null);
    }
}
```

三个重载互不冲突（重载决议唯一）：`Show("a","b")` 精确匹配 string 重载；`Show("a", someBtn)` 匹配 IControl 重载；`Show("a", ToastPosition.Top)` / `Show("a", new Vector2(...))`（`Vector2` 隐式转 `ToastPosition`）走 canonical 重载。`string` 重载与 `IControl` 重载的第二参**无默认值**（必填），故 `Show("a")` 与 `Show("a", mode: ...)` 只能落到 canonical 重载。**不**提供 `string → ToastPosition` 隐式转换（避免到处把字符串悄悄当位置）。

`holdSeconds > 0` 直接用它当停留时长，覆盖文本长度公式；`= 0`（默认）走 `ToastDuration.Compute`。`configure` 在文本写入后调用，拿到 live toast `IScreen`，可临时改色/加节点，与 Loading/模态的 `configure` 同形。

### 3.2 `ToastStackMode`

```csharp
public enum ToastStackMode {
    Default    = 0,   // "继承全局 DefaultStackMode" 的哨兵；仅作 Show 参数缺省值用
    Stacked    = 1,   // 立刻浮现，旧的被顶离基准锚点，多条共存
    Sequential = 2,   // 排队，等当前可见 toast 全部消失后才单独浮现（FIFO）
}
```

`Show` 内部：`if (mode == Default) mode = DefaultStackMode;`。`DefaultStackMode` 被设成 `Default` 时按 `Stacked` 兜底（防自指）。

### 3.3 调用例子

```csharp
UI.Toast.Show("已保存");                              // 底部居中、堆叠、自动时长
UI.Toast.Show("升级！", ToastPosition.Top);           // 预设：顶部居中
UI.Toast.Show("暴击!", new Vector2(0, 200));          // 坐标：参考分辨率空间、中心原点 +y 向上
UI.Toast.Show("+10", rewardBtn);                     // 控件引用（IControl 隐式转 ToastPosition）
UI.Toast.Show("+10 金币 <sprite name=\"coin\">", "Hud/rewardBtn");  // 控件路径字符串
UI.Toast.Show("连续三连击", mode: ToastStackMode.Sequential);       // 跳过 position，具名给 mode
UI.Toast.Show("自定义停留 3 秒", ToastPosition.Center, holdSeconds: 3f);
```

---

## 4. 定位模型 `ToastPosition`

```csharp
public readonly struct ToastPosition {
    // 四种来源（内部 Kind 枚举区分）：
    //   Unspecified（default）→ Show 时解析为 UI.Toast.DefaultPosition
    //   Preset    : Top / Bottom / Center
    //   Coord     : Vector2（参考分辨率空间，中心原点，+y 向上）
    //   Control   : IControl 引用
    //   ControlPath : string（"<screenName>/<idPath>"）

    public static readonly ToastPosition Top, Bottom, Center;     // 预设
    public static ToastPosition At(Vector2 referenceCoords);
    public static ToastPosition At(IControl control);
    public static ToastPosition At(string controlPath);

    public static implicit operator ToastPosition(Vector2 coords);   // 隐式（struct，合法）
    // 注：C# 不允许“到/从接口类型”的用户自定义转换运算符（CS0552），故无 IControl 隐式转换。
    //     IControl 由 UI.Toast.Show 的专用重载承接（§3.1），或显式 ToastPosition.At(control)。
}
```

> 隐式转换仅 `Vector2 → ToastPosition`。`IControl` 与 `string` 各由 `UI.Toast.Show` 的专用重载承接（§3.1），均不做隐式转换。

### 4.1 解析（在"显示时刻"，于 toast 自己的 Canvas 坐标系）

`ToastPosition.Resolve(toastCanvasRect)` → `(Vector2 basePos, Vector2 anchor, Vector2 pivot, StackDir dir)`：

| 来源 | basePos / anchor / pivot | 堆叠方向 |
|---|---|---|
| **Bottom** | anchor=pivot=(0.5, 0)，basePos=(0, EdgeInset) ——锚屏底，resize 鲁棒 | 向上 (+y) |
| **Top** | anchor=pivot=(0.5, 1)，basePos=(0, −EdgeInset) ——锚屏顶 | 向下 (−y) |
| **Center** | anchor=pivot=(0.5, 0.5)，basePos=(0,0) | 向上 (+y) |
| **Coord(v)** | anchor=pivot=(0.5, 0.5)，basePos=v（中心相对） | 向上 (+y) |
| **Control / ControlPath** | anchor=pivot=(0.5, 0.5)，basePos=控件世界中心 → 屏幕点 → 本 Canvas 本地点 | 向上 (+y) |

控件 → 本地点的转换（跨 Canvas、跨 scaler 鲁棒，复用 Carousel 已落地的 `ScreenPointToLocalPointInRectangle` 套路）：

```
worldCenter = ctl.RectTransform.TransformPoint(ctl.RectTransform.rect.center)
screenPt    = RectTransformUtility.WorldToScreenPoint(ctlCanvas.worldCamera, worldCenter)  // Overlay 时 cam=null
RectTransformUtility.ScreenPointToLocalPointInRectangle(toastCanvasRect, screenPt, toastCanvasCamera, out basePos)
```

管理器把这套设到 `id="content"` 节点的 `RectTransform`，再沿 `dir` 叠加堆叠偏移（§6）。

### 4.2 控件路径解析（`"<screenName>/<idPath>"`）

新增 internal 帮手 `UI.TryResolvePath(string path, out IControl control)`（放在 `UI.cs`，因需访问私有 `_open`）：

1. **最长前缀匹配**：遍历 `_open` 的注册键（即各 `<Screen name>`），取满足 `path == K` 或 `path.StartsWith(K + "/")` 的**最长** `K` 作为 screen 名，其后 `/` 起为 `idPath`。
   - 普通无斜杠 screen 名（`"Hud"`）→ 等价"首段=screen，其余=path"。
   - screen 名含斜杠（内置 `"PromptUGUI/Modals/MessageBox.ui"`）→ 因比对真实键、不数斜杠，照样切对。
   - `"A"` 与 `"A/B"` 同开 → 最长前缀优先（取 `A/B`），确定性。
2. `UI.Get(screenName)` → `Screen`（null → 失败）。
3. `idPath` 为空 → 用 Screen root 的 RectTransform（整屏中心附近）；否则 `screen.Get(idPath)` → `IControl`（`KeyNotFoundException` → 失败）。
4. 任一步失败 → `TryResolvePath` 返回 false。`ToastPosition.Resolve` 据此**退回 `UI.Toast.DefaultPosition` + 一条 `Debug.LogWarning`**。

`Control` 来源（直接握引用）跳过 1–3，但若 `RectTransform == null`（已销毁）同样退回默认 + warning。

---

## 5. 生命周期与时长

单条 toast 状态机（`ToastView`，逐帧 `Update`/`LateUpdate`）：

```
   生成 → [FadeIn] alpha 0→1 (FadeInSeconds) → [Hold] 倒计时 hold 秒 → [FadeOut] alpha 1→0 (FadeOutSeconds) → 通知管理器移除→销毁 Screen
            └────────────── 任意状态下都在每帧 lerp anchoredPosition → 管理器分配的目标位（位置回收，§6）──────────────┘
```

时长公式（`ToastDuration.Compute`，纯函数）：

```
hold = clamp(HoldMin,  HoldBase + charCount * HoldPerChar,  HoldMax)
charCount = text?.Length ?? 0      // 原始字符串长度作代理：简单、可预测
```

- `<sprite name="...">` 标记会略微拉长 `charCount`（按标记字符数算，非 1 个图元）——可接受偏差，v1 不做 TMP textInfo 精确字数。
- `holdSeconds > 0` 时直接 `hold = holdSeconds`，跳过公式。
- 默认值下：10 字 ≈ 1.6s（被 `HoldMin` 抬到 1.5 以上）、30 字 ≈ 2.8s、≥67 字封顶 5s。

---

## 6. 堆叠语义

### 6.1 分组

每条 toast 解析出一个**组键 groupKey**：预设→`Top`/`Bottom`/`Center`；坐标→四舍五入后的坐标；控件/路径→该路径串（或解析到的控件实例）。**同组互相顶、异组互不影响**（中心飘字 + 底部系统提示可共存而不重叠计算）。

### 6.2 Stacked（默认）——立刻浮现 + 位置回收

- 新 toast 立刻进入可见集，落在组的**基准锚点**（最新永远贴基准）。
- 组内按到达顺序排列，第 i 条沿组方向偏离基准：
  ```
  offsetDistance(i) = Σ_{j 比 i 新且同组} (height_j + Spacing)
  target(i) = basePos + dir * offsetDistance(i)
  ```
  `height_j` 由 `ToastView` 用 `<Text>.GetNativeSize()`（TMP `preferredHeight`）测得。
- **任一条进/出都重算整组 target**，每条每帧平滑 lerp 到新 target → 旧的被顶开、有空位时回收下来。

```
底部组随时间（▢=基准锚点附近）：
   显示 t1          来了 t2（t1 被顶上去）       来了 t3（t1/t2 再上移）      t1 到期淡出后（t2/t3 回落）
   ┌──┐             ┌──┐ ← t1                    ┌──┐ ← t1                   ┌──┐ ← t2
   │t1│▢            ├──┤                         ├──┤                       ├──┤
   └──┘             │t2│▢                        │t2│ ← t2                   │t3│▢
                    └──┘                         ├──┤                       └──┘
                                                 │t3│▢
                                                 └──┘
```

- **MaxVisible 上限**：同组可见数将超 `MaxVisible` 时，立刻把组内**最老**一条切到 `FadeOut`（快速挤走），给新的腾位。

### 6.3 Sequential——排队，等清空再来

- 全局 FIFO `_waiting` 队列。一条 Sequential toast：若当前**全局可见集非空** → 入队等待；否则立刻显示。
- 每次有 toast 移除后 pump：全局可见集空了且 `_waiting` 非空 → 取下一条显示。
- 语义对齐模态的 `Queued`：Sequential 不会在屏幕非空时**启动**；但若它启动后又来了 Stacked，Stacked 仍立刻叠上去（混用时 Stacked 始终即时）。要严格一条接一条就全程用 Sequential。

---

## 7. 内置 XML 模板 + 换肤约定

文件：`Runtime/Resources/PromptUGUI/Toast.ui.xml`（注意在 `PromptUGUI/` 根下，**不在** `Modals/` 子目录——它不是模态）。默认裸文字、无边框、无背景：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Toast.ui" reference="1920x1080" reference.portrait="1080x1920">
    <!-- id="content"：管理器接管它的 anchor/pivot/anchoredPosition；宽高 hug 文字（intrinsic） -->
    <Frame id="content" anchor="bottom">
      <Text id="text" align="center" fontSize="40" color="white"/>
    </Frame>
  </Screen>
</PromptUGUI>
```

约定：

- `<Screen name>` 与 `UI.Toast.XmlSrc` 默认值**逐字节相等**（`ModalDocCache.EnsureLoaded` 硬约束）。
- `id` 契约：管理器**重定位 `id="content"`**（找不到则退回重定位 `id="text"`），文本写进 `id="text"`。
- 文本承载在 `<Text>` → 图文混排 `<sprite>`、i18n、autosize、`align`、`overflow` 全部现成可用，作者覆盖 XML 即可换肤：加半透明圆角底 pill（`<Image sprite=... > 包住 <Text>`）、阴影、改字号配色、甚至套 `<Animation>` 做滑入。
- 内容节点宽高 hug 文字：`<Text>` 的 `UsesIntrinsicLayoutSize=true` + `id="content"` 上 `ContentSizeFitter`（plan 落实具体写法），使每条 toast 尺寸贴合文本，堆叠测高准确。

---

## 8. 组件分解（贴合 TDD：纯逻辑 EditMode、时序 PlayMode）

| 单元 | 文件 | 职责 | 测试层 |
|---|---|---|---|
| `ToastDuration` | `Runtime/Application/Toasts/ToastDuration.cs` | `Compute(text, knobs) → hold` 纯公式 | EditMode |
| `ToastStack` | `Runtime/Application/Toasts/ToastStack.cs` | 给定高度序列 + Spacing + dir + basePos → 各 target 纯计算 | EditMode |
| `ToastPosition` | `Runtime/Application/Toasts/ToastPosition.cs` | 四来源 + `Resolve` → anchor/pivot/basePos/dir | EditMode（构造 RectTransform / fake 开屏验证） |
| `ToastStackMode` | `Runtime/Application/Toasts/ToastStackMode.cs` | 枚举 | — |
| `ToastOverlay` | `Runtime/Application/Toasts/ToastOverlay.cs` | materialize pump（克隆 Loading）+ 分组 + 准入队列 + 布局重算 + teardown | EditMode（mock show 步骤）+ PlayMode |
| `ToastView` | `Runtime/Application/Toasts/ToastView.cs` | 单条：CanvasGroup 淡入淡出状态机 + 计时 + 位置 lerp + 测高 | PlayMode |
| `UI.Toast` | `Runtime/Application/UI.Toast.cs` | 门面 partial：Show 重载 + 旋钮 | EditMode（重载分派 / 参数缺省解析） |
| `UI.TryResolvePath` | `Runtime/Application/UI.cs`（新增 internal） | 最长前缀 screen 名匹配 + id-path 下钻 | EditMode |

`ToastOverlay` teardown 接到 `UI.ResetForTests` / `UI.UnloadAll` 现有 teardown 链（克隆 `LoadingOverlay.CancelAllForTeardown` 的 epoch 抛弃模式；toast Screen 本身在 `_open` 里，由 `_open` 循环统一关）。`ToastView` 所在 Screen 被 teardown 关闭时组件随之销毁。

---

## 9. 边界情况

| 情况 | 行为 |
|---|---|
| `text` 为 null/空 | `charCount=0` → `hold=HoldMin`；仍会浮现一个空文本框（调用方不该传空，但不崩） |
| 控件路径 screen 没开 / id 不存在 | `TryResolvePath` 失败 → 退回 `DefaultPosition` + `Debug.LogWarning`，toast 照常显示在默认位 |
| 控件引用已销毁（`RectTransform==null`） | 同上：退回默认 + warning |
| 控件路径 idPath 为空（只给 screen 名） | 用该 Screen root RectTransform（≈整屏中心） |
| toast 浮现后控件移动 | toast **不跟随**（显示时刻快照）——非目标 |
| 同组超过 `MaxVisible` | 最老一条立即切 FadeOut 挤走 |
| Sequential 一直被 Stacked 插队 | Sequential 可能久等（语义同模态 Queued 被 Popup 持续插队）——混用时调用方自负 |
| 屏幕旋转 / resize | Top/Bottom 预设锚屏边（resize 鲁棒）；Coord 中心相对（随中心移动）；Control 是快照（不重算）。组重算照常 lerp |
| 开着 toast 时 `UI.ResetForTests` / `UnloadAll` | `ToastOverlay.CancelAllForTeardown`：清队列 + epoch 抛弃在途 pump；toast Screen 由 `_open` teardown 循环统一关 |
| `holdSeconds` 传负数 | 钳到 0 → 走自动公式（防御） |
| Locale / Variant 切换 | toast 是短命 Screen，通常等不到 ReSolve；即便 ReSolve，文本经 `configure`/管理器直接写入 `TextValue`，模板 `<Text>` 无字面 `text=`，不被重置（同 InputBox 的 field 处理思路） |

无新增模态/Loading 基础设施，故那三者的 ESC / sortingOrder / 队列 / teardown / hot-reload 全不受影响、不重测。

---

## 10. 测试策略

### 10.1 EditMode（`PromptUGUI.Tests.EditMode`，新增 `Tests/EditMode/Toast/`）

纯逻辑为主，沿用 `UI.ResetForTests` + fake-resolver 模式：

```csharp
// ToastDurationTests
Compute_short_text_clamped_to_HoldMin();
Compute_scales_with_length();
Compute_long_text_clamped_to_HoldMax();
Compute_explicit_holdSeconds_overrides_formula();

// ToastStackTests
Single_toast_sits_at_basePos();
Newer_toast_pushes_older_along_dir();
Removal_recomputes_offsets();
Direction_down_for_Top_group();

// ToastPositionTests
Preset_bottom_anchors_to_bottom_edge();
Coord_maps_to_center_relative_point();
Control_reference_resolves_to_local_point();          // 构造两个 Canvas 验证转换
Unspecified_resolves_to_DefaultPosition();

// UI.TryResolvePath（PathResolveTests）
Resolves_simple_screen_and_idpath();                   // "Hud/btn"
Resolves_screen_name_containing_slashes();             // 内置模态名
Longest_prefix_wins_when_A_and_A_slash_B_both_open();
Missing_screen_returns_false();
Missing_id_returns_false();
Empty_idpath_returns_screen_root();

// ToastOverlay 准入（StackModeAdmissionTests，mock 掉实际 show）
Stacked_shows_immediately();
Sequential_waits_until_visible_empty();
Sequential_pumps_next_on_clear();
MaxVisible_evicts_oldest();

// UI.Toast 门面（ToastFacadeTests）
Path_overload_routes_to_ControlPath_source();
Mode_Default_resolves_to_DefaultStackMode();
Position_default_resolves_to_DefaultPosition();
```

### 10.2 PlayMode（`PromptUGUI.Tests.PlayMode`，新增 `Tests/PlayMode/Toast/`）

依赖时间推进，推帧验证：

```csharp
FadeIn_then_Hold_then_FadeOut_then_self_destroys();   // 全生命周期推帧
Stacked_two_toasts_coexist_and_reflow();
Older_toast_expires_others_collapse_toward_base();
Sequential_second_appears_after_first_gone();
CanvasGroup_blocksRaycasts_false_never_eats_input();
```

### 10.3 不测的

- 视觉好不好看（默认皮肤）→ 靠 visual QA。
- LitMotion/具体 lerp 曲线手感 → visual QA 调旋钮。

---

## 11. SKILL.md 影响

按 `CLAUDE.md` trigger 规则：

- 新公开 C# API（`UI.Toast` 门面 + `ToastPosition` + `ToastStackMode` + `UI.Toast.XmlSrc` 等旋钮）→ **`scripting-promptugui-csharp/SKILL.md` 必须更新**：新增 "Toast" 节（紧邻 "Modal dialogs"），讲 `Show` 四种定位、两种堆叠、时长公式、`XmlSrc` 换肤约定（`id="content"`/`id="text"`）、path 解析规则。
- 内置 `Toast.ui.xml` 全用已有 built-in tag（`<Frame>`/`<Text>`）→ 无新 XML tag/attribute → **`authoring-promptugui-xml/SKILL.md` 不需要更新**，**无 XSD / `BuiltinTags.cs` 改动**。
- 不涉及 `PROMPTUGUI_HAS_ADDRESSABLES` → **addressables skill 不需要更新**。

---

## 12. 实施顺序（plan 阶段细化）

1. **纯逻辑单元**（红→绿）：`ToastDuration` + `ToastStack` + 各自 EditMode 测。
2. **`UI.TryResolvePath`**（最长前缀 + id-path 下钻）+ PathResolveTests。
3. **`ToastPosition` + `ToastStackMode`** + 解析测。
4. **内置 `Toast.ui.xml`** + `ToastOverlay`（先 materialize/分组/准入，mock 掉 view 的逐帧部分）+ EditMode 准入测。
5. **`ToastView`**（淡入淡出状态机 + 计时 + lerp + 测高）+ 接进 `ToastOverlay`。
6. **`UI.Toast` 门面**（Show 重载 + 旋钮 + 缺省解析）+ ToastFacadeTests。
7. **teardown 接线**（`ToastOverlay.CancelAllForTeardown` 进 `UI.ResetForTests`/`UnloadAll`）。
8. **PlayMode 时序测**。
9. **C# SKILL.md 更新**（新增 Toast 节）。

每步跑 lint（`dotnet format --verify-no-changes --severity warn`）+ UnityMCP 编译检查 + 对应单测。

---

## 13. 验收标准

- `UI.Toast.Show("已保存")` 在底部居中浮现，约 1.5s 后淡出自毁，全程不挡点击。
- 连续 `Show` 三次 → 三条在底部依次向上堆叠；最老的先到期，其余平滑回落。
- `UI.Toast.Show("+10", "Hud/rewardBtn")` 显示在该控件位置；把路径写错 → 退回底部默认 + 控制台一条 warning，**不抛异常**。
- `ToastPosition.Top` / `.Center` / `new Vector2(...)` / `IControl` 四种定位都生效。
- `ToastStackMode.Sequential` 下，新提示等前面全部消失才单独浮现。
- 文本越长停留越久（封顶 5s）；`holdSeconds: 3f` 覆盖为固定 3s。
- 图文混排：`Show("领取成功 <sprite name=\"coin\">")` 内联图标正确渲染。
- `UI.Toast.XmlSrc = "MyUI/Toast.ui"` 能整体换肤（`<Screen name>` 须 byte-equal）。
- EditMode + PlayMode 测试全绿；`dotnet format --verify-no-changes --severity warn` 干净。
