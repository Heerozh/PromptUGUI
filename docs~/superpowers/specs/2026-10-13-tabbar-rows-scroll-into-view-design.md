# TabBar 动态行、滚到指定控件、运行期状态 —— 一组修复与补口

> 状态：**已实施**（分支 `fix/tabbar-rows-scroll-into-view`，逐条 red 先行、分步提交；bug 修正跳过 plan 直接实现）。
> 来源：一份下游接线调研的修复请求（读源码确认，未在 Unity 里复现；本文的复现全部由测试给出）。
> 相关：`2026-09-29-scrolllist-virtualization-design.md` §5.5（带 key 的 `BindItems`、`ItemBinding`）、
> `2026-09-17-common-attr-runtime-state-design.md`（运行期独占约定）、
> `2026-08-31-hug-reveal-flip-checked-design.md` FND-D10（`checked` 在打开时直接落终态）、
> `2026-08-29-tabmenu-design.md`（`TabGroupCore` 由 `<TabBar>` / `<TabMenu>` 共用）。

## 0. 场景

一个常见的宿主形态：可选中卡片的列表 ——

```xml
<ScrollList id="list" height="clamp(_, hug, 149)">
  <Scrollbar thickness="0" overlay="true"/>
  <TabBar id="rows" direction="vertical" allowSwitchOff="true" itemTemplate="Row"> …静态占位行… </TabBar>
</ScrollList>
```

行模板的根是 `<VStack>`（`<Tab>` 不能按内容 hug），里面是铺满的 `<Tab id="tab">` 卡面，加上兄弟层（头像 `<Image>`、
状态 `<Text>`、进度条、按钮），`<Animation on="checked@tab" reverse-on="unchecked@tab" reveal="y">` 展开第二行。
宿主按 id 推送数据、由别处的选择驱动选中并滚到可见、每秒原地刷新数值。

暴露出的问题：§1 漏行（阻断）、§2 每次推送全量重建、§3 没法滚到 TabBar 里的某一行、§4 代码写的图 / 颜色被 ReSolve
打回、§5 代码改选中一律播动画、§6 `allowSwitchOff` 的栏第一个 Tab 基线错。顺带：§7 padding 小数、§8 shader 裁剪。

## 1. 行是模板实例的整体（TB-R1）

**问题**：`TabGroupCore` 只记得每行里的 `Tab`，重建时只 `Dispose` 它；`Control.Dispose` 只销毁自己的 GameObject。
包装模板每推送一次，上一批行的根和 Tab 的兄弟层（含按钮、`.AddTo(row)` 订阅）都留在 item host 里：继续占排版、
按钮可点、作为动态子树每次 ReSolve 被重放。静态行在第一次推送时同样只删了 Tab。

**决定**：
- `_rows` 与 `_tabs` 一一对应：动态行 = 工厂返回的节点；静态行 = owner 的直接子节点里含那个 Tab 的那个。
- 退役一行 = 先移进一个 inactive 的 Pool（带禁用的 `VerticalLayoutGroup`，同 ScrollList 的池：Play 模式下 `Destroy`
  延到帧末，在那之前行不能再参与排版、命中、`<TabMenu>` 量弹层高度；ReSolve 重放到池里的行时走 layout-group 分支），
  再**由深到浅 Dispose 整棵子树**。只 Dispose 根虽然会销毁整棵 GO、释放全部订阅袋，但内层控件自己的 `Dispose`
  （Tab 的 `Locale.Changed`、Animation 的 motion）就不跑了 —— 今天直接 Dispose Tab 是跑的，不能退步。
  静态行的内层控件 Screen 关闭时还会再被 Dispose 一次，Dispose 可重入（同 Carousel 的静态卡）。

## 2. 带 key 的 `BindItems`（TB-R2）

`TabBar` / `TabMenu` 增加 `BindItems<T, TKey>(source, Action<Tab, T>, key)` 与
`BindItems<T, TSlot, TKey>(source, Action<TSlot, T>, key)`，契约与 `ScrollList` 的带 key 重载一致，直接复用
`KeyedBinding<T, TSlot, TKey>`（按 key 匹配上一次**任何**带 key 推送、null / 重复 key 在改动任何东西之前拒绝整次推送）：

- key 还在的行**原地复用**：重新 bind（先释放上次 bind 挂的 `.AddTo(row)`，同 ScrollList §4.2）、挪到新位置；
  Tab 的 `isOn` 不动 —— **选中跟着 key 走**，它驱动的东西（`bind` 页、`checked@` 动画）不重放。
- 新 key 建新行（追加到末尾，再按数据序整体 `SetAsLastSibling` —— 只在顺序乱了时做；非行的静态子节点留在前面，同今天）。
  消失的 key 按 §1 退役。先定结构、再逐行 bind。
- 选中：复用的选中行什么都不发；选中行被删时**发一次当前状态**：`allowSwitchOff`（或 `ClearSelection` 过）发 `null`，
  否则发自动补选的第一个 Tab。空列表照旧发 `null`。
- 复用的前提：当前行出自带 key 的推送、且 `itemTemplate` 名没变；否则（静态行、无 key 的推送、换模板）整表替换。
- 不带 key 的重载**保持全量重建**（只修 §1）：TabBar 的行按位置复用会让「选中第 i 个」跨推送粘在位置上而不是数据上，
  比全量重建更糟；要保持就给 key。

## 3. `ScrollList.ScrollIntoView(IControl target)`（SIV）

`ScrollToIndex` 数的是 ScrollList 自己的槽位，内容是一个 `<TabBar>` 时只有一个槽位。

- **target**：列表行里的任意控件（`LayoutHost` 必须在 Content 之下；否则 / null → 异常）。虚拟列表只能是已实现行里的
  控件（`ScrollToIndex` 能到任意项），不在 → `ArgumentException`。
- **滚动量**：最小滚动让 target 的矩形完整可见；target 比视口高 → 起始边对齐；夹到可滚范围；停掉惯性；
  两条粘边都不粘（同 `ScrollToIndex`）。隐藏的列表等显示后的第一个 tick 再滚。从 bind 回调里调 → sync 结束后执行。
- **跟踪（SIV-D1）**：选项与默认值之争按「默认跟踪」定 —— 宿主的典型调用是「选中 + 立刻滚到」，此刻展开动画还没开始，
  一次性滚动注定差一个展开量。之后每个 tick：布局（普通列表 `ForceRebuildLayoutImmediate(Content)`；**hug 列表重建它所在的
  布局链根**，否则视口比内容晚一帧长高，按旧视口算出的位置越界、被 ScrollRect 的回弹拉回）→ 重新放到可见 → 记一帧几何
  `(start, end, viewport, content)`。连续 `FollowSettleTicks`（3）帧不变、用户拖动 / 滚轮、下一次 `ScrollTo*` /
  `ScrollIntoView`、target 消失 / 隐藏 / 离开列表、或满 600 帧，即放手。
- **用户接管的判定（SIV-D2）**：Content 动了不等于用户动了 —— 回弹和残余惯性也会动它。`PuiScrollRect` 记录真实输入
  （`OnBeginDrag` / `OnScroll`，`ConsumeUserInput`）；跟踪期间只有真实输入结束跟踪，其余位移直接 `StopMovement`，
  由跟踪把 Content 放回。拖动滚动条不算在内（uGUI `Scrollbar` 无从挂钩），跟踪期间会被覆盖 —— 跟踪只持续到几何稳定，可接受。
- 虚拟列表：用 target 所在行做 `Item` 锚点（`Delta = TopOf(row) − 目标滚动量`）走一次 sync。

## 4. `<Image sprite>` / `<Text color>` 运行期独占（RTS-2）

沿用 09-17 约定（同 `Icon.name`）：`Image` 注册 `runtimeStateAttr: "sprite"`、`Text` 注册 `runtimeStateAttr: "color"`，
`PeekRuntimeState` 返回上次写入的原串（解析失败也记 —— 锁比对的是写了什么）。模板 `<Param>` 默认值也只是初态。

- **Text color 也做独占，不提供「ReSolve 完成」回调（RTS-2-D1）**：状态色正是 bind 回调的日常写法；回调只是让宿主再写一遍，
  把 workaround 合法化。代价与 `isOn` 相同：碰过的节点不再跟 Variant / 主题的声明值。
- **代码写的颜色 token 仍跟主题（RTS-2-D2）**：applier 跳过的是**声明**；`Text` 记住颜色是否由代码写入，每次 pass 末按当前主题
  重新解析它。否则 `status.Color = "danger"` 切主题后停在旧主题的红色。
- 两个属性都**留在**主题 `<Style>` 的允许名单里（同 `hidden` / `interactable`）：换皮就是换图换色。

## 5. 不播动画地改选中（SIA）

`Tab.SetIsOn(bool isOn, bool animate)`、`Toggle.SetIsOn(bool, bool)`、`TabBar` / `TabMenu.ClearSelection(bool animate)`。
`animate: false` = Screen 打开时那种落地：作用域是**这次调用**（`Internal.InstantState`，一个深度计数），不是某个控件 ——
组里被顺带关掉的那个 Tab 也在调用栈里，一起落地。作用域内：

- `checked` / `unchecked` 的 `<Animation>` 走 `onInitial`（`SnapTo`，与 `IsOpening` 同一分支）；普通 `<Trigger>` 的 `onInitial`
  就是 `Fire`，照常触发。
- 状态色（`StateTintReactor.Paint`）与 Toggle 勾选淡入直接到终态。
- 事件（`OnValueChanged` / `OnSelectionChanged` / `bind` 页）照常 —— 宿主的回声保护照常需要。
- 不公开 `Animation.SnapTo`：宿主要的是「这次选中不播」，不是逐个找动画。

## 6. `allowSwitchOff` 在子 Tab 之前生效（TB-R3）

DFS 后序 apply：Tab 先应用 `isOn`，TabBar 自己的 `allowSwitchOff` 后到。这期间 ToggleGroup 仍禁止全关，Play 模式下 uGUI
在激活时把第一个 Tab 强制选中：`allowSwitchOff` 的栏一打开就选中了第一个，声明的 `isOn="false"` 被弹回 `true` 并记成基线 ——
玩家打开它之后第一次 ReSolve 判「没动过」，重放 `isOn="false"`，页面被关掉。

**决定**：同 `ScrollList.PreConfigureContent`：`ScreenInstantiator` 在 `AttachTo` 之后、实例化子节点之前把解析好的
`allowSwitchOff` 交给 `TabBar.PreConfigureGroup` / `TabMenu.PreConfigureGroup`。幂等，apply 时 setter 照常再跑。
EditMode 不跑 ToggleGroup，测试在 PlayMode。

## 7. `PUI-PADDING-INT`

`padding` 在 `<VStack>` / `<HStack>` / `<Grid>` / `<ScrollList>` / `<InputField>` / `<TabBar>` / `<TabMenu>` / `<Collapsible>` 上
都进 `RectOffset`：1 / 2 / 4 段整数（`_` / 空 = 0）。前五个用 `VStack.ParseTRBL`（`int.Parse`，Open 时抛 ParseException），
后三个用 `PaddingParser`（`TryParse`，**静默读成 0**）。新规则（`Core/Lint/PaddingRules.cs`，纯 C#）查基础值、variant 覆盖与
`class=` 带来的值；含 `{{` 的留给展开遍。`<Scrollbar padding>` 是另一套语法，不在内。运行时解析不改（收紧会让今天静默为 0 的
文档打不开）。

## 8. shader 的 RectMask2D 裁剪

库内 7 个 UI shader（ImageFx / LinearLightTint / Grayscale / ProceduralPanel / Decor / GlassPanel / GlassGroup）用
`UnityGet2DClipping` 在像素中心硬切；UI/Default（2020.1 起，canvas shader-graph pass 同款）在裁剪边做覆盖率渐变并读
`_UIMaskSoftnessX/Y`。同一 RectMask2D 下库内 shader 多画一列（glow 在遮罩边漏 1px 亮线），`softness` 对它们无效。

**决定**：`UI-Clip.cginc` 抄 UI/Default 的两段（顶点阶段的 mask 插值、片元阶段的覆盖率），7 个 shader 全部改用，含 stencil
遮罩源路径。`<Frame mask="rect" maskSoftness="…">` 属性另案（C# 写 `RectMask2D.softness` 今天已对全部 shader 生效）。

## 9. 不做 / 另案

- `ScrollList` 退役行只 Dispose 行根（内层控件的 `Locale.Changed` 等不解挂）—— 与 §1 同类，但不在本次请求内，另案。
- 跟踪期间拖动滚动条会被跟踪覆盖（§3 SIV-D2）。
- `maskSoftness` 属性（§8）。

## 10. 测试

| 条目 | 测试 |
|---|---|
| §1 | `TabBarBindItemsTests`：包装模板 3 → 1 → 2 每次只剩本次的行、动态子树数量对得上；`.AddTo(row)` 释放；静态包装行首推整行清掉。`TabMenuBindItemsTests.Rebinding_removes_wrapper_rows_whole` |
| §2 | `TabBarKeyedBindItemsTests`：插入 + 删除 + 换序后选中的仍是同一 Tab、`checked@` 动画不触发；复用 + 每行重绑；选中被删发 `null` / 发补选的第一个；无选中不发；复用行仍报点击；首推替换静态行；重复 key 拒绝整次推送；TabMenu |
| §3 | `ScrollListScrollIntoViewTests`（EditMode，手动 tick）：上 / 下最小滚动、已可见不动、比视口高对齐起始边、夹到范围、单槽位里的后代、横向、虚拟列表、隐藏后显示、拒绝外部控件、跟踪到稳定后放手、滚轮结束跟踪、非用户位移不结束、其他 ScrollTo 结束跟踪。`ScrollListPlayTests`：固定高度与 hug 到上限两种列表里，正在展开的最后一行滚到后最终完整可见（先关掉跟踪确认红） |
| §4 | `ImageTextRuntimeStateTests`：代码写的 sprite / color 跨 ReSolve 与 Variant 保留；未碰的跟 Variant / 主题；bind 写的 sprite 跨 ReSolve；代码写的 token 跟新主题 |
| §5 | `SetIsOnWithoutAnimationTests`：选中与被关的 Tab 都落终态；作用域不外泄；`animate: true` 即普通 setter；`ClearSelection(false)`；Toggle；普通 Trigger 照常触发 |
| §6 | `TabBarPlayTests`（PlayMode）：`allowSwitchOff` 的 TabBar 静态 Tab / 静态包装行 / TabMenu：打开时无选中、基线是声明的 `"false"`、玩家打开后 ReSolve 不关 |
| §7 | `PaddingRulesTests` |
| §8 | `ClipRectRenderTests`：一个遮罩下每个 shader 一行，跨遮罩边逐列与 UI/Default 行比较（三种亚像素位置 + softness 24） |
