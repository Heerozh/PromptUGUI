# Btn/Tab/Toggle 实体按钮按压位移（state-offset）设计

**日期**: 2026-06-24
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `Runtime/Controls/Internal/StateOffsetSet.cs` —— 新增。纯 POCO，per-state `Vector2?`（pressed / selected），`For(InteractState)` 折算成 `Vector2`（Normal/Hover/Disabled → `zero`）。类比 `StateColorSet`。
2. `Runtime/Controls/Internal/PressOffsetController.cs` —— 新增。`MonoBehaviour`，挂在 content-holder 上，订阅最近祖先 `IStateSource.OnState`，`OnState(state) → holder.anchoredPosition = set.For(state)`（瞬移）。
3. `Runtime/Controls/Internal/StateOffsetInstaller.cs` —— 新增。静态。懒建 content-holder + 把现有可视子节点 reparent 进去 + 挂/Configure controller。幂等。
4. `Runtime/Controls/Btn.cs` —— 加 `pressedOffset` 属性、`_offsetHolder` 字段、`ChildHostTransform` override、`OnAfterApply` 里接一行 `Install`。
5. `Runtime/Controls/Tab.cs` —— 同 Btn，外加 `selectedOffset` 属性。
6. `Runtime/Controls/Toggle.cs` —— 同 Tab。
7. `Runtime/Core/Parser/` 或现有 vec2 解析工具 —— 复用解析 `"x,y"`（与 `translate` 同一条路）。
8. `.claude/skills/authoring-promptugui-xml/reference/states.md` —— 新增「Press offset」一节（英文）。
9. `.claude/skills/authoring-promptugui-xml/SKILL.md` —— Btn/Tab/Toggle 属性目录行 + stub 指针补新属性（英文）。
10.（条件）XSD 生成器 —— 若枚举 per-control 属性，补 `pressedOffset` / `selectedOffset`。

**依赖**: 无新增包。复用：`IStateSource` / `StateBroadcaster` 的 `OnState` 广播、`StateTintInstaller` / `StateTintReactor` 的 installer+reactor 模式、`StateSubtree` 思路、`<Animation>` 的 `_offsetProxy`（content-holder 同款全拉伸 RectTransform）、现有 `ChildHostTransform` 重定向机制、现有 `translate` 的 vec2 解析。

---

## 1. 背景与动机

「实体按钮 / 果汁感按钮」是常见的游戏 UI 反馈：按下时按钮的可视内容下沉几像素，给出"被按进去"的触觉错觉。Unity 社区主流做法有四类：

1. **Animator 动画过渡**（官方）：Button transition 设成 Animation，Pressed clip 里挪一个子 "Content" RectTransform。无代码但每按钮要 Animator + Controller 资产，重。
2. **`IPointerDown`/`IPointerUp` 脚本挪子物体**（最常见轻量 DIY），常配 DOTween/LeanTween 快补间。
3. **双层「阴影底 + 面」立体按钮**：底垫暗色阴影图，面整体下移贴住。
4. **现成 juice 资产**（MoreMountains Feel、DOTween Pro 等）。

核心原则：**hover 浮起、press 下沉**（emboss up/down）。

本库现状：`Btn`/`Tab`/`Toggle` 已统一通过 `IStateSource`（`StateBroadcaster`）广播 `OnState`（Normal/Hover/Pressed/Selected/Disabled），并已有四套 per-state 视觉家族（`*Color` / `*Modulate` / `*Sprite` / 禁用去色），全部走「`OnAfterApply` 里装 installer，installer 给子树挂 reactor 订阅 `OnState`」的同一模式。

该效果**今天已能用现有动画系统啰嗦地实现**：

```xml
<Btn>
  <Animation translate="0,0:0,-4" duration="0.06s" on="state-pressed"><Frame anchor="stretch">…</Frame></Animation>
  <Animation translate="0,-4:0,0" duration="0.06s" on="state-normal"><Frame anchor="stretch">…</Frame></Animation>
</Btn>
```

缺点：要包两层 `<Animation>`、手写复位、每按钮重复、`Toggle`/`Tab` 不复用。故本设计加一个**一等公民 state-offset 家族**，与现有家族同构，三控件共享。

## 2. 目标 / 非目标

**目标**
- `Btn` 支持 `pressedOffset="x,y"`：按下时子内容整体位移、背景框不动、瞬移到位、松开复位。
- `Tab` / `Toggle` 额外支持 `selectedOffset="x,y"`：选中（`isOn`）静止态保持位移。
- 三控件经共享 installer 复用同一机制。
- 与 `<Animation>` / `*Color` / `*Sprite` 等正交可叠加。

**非目标（留口不做）**
- `hoverOffset`（PC 悬停浮起）。
- 补间动画（`offsetDuration` / easing）—— MVP 一律瞬移。
- 双层阴影底「3D 键」模式（需额外 shadow Graphic + 约定来源）。
- pressed→selected 兜底（见 §5 已知边界）。

## 3. XML 表面

| 属性 | 控件 | 含义 |
|---|---|---|
| `pressedOffset="x,y"` | Btn / Tab / Toggle | 按下时子内容位移（像素，**Unity 符号：负 y = 下**） |
| `selectedOffset="x,y"` | Tab / Toggle | 选中（`isOn`）静止态保持的位移；`Btn` **不声明**此属性 |

- 值格式 `"x,y"`（两个数，可含负号/小数）。
- `""` / `"none"` = 不设此态位移（沿用 `pressedSprite` 的 opt-out 约定）。
- 非法格式 → parse error（复用现有 vec2 解析路径）。
- 可被 Variant 覆盖（任意 `[UIAttr]` 通用：ReSolve 重解析、重 `Configure`）。
- `selectedOffset` 只声明在 `Tab`/`Toggle` 上 —— 与现有 `selectedColor`/`selectedModulate` 只在 Tab/Toggle 的惯例一致（`Btn` 永不进 Selected 态）。

示例：

```xml
<!-- 按下子内容下移 4px -->
<Btn pressedOffset="0,-4">Buy</Btn>

<!-- 按下沉 2px；选中后保持沉 3px -->
<Tab pressedOffset="0,-2" selectedOffset="0,-3"/>

<!-- 水平也能动（少见，但 x/y 都支持） -->
<Toggle pressedOffset="1,-2"/>
```

## 4. 运行时架构

### 4.1 新增组件（均在 `Runtime/Controls/Internal/`）

**`StateOffsetSet`（POCO）**
```
struct/class StateOffsetSet {
    Vector2? Pressed;
    Vector2? Selected;
    bool HasAny => Pressed.HasValue || Selected.HasValue;
    Vector2 For(InteractState s) => s switch {
        Pressed  => Pressed  ?? Vector2.zero,
        Selected => Selected ?? Vector2.zero,
        _        => Vector2.zero,   // Normal / Hover / Disabled
    };
}
```
类比 `StateColorSet`：纯数据 + `For` 查表，无 Unity 依赖（可纯 C# 单测）。

**`PressOffsetController : MonoBehaviour`**（挂在 content-holder 上）
- `Configure(StateOffsetSet set)`：存 set；首次 `EnsureInit` 时 `GetComponentInParent<IStateSource>(true)` 拿到拥有它的 Btn/Tab/Toggle，`OnState.Subscribe(OnState)`（`includeInactive` 同 `StateTintReactor`，应对开屏隐藏页）。
- `OnState(InteractState s) { _holder.anchoredPosition = _set.For(s); }`（_holder = 自己的 RectTransform）。**瞬移，无 LitMotion**。
- 重 `Configure`（Variant ReSolve）：更新 set 后，若已订阅则 `OnState(_source.Current)` 重绘一次当前态（对齐 `StateTintReactor` 的 re-Configure 重绘逻辑）。
- `OnDestroy`：退订。

**`StateOffsetInstaller`（静态）**
```
// 返回 holder（可能为 null）。go = 控件 GO；existing = 控件已缓存的 holder（首次为 null）。
static RectTransform Install(GameObject go, RectTransform existing, StateOffsetSet offsets) {
    if (!offsets.HasAny && existing == null) return null;        // 从未设 → 不建
    var holder = existing ?? CreateHolder(go);                   // 建空 holder（全拉伸，仿 _offsetProxy）
    SweepDirectChildrenInto(go, holder);                        // 把 go 的直接子节点（除 holder）搬进 holder
    var ctrl = holder.GetComponent<PressOffsetController>() ?? holder.gameObject.AddComponent<PressOffsetController>();
    ctrl.Configure(offsets);                                    // offsets 全空也 Configure → For 全 zero → 归位
    return holder;
}
```
- `CreateHolder`：建全拉伸 RectTransform（anchorMin=0 / anchorMax=1 / offset=0 / pivot=0.5，仿 `_offsetProxy`，命名 `"_offsetHolder"`），作为 go 的子节点。
- `SweepDirectChildrenInto`：快照 `go.transform` 当前直接子节点 → **跳过 holder 自身** → 按序 `SetParent(holder,false)`（label / icon + 显式子节点；背景 Image / PuiButton / CanvasGroup 是组件不是子节点，不动）。**每次 Install 都跑**：首次把内容搬入；ReSolve 时内容已在 holder 内、且新子节点经 `ChildHostTransform` 直接落 holder，故通常 no-op；唯一非平凡场景是"内容在晚于 holder 创建的某个 Variant ReSolve 才首次出现为直挂子节点"——此时被扫入，集中覆盖该边界（无需改各控件的 `EnsureLabel`）。

### 4.2 控件侧改动（Btn / Tab / Toggle 各自，极小）

每个控件：
1. 加字段 `private RectTransform _offsetHolder;`
2. `override Transform ChildHostTransform => _offsetHolder != null ? _offsetHolder : RectTransform;`（让后续 Add 块也落进 holder）
3. 加属性 setter（存进 `_pressedOffset` / `_selectedOffset` 字段，解析 `"x,y"`）。
4. 在**已有的** `OnAfterApply` 里、`StateTintInstaller.Install` **之前**接一行：
   `_offsetHolder = StateOffsetInstaller.Install(GameObject, _offsetHolder, new StateOffsetSet{...});`

`Btn` 只有 `pressedOffset`；`Tab`/`Toggle` 有 `pressedOffset` + `selectedOffset`。

控件的 `EnsureLabel` / icon 创建**无需改动**：它们仍挂在 `RectTransform`，由 installer 的 `SweepDirectChildrenInto` 在 `OnAfterApply` 统一扫进 holder（label 在 `OnAfterApply` 前已建好；晚于 holder 出现的也被后续 ReSolve 的 sweep 接住）。`stateReact="false"` **不**豁免位移（它只管 `*Modulate` 扇出；holder 是刚体平移，整块内容一起动）。

## 5. 数据流 / 生命周期

- **懒建**（贴合库里「不挂空转 MonoBehaviour/GO」取舍）：`ChildHostTransform` 在实例化期被读取（属性应用之前），故初始子节点先挂到控件自身 RectTransform；到 `OnAfterApply`（`pressedOffset`/`selectedOffset` 已知、label/子节点已建好）时，若有位移则建 holder 并把直接子节点扫入。
- **ReSolve 幂等**：holder 已存在 → `existing` 非空 → 不重建（`CreateHolder` 跳过），`SweepDirectChildrenInto` 通常 no-op（内容已在 holder 内），只 `Configure` 新 offsets。
- **顺序**：holder 建/搬在 `StateTintInstaller.Install` 之前。后者 `GetComponentsInChildren<Graphic>` 递归遍历不受中间层影响，逻辑 `Children` 列表也不变 → 颜色 / 去色 / id-path / 布局全不受影响（holder 全拉伸 = 与控件同矩形，子节点锚点解析结果一致）。
- **raycast 不变**：holder 无 Graphic → 不拦截 raycast；背景 Image（在父 GO）照常被 PuiButton 接收点击。
- **状态映射**：`For`：Pressed→`pressedOffset??zero`；Selected→`selectedOffset??zero`；Normal/Hover/Disabled→`zero`。
- **首帧确立**：瞬移天然满足——开屏即 `isOn` 的 Tab/Toggle 第 1 帧就停在 `selectedOffset`（`OnState` 订阅重放当前态 → 直接定位），无需 BornFrame 特判。
- **选中态被按下**：`StateBroadcaster` 把「选中+按下」折叠成 `Pressed`，故 → `pressedOffset`，松开回 `Selected` → `selectedOffset`。
- **与 `<Animation>` 正交**：Animation 动它自己的 `_offsetProxy`（嵌在 holder 内），与 holder 位移叠加，互不干扰。

**已知小边界**：若只设 `selectedOffset`、没设 `pressedOffset`，按一个已选中的 Tab → 进 Pressed → `For(Pressed)=zero` → 子内容瞬回 0、松开再弹回 `selectedOffset`（一次"弹"）。MVP 不做 pressed→selected 兜底（兜底需把 `isOn` 像 `StateTintReactor.SetSelected` 那样单独 push 给 controller，超出 MVP）。文档建议：设 `selectedOffset` 时一并设 `pressedOffset`（常取相同或更深值）。

## 6. 边界与错误处理

- 非法 `"x,y"` → parse error（与 `translate` 同一解析失败路径）。
- `""` / `"none"` → 该态不设位移。
- Disabled → `zero`（按住时被禁用会归位）。
- `Btn` 不声明 `selectedOffset` → 作者误写会触发现有的未知属性报错，无需额外 lint。
- 无新增 lint 规则。

## 7. 测试计划（TDD，Red 先行）

**纯 C# 单测**（`StateOffsetSet`）：
- `For` 查表：Pressed/Selected 命中、Normal/Hover/Disabled→zero、未设态→zero。

**EditMode**（经内部 seam 驱动 `StateBroadcaster.SetTransient` / `SetOn`，InternalsVisibleTo 已开）：
- `Pressed_offset_shifts_holder`：Btn `pressedOffset="0,-4"`，驱动 Pressed → `holder.anchoredPosition == (0,-4)`；回 Normal → `(0,0)`。
- `Selected_offset_on_toggle`：Toggle `isOn` → `selectedOffset`；切走 → 0。
- `Btn_never_selected`：Btn 驱动 isOn 路径不存在 → 永不取 selected 值。
- `No_offset_no_holder`：无位移属性的 Btn → `ChildHostTransform == RectTransform`，无额外 GO（性能断言）。
- `Reparent_into_holder`：label + 显式子节点 transform.parent == holder。
- `Resolve_idempotent`：两次 ReSolve 后仍只有一个 holder、子节点不被搬两次。
- `Sign_convention`：`"0,-4"` → y == -4（下）。
- `Instant_no_frame_wait`：位移同步生效（瞬移天然满足，无需 frame loop）。
- （可选）`Compose_with_animation`：holder 位移与内层 `<Animation translate>` 叠加。

> 注：按 [[sdd-unity-mcp-controller-runs-tests]]，测试由 controller 经 Unity MCP 跑（`refresh_unity` → `run_tests` EditMode → `get_test_job`），子 agent 只写代码。

## 8. 文档（同 PR 必改，英文）

- `reference/states.md`：新增「Press offset — `pressedOffset` / `selectedOffset`」一节：用途、`"x,y"` 格式、Unity 负=下符号（强调坑）、瞬移（与 `*Color` 0.1s 淡入的差异）、selected 只在 Tab/Toggle、与 `<Animation>` 叠加、§5 的"只设 selectedOffset 会弹"边界。
- 主 `SKILL.md`：Btn/Tab/Toggle 属性目录行 + stub 指针补 `pressedOffset` / `selectedOffset`。
- 若 XSD 生成器枚举 per-control 属性 → 同步补（plan 阶段确认实际生成器是否需要）。

## 9. 实现顺序（给 plan 的提示）

1. `StateOffsetSet` + 纯 C# 单测（Red→Green）。
2. `PressOffsetController` + `StateOffsetInstaller`。
3. `Btn.pressedOffset` 接线 + EditMode 测试。
4. `Tab` / `Toggle` 的 `pressedOffset` + `selectedOffset` 接线 + 测试。
5. 文档（states.md / SKILL.md / 可能的 XSD）。
6. lint（`dotnet format --verify-no-changes`）+ 全量 EditMode 回归。
