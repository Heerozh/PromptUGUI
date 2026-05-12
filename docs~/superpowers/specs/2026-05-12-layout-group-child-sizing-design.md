# Layout Group 子节点固定尺寸设计：落地 spec §6.5

**日期**：2026-05-12
**状态**：设计阶段（待 review，未进入实施）
**作用域**：让 `<VStack>` / `<HStack>` 子节点的 `size` / `width` / `height` 真正生效（写到 `LayoutElement.preferredWidth/Height` + `flexibleWidth/Height=0`），并把这两个容器的 `LayoutGroup` 默认行为锁到"不强制拉伸"。Grid 不在本次范围（cellSize 已经全权决定子节点尺寸，LayoutElement 在 GridLayoutGroup 下无效）。
**依赖**：[`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §6.5；[`2026-05-09-m5-1-default-control-alignment-design.md`](2026-05-09-m5-1-default-control-alignment-design.md)（VStack/HStack 现有默认值的来源）

---

## 1. 背景与目标

spec §6.5 写过：

> 子节点的 `anchor` 与 `margin` **被布局组接管而失效**。仅 `size` / `width` / `height` 生效（**被写入 LayoutElement.preferredWidth/Height**）。

但 `Control.ApplyCommon`（Runtime/Controls/Control.cs:91-133）对所有节点都只写 `RectTransform.sizeDelta`，不分支处理"父级是 LayoutGroup"的情况，全仓库只有 `InputField.cs:55` 一处显式用过 `LayoutElement`。

Unity 6 `AddComponent<VerticalLayoutGroup>()` 给出的默认值是 `childControlWidth/Height=true, childForceExpandWidth/Height=true`：layout pass 阶段 `VerticalLayoutGroup` 会按子节点 ILayoutElement preferred size 重新分配空间，剩余空间被 `forceExpand` 在子节点间均分。后果就是作者写的 `<Btn size="64x64">` 被无视——sizeDelta 在 layout pass 后被覆盖，按钮跟文字均摊 VStack 剩余高度。

### 触发场景示例

```xml
<VStack width="70" height="84" spacing="2">
  <Btn id="btn" size="64x64" color="#1F3A5FCC">
    <Icon name="{{icon}}" anchor="center" size="22x22"/>
  </Btn>
  <Text height="14" fontSize="10">{{label}}</Text>
</VStack>
```

期望：Btn = 64×64、Text = 70×14、间距 2、底部留 4px 余量（84 − 64 − 14 − 2）。
实际：两子节点高度被 force-expand 均分到 ≈ 41，Btn 被撑高，Icon 在按钮中心反而看着偏低（其实是按钮变高了）。

### 目标

1. `<VStack>` / `<HStack>` 默认行为锁到："不强制拉伸，按 LayoutElement preferred 排列"
2. `Control.ApplyCommon` 检测父级是 `LayoutGroup` 时，把 `size` / `width` / `height` 写到 `LayoutElement.preferredWidth/preferredHeight` 而不是 `sizeDelta`，并设 `flexibleWidth/Height=0`（防止 force-expand 残留意图）
3. 同节点既不再写 `anchor`/`margin`（已有 LogWarning 提醒），也跳过 `RectTransform.sizeDelta` 设置（layout group 会接管定位 + 尺寸；自己写一遍是 dead code，还可能跟 layout pass 抢资源）
4. 跨 Variant 切换（`Screen.ReSolve` → `ApplyCommon`）也走同一路径，不需要单独维护 LayoutElement 状态

### 显式不做

- ❌ Grid 子节点的 LayoutElement 处理（GridLayoutGroup 直接用 `cellSize`，LayoutElement 在它下面被忽略；现状是正确的）
- ❌ 引入 `flex` / `expand` 属性让子节点抢剩余空间（v1 用户只要"固定尺寸"，剩余空间留白；下次需要再加）
- ❌ 改 `<VStack>` / `<HStack>` 的 `anchor` / `margin` / `padding` / `spacing` 语义
- ❌ 对子节点没写 `size` / `width` / `height` 的情况做特殊处理（不挂 LayoutElement → 走子节点自身 ILayoutElement / Unity 默认 → 通常等于 0 或 sprite native；要"撑满"自己显式写 `width="N"` 或将来用 `flex`）
- ❌ `Frame` 当作 LayoutGroup 看（它只是 RectTransform，本来就不是）

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| LGC-D1 | VStack / HStack 默认 LayoutGroup flags | `childControlWidth=true, childControlHeight=true, childForceExpandWidth=false, childForceExpandHeight=false` | childControl=true 让 LayoutElement.preferred 生效；forceExpand=false 阻止剩余空间均摊（这是当前 bug 的核心） |
| LGC-D2 | 写入位置 | `LayoutElement.preferredWidth / preferredHeight` | 直接落 spec §6.5 文本，且 LayoutElement 优先级高于 Image/TMP 等组件自带 ILayoutElement，可压住意外的 native preferred |
| LGC-D3 | flexible 同步置 0 | `flexibleWidth = flexibleHeight = 0` | 即便 user 之后手动改 VStack 的 forceExpand=true，flexible=0 也保证本节点不抢剩余空间。flexible=-1（默认）会让 LayoutGroup 把节点当成"无 flex 偏好"用，跟"固定尺寸"语义不符 |
| LGC-D4 | sizeDelta 的处理 | 父级是 LayoutGroup → 不写 sizeDelta（保留默认 0，layout pass 会覆盖）；不是 LayoutGroup → 仍走 MarginResolver + 写 sizeDelta（现状） | LayoutGroup 接管时写 sizeDelta 是 dead code；不接管时仍要算 anchor/margin → size 的几何 |
| LGC-D5 | anchoredPosition / anchorMin / anchorMax / pivot 处理 | 父级是 LayoutGroup → 全部跳过（不写） | LayoutGroup 自己排位置；提前写 anchor 反而引入一帧错位 |
| LGC-D6 | 判定 "父级是 LayoutGroup" 的位置 | `Control.ApplyCommon` 内部 `GameObject.transform.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null` | ScreenInstantiator 已有 `parentIsLayoutGroup` 标志但没透传；改透传要动 ControlAttributeApplier + ApplyCommon 签名 + ReSolve。直接在 ApplyCommon 内查一次 GetComponent，O(1)，零状态，ReSolve 自动正确 |
| LGC-D7 | LayoutElement 组件挂载策略 | `GetComponent<LayoutElement>() ?? AddComponent<LayoutElement>()`，幂等 | ReSolve 会多次进 ApplyCommon；GetComponent-or-Add 模式跟 VStack.OnAttached 一致 |
| LGC-D8 | 子节点没写任何 size 尺寸属性时 | 不挂 LayoutElement | 让 Unity 默认 ILayoutElement / 子节点自身组件（Image/TMP）的 preferred 生效；强行挂 LayoutElement(preferred=-1, flexible=0) 反而会变成"0 尺寸" |
| LGC-D9 | 子节点只写了一个轴（比如只有 width）时 | 已写的轴 set preferredX + flexibleX=0；未写的轴 set preferredY=-1 + flexibleY=-1（即 LayoutElement 不约束该轴） | 让"只固定宽度，高度跟内容走"成立。Unity LayoutElement 用 −1 表示"忽略此项"。 |
| LGC-D10 | Variant 切换里 size.var 改 axis 数量怎么办 | 每次 ApplyCommon 都重置两个轴：先全置 −1，再按当前 sizeSpec 重写 | 不留前一次 variant 的残留约束；幂等 |
| LGC-D11 | parser 是否新增报错 | 不加 | spec §6.5 既有的 "anchor/margin in layout group → warning" 已经覆盖语义提示；新行为是把作者本来就期望的事做对了 |
| LGC-D12 | Grid 子节点 | 不处理（保留现状） | GridLayoutGroup 直接读 `cellSize`，LayoutElement 在它下面被忽略；放着不动避免引入"看似生效实际无效"的属性 |
| LGC-D13 | 测试位置 | EditMode：`Tests/EditMode/Controls/VStackTests.cs`、`HStackTests.cs` 各加 sizing 测；`ControlApplyCommonLayoutGroupTests.cs` 新建（跨 control 的 LayoutElement 行为断言） | 跟已有 VStackTests 形状对齐 |
| LGC-D14 | PlayMode 验证 | 已有 PlayMode `VStackTests` / `HStackTests` 加一条 "real LayoutGroup pass 后子节点 rect.size 等于声明值" | EditMode 测组件存在性 + 数值；PlayMode 跑真 LayoutRebuilder.ForceRebuildLayoutImmediate 验布局结果 |
| LGC-D15 | SKILL.md 更新 | 在 §"In a layout group" 段落加一句："子节点的 `size` / `width` / `height` 会写入 `LayoutElement.preferredX / flexibleX=0`，所以固定尺寸 = 在子节点上直接写 size/width/height 即可" | §6.5 语义没变（spec 早写了）；但当前 SKILL 没解释"为什么固定尺寸有效"，作者会困惑。归到 CLAUDE.md "新 / 改属性语义" 触发条件下 |
| LGC-D16 | 兼容性兜底 | 不留兜底属性 | 用户说"现在没多少 ui，影响不大"。在 v1.x 阶段直接调整默认行为，避免长期带一个 force-expand-by-default 的兼容 flag |

---

## 3. 改动面

### 3.1 VStack.OnAttached / HStack.OnAttached

加 4 行：

```csharp
_layout = GameObject.GetComponent<VerticalLayoutGroup>()
          ?? GameObject.AddComponent<VerticalLayoutGroup>();
_layout.childControlWidth = true;
_layout.childControlHeight = true;
_layout.childForceExpandWidth = false;
_layout.childForceExpandHeight = false;
```

HStack 对应 `HorizontalLayoutGroup` 同四字段。

> 注：Unity 6 `AddComponent` 给出的默认就是这四个全 true。我们在 OnAttached 里**显式**写一遍，确保不被未来 Unity 默认值变更影响，也让"我们的语义"在代码里可读。

### 3.2 Control.ApplyCommon

把当前逻辑拆成两条分支。伪代码（实际写 C#）：

```csharp
public void ApplyCommon(string anchor, string size, string width, string height,
                        string margin, string pivot,
                        bool hidden, bool interactable)
{
    var preset = string.IsNullOrEmpty(anchor)
        ? new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Left)
        : AnchorPreset.Parse(anchor);

    var sizeSpec = SizeSpec.Parse(size, width, height);

    if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)
    {
        var native = GetNativeSize();
        if (native.HasValue)
            sizeSpec = sizeSpec.WithNativeResolved(native.Value);
    }

    sizeSpec.ValidateAgainst(preset);

    var parentLg = RectTransform.parent != null
        ? RectTransform.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
        : null;

    if (parentLg != null)
    {
        ApplyLayoutElement(sizeSpec);  // 见 3.3
        // 不写 anchor / pivot / sizeDelta / anchoredPosition — LayoutGroup 接管
    }
    else
    {
        // 当前实现完整保留：anchor → preset → sizeDelta + anchoredPosition + pivot
        AnchorResolver.Resolve(preset, out var aMin, out var aMax, out var p);
        RectTransform.anchorMin = aMin;
        RectTransform.anchorMax = aMax;
        if (!string.IsNullOrEmpty(pivot)) { /* parse "x,y" */ }
        else RectTransform.pivot = p;
        var lr = MarginResolver.Resolve(preset, sizeSpec, margin);
        RectTransform.anchoredPosition = lr.AnchoredPosition;
        RectTransform.sizeDelta = lr.SizeDelta;
    }

    Hidden = hidden;
    Interactable = interactable;
}
```

### 3.3 ApplyLayoutElement(SizeSpec sizeSpec)

```csharp
private void ApplyLayoutElement(SizeSpec sizeSpec)
{
    var le = GameObject.GetComponent<UnityEngine.UI.LayoutElement>()
             ?? GameObject.AddComponent<UnityEngine.UI.LayoutElement>();
    // 每次都先重置，避免上一次 variant 残留
    le.preferredWidth = -1;
    le.preferredHeight = -1;
    le.flexibleWidth = -1;
    le.flexibleHeight = -1;
    if (sizeSpec.HasWidth)
    {
        le.preferredWidth = sizeSpec.Width;
        le.flexibleWidth = 0;
    }
    if (sizeSpec.HasHeight)
    {
        le.preferredHeight = sizeSpec.Height;
        le.flexibleHeight = 0;
    }
}
```

### 3.4 ScreenInstantiator

不改。`parentIsLayoutGroup` 局部判定既有逻辑保留（用来发 LogWarning），不再透传给 ApplyCommon。

---

## 4. 行为对照表

| 场景 | 旧行为 | 新行为 |
|---|---|---|
| `<VStack height="84"><Btn size="64x64"/><Text height="14"/></VStack>` | Btn & Text 各取 ~41 高，force-expand 均摊 | Btn=64, Text=14（剩余 6 留白） |
| `<VStack><Btn width="100"/><Btn/></VStack>` | 两个 Btn 各被 force-expand 拉到 VStack 全高 / 部分宽 | 第一个 Btn 宽 100；第二个 Btn 无 LayoutElement → Image/TMP preferred（通常 0，看着像不见） |
| `<HStack><Frame width="50"/><Frame/></HStack>` | 两 Frame 均分宽度 | 第一个固定 50；第二个 0 宽（用户得显式给 width / 等未来 `flex`） |
| `<Btn anchor="top-right" size="240x80" margin="16"/>` 直接挂在 Screen 根（非 layout group） | 现状（AnchorResolver+MarginResolver+sizeDelta） | **不变**：parentLg 为 null，走 else 分支 |
| `<Frame><Btn size="64x64"/></Frame>` | Btn 写 sizeDelta=64x64，top-left anchored | **不变**：Frame 不是 LayoutGroup |
| Grid 子节点 | cellSize 决定，子节点 size 属性其实无效（但不报错） | **不变** |
| 跨 Variant `size.var` 切换 | sizeDelta 被 ReSolve 重写 | LayoutElement.preferred 被重写，flexible 同步重置 |

---

## 5. 公开 API 表

| 状态 | 签名 / 行为 | 说明 |
|---|---|---|
| 行为变更 | `<VStack>` / `<HStack>` 创建时设 `childControlWidth/Height=true, childForceExpand*=false` | 默认值锁定，author 仍可通过手动改组件（非 XML）覆盖。XML 层暂不开放 force-expand 开关 |
| 行为变更 | `Control.ApplyCommon` 在父级为 LayoutGroup 时改写 LayoutElement 而非 sizeDelta | spec §6.5 语义首次落地 |
| 新内部 API | `Control.ApplyLayoutElement(SizeSpec)` private helper | 不暴露给作者；测试通过 `LayoutElement` 组件断言 |
| 不变 | 所有 `[UIAttr]` 属性 / R3 事件流 / `Get<T>` 行为 / Variant 协议 | |

---

## 6. 测试矩阵

### EditMode

新建 `Tests/EditMode/Controls/ControlApplyCommonLayoutGroupTests.cs`：

| 用例 | 期望 |
|---|---|
| Btn in VStack with size="64x64" | LayoutElement: preferredWidth=64, preferredHeight=64, flexibleWidth=0, flexibleHeight=0 |
| Btn in VStack with width="100" only | preferredWidth=100, flexibleWidth=0；preferredHeight=-1, flexibleHeight=-1 |
| Btn in VStack with no size attrs | 没有 LayoutElement 组件（D8） |
| Btn in Frame with size="64x64" | 没有 LayoutElement 组件；sizeDelta=(64,64)（现行为保持） |
| Btn in Grid with size="64x64" | 没有 LayoutElement 组件（Grid 用 cellSize；不挂 LayoutElement 防误导）—— **决策点 D12**，跟 D8 同处理路径就够 |
| Variant 切 size.var | 切到新值后 preferred 跟着变；切回原值 flexible 仍是 0（不残留） |
| Variant 切到只写 width 后再切回写 size | preferredHeight 在中间 variant 处被置 -1；切回后回到 preferredHeight=value, flexibleHeight=0 |

`VStackTests` / `HStackTests` 各加：

```csharp
[Test]
public void Default_flags_lock_force_expand_off()
{
    // OnAttached 后断言四个 child* flag
}
```

### PlayMode

`Tests/PlayMode/Controls/VStackTests.cs`、`HStackTests.cs` 各加一条：

```csharp
[UnityTest]
public IEnumerator Child_size_is_respected_after_layout_rebuild()
{
    // 构造 VStack height=84, Btn size=64x64, Text height=14;
    // LayoutRebuilder.ForceRebuildLayoutImmediate(vstack.rect);
    // Assert Btn.rect.height == 64f, Text.rect.height == 14f
    yield return null;
}
```

### 兼容性回归

跑全部 EditMode + PlayMode：现有 `VStackTests` / `HStackTests` 关于 spacing / padding 的断言不受影响（这些断言看 LayoutGroup 字段，不看子节点 rect）。Btn / Image / Text 测试也不受影响（它们在 Frame 而非 VStack 下断言 sizeDelta）。

---

## 7. SKILL.md 同步

在 SKILL.md 现有 "In a layout group" 描述（line ~152-154）后追加：

```markdown
Inside `<VStack>` / `<HStack>`, `size` / `width` / `height` are written to
`LayoutElement.preferredX` (+ `flexibleX=0`) instead of `sizeDelta`. So a child
written as `<Btn size="64x64"/>` is **strictly 64×64**, never stretched by the
layout group. Omit a dimension (`width` only / `height` only) and that axis is
left unconstrained — the child takes its intrinsic preferred size on that axis.
Inside `<Grid>`, child `size` is ignored; the parent's `cellSize` is the source
of truth.
```

---

## 8. Risk / 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 已存在的 demo XML 依赖"VStack 子节点被 force-expand 均摊高度"的视觉效果 | 子节点变成各自 preferred 高度，VStack 底部出现留白 | 用户明确说"现在没多少 ui，影响不大"；PR 内跑一遍 host 项目的所有 Screen demo 目检 |
| 子节点没写 size 时变成 0 尺寸（看不见） | 视觉上"东西不见了" | 在 SKILL.md 显式说明；rely on author 在新行为下显式写 size 或将来 `flex` |
| `RectTransform.parent` 在某些奇怪生命周期下为 null | NullRef | Control.AttachTo 是 SetParent 之后调用的；ApplyCommon 也是 attach 之后调；逻辑上 parent 不可能 null（除非 root Screen GameObject，但 Screen 根不走 ApplyCommon——`InstantiateInto` 是给 children 用的）。加一道 `parent != null` 兜底，不要靠它走主路径 |
| 未来加 `flex` 属性时跟现行为冲突 | API 设计冲突 | flex 默认 0 跟现行 `flexible=0` 默认一致；写 `flex="1"` 改成 `flexible=1`。可平滑扩展 |

---

## 9. 实施顺序（写 plan 时细化）

1. PlayMode + EditMode 各写一条 red 测：VStack 里 size="64x64" 的 Btn 渲染出来不是 64
2. 改 `VStack.OnAttached` / `HStack.OnAttached`：四个 flag
3. 改 `Control.ApplyCommon`：分支 + `ApplyLayoutElement`
4. 跑 EditMode + PlayMode 全套
5. SKILL.md 追加段落
6. host 项目目检：现有 Screen demo 视觉回归
