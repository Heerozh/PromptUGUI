# 像素位置吸附（pixel position snap）设计

**日期**：2026-06-17
**状态**：设计阶段（待 review，未进入实施）
**作用域**：Pixel 模式（`scale-mode="pixel"`）下，让 **TMP 文本**的渲染原点自动吸附到设备整数像素网格，补上 `Canvas.pixelPerfect` 唯一漏掉的常见屏幕渲染器。修复"像素字体/中心锚点文字随屏幕尺寸时糊时清、看着像斜体"的问题。XML 零新增属性、C# 零新增 API（透明默认行为）。
**依赖**：
- [`2026-05-31-scale-device-density-design.md`](2026-05-31-scale-device-density-design.md)（`ScaleMode.Pixel`、整数 `scaleFactor`、`Canvas.pixelPerfect = (mode==Pixel)`、`_canvasFactor`、`OnCanvasDimensionsChanged` resize 门控、`ApplyScales`/box-preserving）
- [`2026-06-11-scaled-text-layout-bridge-design.md`](2026-06-11-scaled-text-layout-bridge-design.md)（V/HStack 直下 scaled `<Text>` 的 wrapper + TMP 内层）
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md)（scale-mode 章节）

---

## 1. 背景与目标

### 1.1 问题

Pixel 模式下 `scaleFactor` 是整数（`PixelScaleSolver` 取 floor），`Canvas.pixelPerfect` 已打开。理论上一切清晰，但实测仍出现：中心锚点的像素字（Cubic 11）、自由定位的图标，在**某些屏幕尺寸**下半边偏移 1px、看着像斜体；改一下窗口尺寸有时修复、再改又歪；移动元素坐标能修。

**已在宿主 Unity 实测定位根因**（`scaleFactor=3`、`pixelPerfect=true`、临时 canvas 探针）：

- `Canvas.pixelPerfect` 是靠各个 uGUI `Graphic`（Image/RawImage/旧 Text）在生成网格时调 `GetPixelAdjustedRect` 实现的，**把矩形位置和尺寸都吸到整数设备像素**（实测：位置 `-5.00→-4.90`；尺寸 `30.9→31`、`49.2→49`）。
- **TMP 完全绕过这条路径**——它自己接管字形网格生成，不调 `GetPixelAdjustedRect`。所以 TMP 文本的 rect 原点一旦落在半像素（中心锚点 + 屏幕尺寸奇偶性 → 画布中心落在 `.5`），字形位图就跨纹素采样 → 半边偏 1px。
- **为何 resize 时好时坏**：纯中心锚点文字，resize 时本地 rect 不变、库的 `_hasFactorScale` ReSolve 门控也不触发，但 Unity 的锚点系统会自动把它重定位到新中心 → 世界坐标变成半像素，没有任何一方去重新吸附。屏幕尺寸的奇偶翻转 → 中心落格/落半像素翻转 → 时清时糊。

> 一句话：**Pixel 模式承诺"清晰"，但只靠 `Canvas.pixelPerfect` 兑现不了——它管不到 TMP 文本。** 库的 C# 侧目前没有任何世界坐标吸附（`scale` 链路只动 `localScale` 管密度、box-preserving 只为保盒子动 anchor/sizeDelta，`anchoredPosition` 原样不动），位置对齐全托付给了 `pixelPerfect`。

### 1.2 目标

Pixel 模式下，库创建的每个 TMP 文本，其渲染原点自动落在设备整数像素上，且在**布局重排 / 画布 resize / ReSolve / 父级移动**后保持落格。作者/调用方零改动。验收语义：

1. 中心锚点 `<Text>`：任意屏幕尺寸（奇/偶宽）下渲染清晰，不再随尺寸时糊时清；
2. resize 拖动后稳定落格（独立于库的 ReSolve 门控）；
3. LayoutGroup 子节点文本（如 Tab label）落格；
4. 与 `scale`（密度）正交叠加、与 scaled-text wrapper 共存；
5. 动态文本（`BindItems` / `<Markdown>`）同样落格；
6. Auto 模式零开销、零行为变化。

### 1.3 非目标

- **精灵的非整数倍缩放撕裂**（缩放轴，PPS-D1）：例如 16×16 源图被放大到 24×24（1.5×）→ 点采样不均撕裂。这是"屏上尺寸 ≠ 源图整数倍"，与位置无关，`pixelPerfect` 把尺寸吸到整数设备像素也救不了（救不到整数**倍**）。本次**明确搁置**——属作者侧规范（图标用原生/整数倍尺寸）+ 未来 lint 兜底。
- **Image/RawImage 的位置吸附**：实测 `pixelPerfect` 已覆盖；再去吸是冗余，且会和它打架、或让相邻 LayoutGroup 子节点（如 TabBar tab 背景）出现 1px 缝。**不碰**（PPS-D1）。
- **TabBar 分数宽**（`xx.75`）：tab 背景是 Image（边已被 `pixelPerfect` 吸），label 是 TMP（本方案管）。tab 的 `anchoredPosition` 值仍是分数——这是布局值，渲染由上述两条覆盖，不追求 Inspector 数值整数化。
- 不提供 opt-in / 新属性；唯一 opt-out 复用既有的 `pixelPerfect` 开关（PPS-D7）。

## 2. 方案概览（PPS-D1：只补 TMP 文本，自包含组件）

新增内部组件 `PixelSnap : UIBehaviour`，挂在库创建的每个 `TMP_Text` 上（仅 Pixel 模式，PPS-D2），在 `Canvas.willRenderCanvases`（PostLateUpdate）里把该文本的**对齐感知参考点**吸到设备整数像素网格（PPS-D3/D4），运行期自门控于 `canvas.pixelPerfect`（PPS-D7）。

```
<Text>/label/Markdown 片段 (TMP_Text)
   + PixelSnap        ← 新组件：每帧（仅当位置变化时）把渲染参考点吸到设备格
```

**为何只补 TMP 文本**：实测 `pixelPerfect` 已把所有 `Graphic` 派生组件（Image/RawImage/旧 Text）的位置+尺寸吸到整数设备像素；屏幕 UI 里唯一绕过这条路的就是 TMP。把吸附限定在 TMP 文本，是对"已实证的洞"的最小精确修复——更紧、更低风险（不和 `pixelPerfect` 的 Image 路径重叠/冲突）。

**为何用 per-element 组件而非 Screen 集中 pass**：核心诉求是"resize 后落格"。纯中心锚点文字在 resize 时由 **Unity 的锚点系统**重定位，库的 `_hasFactorScale` ReSolve 门控**不触发** → Screen 集中 pass 不会重跑 → 文字 resize 后又歪（正是本 bug）。per-element 组件盯自己 transform 的变化，能在 Unity 重排后立即重吸，**不依赖库的 ReSolve**。这是组件方案相对集中 pass 的决定性优势。

被否方案：
- **Screen 集中后处理 pass**：resize 反应要么漏（门控不触发），要么得为所有 Pixel 屏在每次 resize 无条件重跑全树——比一个自反应组件更重且更易漏。
- **改 Canvas.pixelPerfect 之外另设全局吸附**：无法触达 TMP 字形（同样得 per-text）。
- **TMP Pixel-Snap 着色器**：per-material、对图文混排/描边材质组合脆弱，且不受库控制；组件吸 Rect 更可靠、版本无关。

## 3. 组件设计

### 3.1 挂载（PPS-D2，由 Screen 在 Pixel 模式下注入）

挂载发生在 **Screen**（而非 ScreenInstantiator），因为 scale-mode 在 `Screen.Open` 的 `ResolveScaleMode()` 才确定：

- **静态**：`Open` 构建完、`ApplyScales` 之后，若 `ResolveScaleMode() == Pixel`，对 `root.GetComponentsInChildren<TMP_Text>(includeInactive: true)` 每个**幂等**确保挂一个 `PixelSnap`（已有则跳过；注入该 TMP 引用）。控件类型无关——`<Text>`、Btn/Tab/Toggle/Dropdown 的 label、InputField 文本、`<Markdown>` 片段全覆盖。
- **动态**：`RegisterDynamicSubtree`（`BindItems` / `<Markdown>` 的 `InstantiateNode` 后调用）在 Pixel 模式下扫描新子树的 `TMP_Text` 并补挂。
- **ReSolve**：`ReSolve` 在 `ApplyScales` 之后也调用 `AttachPixelSnaps(RootGameObject)`（幂等重扫描），覆盖两类情形：运行时首次激活的 `<Add when="...">` 块（Strategy C：首次激活在 ReSolve 内通过 `InstantiateRecursive` 完成，绕开 `RegisterDynamicSubtree`）；以及运行时 `scale-mode` auto→pixel 变体翻转（`ApplyCanvasScaler` 将 `_isPixelMode` 设为 `true`，但 `Open` 时未扫描）。
- **Auto 模式**：不扫描、不挂 → 零开销、零行为变化（满足目标 6）。

> `includeInactive: true` 是必须的——初始隐藏的 Tab 页里的文本也要在显示时落格（镜像 common-controls-sample 抓到的 inactive-bound-page 坑）。

### 3.2 吸附数学与时序（PPS-D3）

`internal sealed class PixelSnap : UIBehaviour`，持目标 `TMP_Text` + 其 `RectTransform` + `Canvas` 引用。

**门控**：`canvas == null || !canvas.pixelPerfect || canvas.renderMode == WorldSpace` → 直接返回（PPS-D7）。

**時序（`Canvas.willRenderCanvases`，PostLateUpdate，不碰共享 `transform.hasChanged` 标志）**：
- 每帧计算对齐感知参考点（局部坐标，见 §3.3）；
- 用 `RectTransformUtility.WorldToScreenPoint(camera, refWorld)` 把参考点转成屏幕坐标（Overlay canvas 传 `null` camera，Camera-Space 传 `worldCamera`）；
- 对屏幕坐标各分量 `Mathf.Round`，得目标整数屏幕像素；
- 用 `RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, screenRounded, camera, out snapWorld)` 反算回世界坐标；
- `rt.position += (snapWorld - refWorld)`——平移整个 RT 使参考点落格；
- 已落格保护：`(snapWorld - refWorld).sqrMagnitude < 1e-4f` 则提前返回，不写入。
- 静态屏稳定：落格后下一帧仍 `sqrMagnitude < 1e-4f` → 提前返回、不重复写入。（注意：仅此"已落格"保护**不足以**防累积——慢速滚动时每帧亚像素位移使参考点反复"刚好偏离格"、每帧都写入，故必须配合下面的基线追踪。）

**非累积基线追踪（防慢速滚动脱轨 bug）**：吸附偏移是瞬时视觉修正，不属于元素的逻辑位置。实现采用基线追踪策略：每帧记住"逻辑 `localPosition`（不含上一帧偏移）"作为 `_baseLocalPos`；若当前 `localPosition == _baseLocalPos + _lastOffset`（误差 < 1e-6），则外部未改动，直接恢复到 `_baseLocalPos` 再重新吸附——修正不叠加；若等式被打破（layout / resize / ReSolve 动了 `localPosition`），则重新取基线（无陈旧残差）。滚动改的是父级 content 的 `anchoredPosition`，不动子节点的 `localPosition`，因此慢速滚动（< 0.5px/帧，每帧修正量相同）不会让修正累积为漂移——文字跟随滚动移动，不被"钉"在初始屏幕像素上。

**为何 `Canvas.willRenderCanvases`、为何不用 `hasChanged`**：`willRenderCanvases` 是 Unity 的 PostLateUpdate 阶段静态事件，在**所有**组件的 `LateUpdate` 和布局重建均完成后、canvas 实际渲染之前触发——设置 transform 在该帧渲染中即生效（网格坐标是局部空间，transform 矩阵在绘制时才应用）。使用 `LateUpdate` 有一个关键问题：`ScrollRect` 在自己的 `LateUpdate` 中通过惯性移动 content 的 `anchoredPosition`，而两个组件的 `LateUpdate` 执行顺序在 Unity 中是**未定义**的——若 `PixelSnap.LateUpdate` 先跑，吸附用的是 ScrollRect 移动前的陈旧位置，本帧渲染前 ScrollRect 再移动 content，吸附偏移不再正确 → 惯性滚动时文字每帧偏离整数像素网格而发糊/斜；静止时 ScrollRect 不移动，吸附永远用最终位置，故只有滚动时可见。改挂 `willRenderCanvases` 后，吸附必然跑在 ScrollRect 惯性移动之后，逐帧落格。`hasChanged` 是全进程共享的单一 bool（被别处读/重置会互相干扰），故不依赖它——每帧直接重算参考点屏幕坐标、已落格则提前返回（不写入），无额外缓存状态。UIBehaviour.`OnDisable`（含销毁前调用）中必须反订阅 `Canvas.willRenderCanvases -= Snap`，避免对禁用/已销毁对象调用。

### 3.3 对齐感知参考点（PPS-D4，取代 brainstorm 的"吸偶数宽"）

只吸 rect 原点对**居中/右对齐**文本不够：TMP 把文本块按对齐方式相对 rect 摆放，块的渲染边 = `rectEdge + 对齐偏移`，偏移随 rect 宽与块宽变化。故吸附的参考点要**随 TMP 对齐方式选取**：

| `TMP_Text` 对齐 | 水平参考点 | 垂直参考点 |
|---|---|---|
| Left / Top | rect 左边 / 上边（= 原点） | |
| Center | rect 水平中心 | rect 垂直中心 |
| Right / Bottom | rect 右边 / 下边 | |
| Justified | 同 Left（块左边对齐） | |

对**像素字体**（字形步进为整数像素），参考点落格 + 整数步进 ⇒ 字形落格。

> **改自 brainstorm 提案**：原提"把 rect 宽吸成偶数设备像素"——但 (a) LayoutGroup 子节点的宽由布局驱动（DrivenRectTransformTracker），外部改宽贴不住；(b) 即便偶数宽，块宽为奇时块左边仍落半像素。改吸**参考点**（只动 `localPosition`，不动 rect 尺寸）→ 既不和驱动宽打架，又对 stretch/驱动宽的居中文本同样有效。
>
> **已知残差**：居中/右对齐 + 文本**内容宽为奇**像素时，按 rect 几何吸参考点仍可能留 ≤0.5px 残差（块宽奇偶取决于内容）。完全消除需读 TMP 实际渲染块边（`textBounds`/`textInfo`，随 `TEXT_CHANGED` 重吸）——列为后续增强，非 v1 范围（像素字体多左对齐，居中为少数）。文档化。

### 3.4 与 `scale` / scaled-text wrapper 的叠加（PPS-D5）

- `scale` 管密度（`localScale`），`PixelSnap` 管位置，正交。吸附经目标 transform 的 `TransformPoint`/`WorldToScreenPoint` 计算，自动含 `localScale`。
- ReSolve 时 `ApplyCommon` 把 RT 重置到 margin 基线、`ApplyScales` 再膨胀；`PixelSnap` 的 `localPosition` 微调随之被抹掉，下一帧 `willRenderCanvases` 重吸（不累积、收敛）。
- scaled-text wrapper 模式（V/HStack 直下 scaled `<Text>`）：`PixelSnap` 挂在**内层 TMP** 上、吸内层原点；wrapper 的位置由布局驱动，内层在 wrapper 内 stretch，吸附是内层 `localPosition` 的亚像素微调，共存无冲突。

## 4. 边界情形

| 情形 | 行为 |
|---|---|
| Auto 模式（非 pixel） | Screen 不挂 PixelSnap → 零开销、零变化 |
| Pixel 模式 + CanvasConfigurator 关掉 `pixelPerfect`（求平滑 tween） | 组件在但门控返回 → inert（与既有 opt-out 杠杆一致，PPS-D7） |
| World Space canvas | 门控 `renderMode == WorldSpace` 直接返回（pixelPerfect 在世界空间本就无意义） |
| 居中/右对齐 + 奇内容宽像素字 | 按 rect 几何吸 → 可能 ≤0.5px 残差（文档化，后续增强） |
| `<Text scale=…>`（含 Nx/`<r>r`/wrapper） | 吸内层 TMP 原点，与密度正交叠加 |
| 动态文本（BindItems/Markdown） | `RegisterDynamicSubtree` 在 Pixel 模式补扫描挂载 |
| 运行时改文本内容 | 块宽变 → 参考点签名变 → 下一帧重吸 |
| ReSolve（Variant/theme/resize 重算） | ApplyCommon 重置、`AttachPixelSnaps(RootGameObject)` 幂等补挂新增文本、PixelSnap 下一帧重吸 |
| 画布 resize（无 scale 的中心锚点文字） | Unity 锚点系统重定位 → transform 变 → PixelSnap 重吸（**本 bug 的修复点**，独立于 `_hasFactorScale` ReSolve 门控） |
| ScrollRect 惯性滚动（Y 持续变化） | 在 willRenderCanvases 吸附（晚于 ScrollRect 的 LateUpdate 内容移动）→ 逐帧落格、不发糊 |
| ScrollRect 内文本 + 慢速滚动（< 0.5px/帧） | 非累积基线追踪：文字随滚动移动、不被钉在初始像素、localPosition 不漂移 |
| 旋转/倾斜（`<Animation>` 旋转文字） | 只吸位置参考点；动画进行中 ≤1px 抖动不可见，静止时正确 |
| hot reload | 整树重建 → Screen 重新扫描挂载 |
| `Screen.Close` / Dispose | 组件随 GO 销毁 |

## 5. 兼容性、性能与文档

### 5.1 行为变更（PPS-D6）

仅影响 **Pixel 模式**屏幕：TMP 文本从"可能随尺寸亚像素发糊"变为"稳定落格清晰"——纯修复，方向只更符合意图。Auto 模式无变化。无新增 XML/API；唯一 opt-out 是关 `pixelPerfect`（既有杠杆）。非 XML builtin tag → **无需同步 `Runtime/Core/Lint/BuiltinTags.cs`**。

**性能**：每个 TMP 文本订阅一个 `Canvas.willRenderCanvases` 回调，每帧执行约 2 次廉价变换调用 + 一次 `localPosition` 写入。**静态屏的每帧写入无法跳过**——父级 ScrollRect 移动元素的世界坐标而不改变其 `localPosition`，故吸附必须每帧重算（"已对齐"保护的是 `rt.position` 写入，不能跳过 `localPosition` 恢复 + 参考点求值）；不触发 layout/canvas 重构，开销低但非零，属有意为之（正确性优先于微优化）。几十上百文本量级在 Profile 下可忽略；若将来成为热点可加签名缓存（需同时解决 ScrollRect 可见性问题）。

### 5.2 文档同步

属"透明默认运行时行为"（无作者可写的 API/属性）——按既有惯例（pure default/fallback 运行时行为豁免 SKILL 详述），**不新增 XML SKILL 条目**。仅：
- **C# SKILL**（`scripting-promptugui-csharp`）的 `UI.CanvasConfigurator` / Pixel 模式一节加一句：Pixel 模式下库会把 TMP 文本吸附到像素网格保持清晰；如需平滑 tween 可在 CanvasConfigurator 里关掉该 Canvas 的 `pixelPerfect`（同时关闭吸附）。
- **master spec** 补节引用本设计（决策号 PPS-D1…D7）。

## 6. 测试策略（Red 先行）

**先写复现红测试**（systematic-debugging Phase 4）：Pixel 模式、受控 canvas 尺寸（奇宽）、中心锚点 TMP → 断言其设备原点为分数（无吸附时失败方向）→ 实现后转为"设备原点为整数"。

**EditMode**（`PromptUGUI.Tests.EditMode`，`UI.ResetForTests()`）：
1. 挂载矩阵：Pixel 模式 Open 后每个 TMP 文本有 PixelSnap（静态 + BindItems/Markdown 动态子树）；Auto 模式无。
2. 门控：`pixelPerfect=false` 时组件 inert（不改 localPosition）。
3. 吸附数学（受控 canvas + 已知 `scaleFactor`）：给定亚像素设备位置 → 吸后参考点设备坐标为整数；按对齐选参考点（left→左边、center→中心、right→右边）。
4. 与 `scale` 叠加：scaled 文本（含 wrapper）吸后原点落格、密度不变。
5. 幂等：连跑两帧 `LateUpdate` 结果稳定、不累积。

**PlayMode**（真实布局 + Canvas，`PromptUGUI.Tests.PlayMode`）：
6. 中心锚点文本在奇/偶画布尺寸下设备原点均为整数；模拟 resize（改 canvas 尺寸）后仍落格（**本 bug 回归**）。
7. LayoutGroup 子节点文本（Tab label）落格；left vs center 对齐均落格（居中容差含 §3.3 残差说明）。

**工具链**：UnityMCP 跑三套（EditMode / EditorOnly / PlayMode）+ `dotnet format --verify-no-changes --severity warn`。
