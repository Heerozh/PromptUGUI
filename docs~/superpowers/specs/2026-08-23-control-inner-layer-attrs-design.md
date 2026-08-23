# 内置控件的内部图层属性补全 + TabBar 布局组配置修复

**日期**：2026-08-23
**状态**：已实施（分支 `feat/procedural-style`）。实施期的两处追加决定记在 §2.3 / §2.4。
**作用域**：两件独立的事。（A）`<TabBar>` 建的 LayoutGroup 忘了配 `childControl*` / `childForceExpand*`，导致 `<Tab>` 的 `width` / `height` 完全不生效 —— 补齐，与 `<VStack>` / `<HStack>` 对齐。（B）`<Slider>` / `<Toggle>` / `<Dropdown>` / `<ScrollList>` 的若干内部图层（滑块、对勾、下拉箭头、滚动条、下拉项高亮）在 `OnAttached` 里写死默认皮肤，没有任何 XML 钩子 —— 按既有 `<Progress>` 的命名规约补上。
**触发**：`Samples~/ProceduralStyle` 做扁平皮肤时撞到，当时只能在 runner 里用 `ApplyLibraryWorkarounds` 遍历 GameObject 绕过。本 spec 落地后那个 workaround 整个删除。
**关联**：命名规约沿用 [`<Progress>`](2026-05-27-progress-control-design.md) 的 `fill` / `fillColor` / `bg` / `bgColor` / `frame` / `frameColor`；色值解析走 [color-tokens](2026-05-28-color-tokens-design.md) 的单点 chokepoint；`IsSprite` / `IsColor` 标记见 scripting skill「Color attributes on custom controls」。

---

## 1. 问题 A：`<Tab>` 的 width / height 不生效

### 1.1 现象与根因

`TabBar.EnsureLayout` 只写 `AddComponent<HorizontalLayoutGroup>()`，之后仅设 `spacing` / `padding`，**从不碰 `childControlWidth/Height` 与 `childForceExpandWidth/Height`**，于是留在 Unity 默认值 `childControl*=false` + `childForceExpand*=true`。

`HorizontalOrVerticalLayoutGroup` 在 `childControlSize=false` 时**只摆位置、不改尺寸**：

```csharp
if (!controlSize) { min = child.sizeDelta[axis]; preferred = min; flexible = 0; }
```

而 `Control.ApplyCommon` 对 LayoutGroup 子节点走的是 `ApplyLayoutElement`（写 `LayoutElement`，**不碰 RectTransform**）。两边一对上，结论就是：`<Tab>` 上的 `width` / `height` 只参与间距分配，Tab 自己的 RectTransform 永远停在默认 100×100。

实测（960×540，5 个 `width="stretch" height="36"` 的 Tab）：

| | 期望 | 实际 |
|---|---|---|
| TabBar rect | 928×36 | 928×36 ✓ |
| 每个 Tab rect | 182.4×36 | **100×100** ✗ |
| Tab 位置间距 | 186.4 | 186.4 ✓ |

**这不是新问题**：现有 `Samples~/CommonControls` 写的 `<Tab width="84">` 实际是 100 宽、按 84 间距排列 —— 相邻 Tab 互压 16px，只因为默认皮是不透明的才看不出来。

`<VStack>` / `<HStack>` 早就配对了，注释写得很明白：

```csharp
// childControl* 必须 true，LayoutElement 才生效；forceExpand* 必须 false，
_layout.childControlWidth = true;   _layout.childForceExpandWidth = false;
_layout.childControlHeight = true;  _layout.childForceExpandHeight = false;
```

**TabBar 只是漏了这一段。** `<Carousel>` 也建 LayoutGroup，一并核对。

### 1.2 修法

`TabBar.EnsureLayout` 建组后按 VStack/HStack 同款配置：

```csharp
switch (_layout)
{
    case HorizontalLayoutGroup h:
        h.childControlWidth = true;  h.childForceExpandWidth = false;
        h.childControlHeight = true; h.childForceExpandHeight = false;
        break;
    case VerticalLayoutGroup v: /* 同上 */ break;
}
```

`childForceExpand*` **必须** false：Unity 在 `GetChildSizes` 末尾做 `if (childForceExpand) flexible = Mathf.Max(flexible, 1)`，留 true 会把 `width="84"` 这种定尺寸 Tab 也一起拉伸，等于换一个方向继续无视作者写的值。

配置写进 `ApplySpacingPadding`（它已经被"复用现有组"和"新建组"两条路径共用），避免 direction 切换后配置丢失。

### 1.3 `<Tab>` 补 `GetNativeSize`（防塌陷）

修完之后 `childControlWidth=true`，没写 `width` 的 Tab 会去问 `LayoutUtility.GetPreferredSize`。`Tab` 没有 `GetNativeSize` 覆写，根上的 `Image` 在 `sprite="none"` 时报 -1 → **preferred 落到 0，Tab 塌成 0 宽**。修 bug 顺手引入新 bug，不可接受。

给 `Tab` 补 `GetNativeSize`，镜像 `Btn`（label 自然尺寸 + padding，最小 tap target 兜底）：

```csharp
public override Vector2? GetNativeSize()
{
    if (_label != null && !string.IsNullOrEmpty(_label.text))
    {
        var pref = _label.GetPreferredValues(_label.text);   // 不用 preferredWidth：见 Btn 的注释
        return new Vector2(pref.x + HorizontalPadding * 2f,
                           Mathf.Max(MinTapHeight, pref.y + VerticalPadding * 2f));
    }
    return new Vector2(DefaultTabWidth, MinTapHeight);
}
```

于是「不写 width 的 Tab」= 按文字宽度自适应（比过去那个 100 的巧合更符合直觉），「写了 width」= 精确生效，「`width="stretch"`」= 平分剩余空间。

### 1.4 行为变化与兼容性

这是一次**行为修正**，现有 TabBar 的实际布局会变（朝正确方向）：

| 写法 | 修复前 | 修复后 |
|---|---|---|
| `<Tab width="84">` | 100 宽（相邻重叠 16px） | 84 宽 |
| `<Tab width="stretch">` | 100 宽 | 平分剩余空间 |
| `<Tab>`（不写） | 100 宽 | 按 label 自适应 |
| `height` 同理 | 恒 100 | 按写的值 / 跨轴 |

`Samples~/CommonControls` 的观感会变（Tab 变窄、不再重叠）。这是修 bug 的必然结果，不做兼容开关 —— 留一个"旧的错误行为"开关只会让两种语义长期共存。

## 2. 问题 B：内部图层没有 XML 钩子

### 2.1 命名规约（已有先例，不新发明）

`<Progress>` 早就确立了规约：**每个内部图层一对属性 —— `<layer>` 收 sprite、`<layer>Color` 收颜色**（`fill` / `fillColor`、`bg` / `bgColor`、`frame` / `frameColor`）。本次把同一条规约铺到其余控件，作者只需记一条规则。

- sprite 侧标 `[UIAttr(IsSprite = true)]` —— Editor 的 "Sync Sprite Sets" 靠这个标记发现非 `sprite` 名字的图引用，漏标会在 atlas 同步时报 missing。
- 颜色侧标 `[UIAttr(IsColor = true)]`，setter 走 `Internal.ColorApplier.Apply(img, UI.Theme.ResolveSpec(value))` —— 于是 token / `/alpha` / 逗号渐变全部免费获得，与其它颜色属性一致。
- 空串 `""` = 去掉 sprite（`UI.ResolveSprite` 对 null/empty 直接返回 null），与既有 `sprite=""` / `sprite="none"` 语义一致。

### 2.2 补齐清单

| 控件 | 内部图层 | 新增属性 | 说明 |
|---|---|---|---|
| `<Slider>` | Fill（已填充段） | `fill` · `fillColor` | 与 `<Progress fill>` 同名同义 |
| `<Slider>` | Handle（滑块） | `handle` · `handleColor` | |
| `<Toggle>` | Checkmark（对勾） | `checkmark` · `checkmarkColor` | 见 §2.3 —— `sprite` 顺带修正为指向 box |
| `<Dropdown>` | Arrow（下拉箭头） | `arrow` · `arrowColor` | |
| `<Dropdown>` | Item Background（选项高亮带） | `itemColor` | **当前硬编码 `#F5F5F5`**，深色主题下必炸；无默认 sprite 故不配 `item` |
| `<Dropdown>` | Item Checkmark（选中项对勾） | `checkmark` · `checkmarkColor` | 与 Toggle 同名；Dropdown 没有第二个 checkmark，无歧义 |
| `<Dropdown>` | Item Label | `itemTextColor` | 区别于 `textColor`（那是收起状态的 caption） |
| `<Dropdown>` · `<ScrollList>` | Scrollbar 轨道 | `scrollbar` · `scrollbarColor` | 两个控件同名同义 |
| `<Dropdown>` · `<ScrollList>` | Scrollbar Handle | `scrollbarHandle` · `scrollbarHandleColor` | |

合计：Slider +4、Toggle +2、Dropdown +10、ScrollList +4。

### 2.3 顺带修正：`<Toggle sprite>` 指错了层

`Toggle.Color` 落在 `_bg`（20×20 勾选框），而 `Toggle.Sprite` 落在 `_checkmark` —— **同一对 `color`/`sprite` 指向两个不同图层**，与库里其它每一个控件都相反。这不是设计，是遗漏。

修正为 `sprite` → `_bg`（与 `color` 同层、与全库一致），对勾图形改由新的 `checkmark` 承担。

风险已核实：全仓库只有 `SpriteAtlasSyncerTests` 里一处 `<Toggle sprite='ui:check'/>`（只断言 sprite 引用被扫描到，不关心落点）和本次新写的 sample 在用，无实际依赖。属**破坏性变更**，写进 SKILL。

### 2.4 属性名与 Unity 类型撞名

`ScrollList` 里不能声明名为 `Scrollbar` 的属性 —— 它会在类作用域内遮蔽 `UnityEngine.UI.Scrollbar` 类型名，`private Scrollbar _vertScrollbar` 和所有方法签名一起编译不过（CS0154）。属性名取 `ScrollbarSprite`，XML 名由 `[UIAttr("scrollbar", IsSprite = true)]` 显式给。`Dropdown` 无此问题（它对 Unity 类型一直是全限定写法），但两边 XML 名保持一致。

### 2.5 懒建图层的 pending 值

`ScrollList` 的滚动条由 `Direction` setter 懒建（`EnsureVerticalScrollbar` / `EnsureHorizontalScrollbar`），属性 setter 可能早于图层存在就被调用。沿用仓库既有的 pending 字段模式（`Frame._pendingMaskPadding`、`ScrollList._spacing`）：

- setter 把原始字符串存进 `_pendingScrollbarColor` 等字段，若图层已存在则立即应用；
- `EnsureVerticalScrollbar` / `EnsureHorizontalScrollbar` 建完后统一调 `ApplyScrollbarSkin()` 回放。

两条方向都要覆盖：`direction` 切换会启用另一根滚动条，皮肤必须跟着走。

`Dropdown` 的所有图层都在 `OnAttached` 里建，无此问题；但 `arrow` / `itemBg` / `itemCheckmark` / `itemLabel` / `scrollbarBg` / `sbHandle` 目前是局部变量，需提升为字段。

### 2.6 Dropdown 选项模板的传播

`TMP_Dropdown` 每次展开都从 `template` 子树克隆选项行。所以改模板上的 Item Background / Item Checkmark / Item Label 会自然作用于之后所有选项 —— 不需要在 `SetOptions` 里重放。但**已展开时改属性**不会影响当前那批实例；ReSolve 通常发生在关闭态，接受此限制，写进 SKILL。

### 2.7 明确不做

| 目标 | 原因 |
|---|---|
| `<Slider>` 的 Fill Area / Handle Slide Area 的几何参数（内缩 10px 等） | 那是布局不是皮肤，暴露出来等于把 prefab 结构变成公共 API |
| Scrollbar 宽度 / 显隐策略 | 与本次「上色换图」正交，等真实需求 |
| `<Dropdown>` 选项行高 / 弹窗高度 | 同上 |
| 内部图层的 `tint`（linear/multiply） | 现有 `tint` 已作用于控件主体；逐图层 tint 无真实需求 |

## 3. 测试

EditMode（`UI.ResetForTests` 约定）：

1. **TabBar 布局**：`<Tab width="84">` → rect 精确 84；`width="stretch"` ×N → 平分（含 spacing）；不写 width → 按 label 自适应且 **> 0**；`height` 同理；横竖屏 direction 切换后配置不丢（重建组的路径）。
2. **Tab.GetNativeSize**：有 label / 空 label 两条分支；不塌陷回归测试。
3. **每个新属性**：sprite 侧断言 `Image.sprite` 落点正确、`""` 清空；颜色侧断言 token / `/alpha` / 渐变（渐变落 `GradientTint`）。
4. **ScrollList pending**：属性先于 `direction` 设置 → 建条后仍生效；两个方向都覆盖；direction 来回切皮肤不丢。
5. **Dropdown itemColor**：模板上的 Item Background 颜色被改；`SetOptions` 之后新实例继承。
6. **XSD**：新属性出现在对应元素上（substring 断言）。

回归面：`Samples~/CommonControls` 的 TabBar 观感会变，属预期；跑一遍全量测试确认无其它连带。

## 4. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：`<Slider>` / `<Toggle>` / `<Dropdown>` / `<ScrollList>` 四张属性表补新行；`<Dropdown>` 的 `itemTextColor` vs `textColor` 区别；「已展开时改属性不影响当前实例」的注意项。
- `reference/controls-tabs.md`：`<Tab>` 的 `width` / `height` 现在真正生效 + 不写时按 label 自适应；这是行为变化，明确标注。
- C# skill：**修正「subclass 并 override `OnAttached`」那句** —— `Runtime/Controls/` 下除 `Trigger` 外全是 `sealed`，那条路走不通。改成「写自己的 `Control` 子类并 `UI.Registry.Register<T>("Tag")` 覆盖同名 tag」，并指向本次补齐的属性作为首选方案。

## 5. 落地后的清理

`Samples~/ProceduralStyle/ProceduralStyleRunner.cs` 的 `ApplyLibraryWorkarounds` 整个删除，对应的 XML 改为直接写新属性；`README.md` 的「已知缺口」一节同步删掉。这既是清理，也是这套属性够不够用的验收标准 —— 删不干净就说明还漏了图层。
