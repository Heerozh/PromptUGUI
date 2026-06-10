# Router 导航系统:稳定名字 + 标准链路 reconcile + Page/Modal/Tab/Prompt 四呈现

**日期**:2026-06-09
**状态**:设计阶段(待 review,未进入实施)
**作用域**:新增一套 `UI.Router` 导航子系统,作为**游戏内所有界面打开操作的统一入口**。支持深链(`appid://home/friend?tab=x`)与手点按钮走完全相同的码路、落到完全相同的显示结果。建立在现有 `UI.Open` / `UI.Modal` / `InputBox` / `MessageBox` 之上,不改动它们的对外语义。
**关联**:复用 [`2026-05-20-modal-layering-design.md`](2026-05-20-modal-layering-design.md) 的 dialog 栈(Modal 呈现)与 `InputBox`/`MessageBox`(Prompt 呈现);不修订该 spec。无 XML 元素/属性变更,故 `authoring-promptugui-xml` 不动;公开 C# API 新增,`scripting-promptugui-csharp` 必须更新(见 §14)。

---

## 1. 背景与目标

### 1.1 现状

当前三套界面机制彼此平级、各自为政,**没有任何层级/路由/统一入口**:

- **普通 Screen**:`UI.Open(name)` / `UI.Close(name)`,按 name 存进 `_open` 字典,sortingOrder 由 `CanvasConfigurator` 给。屏上多个 Screen 之间无父子、无栈、无先后约束。
- **Modal dialog 栈**:`UI.Modal` 的 `_displayStack` + `_waiting`(`Popup`/`Queued`),sortingOrder 1000+,ESC 只关栈顶。
- **Loading overlay / Toast**:各自独立的瞬态层。

打开一个界面 = 调用方直接 `UI.Open` / `UI.Modal.OpenAsync`。没有人统一知道"现在屏上这条链路是什么"。

### 1.2 需求

游戏里存在**深链跳转**:轮播广告卡、聊天里的超链接、推送通知等,点击后跳到游戏内某界面,形如 `appid://home/friend?tab=members`,类似 web router。两条硬约束:

1. **路径进来 = 手点进来,显示结果必须一模一样。** 不能因为"从深链进"就出现手点永远到不了的状态。
   - 反例:用户手动操作时,模态窗口挡住下层,他**不可能**点到下面的按钮再开另一个窗口。深链若直接把目标盖上去,就打破了这条天然约束。
2. **游戏层级经常变动,但 URL/广告卡不应跟着改。** 今天 `friend` 挂在 `home` 下,明天挪到 `social` 下,满世界发出去的 `appid://friend` 链接一个都不能失效。

并且这套东西**不只为深链**——它是一套管理系统,游戏内**所有**界面打开都要经过它,深链只是其中一个入口。

### 1.3 目标

- 一个统一入口 `UI.Router.Open(name)`:把当前层栈 **reconcile** 到 `name` 的"标准链路"(从根到它)。手点按钮与深链调用同一入口 ⇒ 同一结果。
- 名字与层级**解耦**:URL 只认稳定名字;父子关系在注册时单独声明,层级变动只改注册那一处。
- 节点支持四种呈现:整屏 **Page**、遮罩弹窗 **Modal**、父界面 TabBar 里的 **Tab**、由内置对话框支撑的 **Prompt**(`InputBox`/`MessageBox` 包成路由目的地)。
- 临时对话框(就地 `await MessageBox.Open`)仍可脱离 router 存在;reconcile 时自动清掉挡在目标链路之外的临时模态,守住 §1.2 约束 1。

### 1.4 非目标(v1)

- **转场动画**:节点激活/反激活先是即时的(SetActive / 实例化 / 销毁)。动画留待后续(可挂在现有 `<Trigger>`/`<Animation>` 上)。
- **一个节点多 parent(DAG)**:每个节点恰好一个标准 parent → 一棵树。多入口同一界面的需求用"多个名字指向同一 src"或后续扩展解决。
- **超出当前链路的历史前进/后退**:不维护浏览历史栈。当前链路本身就是状态,`Back()` = 回到 parent(见 §8)。
- **并行常驻层**(常驻 HUD、世界 UI 等):不进 router,继续用裸 `UI.Open`(见 §11)。
- **路由守卫 / 拦截器**(navigation guard、登录校验重定向):v1 不做;`onEnter` 里调用方可自行 `Open` 别处近似实现。

---

## 2. 核心模型:导航节点树

一棵**导航节点树**。每个**路由节点**(`RouteNode`,内部 POCO)有:

| 字段 | 含义 |
|---|---|
| `Name` | 稳定不透明 ID。可含斜杠(`"home/friend"`),但**斜杠只是名字字符,不解析成层级**。URL 与所有 API 只认它。全局唯一。 |
| `Parent` | 标准父节点的 `Name`;`null` = 根。注册时声明,层级变动只改这里。 |
| `Kind` | `Page` / `Modal` / `Tab` / `Prompt`(见 §5)。 |
| 目标 | Page/Modal → `Src`(`.ui.xml` 的 src key) + 要 `Open` 的 screen 名;Tab → `TabId`(父 Page screen 内 `<Tab>` 控件的 id 路径);Prompt → `Run` handler。 |
| `OnEnter` | (Page/Modal/Tab)激活后的回调 `Action<IScreen, RouteQuery>`,拿到目标 screen + 查询参数。可空。 |

**名字与层级解耦的意义**:`appid://friend` 永远写 `friend`;`friend` 的 parent 从 `home` 改成 `social`,只动 `MapTab/Map` 那一行注册,所有外发链接与按钮代码不变。这是 §1.2 约束 2 的实现。

> **为什么不是"URL 路径即层级"(web router 那套)?** 那样 `appid://home/shop` 里 shop 是 home 的子——层级一变就得改满世界 URL,正中 §1.2 约束 2 的痛点。**为什么不是"纯运行时栈(parent=谁打开我)"?** 那样深链时无法重建标准链路,做不到 §1.2 约束 1。本设计取两者之外的第三条:稳定名字 + 注册时声明的标准 parent + reconcile。

---

## 3. 中枢:`Open(name)` 与 reconcile

### 3.1 不变量

**屏上的路由链路,永远是从某个根到某个节点的一条标准路径。** 没有"凭空盖在任意当前界面上"的操作。所有打开都经 `Open` → reconcile,因此深链与手点天然同构。

### 3.2 reconcile 算法

`Open(name, query)`:

1. **求目标链路** `target = [root … name]`:从 `name` 沿 `Parent` 向上走到根,反转。途中检测环(visited 集)与未注册的 parent → 抛 `RouteException`(见 §10)。
2. **取当前链路** `current`:router 持有的活动节点栈(root→top,见 §3.4)。
3. **最长公共前缀** `k`:逐位比较 `current[i].Name == target[i].Name`,首个不等处为分界。
4. **反激活**:`current` 中下标 `≥ k` 的节点,**从顶往下**逐个 deactivate(§5)。
5. **激活**:`target` 中下标 `≥ k` 的节点,**从下往上**逐个 activate(§5)。每个**新激活**的节点的 `OnEnter` 收到本次 `query`。
6. **目标刷新**:若目标节点已在公共前缀内(重复导航到已开节点),不重建、不动其余节点,只对**目标节点**重新触发一次 `OnEnter(screen, query)`(Prompt 已在显示中则为 no-op)。
7. reconcile 完成后 fire `Changed`(§9)。

> **query 的投递**:本次新激活的每个节点、以及目标节点(即便已开)都收到**完整** `query` 字典,各取所需(例:`appid://shop/item?shopId=5&itemId=10` 中 shop 与 item 同时被新激活,各读各的 key)。公共前缀里未被重激活的中间节点不收、不重触发。

### 3.3 临时模态的处理(§1.2 约束 1 的实现)

reconcile 第 4 步**之前**,若 router 链路**之上**还压着脱离 router 的临时模态(它们在 modal 栈里、不是 router 节点;`MessageBox`/`InputBox`/`Loading` 等,见 §6),则先按"关栈顶"的方式逐个关掉。等价于"用户必须先关掉弹窗才能导航",杜绝越过模态偷开下层。

### 3.4 活动链路的数据结构

`UI.Router` 内部:

- `_chain`:`List<ActiveNode>`,自底向上(root→top)。
- `ActiveNode`:`{ RouteNode Def; … 各 Kind 的运行时 handle }` —— Page/Modal 持有其 `Screen`(及 Modal 的 slot/Canvas);Tab 持有所选 `Tab` 控件引用;Prompt 持有其 `run` 的运行任务 + cancel 通道(§5.4)。

### 3.5 异步与串行化

激活 Page/Modal 要 `await` XML 加载/实例化(异步)。reconcile 全程 `async Awaitable`,且**串行化**:同一时刻只跑一个 reconcile(仿 `UI.Modal` 的 materialize pump 的 epoch 机制)。reconcile 进行中再来 `Open` → 入一个**单槽 latest-wins** 待办:当前 reconcile 结束后,以最新一次请求为目标再跑一次(中间被覆盖的请求直接丢弃——它们的最终意图已被后来者取代,符合"导航以最后一次为准"的直觉)。`Open`/`Navigate`/`Back`/`Reset` 返回的 `Awaitable` 在**本次请求对应的 reconcile 真正完成**时 resolve;被 latest-wins 覆盖丢弃的请求,其 `Awaitable` 同样在那一刻 resolve(目标已被取代,不抛错)。

---

## 4. 名字、URL 与参数

### 4.1 URL 解析

`Navigate(url)` 把 `<scheme>://<name>?<query>` 拆成 `(name, query)` 后等价于 `Open(name, query)`:

- `://` 与 `?`(或串尾)之间**整段**当 `name`(含斜杠,不拆)。
- `?` 之后按 `k=v&k2=v2` 解析成 `RouteQuery`;`k`/`v` 做 URL decode。
- **scheme 校验**:`UI.Router.Scheme`(可写,默认 `null`)。为 `null` 时不校验 scheme(接受任意 `xxx://…`,甚至无 scheme 的裸 `name?…`);非 `null` 时 scheme 不符抛 `RouteException`。

### 4.2 RouteQuery

只读小包装,从 URL query 或 `Open` 传入的字典构造:

```csharp
public sealed class RouteQuery
{
    public static readonly RouteQuery Empty;
    public bool Has(string key);
    public string Get(string key, string fallback = null);   // 缺省 → fallback
    public int GetInt(string key, int fallback = 0);
    public string this[string key] { get; }                  // 缺省 → null
    public IReadOnlyDictionary<string, string> Raw { get; }
}
```

`Open(name)`(无 query)→ 节点收到 `RouteQuery.Empty`。

---

## 5. 四种呈现的激活 / 反激活

reconcile(§3.2)对四种 Kind 一视同仁,只是 activate/deactivate 动作不同。

### 5.1 Page

| | 动作 |
|---|---|
| activate | `await` 加载 src(若未加载)→ `UI.Open(screenName)`(普通 sorting 带)→ `OnEnter(screen, query)`。 |
| deactivate | `UI.Close(screenName)`(销毁 Screen)。 |

### 5.2 Modal

| | 动作 |
|---|---|
| activate | 实例化 Screen 到 **modal sorting 带**(复用 §2026-05-20 的 `ModalSourceLoader` + backdrop + route-aware ESC),作为**持久面板**压上 → `OnEnter(screen, query)`。**不**走结果导向的 `OpenAsync<T>` / `ModalRequest`——routed Modal 是持久目的地,不 await 结果、由 router 关闭。 |
| deactivate | 关闭该面板的 Screen + backdrop。 |

> 复用方式(把 `UI.Modal` 重构出一个"持久面板"原语供两边共享 vs. Router 自行复刻 band/backdrop/ESC 那几步)= plan 阶段定;见 §12。

**ESC / backdrop / 自带关闭按钮的同步**(关键):routed Modal 的关闭**必须经 router**,否则 router 链路与屏幕脱节。其 `ModalEscapeListener` 的回调改为 route-aware:若该节点是链路顶 → 等价 `Router.Back()`;否则不响应(沿用"只关栈顶"语义)。作者在 Modal screen 里放的"关闭"按钮也应接 `Router.Back()`,不要裸 `screen.Close()`。

### 5.3 Tab

Tab 节点不是自己的 Canvas,它寄生在某个 Page/Modal screen 的 `<TabBar>` 里。

- **宿主 screen** = 沿 `Parent` 向上走遇到的第一个 Page/Modal 节点(允许 Tab 的 parent 是同屏内另一个 Tab,做嵌套 tab)。reconcile 保证宿主在 Tab 之前已激活。
- `TabId` = 宿主 screen 内 `<Tab>` 控件的 id 路径。

| | 动作 |
|---|---|
| activate | `host.Get<Tab>(TabId).IsOn = true`(ToggleGroup 自动互斥,内容随 `ApplyBindFrame` 切换)→ `OnEnter(host, query)`。切换兄弟 tab 时**宿主不重建**。 |
| deactivate | **不强制改控件**:宿主仍在屏时,TabBar 永远有一个被选中(`SyncInitialSelection` 保证),Tab 节点出栈只是 router 不再跟踪它,控件保持当前选中态。真正换 tab 只发生在"导航到兄弟 Tab 节点"时。宿主整屏关闭则 tab 随之消失。 |

> 边界(已知、可接受):`current=[home,shop,shop/deals]` 时 `Open("shop")` → 公共前缀 `[home,shop]`,deals 节点出栈但 shop 屏上仍显示 deals tab(不重置成默认 tab,避免突兀)。文档化此行为。

### 5.4 Prompt

由内置对话框(`InputBox`/`MessageBox`/自定义 await 流)支撑、**无自己的 src** 的节点。把"小到用 InputBox 就够、但又得能被深链直达"的功能(典型:改名)包成路由目的地。

Prompt 是**叶子**:不可作为任何节点的 parent(它会自动出栈,挂持久子节点无意义)。注册时若有人把 Prompt 声明为自己的 parent → 抛 `RouteException`(§10)。

```csharp
// run 的签名:RouteQuery + 取消信号 → Awaitable
public delegate UnityEngine.Awaitable RoutePromptRun(
    RouteQuery query, System.Threading.CancellationToken ct);
```

| | 动作 |
|---|---|
| activate | **启动** `run(query, ct)`,记录其任务,**立即返回**(不等用户答完)——所以 reconcile 在"对话框已显示"时即完成。 |
| 存活期 | = `run` 运行时长。 |
| 自动出栈 | `run` 正常返回(用户答完、逻辑跑完)→ router 把该节点从链路顶移除 + fire `Changed`。 |
| deactivate(被导航走) | router `Cancel` 它的 `ct` → 内置对话框的 `await` 抛 `OperationCanceledException` → `run` 退栈 → router 移除节点。 |

**取消的接线**:`InputBox.Open` / `MessageBox.Open` 新增**可选** `CancellationToken ct = default` 重载——`ct` 取消时令底层 modal entry `Cancel(OCE)`(复用 §2026-05-20 既有的 `entry.Cancel(oce)` 路径,`ct.Register` 触发)。这是对 `UI.Modal` 体系**纯增量**的重载,不改既有签名/语义。`run` 作者把 `ct` 透传给 `InputBox.Open(..., ct)` 即获得"被导航走时自动撤销改名"的正确行为;不透传则 router 兜底关掉它当前的对话框 slot(对话框 resolve 为取消值,`run` 通常随即 `if(name!=null)` 跳过收尾)。

**示例(改名:小按钮 + 违规提醒深链,逻辑只写一份)**:

```csharp
UI.Router.MapPrompt("rename", parent: "home", run: async (q, ct) =>
{
    var name = await InputBox.Open("请输入新名字", initial: PlayerName, ct: ct);
    if (name != null) await Api.Rename(name);
});

// 小按钮:
renameBtn.OnClick.Subscribe(_ => UI.Router.Open("rename")).AddTo(screen);
// 违规提醒处:
await UI.Router.Navigate("appid://rename?reason=illegal");   // q["reason"] 可用
```

---

## 6. 临时模态的边界

- **纯就地、非目的地的对话框**(流程中间的一个确认)→ 仍完全 ad-hoc `await MessageBox.Open(...)`,**不注册、不进 router**。reconcile 时若它挡在目标链路外则被关掉(§3.3)。
- **既是目的地、又想用内置对话框** → 注册成 **Prompt 节点**(§5.4)。
- `Loading` / `Toast`:始终脱离 router(瞬态、无结果/不可深链)。`Loading` 在 dialog 之下、`Toast` 自管层级,均不参与 reconcile;但顶层临时 `Loading` 若挡路同样可被 §3.3 关掉。

---

## 7. 与 `<TabBar>`/`<Tab>` 控件的集成

复用现有控件公开面,**不新增 XML**:

- `Tab.IsOn { get; set; }`:`set true` 选中并触发内容切换(`OnIsOnChanged → ApplyBindFrame`)。
- `screen.Get<Tab>(idPath)`:按 id 路径取 Tab 控件。
- `TabBar.SelectedIndex` / `SelectedTab` / `GetAt(int)` / `Count` / `OnSelectionChanged`:供诊断与可能的反向同步(用户在 UI 上手点 tab 时,router 链路是否跟随——见 §12 待评估项)。

---

## 8. Back 与当前状态

- `Back()` = `Open(current.Top.Parent.Name)`;top 已是根 → no-op(返回已完成 `Awaitable`)。top 是 Prompt → 取消该 Prompt(deactivate)即回到 parent。
- `Current`:链路顶节点 `Name`(空链路 → `null`)。
- `Chain`:`IReadOnlyList<string>`,root→top 的名字序列。
- `Changed`:`event Action`,每次 reconcile 成功改变链路后触发(供 HUD 高亮当前页等)。

---

## 9. 公开 API 汇总

命名空间沿用 `UI.Xxx` 嵌套惯例(与 `UI.Modal`/`UI.Toast`/`UI.Locale`/`UI.Theme` 一致):

```csharp
namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static class Router
        {
            // —— 配置 ——
            public static string Scheme { get; set; }            // 默认 null:不校验 scheme

            // —— 注册(功能式)——
            public static void Map(string name, string src, string screen = null,
                RoutePresent present = RoutePresent.Page, string parent = null,
                System.Action<IScreen, RouteQuery> onEnter = null);
            public static void MapTab(string name, string parent, string tabId,
                System.Action<IScreen, RouteQuery> onEnter = null);
            public static void MapPrompt(string name, string parent, RoutePromptRun run);
            public static bool IsMapped(string name);
            public static void Clear();                          // 清空注册表(主要给测试 / 重配)

            // —— 导航 ——
            public static UnityEngine.Awaitable Open(string name, RouteQuery query = null);
            public static UnityEngine.Awaitable Navigate(string url);
            public static UnityEngine.Awaitable Back();
            public static UnityEngine.Awaitable Reset();         // 关闭整条链路

            // —— 状态 ——
            public static string Current { get; }
            public static System.Collections.Generic.IReadOnlyList<string> Chain { get; }
            public static event System.Action Changed;
        }
    }

    public enum RoutePresent { Page, Modal }     // Tab/Prompt 由 MapTab/MapPrompt 表达
    public sealed class RouteQuery { /* §4.2 */ }
    public delegate UnityEngine.Awaitable RoutePromptRun(
        RouteQuery query, System.Threading.CancellationToken ct);
    public sealed class RouteException : System.Exception { /* §10 */ }
}
```

- `Map` 的 `screen` 省略时,在该节点**首次激活**(doc 已加载)时解析:doc 恰含一个 `<Screen>` → 用它;含多个 → 抛 `RouteException`(要求显式给 `screen`)。`Map` 本身同步、不加载 doc,故此校验落在激活时。
- `present` 仅区分 Page/Modal(都是"一个 screen",差在 sorting 带 + backdrop)。

`InputBox` / `MessageBox` 各新增一个 `CancellationToken ct = default` 重载(§5.4),为唯一对既有类型的改动,且纯增量。

---

## 10. 错误处理

统一抛 `RouteException`(继承 `System.Exception`),消息含触发的 `name`:

| 情形 | 处理 |
|---|---|
| `Open`/`Navigate` 未注册的 `name` | 抛 `RouteException`("route 'x' not mapped")。 |
| parent 链中有未注册的 parent | 抛 `RouteException`(指出断点)。 |
| parent 链成环 | 抛 `RouteException`(visited 集检测)。 |
| 重复 `Map`/`MapTab`/`MapPrompt` 同名 | 抛 `RouteException`("route 'x' already mapped")。 |
| Tab 节点 activate 时 `Get<Tab>(TabId)` 解析失败 | 抛 `RouteException`(指出 tabId 与宿主 screen)。 |
| 把 Prompt 节点声明为别人的 parent | 抛 `RouteException`(Prompt 只能是叶子,§5.4)。 |
| Page/Modal 节点的 src 的 doc 含多 `<Screen>` 却未显式给 `screen` | 抛 `RouteException`(激活时,§9)。 |
| `Navigate` scheme 与 `Router.Scheme` 不符 | 抛 `RouteException`。 |
| `Navigate` URL 格式无法解析出 name | 抛 `RouteException`。 |

reconcile 进行中某节点 activate 抛错:已激活的部分**不回滚**(屏上是一条合法的部分链路),异常向 `Open` 的 `Awaitable` 抛出由调用方处理(深链入口通常 try/catch + 记日志)。

---

## 11. 与现有系统的关系 / 共存

- Router **建立在** `UI.Open` / `UI.Close` / `UI.Modal` / `InputBox` / `MessageBox` 之上,不改它们对外语义(仅 §5.4 的两个增量 `ct` 重载)。
- **路由管理的 screen 只能经 router 开关**:裸 `UI.Open` 一个 router 节点的 screen 会绕过 reconcile、污染 `_chain` —— 视为调用方错误。Modal 节点的 ESC/关闭按钮须接 `Router.Back()`(§5.2)。
- **非路由界面**(常驻 HUD、splash、世界空间 UI 等)继续用裸 `UI.Open`,与 router 链路并存、互不干扰(它们 sortingOrder 由各自 `CanvasConfigurator` 决定)。
- **teardown 接线**:`UI.ResetForTests` / `UI.UnloadAll` 必须清空 `_chain`(逐个 deactivate 或直接随 `_open` 统一销毁)并取消进行中的 reconcile / Prompt(epoch 自增,仿 `UI.Modal.CancelAllForTeardown`)。`Router.Clear()` 另清注册表。

---

## 12. 待评估项(plan 阶段定夺,不阻塞 v1 主线)

- **用户手点 UI 上的 tab 时,router 链路是否跟随?** 例:shop 屏里用户直接点了 deals tab(没走 `Router.Open`)。v1 默认 router 链路**不**自动跟随(链路只反映经 `Open` 的导航);若要跟随,需订阅 `TabBar.OnSelectionChanged` 把链路顶替换为对应 Tab 节点。倾向 v1 先不跟随,文档说明。
- **Prompt 兜底取消**(作者没透传 `ct`)的精确 slot 关闭策略(关"最近一次该 Prompt 开的 modal" vs "当前栈顶 modal")。
- **routed Modal 与 ad-hoc dialog 栈共处 modal 带时的 sorting / ESC 归属**:原则定为 ad-hoc dialog 永远在 routed Modal 面板**之上**、ESC 先归 ad-hoc 栈顶(routed Modal 在屏时弹的 `MessageBox` 盖在它上面);两套 sorting 计数如何不打架 = plan 阶段定。这也牵涉 §5.2 "复用 vs 复刻" 的取舍。
- **`Open` 到"已在链路中但非顶"的节点**(例 current=`[home,shop,item]`,`Open("shop")`):按 §3.2 等价"反激活 item、停在 shop",即回退。确认这就是期望(应当是——等于 `Back` 到 shop)。

---

## 13. 测试策略

EditMode(`PromptUGUI.Tests.EditMode`,沿用 `UI.ResetForTests` + fake-files 模式):

- **reconcile 核心**:公共前缀计算;跨分支(无公共前缀)全关全开;`Open(child)` 纯 push;`Open(ancestor)` 纯 pop;`Open(sibling)` 关到公共祖先再开。
- **名字/层级解耦**:改一个节点的 `parent`,同一 `Open(name)` 落到新链路;URL `appid://friend` 不因 parent 变而失效。
- **Page/Modal**:激活实例化、反激活销毁;Modal 走高 sorting 带;ESC=Back。
- **Tab**:激活选中 + 内容切换;切兄弟 tab 宿主不重建;离屏随宿主消失;§5.3 边界行为。
- **Prompt**:`run` 跑完自动出栈;导航走时 `ct` 取消、`run` 退栈、不执行收尾;改名示例(button 与 Navigate 同结果)。
- **临时模态边界**(§3.3):顶上压着 ad-hoc `MessageBox` 时 `Open("shop")` 先关它再导航。
- **query 投递**:目标与新激活中间节点都收到完整 query;重复导航刷新目标 `OnEnter`。
- **串行化**:连续两次 `Open`,latest-wins;两个 `Awaitable` 都在最终 reconcile 后 resolve。
- **错误**:§10 各情形抛 `RouteException`;reconcile 中途抛错不回滚、异常上抛。
- **teardown**:`ResetForTests` 清空链路、取消进行中 reconcile/Prompt。

PlayMode(`PromptUGUI.Tests.PlayMode`):sortingOrder 分带(Page 带 < Modal 带);ESC 在 routed Modal 上等价 Back;Prompt 的 InputBox 实弹实收。

### 13.1 测试期 Awaitable 同步展开

EditMode 用 `.GetAwaiter().GetResult()` 同步展开;fake resolver 经 `AwaitableHelpers.Completed(value)` 同步完成,使 reconcile 无真实 yield 点、在测试线程返回(沿用仓库既有模式)。

---

## 14. SKILL.md 影响

`scripting-promptugui-csharp/SKILL.md` —— **必须更新**(新增公开 C# API):

- 新增 "Router / 导航" 一节:`UI.Router.Map`/`MapTab`/`MapPrompt`、`Open`/`Navigate`/`Back`/`Reset`、`Current`/`Chain`/`Changed`、`RoutePresent`/`RouteQuery`/`RoutePromptRun`/`RouteException`;reconcile 心智模型(稳定名字 + 标准链路);四种呈现表;改名 Prompt 范式。
- "Modal dialogs" 节补一句:`InputBox.Open`/`MessageBox.Open` 新增 `CancellationToken ct` 重载,routed Prompt 用它支持"被导航走时撤销"。
- cheatsheet 增 ROUTER 区。

`authoring-promptugui-xml` —— **不更新**(无 XML 元素/属性变更;Tab 节点复用现有 `<TabBar>`/`<Tab>` 与 id 路径)。
`using-promptugui-addressables` —— **不更新**(Router 经 `SourceResolver` 间接用 Addressables,无 Addressables 专属面)。

---

## 15. 实施顺序(plan 阶段细化)

每步 TDD(红→绿)+ lint + UnityMCP 编译/测试。

1. **注册表 + 链路骨架** —— `RouteNode` IR、`UI.Router.Map`/`IsMapped`/`Clear`、parent 链解析 + 环/缺失检测、`RouteException`、`Chain`/`Current`/`Changed`。纯逻辑,无 Screen。
2. **reconcile + Page** —— `Open` 的公共前缀算法 + Page activate/deactivate(走 `UI.Open`/`UI.Close`)+ 串行化(epoch + latest-wins)+ teardown 接线。
3. **RouteQuery + Navigate** —— URL 解析、`Scheme`、query 投递规则(§3.2)。
4. **Modal 呈现** —— 走 dialog 栈,ESC/关闭 = route-aware Back。
5. **Tab 呈现** —— `MapTab`、宿主 screen 解析、`IsOn` 选中、§5.3 边界。
6. **Prompt 呈现** —— `MapPrompt`/`RoutePromptRun`、`InputBox`/`MessageBox` 的 `ct` 重载、自动出栈 + 取消。
7. **临时模态边界**(§3.3)接入 reconcile。
8. **`scripting-promptugui-csharp/SKILL.md` 更新。**

---

## 16. 验收标准

- 同一 `appid://friend?...` 深链与对应按钮 `Open("friend")` 落到**完全相同**的链路与显示。
- 顶上压着模态时深链到别处,先关模态再导航(§1.2 约束 1)。
- 改 `friend` 的 parent,`appid://friend` 不失效、reconcile 走新链路(§1.2 约束 2)。
- 改名:小按钮与"违规提醒"深链走同一 `rename` Prompt,逻辑只写一份;改名中被导航走则撤销。
- Tab:导航选中并切内容;切兄弟 tab 宿主不重建;`home→shop→deals→item` 整链 reconcile。
- 跨分支导航全关全开;`Back`/`Open(ancestor)` 正确回退。
- EditMode + PlayMode 全绿;`dotnet format --verify-no-changes --severity warn` 干净;`UIXmlLint` 对涉及的 XML exit 0。
