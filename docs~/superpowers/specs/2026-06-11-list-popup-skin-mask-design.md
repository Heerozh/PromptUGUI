# ScrollList / Dropdown 换肤 + mask 属性（对齐 Progress 词汇）

**日期**：2026-06-11
**状态**：设计阶段（待 review，未进入实施）
**作用域**：`<ScrollList>` 新增 `frame=` / `frameColor=` / `mask=`，`<Dropdown>` 新增 `popupSprite=` / `popupColor=` / `popupMask=`。不改任何现有属性语义，不动 Carousel / Markdown 的 viewport。
**关联**：属性词汇对齐 `<Progress>` 的 `bg` / `frame` / `mask` 三件套（[`2026-05-27-progress-control-design.md`](2026-05-27-progress-control-design.md)，实现见 `Runtime/Controls/Progress.cs`）。新增作者可写属性 → `authoring-promptugui-xml` SKILL 必须更新（见 §8）。

---

## 1. 背景与问题

控件的视觉一般由**边框**和**背景**两层组成：要 mask 的是背景与内容，边框画在内容之上、不进 mask，这样滚动内容不会叠到边框上。`Progress` 已经按这个模型实现（`bg=` / `frame=` / `mask=`，frame 是 MaskWrapper 之外的顶层 sibling）。

但两个带 viewport 的控件没有跟上：

1. **ScrollList**（`Runtime/Controls/ScrollList.cs:48-58`）：Viewport 的 stencil Mask + `pugui_9slice_mask` 写死。`sprite="" color="#0000"` 做透明列表时，内容仍被圆角 mask 咬角，无任何属性可改；也没有边框层。
2. **Dropdown**（`Runtime/Controls/Dropdown.cs:56-85`）：弹出列表的 `templateBg` 写死 `DefaultPopupBgColor` + 默认 sprite，完全不能换肤；Viewport mask 写死 `pugui_9slice_round`（9-slice 圆角仅 2×2px，stencil 圆角视觉上不可见，症状轻，但同样无口子）。

## 2. 属性设计

### 2.1 ScrollList（现有 `sprite=` / `color=` / `tint=` 语义不变，仍是背景层）

| 属性 | 类型 | 行为 |
|---|---|---|
| `frame=` | IsSprite | 边框层。root 下**最后一个 sibling**（Viewport 和 Scrollbar 之上），anchor 全 stretch，`raycastTarget=false`，`AutoSlice`（sprite 有 border → Sliced，否则 Simple）。不被 mask；滚动内容从边框下面穿过。**懒创建**：不写该属性不建节点。 |
| `frameColor=` | IsColor | 边框着色；单独写也会激活 frame 层（镜像 `Progress.FrameColor`）。 |
| `mask=` | IsSprite | Viewport 裁剪形状，三态见 §2.3。 |

### 2.2 Dropdown（按钮本体的 `sprite=` / `color=` / `tint=` 不动）

| 属性 | 类型 | 行为 |
|---|---|---|
| `popupSprite=` | IsSprite | 弹出列表 `templateBg` 的 sprite（`AutoSlice`）。 |
| `popupColor=` | IsColor | 弹出列表 `templateBg` 着色。 |
| `popupMask=` | IsSprite | 弹出列表 Viewport 裁剪形状，三态语义与 ScrollList `mask=` 完全一致（默认 sprite 不同，见 §2.3）。 |

弹出框不做 frame 层（YAGNI；要双层视觉时弹出框可以直接用带边框画进 sprite 的图——它不滚动到边缘外的内容远少于 ScrollList）。

### 2.3 mask 三态语义（两控件共享）

| 写法 | 行为 |
|---|---|
| 未写（自动跟随 bg sprite） | mask 形状跟随背景：`sprite` 有图（含默认圆角底）→ stencil Mask（ScrollList=`pugui_9slice_mask`，Dropdown popup=`pugui_9slice_round`，Sliced）；`sprite=""` / `sprite="none"`（背景清成 null）→ `RectMask2D` 直角。默认 ScrollList/Dropdown 带默认底 sprite → 圆角，**现有 UI 视觉零变化**；只有"清了背景"这一种情况从圆角自动变直角。 |
| `mask="路径#名字"`（显式） | stencil Mask 保留，Viewport Image 换成指定 sprite + `AutoSlice`；`showMaskGraphic=false` 不变。sprite 解析失败走现有 `UI.ResolveSprite` 失败路径（与 `sprite=` 同行为），无新错误类型。**显式即脱离自动跟随**。 |
| `mask=""`（显式） | 拆掉 stencil Mask + Viewport Image，挂 `RectMask2D` 直角裁剪。省一个 drawcall + 一张 mask 图。**显式 `""` 永远直角，即使背景有 sprite**。 |

**自动跟随（InputField 范式）**：起因——`sprite=""` 只清背景 `_bg`（退成直角实心矩形），mask 若恒留圆角则内容被"无主"圆角裁剪、与直角背景错配。解法对齐 `<InputField>` 的"text-area mask inset 跟随边框 sprite"先例：**未显式写 `mask` 时，mask 形状跟随 `sprite` 是否有图**。一旦显式写过 `mask=`（任意值含 `""`）即 latch 为显式，之后不再自动跟随。

- 跟踪 `_maskExplicit`（bool）：`Mask` setter 置 true。
- 在 `OnAfterApply`（每次 apply / ReSolve 后，`ControlAttributeApplier` 保证晚于所有 setter）reconcile：`if (!_maskExplicit) ApplyViewportMask(viewport, bgSprite != null ? null : "", default)`。`sprite=` 与 `mask=` setter 顺序无所谓——OnAfterApply 统一收口。
- 幂等：null/"" 两分支重复调用结果不变；Variant 把 `sprite.portrait=""` 时 ReSolve 自动让 mask 退直角。
- 启发式边界：只区分"有 sprite→圆角 / 无 sprite→直角"；自定义方角面板 sprite 仍得圆角 mask，需要时用显式 `mask=` 覆盖。

**Variant 可逆切换**：ReSolve 可能让 mask 值在三态间任意方向变化（圆角 → 直角 → 自定义 sprite 来回切）。实现用 **lazy-add + `enabled` 开关**（首次需要时 `AddComponent`，之后只切 `enabled`），不 Destroy——对齐"Variants don't rebuild GameObjects"惯例，也避免 PlayMode 下 `Destroy` 延迟销毁导致同帧来回切换读到待销毁组件。

## 3. 共享实现

mask 三态逻辑抽一份，放 `Runtime/Controls/Internal/ProceduralBuilders.cs`（或同目录新 helper）：

```csharp
// value 三态：null=默认（defaultSprite + stencil）；""=RectMask2D；其余=指定 sprite + stencil
internal static void ApplyViewportMask(RectTransform viewport, string value, string defaultSpriteName)
```

- ScrollList `OnAttached` 的现有 Viewport 构建改为调用 `ApplyViewportMask(viewport, null, SpriteMaskRoundedRect)`；Dropdown 同理传 `SpriteRoundedRect`。
- `mask=` / `popupMask=` setter 直接转调。
- stencil ↔ RectMask2D 切换时同步处理 Viewport 上的 `UnityImage`（RectMask2D 路径不需要 graphic，必须移除，否则白图会渲染出来——RectMask2D 不像 Mask 有 showMaskGraphic）。

frame 层（ScrollList）：懒创建，`_frame ??= 建节点`，每次激活后 `SetAsLastSibling()` 保证压在 Scrollbar 之上（Scrollbar 是 `EnsureVerticalScrollbar` 懒建的，顺序不可靠）。

## 4. 与现有体系的交互

- **RuntimeStateAttr**：不涉及——新属性全是静态皮肤，无运行时回写。
- **Variant ReSolve**：`ControlAttributeApplier` 重放 setter 即可，§2.3 的可逆切换是唯一额外要求。
- **AutoHideAndExpandViewport**：mask 形态切换不改 Viewport 的 RectTransform 几何（`sizeDelta` / 锚点不动），ScrollRect 的 scrollbar 撑开逻辑不受影响。
- **XSD**：新属性走 customs 反射路径自动进 schema，无需手写。
- **Lint**：无新规则；`BuiltinTags.cs` 无新 tag，不需要同步。

## 5. 非目标（v1）

- 不做 `maskPadding=`（Frame/Image 有先例，但这里无真实需求）。
- 弹出框不做 `popupFrame=`。
- 不动 Carousel / Markdown 的裸 `RectMask2D` viewport（已是直角，无症状）。
- 不做"`sprite=""` 时 mask 自动退直角"的隐式推导——显式 `mask=""` 更可控。

## 6. 测试（EditMode，沿用现有 ScrollList / Dropdown 测试文件模式）

ScrollList：

1. `frame="..."` → root 最后一个 child，stretch 全幅，`raycastTarget=false`，sprite 正确，有 border 的 sprite → Sliced；
2. 只写 `frameColor=` → frame 节点被激活且颜色正确；
3. 不写 `frame` → 无 frame 节点（懒创建）；
4. 不写 `mask` → Viewport 上 stencil Mask + `pugui_9slice_mask`（现状回归）；
5. `mask="自定义"` → Viewport Image 换 sprite，Mask 仍在，`showMaskGraphic=false`；
6. `mask=""` → Viewport 无 Mask 无 Image，有 RectMask2D；
7. Variant 切换 `mask` 三态来回 → 组件不残留、不重复。

Dropdown：

8. `popupSprite` / `popupColor` → 落到 templateBg；
9. `popupMask` 三态 → 同 4-6（作用于 Template/Viewport）；
10. 不写 popup 属性 → templateBg 维持 `DefaultPopupBgColor`（现状回归）。

XSD：substring 断言补 `frame` / `popupMask` 各一条。

## 7. 风险

- **stencil → RectMask2D 切换的渲染残留**：移除 Mask 组件时 uGUI 会重建 stencil 状态，子 graphic 的 material 需要 dirty；用 `MaskUtilities` 已有机制（Destroy Mask 自带 NotifyStencilStateChanged），EditMode 测试覆盖组件状态，视觉确认留给 QA。
- **frame 压住 Scrollbar**：边框 9-slice 通常只有数像素，Scrollbar 在 AutoHideAndExpandViewport 下贴右缘，边框图案会盖住其最外侧几像素——这是"内容不叠到边框上"模型的预期表现，作者可用 `padding=` 调整。

## 8. SKILL 更新

- `authoring-promptugui-xml/SKILL.md`：ScrollList 条目加 `frame` / `frameColor` / `mask` 三行，Dropdown 条目加 `popupSprite` / `popupColor` / `popupMask` 三行（含三态语义一句话说明）。两控件无 reference 深水区文件，改主文档。
- `scripting-promptugui-csharp`：不动（无新公开 C# API）。
