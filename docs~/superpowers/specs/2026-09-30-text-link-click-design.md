# `<Text>` 链接点击 —— `Text.OnLinkClicked`

> 状态：**已实现**（2026-09-30，分支 `feat/text-link-click`）。
> 需求来源：宿主 ssw_re_client 的聊天行（`ChatLine`：`[时间]` + 会折行的富文本正文，正文里有玩家名 / 物品等
> TMP `<link="…">`）。聊天行——包括虚拟化后被回收复用的行、迷你态——**在 bind 时订一次**，点击时现查 TMP 的链接信息。
> 相关：`2026-06-09-markdown-control-design.md` MD-D15（`<Markdown>` 的 `OnLinkClicked`，内部 `MarkdownLinkClicker`）、
> `2026-09-15` raycastTarget 规格 §3（命中是声明的，不是画出来的；`RaycastIntent`）、
> `2026-09-29-scrolllist-virtualization-design.md`（行回收与 `bind` 契约）。

## 1. 问题

TMP 能渲染 `<link="id">…</link>`，但只有 `<Markdown>` 在它自己渲染出来的正文上挂了点击组件（internal
`MarkdownLinkClicker`）。普通 `<Text>` 上的链接点不到；游戏只能自己往 TMP 上挂 MonoBehaviour，还要面对：

- `<Text>` 默认 `raycastTarget="false"` 且每个属性 pass 都会把它结算回去 —— 手动打开会被 ReSolve 打回；
- 行被回收 / 文本被改写后，挂组件时记下的任何东西都是旧的；
- 整框打开命中后，迷你态聊天浮在游戏画面上会吃掉正文区域的全部点击；
- uGUI 把 click 交给**离命中点最近的** `IPointerClickHandler`：`<Btn>` 里的带链接文字会把按钮的点击吞掉。

## 2. 决策

| # | 决策 | 理由 |
|---|---|---|
| TL-D1 | 公开 API：`Text.OnLinkClicked : Observable<string>`，发出被点链接的 ID（`<link="ID">` 的 ID） | 与 `Markdown.OnLinkClicked` 同形；ID 由业务编码（`player:42` / `item:9001`） |
| TL-D2 | **点击时现查**：`TMP_TextUtilities.FindIntersectingLink(tmp, e.position, e.pressEventCamera)`，查的是 TMP 此刻的布局；不缓存链接表、不在订阅时读任何东西 | 行被回收换绑、`TextValue` 改写、切语言之后点到的都是屏幕上此刻的链接；bind 里 `.AddTo(row)` 订一次即可（行的订阅袋在下次 bind 前释放）。用松手点而非按下点：按住期间聊天贴底来了新消息、内容上移时，按下点在新布局里已指向别处 |
| TL-D3 | 懒装配：第一次读 `OnLinkClicked` 才挂内部组件 `TextLinkClicker`（`MarkdownLinkClicker` 改名泛化） | 普通 `<Text>` 零成本 |
| TL-D4 | 命中区 = 链接本身：读 `OnLinkClicked` 即意图（`RaycastIntent.Want`，同 `Image.OnPointer*`）打开 `raycastTarget`；组件同时是 `ICanvasRaycastFilter`，作者没写 `raycastTarget` 时**只有落在链接上的点命中**，正文其余部分照旧穿透 | 迷你态聊天浮在游戏画面上，正文不能吃点击；「命中是声明的」—— 订阅声明的是链接，不是整段文字 |
| TL-D5 | 作者写了 `raycastTarget` 时作者赢：`true` = 整框命中（同今天）；`false` + 订阅 = 保持关闭并 warn 一次（链接永远点不到） | `RaycastIntent` 同一套仲裁；Text 的 raycast 结算改由它负责 |
| TL-D6 | 不产生链接点击的 click（没点在链接上 / 非左键 / 链接被静音）交给祖先：`ExecuteEvents.ExecuteHierarchy(parent, e, pointerClickHandler)`，如同这段文字没有链接 | 否则 `<Btn>` / 可点的行里的带链接文字会吞掉它们的点击。同 `CarouselView` / `ReorderDriver` 的 `ForwardToParent` |
| TL-D7 | 只认左键；`interactable="false"`（文字自身或祖先 `CanvasGroup`，按 `Selectable` 的 `ignoreParentGroups` 规则）静音链接。静音的链接照样挡住指针（同 disabled 的 `Selectable`），点击按 TL-D6 交给祖先 | 与 `Btn.OnClick` 一致；右键 / 中键留给祖先（上下文菜单） |
| TL-D8 | `<Markdown>` 改走 `Text.OnLinkClicked`：对渲染子树里每个 `Text` 订阅，并置 `RaycastTarget = true`（整框命中） | 一套机制。Markdown 的视口没有 Graphic，拖动滚动靠正文被命中 —— 所以它要整框，行为与今天一致 |

## 3. 行为一览

| 场景 | 结果 |
|---|---|
| 左键点在链接上 | `OnLinkClicked` 发出该链接 ID；祖先收不到这次 click |
| 点在正文非链接处（未写 `raycastTarget`） | 文字不被命中，指针落到后面的东西上（与没有链接时相同） |
| 点在正文非链接处（`raycastTarget="true"`） | 不发 `OnLinkClicked`；click 交给祖先 |
| 右键 / 中键点在链接上 | 不发；click 交给祖先 |
| `interactable="false"`（自身或祖先） | 不发；click 交给祖先 |
| 按住链接拖动滚动列表 | uGUI 开始拖动即取消 click（`eligibleForClick = false`），不发 |
| 回收复用的行被换绑到另一条消息 | 点到的是这一行现在显示的消息的链接；上一次 bind 的订阅已随订阅袋释放 |

## 4. 非目标

悬停高亮 / 光标形状；事件里带点击位置（锚定浮窗）；`<Btn>` 自动 label、`<InputField>` 里的链接；
XML 端的声明式入口（`<Trigger on="link@…">`）。

## 5. 测试

EditMode `TextLinkTests`（`ExecuteEvents` 走 uGUI 自己的派发，`Graphic.Raycast` 走真实的过滤链；
无相机时屏幕点即世界点）：点中链接发 ID；改写文本后点到新链接；两个链接取指针下那个；非链接处不发且交给祖先；
链接点击不冒泡；右键 / `interactable="false"`（自身、祖先）不发；无订阅时穿透且不挂组件；订阅后只有链接命中；
`raycastTarget="true"` 整框命中；`raycastTarget="false"` + 订阅保持关闭并 warn；ReSolve 后仍命中；
ScrollList 复用行换绑后只发新消息的链接一次。Markdown：点渲染出来的链接发 url；正文整框命中（拖动滚动）。
PlayMode `RaycastHitPlayTests.Linked_text_is_hit_on_its_links_only`：真实 `GraphicRaycaster` 下，
叠在 `<Btn>`（非祖先）上的带链接文字——点正文穿透到后面的 Btn，点链接归文字。
