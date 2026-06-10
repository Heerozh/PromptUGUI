# V/HStack 里 `<Text scale=…>` 自动布局桥（scaled-text layout bridge）设计

**日期**：2026-06-11
**状态**：设计阶段（待 review，未进入实施）
**作用域**：让直接放在 `<VStack>` / `<HStack>` 下、声明了 `scale`（任意形态 `N` / `Nx` / `<r>r`，base 或 variant）的 `<Text>` 自动获得正确布局——半密度渲染、按整行宽换行、**行高随内容自动增长**、占位 = 视觉尺寸。实现为实例化期自动插入的 wrapper GO + 一个向 LayoutGroup 报告 `TMP preferred × s` 的 `ILayoutElement` 桥组件。XML 零新增属性。
**依赖**：
- [`2026-05-31-scale-device-density-design.md`](2026-05-31-scale-device-density-design.md)（`ApplyBoxPreservingCompensation`、`_canvasFactor`、resize→ReSolve 重算门控）
- [`2026-06-01-scale-canvas-relative-snap-design.md`](2026-06-01-scale-canvas-relative-snap-design.md)（`scale="<r>r"`）
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §6（layout）、§6.5（LayoutElement 通道）

---

## 1. 背景与目标

### 1.1 问题

聊天消息这类场景需要正文同时满足四件事：小一号字（像素字体下用 `scale="0.5r"` 保对齐）、占满行宽、自动换行、**行高随内容撑高**。当前库里这四件事不可兼得：

- **scale 直接写在 V/HStack 子 Text 上**：box-preserving 补偿被有意跳过（`Screen.ApplyBoxPreservingCompensation` 的 LayoutGroup guard——LayoutGroup 的 DrivenRectTransformTracker 每帧重写子节点 anchors/sizeDelta，一次性膨胀贴不住）。结果 `localScale` 生效但无补偿：TMP 按整行宽换行再整体缩半 → 视觉只占半行宽，行高却按未缩放的 preferredHeight 占位（视觉高度的两倍）。即文档里记录的 "small text gap" footgun。
- **文档建议的 Frame 包装**：前提是 Frame 有显式声明的盒子。`width="stretch"` 可以，但 height 必须定值——Frame 是裸 RectTransform，没有任何 `ILayoutElement`，永远无法向 LayoutGroup 报告"内部 Text 换行后有多高"。聊天正文恰恰没有可声明的高度。
- **`height="stretch"`**：语义是 flexible 分剩余空间，内容撑高的 VStack 没有剩余空间 → 0 高。
- **隐形镜像 Text hack**（fontSize 减半的占位文本撑高度）：`0.5r` 在 factor=3 时净缩放是 2/3 而非 1/2，换行点与高度都对不上，不成立。

根因：**自动撑高靠 TMP 自己是 live `ILayoutElement` 被 LayoutGroup 直接量到；scale 的 box-preserving 需要一个"已声明的盒子"。两者中间缺一座桥**——没有任何组件能把"按 W/s 宽换行后的 TMP preferredHeight × s"回报给 LayoutGroup。

### 1.2 目标

作者写下面这段就拿到全部四件事，XML 零新增属性：

```xml
<VStack width="stretch" spacing="2">
  <HStack height="12">…名字/时间行（既有 Frame 定高写法不变）…</HStack>
  <Text id="text" width="stretch" wrap="true" align="middle-left"
        fontSize="12" color="#E8F4FF" tr="false" scale="0.5r">正文占位</Text>
</VStack>
```

验收语义：

1. 文本按整行宽（W/s 设计单位）换行，渲染密度 = scale 指定；
2. 行高（VStack 量到的 preferredHeight）= 视觉高度 = TMP 换行后高度 × s；
3. C# 运行时 `text.Text = "…"` 改内容后，行高下一帧跟着长/缩；
4. 窗口 resize / factor 变化（`Nx` / `<r>r` 重算）、Variant 翻转后几何稳定（幂等）；
5. 顺带修复 HStack 里 intrinsic 宽度 scaled Text 的占位 gap（preferredWidth 同样 × s）。

### 1.3 非目标

- **Grid 子节点**（STW-D2）：cellSize 本身就是声明盒，无撑高需求；DrivenRectTransformTracker 问题同样存在。维持现状（footgun 文档化）。
- **Text 以外的控件**：Btn/Image 等在 LayoutGroup 里的 scale footgun 不在本次范围（它们没有"内容随宽度二次量算"的需求，Frame 包装写法够用）。
- **Carousel 卡片**：Strip 不是 LayoutGroup，本来就走自由定位补偿路径，不受影响、不需要桥。
- 不提供行为开关 / opt-out（YAGNI，见 §5.1）。

## 2. 方案概览（STW-D1：运行时自动包装，IR 不动）

`ScreenInstantiator.InstantiateRecursive` 创建 GO 时，三条件同时满足则在 Text GO 外插一个 wrapper：

1. 控件实例 `is Controls.Text`（`Text` 是 sealed，即内置 `<Text>` 本尊；其他控件不在范围，见 §1.3）；
2. 父 transform 带 `HorizontalOrVerticalLayoutGroup`（覆盖 V/HStack、ScrollList Content、TabBar 根、Markdown 内部栈；排除 Grid）；
3. 节点声明了 `scale`——base 属性**或任意 variant 覆盖**（variant 运行期才激活而 GO 永不重建，必须创建期备好）。

```
VStack (VerticalLayoutGroup)
 └─ "<id> [scale-host]"          ← wrapper：RectTransform
        + LayoutElement           （ApplyLayoutElement 照常挂，承接 width="stretch"/显式定值）
        + ScaledTextLayoutBridge  （新组件：ILayoutElement，报告 TMP preferred × s）
     └─ <Text> GO (TMP)           ← 全 stretch 基线 + 现有 box-preserving 膨胀（×1/s）
```

被否方案：

- **IR 层 desugar**（TemplateExpander 后改写成合成 Frame 节点 + 属性搬家）：需要合成 tag 注册、`_nodeMap` 出现作者没写过的节点、属性按布局/视觉在 IR 层拆分搬家（含 variant 列表）——三个新不变量，改动面大于本方案的"一个路由点 + 一个新组件 + 一个实例化点"。
- **同 GO 代理 / Unity childScale**：`childControlWidth=true` 下子 rect 必然等于分配尺寸，TMP 拿不到 W/s 的换行宽度；`childScaleWidth/Height` 只影响固定尺寸子节点的空间预算，对 flexible 子节点是 no-op（`Screen.cs` guard 注释已实证）。

生命周期（STW-D3）：wrapper **一次创建、永不销毁**（同 Add 块 Strategy C，引用与 R3 订阅存活）。`scale` 在当前 variant 组合下未解析时，桥退化为 ×1 透传（≡ 裸 TMP 语义，§3.2）。

## 3. 组件设计

### 3.1 `Control.LayoutHost`（路由点，STW-D4）

`Control` 增加内部属性 `RectTransform LayoutHost`，默认 `=> RectTransform`；实例化器包装时指向 wrapper。`ApplyCommon` 改两处：

- **父级 LayoutGroup 判断改读 `LayoutHost.parent`**——wrapper 模式下找到 VStack，`parentIsAutoLayout` 照常为 true，进 `ApplyLayoutElement` 通道；
- **`ApplyLayoutElement` 的 LayoutElement 挂到 `LayoutHost.gameObject`**——`stretch[*N]`、显式定值钉 `min=preferred`（LGC-D17）、`UsesIntrinsicLayoutSize` 省略轴留 -1 哨兵（BCS-D7）这整套现有逻辑不动，只换落点。

wrapper 模式下 ApplyCommon 额外把**内层 RT 重置为全 stretch 基线**（anchorMin 0,0 / anchorMax 1,1 / sizeDelta 0 / anchoredPosition 0 / pivot 0.5,0.5）。这正是 `ApplyScales` 期待的"ApplyCommon 先重置、我再膨胀"契约——于是 **`ApplyScaleToNode` / `ApplyBoxPreservingCompensation` 零改动**：内层 parent 是 wrapper（非 LayoutGroup），guard 自动放行，三种 scale 形态的膨胀逻辑原样复用。

零散路由（STW-D5）：

| 属性 | 落点 | 理由 |
|---|---|---|
| `width` / `height` / `size`（含 stretch/定值/variant） | wrapper 的 LayoutElement | LayoutGroup 量的是 wrapper |
| `hidden`（含 `.variant`，及 C# `Hidden` setter） | wrapper `SetActive` | 隐藏内层会留下占行高的空 wrapper（LayoutGroup 只忽略 inactive 的**直接子节点**） |
| `interactable` | 内层 CanvasGroup（现状不动） | raycast 阻断覆盖子树即可，wrapper 无 Graphic |
| `id` / `Get<T>` / `_nodeMap` / id scope | 不变（wrapper 无 id、不进任何 map） | wrapper 是 Text 控件的实现细节 |
| 其余视觉属性（fontSize/color/align/…） | 内层（现状不动） | |

ReSolve 自然正确：`Screen.ReSolve` 对每个 node 重跑 `ControlAttributeApplier.Apply` → `ApplyCommon` 经 `LayoutHost` 路由 → 再 `ApplyScales` 膨胀内层。Variant 翻转 `width.mobile="stretch"`、resize 重算 `<r>r` 都走这条既有链路。

### 3.2 `ScaledTextLayoutBridge`（新组件，STW-D6）

`internal sealed class ScaledTextLayoutBridge : UIBehaviour, ILayoutElement`，挂 wrapper GO，持内层 TMP + 内层 RT 引用（实例化器注入）。

**报告值**（s = 实时读内层 `RT.localScale.x`，未解析时 ApplyScaleToNode 已置 1 → 自动透传）：

| ILayoutElement 属性 | 值 |
|---|---|
| `minWidth` / `minHeight` | TMP 对应值 × s |
| `preferredWidth` / `preferredHeight` | TMP 对应值 × s |
| `flexibleWidth` / `flexibleHeight` | TMP 对应值原样（权重无量纲） |
| `layoutPriority` | 0（与 TMP 持平） |

priority 0 被 wrapper 上标准 LayoutElement（priority 1）逐属性压过：作者显式写的轴按显式值走、省略的轴 fall through 给桥——与今天"裸 TMP + LayoutElement"的逐属性交互完全同构。

**量算时序**：依赖 Unity 布局重建的标准四段 pass（水平输入 calc → 水平 set → 垂直输入 calc → 垂直 set，整树递归）。水平 set 把 wrapper 宽度定下 → 内层 TMP rect 经放宽的 anchors **被动**跟到 W/s → 垂直输入 calc 时桥读 `tmp.preferredHeight`（TMP 按需在 W/s 宽下重量算）× s。不需要自定义 pass、不需要 ILayoutController。

**脏标传播（STW-D7，动态撑高的关键）**：包装后 TMP 自己的 `LayoutRebuilder.MarkLayoutForRebuild(tmpRT)` 向上找 layout root 时在 wrapper（无 ILayoutGroup）就停了，**到不了 VStack**。两个补偿通道：

1. 桥订阅 `TMPro_EventManager.TEXT_CHANGED_EVENT`，回调里过滤 `obj == _tmp` → `LayoutRebuilder.MarkLayoutForRebuild(wrapperRT)`（wrapper 的 parent 是 LayoutGroup → root 正确落到 VStack）。OnEnable 订阅 / OnDisable 退订，OnEnable 时补一次 mark（active 翻转后兜底）。
2. `Screen.ApplyScaleToNode` 写完 localScale 后，若 `control.LayoutHost != control.RectTransform` 补一次 `MarkLayoutForRebuild(LayoutHost)`——覆盖 resize / Variant 改 scale 但文本未变的场景。

无回环风险：mark 只入队 rebuild，桥的报告值是纯读取，不在回调里改几何。

### 3.3 实例化点（STW-D8）

`InstantiateRecursive` 在 `new GameObject(...)` 后、`AttachTo` 前判定三条件；命中则：

1. `wrapperGO = new GameObject($"{node.Id ?? node.Tag} [scale-host]", typeof(RectTransform))`，parent 到原 parent；
2. Text GO parent 到 wrapperGO；
3. `AddComponent<ScaledTextLayoutBridge>()` 并注入 TMP / 内层 RT 引用；
4. `control.LayoutHost = wrapperRT`。

prefab 形式注册的 `Text` tag 同样适用（包装发生在 `Object.Instantiate` 之后，对 prefab 内部结构无感知）。动态路径（BindItems / Markdown 的 `InstantiateNode`）共用本方法，零特判；`RegisterDynamicSubtree` → `ApplyScalesTo` 的 `_dynamicScaleBaseline` 捕获/还原的是内层 RT 的 stretch 基线，机制不变。

## 4. 边界情形

| 情形 | 行为 |
|---|---|
| `scale.mobile=""` 激活（scale 未解析） | ApplyScaleToNode 置 localScale=1 且不膨胀；ApplyCommon 已重置内层为 stretch 基线；桥 ×1 透传 ≡ 裸 TMP。视觉与无 scale 等价（多一个透明 GO） |
| 显式 `height="40"` + scale | wrapper LE（priority 1）钉高度；桥只补省略轴 |
| `wrap="false"` + `overflow="ellipsis"` | wrapper 宽度被组定死 → 内层 W/s 也定死 → 省略号照常触发 |
| `autosize="true"` | TMP 在膨胀 rect 内自行压字宽，与桥正交（桥只读最终 preferred） |
| HStack 里 intrinsic 宽 scaled Text | preferredWidth × s → 占位 = 视觉（gap footgun 修复） |
| 空文本 `<Text/>`（运行时才赋值） | 桥报 TMP 实测值（≈0），首帧 0 高，赋值后 TEXT_CHANGED 撑开——与裸 TMP 在 VStack 里行为一致 |
| `hidden="true"` | wrapper SetActive(false)，不占行高 |
| Grid 子节点 | 不包装，维持现状 footgun（文档化） |
| hot reload | 整树重建 → wrapper 随之重建 |
| `Screen.Close` / Dispose | wrapper 在子树内，随根销毁；桥 OnDisable 退订 TMP 事件 |

## 5. 兼容性与文档

### 5.1 行为变更（breaking-ish，STW-D9）

所有现存"V/HStack 直接子 `<Text>` + `scale`"的 XML 渲染会变：从"半尺寸渲染 + 整尺寸占位（gap）"变为"占位 = 视觉"。这是修复文档化的 footgun，方向只会更符合作者意图；**不提供 opt-out**。PR 描述中明示。C# 侧可见的变化：`Get<Text>(id).RectTransform.parent` 多一层 wrapper（不影响 `Get<T>` 本身）。

### 5.2 文档同步

- **XML SKILL**（`authoring-promptugui-xml/SKILL.md`）"Where to put scale" 一节：LayoutGroup-skip caveat 改写为"`<Text>` 在 V/HStack 下自动桥接（占位 = 视觉、行高随内容）；**其余控件**仍需 Frame 包装"；同步 `<Text>` 小节与 Quick reference。
- **master spec** 补节引用本设计（决策号 STW-D1…D9）。
- C# SKILL 无需改（无公开 API 变化；wrapper 属"transparent default"运行时行为，但 RectTransform.parent 多一层这点在 XML SKILL 的 scale 节顺带一句）。

## 6. 测试策略（Red 先行）

**EditMode**（`PromptUGUI.Tests.EditMode`，`UI.ResetForTests()` 惯例）：

1. 包装条件矩阵：Text+VStack+scale → 有 wrapper（GO 名 / LayoutHost != RectTransform）；无 scale / Frame 父 / Grid 父 / 非 Text 控件 → 无；仅 variant 声明 → 有。
2. 路由：`width="stretch"` 落 wrapper LE（preferred=0/flexible=1）；显式 height 钉 wrapper LE `min=preferred`；省略轴 wrapper LE 留 -1；`hidden` 切 wrapper active；内层 RT = stretch 基线 + 膨胀后 anchors（断言模式复用既有 scale 测试）。
3. 桥数学：preferred/min × s、flexible 透传、scale 未解析时 ×1、`Nx` / `<r>r` 下 s = 实际 localScale。
4. ReSolve 幂等：variant 翻转 scale、模拟 factor 变化（`0.5r`）后内层 anchors / wrapper LE 稳定（连跑两次 ReSolve 结果相同）。
5. 动态子树：BindItems 项模板里的 scaled Text 有 wrapper 且 `_dynamicScaleBaseline` 还原正确。

**PlayMode**（真实布局 pass，`PromptUGUI.Tests.PlayMode`）：

6. VStack 行高 ≈ `tmp.preferredHeight × s`（容差 1px）；换更长文本 → 下一帧行高增长（脏标传播）；resize 改 factor → `0.5r` 净缩放变化后行高跟随。

**工具链**：UnityMCP 跑三套测试（EditMode / EditorOnly / PlayMode）+ `dotnet format --verify-no-changes`。
