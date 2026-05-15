# Btn 内容自适应 + 自由定位 native fallback 设计

**日期**: 2026-05-15
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `Btn` 覆写 `GetNativeSize()`，从 `_autoLabel`（TMP_Text）的 preferred 文本尺寸 + 常量 padding / tap target 计算返回；无 text 时回退到 icon-only 默认 (80, 44)。
2. `Control.ApplyLayoutElement`（LayoutGroup 子节点路径）扩展：未写 size 但控件提供 `GetNativeSize()` 时，挂 LayoutElement 并把 native 写入 preferred（替代当前"重置到 -1"的分支）。
3. `Control.ApplyCommon` 自由定位分支新增 fallback：anchor 没 stretch + 没写 size/width/height + `GetNativeSize()` 不为 null → 用 native 作为默认尺寸。
4. 测试：`<HStack><Btn>OK</Btn></HStack>` Btn LayoutElement.preferredWidth > 文字裸宽；`<Frame><Btn>Cancel</Btn></Frame>` `sizeDelta.x > 0`；icon-only Btn 默认 80x44；显式 size 覆盖 native；Variant 切文字长度时 preferred 跟着变。
5. 同步 `authoring-promptugui-xml` SKILL.md 里 `<Btn>` 段 + free-positioning fallback 行为说明。
6. 更新 `ControlApplyCommonLayoutGroupTests` 里"no size → no LE"的两个测试（Btn 路径下行为变了，但 Image 仍保持原行为；测试改用 Image 覆盖原契约，Btn 测试改成"no size → LE with native preferred"）。

**依赖**: [`2026-05-12-layout-group-child-sizing-design.md`](2026-05-12-layout-group-child-sizing-design.md)（LGC-D8 的"作者没写任何 size 属性 → 不挂 LayoutElement"决策被本次扩展为"控件可以 opt-in 通过自挂 LayoutElement 主导 preferred 报告"）。

---

## 1. 背景

当前 `<Btn>OK</Btn>`（不写任何 size）在两种父级下都不可用：

| 父级 | 路径 | 现象 |
|---|---|---|
| `<HStack>` / `<VStack>` | `Control.ApplyLayoutElement` 早 return（HasWidth/Height 都 false → 不挂 LayoutElement），HorizontalLayoutGroup 把 `Image` 的 ILayoutElement 当 preferred 源 | 按钮宽 ≈ `pugui_9slice_round` sprite 原生宽（约 10px） |
| `<Frame>`（自由定位） | `Control.ApplyCommon` else 分支 → `MarginResolver.Resolve`，`sizeSpec.HasWidth=false` → `sizeDelta.x = 0` | 按钮 0x0，肉眼不可见 |

两条路径都没把 Btn 内部 `_autoLabel`（TMP_Text）的 `preferredWidth` 接上 —— 它是 Btn GameObject 的子节点，layout group 不递归询问，自由定位也没人查它。

后果：作者写 `<HStack><Btn>OK</Btn><Btn>Cancel</Btn></HStack>` 这种最自然的简写会得到一排看不见或挤成一坨的小按钮，必须每个 Btn 都显式 `width="..."` 或 `width="stretch"` 才能用。这是劝退级的默认行为。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| BCS-D1 | LayoutElement 所有权 | `Control.ApplyLayoutElement` 统一管理；Btn 不在 OnAttached 时挂 LE，只覆写 `GetNativeSize()` | 避免双管理（Btn 挂一份、ApplyLayoutElement 又写一份）；让未来给 Image/Text 加 GetNativeSize 时自动接入，不用每个控件都挂 LE |
| BCS-D2 | preferred 计算公式 | `preferredWidth = label.preferredWidth + 32`；`preferredHeight = max(44, label.preferredHeight + 12)` | 左右各 16、上下各 6 padding 接近 Unity Editor 标准按钮观感；44 是 iOS / Material Design 通用 tap target 下限 |
| BCS-D3 | 无 Text 时的尺寸 | `GetNativeSize` 回退到 `(80, 44)` | sprite-only 按钮（icon button）有合理默认；80 跟 Cancel 文字宽差不多 |
| BCS-D4 | Text 测量是否 `ForceMeshUpdate` | 是，在 `GetNativeSize` 里调一次 | TMP_Text.preferredWidth 在 SetText 当帧不稳，强制立即生效避免首帧抖动；ApplyCommon 在 attr 应用末尾调一次 GetNativeSize，cost 可接受 |
| BCS-D5 | 显式 size 与 native 的优先级 | 显式 size 覆盖（已有逻辑，无需改） | `ApplyLayoutElement` 在 sizeSpec.HasWidth=true 时直接走 numeric/stretch 分支，根本不进 native 路径 |
| BCS-D6 | `ApplyLayoutElement` 的 "no size" 分支扩展 | `sizeSpec.HasWidth=false && HasHeight=false` 时：调 `GetNativeSize()`，若非 null → 挂 LE 并写 native 到 preferred；若 null → 维持现有"清空 LE 到 -1（如存在）"的行为 | 把 LGC-D8 决策"无 size 不挂 LE"扩展成"无 size 但有 native 则挂 LE 报告 native"；现有依赖 Image/TMP 自带 ILayoutElement 的控件（自身 GetNativeSize 返回 null）行为不变 |
| BCS-D7 | 自由定位 fallback 规则 | `Control.ApplyCommon` else 分支：anchor 两轴都不 stretch + sizeSpec 两轴都 HasWidth=HasHeight=false + `GetNativeSize()` 不为 null → 把 native 当 size 用 | 比"作者必须写 size"友好；对 anchor=stretch 轴零影响（那条轴已经被 margin 接管）；目前 GetNativeSize 重写的只有 Icon（已经走 native 路径）和新加的 Btn，行为不回归 |
| BCS-D8 | fallback 是否分轴 | 只在"两轴都没写"时整体 fallback；任一轴写了 size/width/height 就放弃 native 整体逻辑 | 分轴 fallback 语义复杂（写 width="100" 但 height 不写时该取 native.y 还是 0？），不值得；作者要部分 native 可以显式写 size="native" 走 WithNativeResolved 那条路 |
| BCS-D9 | Variant 切换下的行为 | `ApplyCommon`（含 ApplyLayoutElement 与自由定位 fallback）在每次 ReSolve 时重跑，会重新调 GetNativeSize → preferred / sizeDelta 自然跟 Variant 走 | 与 Variant 重应用机制天然兼容，无需额外 hook；Btn 的 Text 在重应用时也会被 ControlAttributeApplier 重写，TMP preferredWidth 在 GetNativeSize 里通过 ForceMeshUpdate 强制同步 |
| BCS-D10 | 报错路径 | 不新增报错 | 这次是默认值优化，没有新增非法状态 |
| BCS-D11 | spec 更新点 | spec §6 加一段"自由定位 native fallback"；§6.5 LayoutGroup 子节点段落补一句"控件可在 `OnAttached` 自挂 LayoutElement 报告 preferred；ApplyLayoutElement 在作者没写 size 时不再清零，让该 LayoutElement 主导" | spec §6.5 现有文字暗示"作者没写就交给原生 ILayoutElement"，本次没改这条，但 Btn 的实际现象会变 |
| BCS-D12 | SKILL.md 更新点 | `authoring-promptugui-xml/SKILL.md` 的 `<Btn>` 段加一句"未写 size 时按文字自适应"；free-positioning 段加一句"控件提供 native 时不写 size 默认为 native" | CLAUDE.md 触发条件：新增可见行为（默认值变化） |

---

## 3. 实现要点

### 3.1 `Btn.cs` 改动

新加私有常量:
```csharp
private const float HorizontalPadding = 16f;  // 单侧
private const float VerticalPadding = 6f;     // 单侧
private const float MinTapHeight = 44f;
private const float DefaultIconBtnWidth = 80f;
```

新增覆写:
```csharp
public override Vector2? GetNativeSize()
{
    if (_autoLabel != null && !string.IsNullOrEmpty(_autoLabel.text))
    {
        _autoLabel.ForceMeshUpdate();  // BCS-D4
        var w = _autoLabel.preferredWidth + HorizontalPadding * 2f;
        var h = Mathf.Max(MinTapHeight, _autoLabel.preferredHeight + VerticalPadding * 2f);
        return new Vector2(w, h);
    }
    return new Vector2(DefaultIconBtnWidth, MinTapHeight);
}
```

不改 `OnAttached` / `Text` / `Font` setter —— LayoutElement 由 `ApplyLayoutElement` 统一挂管，每次 `ApplyCommon` 重跑都会重新调 `GetNativeSize`，TMP 文字变化自然被捕获。

### 3.2 `Control.cs` ApplyLayoutElement 改动

修改 "无 size" 分支（line 222-234 附近），把"清空 LE 到 -1"扩展成"看控件有没有 GetNativeSize"：

```csharp
if (!sizeSpec.HasWidth && !sizeSpec.HasHeight)
{
    var existing = GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
    var native = GetNativeSize();
    if (native.HasValue)
    {
        // BCS-D6: 控件能报告 native → 挂 LE 把 native 当 preferred 暴露给 LayoutGroup。
        // 不挂的话 LayoutGroup 会回落到 Image/TMP 自带 ILayoutElement（在 Btn 上就是 9-slice
        // sprite 的原生 ~10px），淹没文字宽度信号。
        existing ??= GameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        existing.preferredWidth = native.Value.x;
        existing.preferredHeight = native.Value.y;
        existing.flexibleWidth = -1;
        existing.flexibleHeight = -1;
    }
    else if (existing != null)
    {
        // 现有行为：前一次 Variant 可能挂过 LayoutElement，本次没尺寸 + 控件没有 native → 还原成"无约束"
        existing.preferredWidth = -1;
        existing.preferredHeight = -1;
        existing.flexibleWidth = -1;
        existing.flexibleHeight = -1;
    }
    return;
}
```

### 3.3 `Control.cs` ApplyCommon 自由定位 fallback

在 else 分支（自由定位）开头，`AnchorResolver.Resolve` 调用之前插一段:

```csharp
// BCS-D7: 自由定位 + anchor 两轴都不 stretch + sizeSpec 完全无尺寸 →
// 若控件能提供 native size，用 native 作为默认（避免 sizeDelta=(0,0) 不可见）。
if (!preset.StretchX && !preset.StretchY
    && !sizeSpec.HasWidth && !sizeSpec.HasHeight)
{
    var nativeFallback = GetNativeSize();
    if (nativeFallback.HasValue)
    {
        sizeSpec = SizeSpec.FromNumeric(nativeFallback.Value.x, nativeFallback.Value.y);
    }
}
```

`SizeSpec` 加一个 internal 静态构造:
```csharp
internal static SizeSpec FromNumeric(float w, float h) =>
    new(w, h, true, true, false, false, false, false, 1f, 1f, false, false, 0f, 0f);
```

注意：这个 fallback 在 `IsNativeWidth/Height` 那条已有的 `GetNativeSize` 调用之前还是之后？已有的是 `if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)` 处理 `size="native"` 关键字，本次新加的 fallback 处理的是"完全没写 size"，两条路互斥（一条有 HasWidth=true 但 IsNativeWidth=true，另一条 HasWidth=false），不会冲突；放在已有 native-resolve 块之后即可。

### 3.4 已有测试调整

`ControlApplyCommonLayoutGroupTests.cs` 两处需要改：

1. `Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement`（line 56-70）—— Btn 路径下"无 size → 无 LE"的契约变了。改成：
   - `Btn_in_VStack_with_no_size_gets_LayoutElement_from_native`：无 size 时 Btn 有 LE，preferredWidth = 80（DefaultIconBtnWidth，因为没 text）、preferredHeight = 44。
   - 新增一个 `Image_in_VStack_with_no_size_attrs_gets_no_LayoutElement` 测试，保留"GetNativeSize=null 的控件无 size → 无 LE"这条 LGC-D8 契约（Image 还没覆写 GetNativeSize）。

2. `Btn_in_Frame_with_size_writes_sizeDelta_not_LayoutElement`（line 73-88）—— Btn 在 Frame 下走自由定位分支，ApplyLayoutElement 完全不跑，所以无论 size 有没有，都不会挂 LE。当前测试还是对的，但注释里"Btn under Frame must NOT get a LayoutElement"措辞要细化成"non-LayoutGroup parent: ApplyLayoutElement 不跑，不会挂 LE"。

3. `Btn_in_Grid_with_size_gets_no_LayoutElement`（line 91-107）—— 同上，Grid 仍是 LayoutGroup（GridLayoutGroup），所以 ApplyLayoutElement 跑，但走的是 HasWidth=true 分支（写 LE）。当前测试断言"无 LE"实际是 `parentIsGrid` 在 ApplyCommon 里被 `parentIsAutoLayout = parentLg != null && !parentIsGrid` 排除掉。重读 Control.cs 确认：Grid 父级下 parentIsAutoLayout=false → ApplyLayoutElement 不跑 → 仍无 LE。测试维持。

### 3.5 测试覆盖（新增）

`Tests/EditMode/Controls/BtnContentSizingTests.cs`（新建）:
- `BtnInHStack_NoSize_PreferredWidthExceedsTextNative`：装 `<HStack><Btn>OK</Btn></HStack>`，确认 Btn LE 存在且 `preferredWidth >= "OK" 文字宽 + 32`、`preferredHeight == 44`。
- `BtnInFrame_NoSize_SizeDeltaIsNative`：装 `<Frame><Btn>Cancel</Btn></Frame>`，确认 `sizeDelta.x > "Cancel" 文字宽`、`sizeDelta.y == 44`。
- `BtnInFrame_ExplicitSize_OverridesNative`：装 `<Frame><Btn size="200x60">OK</Btn></Frame>`，确认 sizeDelta = (200, 60)，fallback 不生效。
- `BtnWithoutText_FallsBackToIconDefaults`：装 `<Frame><Btn id='b'/></Frame>`（无 text shorthand、无 Sprite attr），确认 sizeDelta = (80, 44)。
- `BtnInHStack_VariantChangeTextLength_PreferredUpdates`：base text="OK"，variant `mobile` 把 text 改成 "Cancel"。激活 mobile，确认 `LE.preferredWidth(mobile) > LE.preferredWidth(base)`。
- `BtnUnderAnchorStretch_NativeFallbackSkipped`：`<Frame><Btn anchor='stretch' margin='8'>OK</Btn></Frame>`，确认 sizeDelta 走 margin 路径（`= parent内宽 - 16`），不被 native fallback 覆盖。

### 3.6 SKILL.md 更新

`authoring-promptugui-xml/SKILL.md` 的 `<Btn>` 段新增一条：
> **Btn 默认按文字自适应**: 不写 `size`/`width`/`height` 时，Btn 的 preferred 宽度 = 文字宽 + 左右 16px padding，preferred 高度 = max(44, 文字高 + 12px)。在 layout group 里通过 LayoutElement 主导；在自由定位下作为 native fallback。无文字（icon-only）默认 80x44。

free-positioning 段新增一条：
> **自由定位 fallback to native**: 在 `<Frame>` 等自由定位父级下，若控件没写 `size`/`width`/`height` 且 anchor 没 stretch，控件提供了 `GetNativeSize()` 时会自动用 native 尺寸（避免 sizeDelta=(0,0)）。当前 `<Btn>` 和 `<Icon>` 走这条路。

---

## 4. 非目标 / Out of Scope

- 不引入 `PromptUGUISettings` 级的 padding/tap-target 主题配置 —— 常量先写死，后续如果不同游戏有不同手感需求再单独开 spec。
- 不改 `<Toggle>` / `<Dropdown>` / `<InputField>` 的默认尺寸 —— 它们目前也有类似问题，但每个控件的"合理 native"形状不同（Toggle 有 checkmark + label，Dropdown 有 arrow + label + 弹出），需要逐个设计；本次只做 Btn。
- 不改 `<Image>` / `<Text>` 的 `GetNativeSize()` —— Image 的 native 就是 sprite native，Text 的 native 是单行文本宽，但本次范围是 Btn 的默认行为，先不扩。后续如果 D7 的全局 fallback 规则站得住脚，可以单独开一份 spec 把 Image/Text 一起接进来。

---

## 5. 风险与回滚

| 风险 | 缓解 |
|---|---|
| TMP_Text.ForceMeshUpdate 在 EditMode 下可能因为字体未加载而报错 | 测试用 PromptUGUISettings.Instance 的 default font，确认 EditMode 走得通；若失败，吞 try/catch（preferred 用默认 80x44） |
| 已有项目的 `<Frame><Btn size="100x40">` 仍正常 → 但如果有 `<Frame><Btn/>` 不写 size 现在拿 0x0、靠 anchor=stretch+margin 工作的写法，本次会让它拿到 80x44 native 然后定位错 | 检查：anchor=stretch 任一轴时 `preset.StretchX/Y` 为 true → fallback 跳过；只有"完全居中/单角定位且零 size"才受影响，那个写法本来就没意义（不可见），向后改善 |
| 全局 fallback 与未来 `<Image>` 覆写 GetNativeSize 时相互影响 | 后续给 Image 加 GetNativeSize 是单独 spec；本次只 Btn + Icon 已经覆写过，受影响面有限 |
