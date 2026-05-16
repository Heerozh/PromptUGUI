# Toggle 内容自适应 native fallback 设计

**日期**: 2026-05-16
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `Toggle` 覆写 `GetNativeSize()`，从 `_label`（TMP_Text）的 preferred 文本尺寸 + 与现有 layout 一致的常量 padding / tap target 计算返回；无 text 时回退到 `(44, 44)` 方形 checkbox-only 默认。
2. 复用 Btn 那次（`2026-05-15-btn-content-sizing-design.md`）已经接好的 `Control.ApplyLayoutElement` 与 `Control.ApplyCommon` 自由定位 fallback —— 本次不再改 Control / SizeSpec 基础设施。
3. 测试：`<HStack><Toggle>静音</Toggle></HStack>` Toggle LayoutElement.preferredWidth = `label.preferredWidth + 28`；`<Frame><Toggle>静音</Toggle></Frame>` `sizeDelta.x` = 同公式；checkbox-only Toggle 默认 44×44；显式 size 覆盖 native；Variant 切文字长度时 preferred 跟着变。
4. 同步 `authoring-promptugui-xml` SKILL.md 里 `<Toggle>` 段 + free-positioning fallback 行为说明（把 Toggle 加入"走 native fallback 的控件"列表）。

**依赖**: [`2026-05-15-btn-content-sizing-design.md`](2026-05-15-btn-content-sizing-design.md)（BCS-D6 / BCS-D7 引入的"控件 opt-in 通过覆写 GetNativeSize 主导 preferred 报告"机制本次直接复用，无新增基础设施改动）。

---

## 1. 背景

当前 `<Toggle>静音</Toggle>`（不写任何 size）在两种父级下都不可用，跟 Btn 之前的症状同源：

| 父级 | 路径 | 现象 |
|---|---|---|
| `<HStack>` / `<VStack>` | `Control.ApplyLayoutElement` 走 BCS-D6 路径，`GetNativeSize()` 返回 null（基类默认）→ LE 不挂；HorizontalLayoutGroup 没有合理 preferred 源，Toggle root 没 Image，所有子节点（Background/Label）都是相对锚定，layout group 不递归询问 | 按钮宽度坍缩到 0 或被 stretch 撑满，不可控 |
| `<Frame>`（自由定位） | `Control.ApplyCommon` else 分支走 BCS-D7 路径，`GetNativeSize()` 返回 null → 跳过 native fallback → `MarginResolver.Resolve` 拿到 sizeSpec.HasWidth=false → `sizeDelta = (0, 0)` | Toggle 0×0，肉眼不可见 |

跟 Btn 不同的是，Toggle 内部结构是固定的（Background 0-20 + Label 23-到右-5），所以 native 横向公式不是"自由参数"而是被 layout 直接决定的。

`2026-05-15-btn-content-sizing-design.md` 的 "Non-targets" 一节明确把 Toggle / Dropdown / InputField 推到后续 spec —— 本份 spec 接 Toggle 这一项。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| TCS-D1 | 基础设施改动 | 无 —— 直接复用 BCS-D6 / BCS-D7 已铺好的 `ApplyLayoutElement` no-size 分支 + `ApplyCommon` 自由定位 fallback | Toggle 只需要覆写 `GetNativeSize()` 就能接入两条路径；这正是 BCS-D6 当时希望支持的"未来给其它控件加 GetNativeSize 时自动接入"的预期路径 |
| TCS-D2 | preferred 宽度公式 | `preferredWidth = label.preferredWidth + 28` | 28 = 23（Label.offsetMin.x = Background 20px + 3px gap）+ 5（Label.offsetMax.x = -5 → 右 padding）。是 Toggle.OnAttached 里 layout 数值的直接转译，不是新选择的设计参数 —— 改这个常量等于改 layout |
| TCS-D3 | preferred 高度公式 | `preferredHeight = max(44, label.preferredHeight + 12)` | 跟 Btn 完全一致（VerticalPadding=6 双边）。44 是 iOS / Material tap target 下限；用 max 是为了双行长文本时 Toggle 高度自然撑开。Toggle/Btn 同行放置时不会因为高度公式不同而错位 |
| TCS-D4 | 无 Text 时的尺寸 | `(44, 44)` 方形 | checkbox-only 视觉本来就是方的；44 满足 tap target；不沿用 Btn 的 80×44 因为 Toggle 没文字时是单个 checkbox（视觉不平衡的 80 宽空白）。这是与 Btn 唯一的语义差异点 |
| TCS-D5 | Text 测量是否 `ForceMeshUpdate` | 是，跟 Btn 同处理 | TMP_Text.preferredWidth 在 SetText 当帧不稳；Toggle 用 fontSize 14（比 Btn 的 24 小），preferredHeight 通常约 16-18，所以 max(44, ...) 几乎总走 44，但 force update 仍是 width 准确性的必要条件 |
| TCS-D6 | 显式 size 与 native 的优先级 | 显式 size 覆盖（已有逻辑无需改） | `ApplyLayoutElement` 在 `sizeSpec.HasWidth=true` 时直接走 numeric/stretch 分支，不进 native 路径；自由定位 fallback 同样只在两轴都没写时生效 |
| TCS-D7 | Variant 切换下的行为 | `ApplyCommon` 每次 ReSolve 时重跑 → 重新调 `GetNativeSize` → preferred / sizeDelta 自然跟 Variant 走 | 跟 Btn 同机制，无需额外 hook |
| TCS-D8 | 报错路径 | 不新增报错 | 默认值优化，没有新增非法状态 |
| TCS-D9 | 测试用 Variant 验证文字长度变化 | 用 `text='短' text.long='长长长'` 验证 LE.preferredWidth 跟 Variant 切换 | 跟 `BtnContentSizingTests.Btn_in_HStack_variant_text_change_updates_preferred` 镜像 |
| TCS-D10 | SKILL.md 更新点 | `authoring-promptugui-xml/SKILL.md` 的 `<Toggle>` 段加默认尺寸说明；free-positioning 段把 Toggle 加进"提供 native 的控件"列表 | CLAUDE.md 触发条件：新增可见行为（默认值变化） |

---

## 3. 实现要点

### 3.1 `Toggle.cs` 改动

新加私有常量（紧贴 `OnAttached` 里的 layout 数值，注释指出对应关系）:
```csharp
// 跟 OnAttached 里 Background 和 Label 的 offset 数值绑定 —— 改这些等于改 layout
private const float CheckmarkZoneWidth = 23f;   // Background (0-20) + 3px gap = Label.offsetMin.x
private const float RightPadding = 5f;          // -Label.offsetMax.x
private const float VerticalPadding = 6f;       // 跟 Btn 一致
private const float MinTapHeight = 44f;         // 跟 Btn 一致
private const float DefaultIconOnlySize = 44f;  // 无 text 时方形 checkbox tap area
```

新增覆写（紧跟 `OnAttached` 之后或类底部）:
```csharp
public override Vector2? GetNativeSize()
{
    if (_label != null && !string.IsNullOrEmpty(_label.text))
    {
        _label.ForceMeshUpdate();  // TCS-D5
        var w = _label.preferredWidth + CheckmarkZoneWidth + RightPadding;
        var h = Mathf.Max(MinTapHeight, _label.preferredHeight + VerticalPadding * 2f);
        return new Vector2(w, h);
    }
    return new Vector2(DefaultIconOnlySize, DefaultIconOnlySize);
}
```

不改 `OnAttached` / `Text` setter / `ApplyFont` —— LayoutElement 由 `ApplyLayoutElement` 统一挂管，每次 `ApplyCommon` 重跑都会重新调 `GetNativeSize`，TMP 文字 / 字体变化自然被捕获。

### 3.2 `Control.cs` / `SizeSpec.cs` 不改

BCS-D6 已经在 `ApplyLayoutElement` no-size 分支接入了 `GetNativeSize()`；BCS-D7 已经在 `ApplyCommon` 自由定位 else 分支接入了 native fallback。两条都是 "若 `GetNativeSize()` 非 null 则用 native，否则维持原行为"，Toggle 覆写后自动接入。

### 3.3 已有测试不需要调整

不像 Btn 那次需要改 `ControlApplyCommonLayoutGroupTests` 里的"no size → no LE"测试 —— 那次的契约已经从"no size → no LE"变成"no size + native=null → no LE; no size + native!=null → LE with native"。本次 Toggle 加入"native!=null"阵营，不改契约。

但 `ToggleTests` 里现有的 Geometry 测试（`Geometry_BackgroundIsTwentyByTwentyLeftMiddle` 等）需要确认仍然过 —— 这些测的是 OnAttached 里写的固定数值，不依赖 native fallback，应该不受影响。需在实施时跑一遍确认。

### 3.4 测试覆盖（新增）

`Tests/EditMode/Controls/ToggleContentSizingTests.cs`（新建，平行于 `BtnContentSizingTests`）:

- `Toggle_without_text_GetNativeSize_returns_icon_only_defaults`：`<Toggle id='t'/>`，断言 `GetNativeSize() == (44, 44)`。
- `Toggle_with_text_GetNativeSize_reports_label_preferred_plus_padding`：`<Toggle id='t'>静音</Toggle>`，断言 `native.x == label.preferredWidth + 28`、`native.y == 44`（fontSize 14 下文字高约 16+12=28 < 44 → max 走 44）。
- `Toggle_in_Frame_no_size_sizeDelta_matches_native`：`<Frame size='400x200'><Toggle id='t'>静音</Toggle></Frame>`，断言 `sizeDelta == native`。
- `Toggle_in_Frame_anchor_stretch_skips_native_fallback`：`<Frame size='400x200'><Toggle id='t' anchor='stretch' margin='8'>静音</Toggle></Frame>`，断言 `sizeDelta = (-16, -16)`。
- `Toggle_in_VStack_no_size_gets_LayoutElement_with_native_preferred`：`<VStack width='400' height='200'><Toggle id='t'>静音</Toggle></VStack>`，断言 LE 存在 + preferred = native。
- `Toggle_in_Frame_explicit_size_overrides_native`：`<Frame size='400x200'><Toggle id='t' size='200x60'>静音</Toggle></Frame>`，断言 `sizeDelta == (200, 60)`。
- `Toggle_in_VStack_variant_text_change_updates_preferred`：`text='短' text.long='长长长长长长'`，Variant `long` 激活后 LE.preferredWidth 变大。

### 3.5 SKILL.md 更新

`authoring-promptugui-xml/SKILL.md` 的 `<Toggle>` 段新增一条:
> **Toggle 默认按文字自适应**: 不写 `size`/`width`/`height` 时，Toggle 的 preferred 宽度 = `label.preferredWidth + 28`（左侧 23px checkmark 区 + 5px 右 padding），高度 = `max(44, 文字高 + 12)`。无文字（仅 checkbox）默认 44×44。在 layout group 里通过 LayoutElement 主导；在自由定位下作为 native fallback。

free-positioning 段把现有"当前 `<Btn>` 和 `<Icon>` 走这条路"改成"当前 `<Btn>`、`<Toggle>` 和 `<Icon>` 走这条路"。

---

## 4. 非目标 / Out of Scope

- 不改 `<Dropdown>` / `<InputField>` 的默认尺寸 —— 它们的 native 形状（Dropdown 有 arrow + label + 弹出，InputField 有 placeholder + caret）需要单独设计；本次只补 Toggle。
- 不引入 `PromptUGUISettings` 级的 padding / tap-target 主题配置 —— 跟 Btn 那次保持一致，常量先写死，后续如果不同游戏有不同手感需求再单独开 spec。
- 不抽 Btn / Toggle 共享的 `MinTapHeight=44` / `VerticalPadding=6` 到基类常量 —— 只有 2 个控件用，抽象会比重复更扭曲；等第 3 个加入再考虑。
- 不改 Toggle 内部 Background/Checkmark/Label 的几何 —— 那些数值是公式的输入而不是输出，本次只读不写。

---

## 5. 风险与回滚

| 风险 | 缓解 |
|---|---|
| TMP_Text.ForceMeshUpdate 在 EditMode 下因为字体未加载报错 | Btn 那次的测试已经在跑 EditMode 下的 ForceMeshUpdate（`BtnContentSizingTests.Btn_with_text_GetNativeSize_reports_label_preferred_plus_padding` 等），证明 PromptUGUISettings.Instance 的 default font 在 EditMode 走得通；Toggle 沿用同 font 解析路径，风险面相同 |
| 已有 `<Frame><Toggle/>` 不写 size 现在拿 0×0、靠 anchor=stretch+margin 工作的写法本次会变成 44×44 | 检查：anchor=stretch 任一轴时 `preset.StretchX/Y` 为 true → fallback 跳过；只有"完全居中/单角定位 + 零 size"才受影响，那个写法本来就不可见、向后改善 |
| Toggle layout 数值（23 / -5）将来如果改 OnAttached，常量会和 layout 失配 | 常量上方注释直接绑定到 `OnAttached` 里的字段名（Background.sizeDelta.x、Label.offsetMin.x、Label.offsetMax.x）；改 layout 时同时改常量；这一对约束由 `Toggle_with_text_GetNativeSize_reports_label_preferred_plus_padding` 测试持续守护（如果失配，断言 `native.x == label.preferredWidth + 28` 会挂） |
