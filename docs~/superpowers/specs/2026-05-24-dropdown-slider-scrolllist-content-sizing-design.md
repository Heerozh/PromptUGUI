# Dropdown / Slider / ScrollList 内容自适应 native fallback 设计

**日期**: 2026-05-24
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `Dropdown` / `Slider` / `ScrollList` 三个控件都覆写 `GetNativeSize()`，让它们在 `<VStack>` / `<HStack>` 不写 size 时不再坍缩到接近 0、在 `<Frame>` 不写 size 时不再 0×0 不可见。
2. 复用 Btn 那次（`2026-05-15-btn-content-sizing-design.md`）已经接好的 `Control.ApplyLayoutElement` (BCS-D6) 与 `Control.ApplyCommon` 自由定位 fallback (BCS-D7) —— 本次不再改 Control / SizeSpec 基础设施。
3. 测试：每个控件一份独立 `XxxContentSizingTests.cs`（平行于 `BtnContentSizingTests` / `ToggleContentSizingTests`），覆盖 LayoutGroup auto-LE、自由定位 sizeDelta fallback、anchor=stretch skip、显式 size override 四条主路径。
4. 同步 `authoring-promptugui-xml/SKILL.md` 里 `<Dropdown>` / `<Slider>` / `<ScrollList>` 三段的默认尺寸说明 + LayoutGroup / free-positioning 段的"提供 native 的控件"列表。
5. **附加 (DSS-D13)**: `<Frame>` 默认 anchor 从"统一 top-left"改成"按轴看 size 是否存在"——没写 size 的轴 stretch，写了 size 的轴 top-left。这跟前面四点的 `GetNativeSize` 路径不同（Frame 没有"自然尺寸"概念），通过给 `Control` 加 protected virtual `GetDefaultAnchor(SizeSpec)` 实现；只有 `Frame` 覆写，其他控件维持 `(Top, Left)`。镜像 CSS `<div>` 块流默认：未约束的轴 = `auto`/stretch；显式 `anchor=` 仍按原规则严格校验。

**依赖**: [`2026-05-15-btn-content-sizing-design.md`](2026-05-15-btn-content-sizing-design.md)（BCS-D6 / BCS-D7 引入的"控件 opt-in 通过覆写 GetNativeSize 主导 preferred 报告"机制本次直接复用，无新增基础设施改动）。

---

## 1. 背景

`2026-05-15-btn-content-sizing-design.md` 的 "Non-targets" 一节明确把 Dropdown / Slider / ScrollList / InputField 推到后续 spec。Toggle 已在 `2026-05-16-toggle-content-sizing-design.md` 接入。本份 spec 一次性把剩下的三个 LayoutGroup-不友好的控件（InputField 暂缓，跟 Dropdown 类似但内部更复杂）接入同一机制。

三个控件症状不完全相同，但根因都是"作者没写 size 时，LayoutGroup 拿不到合理的 preferred 信号"：

| 控件 | 根节点上的 ILayoutElement 源 | 当前坍缩到 |
|---|---|---|
| `<Dropdown>` | `UnityImage`（sliced 9-slice 背景） | sliced 模式下 `preferredWidth = DataUtility.GetMinSize(sprite).x / pixelsPerUnit`，即 9-slice border 不重叠的 min size（十几像素）。整个 Dropdown 缩到 ~16×16，连 caption 都看不见。 |
| `<Slider>` | 无 —— 根上只有 `UnityEngine.UI.Slider`，`Selectable` 不实现 `ILayoutElement` | 字面 0×0 |
| `<ScrollList>` | `UnityImage`（sliced 9-slice 背景）+ `ScrollRect` | 同 Dropdown 的 sliced 陷阱 |

`Slider` / `ScrollList` 跟 Btn / Toggle 还有概念上的差异：它们没有真正的"内容驱动自然尺寸"。Slider 的高度只是 tap target，长度是设计选择；ScrollList 是个看一部分内容的窗口，窗口大小独立于内容多少。所以这两个控件的 fallback 是**视口默认值**，不是"测量内容拼出来"的公式。这是与 Btn / Toggle 路径的语义分歧点。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| DSS-D1 | 基础设施改动 | 无 —— 直接复用 BCS-D6 / BCS-D7 已铺好的 `ApplyLayoutElement` no-size 分支 + `ApplyCommon` 自由定位 fallback | 三个控件都只需要覆写 `GetNativeSize()` 就能接入两条路径；这正是 BCS-D6 当时希望支持的"未来给其它控件加 GetNativeSize 时自动接入"的预期路径 |
| DSS-D2 | Dropdown 是否读 caption 文字宽来算 native | **不读** —— 返回固定 `(DefaultDropdownWidth=160, MinTapHeight=44)` | 与 Btn/Toggle 不同，Dropdown 的可见文字（caption）会随用户选项变化。如果 native 跟着 caption 走，每选一个选项 Dropdown 宽度都跳一下，UX 反直觉。Dropdown 在实际项目里几乎都是被作者显式指定宽度的；native fallback 的目的只是"作者忘写时不可见"，固定 160 是更可预测的默认。caption 长到溢出由 Dropdown 自身 ellipsis / clipping 处理，不影响 fallback |
| DSS-D3 | Slider native 是否分方向 | 是 —— 横向 `(160, 44)`、纵向 `(44, 160)` | Slider 的视觉本来就是单轴的，"长边 160 + 短边等于 tap target" 是唯一不别扭的默认。判定走 `_slider.direction`（已经在 `Direction` setter 同步过），覆盖 LeftToRight/RightToLeft → 横向，BottomToTop/TopToBottom → 纵向 |
| DSS-D4 | ScrollList native 是否分方向 | 是 —— 纵向滚动 `(160, 200)`、横向滚动 `(200, 160)` | 与 Slider 同思路：滚动轴方向给长边，跨滚动轴给短边。`200 / 160` 是肉眼"看起来像视口"的合理量级；实际项目几乎都会显式写 size，这里只防"完全不可见"的 0×0 |
| DSS-D5 | 三个控件的常量放哪儿 | 各自类的 `private const` —— **不**抽到 `Control` 基类 | 跟 Btn / Toggle 那两次保持一致："抽象比重复更扭曲"原则；常量含义跟各自控件的内部 layout / 用途强绑定，公共化只会增加误用面 |
| DSS-D6 | Slider / ScrollList 的 native 是否随 Variant 变化 | 是 —— 但只因 Direction 属性切换 | Slider 的 `Direction` setter / ScrollList 的 `Direction` setter 都已经触发 `ApplyDirection()`；每次 `ApplyCommon` 重跑时调 `GetNativeSize()` 读当前方向，方向变化自然反映到 LE.preferred / sizeDelta。无需额外 hook |
| DSS-D7 | Dropdown 的 native 是否随选项变化 | 否 —— 固定默认（见 DSS-D2） | 同 DSS-D2 |
| DSS-D8 | 显式 size 与 native 的优先级 | 显式 size 覆盖（已有逻辑无需改） | `ApplyLayoutElement` 在 `sizeSpec.HasWidth=true` 时直接走 numeric/stretch 分支，不进 native 路径；自由定位 fallback 同样只在两轴都没写时生效 |
| DSS-D9 | 报错路径 | 不新增报错 | 默认值优化，没有新增非法状态 |
| DSS-D10 | 测试拆分 | 三个独立测试文件：`DropdownContentSizingTests.cs` / `SliderContentSizingTests.cs` / `ScrollListContentSizingTests.cs` | 每个控件公式 / 默认值 / 方向分支不同，独立文件更易在 Unity Test Runner 里单独跑 + filter；跟 `BtnContentSizingTests` / `ToggleContentSizingTests` 风格一致 |
| DSS-D11 | 是否覆盖 InputField | 否 —— 留下一个 spec 单独做 | InputField 内部还有 placeholder / caret / multi-line 等参数，公式比这三个复杂；与其塞进本 spec，不如单独做一份 |
| DSS-D12 | SKILL.md 更新点 | `authoring-promptugui-xml/SKILL.md` 的三个控件表格行各加一句默认尺寸说明；LayoutGroup 段 + free-positioning 段把"提供 native 的控件"列表从 `<Btn>`、`<Toggle>`、`<Icon>` 扩展到 + `<Dropdown>`、`<Slider>`、`<ScrollList>` | CLAUDE.md 触发条件：新增可见行为（默认值变化） |
| DSS-D13 | Frame 默认 anchor 改成"按轴 fill-or-fit" | 没写 anchor 时：作者写过 size 的轴默认 top/left；没写过的轴默认 stretch | 镜像 CSS 块流：`<div>` 没写 size 的轴 auto，写了的轴用值。Frame 没有"自然尺寸"，GetNativeSize 路径不适用；这条修法是 anchor 层的，是 free-positioning 容器问题的正解 |
| DSS-D14 | 实现 API | 给 `Control` 加 `protected virtual AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec) => new(Top, Left);`；`Frame` 覆写按 sizeSpec.HasWidth/HasHeight 决定 H/V；`ApplyCommon` 先 parse sizeSpec 再算 default preset（顺序调整） | 用 virtual 而不是 hardcode "is Frame" 的 type check，让未来其他容器类（如自定义 PanelControl）可以选择性 opt-in；保持 Frame 之外的所有控件行为不变 |
| DSS-D15 | 显式 anchor=stretch 与 size 的现有 parse error 保留 | 不变 —— "anchor=stretch 与 size 同时写"仍然报错；只有"省略 anchor"时才走"按轴 fill-or-fit" | 显式写 anchor=stretch 是作者明确表达"两轴都拉伸"，再写 size 是逻辑矛盾；省略 anchor 是作者表达"不在意定位细节"，这时按 web 直觉给最不容易出错的默认 |

---

## 3. 实现要点

### 3.1 `Dropdown.cs` 改动

新加私有常量（紧贴 `OnAttached` 里 caption label 的 offset 数值）:
```csharp
// Bound to OnAttached caption Label layout — change OnAttached + these in lockstep.
// 不读 caption 文字宽（DSS-D2）；这些常量只为将来若改成 content-aware 时易于切换。
private const float MinTapHeight = 44f;
private const float DefaultDropdownWidth = 160f;
```

新增覆写（紧跟 `OnAttached` 之后或类底部，`Dispose` 之前）:
```csharp
public override Vector2? GetNativeSize()
    => new Vector2(DefaultDropdownWidth, MinTapHeight);
```

不读 caption，所以也无需 `ForceMeshUpdate`。

### 3.2 `Slider.cs` 改动

新加私有常量:
```csharp
private const float MinTapHeight = 44f;
private const float DefaultSliderLength = 160f;
```

新增覆写:
```csharp
public override Vector2? GetNativeSize()
{
    var horizontal = _slider == null
                  || _slider.direction == UnitySlider.Direction.LeftToRight
                  || _slider.direction == UnitySlider.Direction.RightToLeft;
    return horizontal
        ? new Vector2(DefaultSliderLength, MinTapHeight)
        : new Vector2(MinTapHeight, DefaultSliderLength);
}
```

`_slider == null` 的 short-circuit 是为防御性：`ApplyLayoutElement` / `ApplyCommon` 在某些边界条件下（attribute applier 在 OnAttached 之后立即触发）可能在 `_slider` 尚未就绪时被调用。fallback 到 horizontal 是合理默认（Unity 默认 Slider 也是 LeftToRight）。

### 3.3 `ScrollList.cs` 改动

新加私有常量:
```csharp
private const float DefaultMainAxisLength = 200f;   // 沿滚动轴
private const float DefaultCrossAxisLength = 160f;  // 跨滚动轴
```

新增覆写:
```csharp
public override Vector2? GetNativeSize()
    => _direction == "horizontal"
        ? new Vector2(DefaultMainAxisLength, DefaultCrossAxisLength)
        : new Vector2(DefaultCrossAxisLength, DefaultMainAxisLength);
```

`_direction` 默认 `"vertical"`（已经在字段初始化里），不需要 null check。

### 3.4 `Control.cs` / `SizeSpec.cs` 不改

BCS-D6 已经在 `ApplyLayoutElement` no-size 分支接入了 `GetNativeSize()`；BCS-D7 已经在 `ApplyCommon` 自由定位 else 分支接入了 native fallback。两条都是 "若 `GetNativeSize()` 非 null 则用 native，否则维持原行为"，三个控件覆写后自动接入。

### 3.5 已有测试不需要调整

`DropdownTests` / `SliderTests` / `ScrollListTests` 里现有的几何 / 行为测试都使用显式 size，走 `ApplyLayoutElement` 的 HasWidth=true 分支，不依赖 native fallback；本次只新增 `GetNativeSize` 返回值并接入 fallback 分支，不改任何现有断言所测的路径。需在实施时跑一遍三个 control 各自的 `Tests` 套件确认。

### 3.6 新增测试

每个控件一份独立测试文件，命名 / 结构平行于 `ToggleContentSizingTests.cs`：

**`Tests/EditMode/Controls/DropdownContentSizingTests.cs`**:
- `Dropdown_GetNativeSize_returns_default_size` — `<Dropdown id='d'/>`，断言 `GetNativeSize() == (160, 44)`。
- `Dropdown_in_Frame_no_size_sizeDelta_matches_native` — `<Frame size='400x200'><Dropdown id='d'/></Frame>`，断言 `sizeDelta == (160, 44)`。
- `Dropdown_in_Frame_anchor_stretch_skips_native_fallback` — anchor=stretch + margin=8，断言 sizeDelta 走 margin 公式。
- `Dropdown_in_VStack_no_size_gets_LayoutElement_with_native_preferred` — `<VStack width='400' height='200'><Dropdown id='d'/></VStack>`，断言 LE 存在 + preferred = native。
- `Dropdown_in_Frame_explicit_size_overrides_native` — `<Frame size='400x200'><Dropdown id='d' size='240x36'/></Frame>`，断言 `sizeDelta == (240, 36)`。

**`Tests/EditMode/Controls/SliderContentSizingTests.cs`**:
- `Slider_horizontal_GetNativeSize_returns_horizontal_defaults` — 默认（horizontal），断言 `(160, 44)`。
- `Slider_vertical_GetNativeSize_returns_vertical_defaults` — `direction='vertical'`，断言 `(44, 160)`。
- `Slider_in_Frame_no_size_sizeDelta_matches_native` — `<Frame size='400x200'><Slider id='s'/></Frame>`，断言 `sizeDelta == (160, 44)`。
- `Slider_in_VStack_no_size_gets_LayoutElement_with_native_preferred` — 断言 LE preferred = native。
- `Slider_in_Frame_explicit_size_overrides_native` — 断言 explicit size 胜出。
- `Slider_direction_change_via_variant_updates_native` — base direction=horizontal, variant `mobile` 改成 vertical；激活后 LE.preferred 翻转。

**`Tests/EditMode/Controls/ScrollListContentSizingTests.cs`**:
- `ScrollList_vertical_GetNativeSize_returns_vertical_defaults` — 默认（vertical），断言 `(160, 200)`。
- `ScrollList_horizontal_GetNativeSize_returns_horizontal_defaults` — `direction='horizontal'`，断言 `(200, 160)`。
- `ScrollList_in_Frame_no_size_sizeDelta_matches_native` — 断言 `sizeDelta == (160, 200)`。
- `ScrollList_in_VStack_no_size_gets_LayoutElement_with_native_preferred` — 断言 LE preferred = native。
- `ScrollList_in_Frame_explicit_size_overrides_native` — 断言 explicit size 胜出。

总共 16 个新测试（5 + 6 + 5）。

### 3.7 SKILL.md 更新

`authoring-promptugui-xml/SKILL.md` 的三个控件表格行各加一句默认尺寸说明（位置类似 `<Toggle>` 行那次的改法）。LayoutGroup 段和 free-positioning 段的"提供 native 的控件"列表把 `<Dropdown>`、`<Slider>`、`<ScrollList>` 加进去。

具体改动文本在 plan 里给出。

---

## 4. 非目标 / Out of Scope

- 不改 `<InputField>` 的默认尺寸 —— 它内部 placeholder / caret / multi-line 公式更复杂，单独 spec。
- 不读 Dropdown 的 caption 来动态算 width（见 DSS-D2 / DSS-D7）。
- 不引入 `PromptUGUISettings` 级的 tap-target / 默认控件尺寸主题配置 —— 跟 Btn / Toggle 那两次保持一致。
- 不抽 `MinTapHeight=44` / `DefaultSliderLength=160` 等常量到 `Control` 基类（见 DSS-D5）。
- 不改三个控件内部 Background / Fill / Handle / Viewport 等子节点的几何 —— 它们独立于根节点 native 报告。

---

## 5. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 已有项目的 `<Frame><Dropdown/></Frame>` 不写 size 现在拿 0×0、靠 anchor=stretch+margin 工作的写法本次会变成 160×44 | 检查：anchor=stretch 任一轴时 `preset.StretchX/Y` 为 true → fallback 跳过；只有"完全居中/单角定位 + 零 size"才受影响，那个写法本来就基本不可见（~16×16 sliced min），向后改善 |
| Slider direction 通过 `[UIAttr] string Direction` setter 动态改 → native 反向 → LE.preferred 不变（因为 ApplyLayoutElement 已经跑完） | Direction setter 现有逻辑只更新 `_slider.direction`，不重跑 layout。**这是已经存在的小缺口**（即使有 native fallback 也不在变化时刷新），但实际场景是 Variant 切换 Direction，Variant 重应用会触发完整 ReSolve → 重跑 ApplyCommon → 重读 GetNativeSize → 正确。运行时纯代码改 direction 的场景很罕见，留作后续 spec 议题 |
| ScrollList 的 200/160 默认值在某些项目里太小（手机长列表）或太大（紧凑 toolbar） | 这正是 native fallback 的设计意图：避免 0×0 不可见。需要不同尺寸的项目应该显式写 size，这跟 Btn 80×44 默认的处境相同 |
| 三个控件公式各异 → 后续维护时常量散落难找 | 每个控件的常量贴在类顶部 + 注释说明（如 `// 沿滚动轴`），通过测试持续守护（公式失配 → 断言挂） |
