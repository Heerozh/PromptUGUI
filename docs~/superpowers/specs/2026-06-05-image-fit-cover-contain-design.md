# `<Image>` fit 模式（cover / contain）设计

- **日期**: 2026-06-05
- **状态**: 设计已与用户敲定，待落 plan
- **分支**: `feat/image-fit-cover-contain`
- **关联**: 主 spec `2026-05-07-promptugui-description-language-design.md` §（`<Image>` 行）

## 1. 背景与动机

`<Image>` 当前的 `type=` 直接 1:1 映射 Unity `Image.Type`（`simple` / `sliced` / `tiled` / `filled`，见 `Runtime/Controls/Image.cs:53`），不写时按 sprite border 自动在 `sliced`/`simple` 间选（`OnAfterApply`）。这四个值都只管「sprite 像素怎么画进 rect」，**没有任何一个跟纵横比适配相关**：

- `simple` 把 sprite 无脑拉伸铺满 rect → 变形；
- `preserveAspect` 没有暴露（只有 `<Icon>` 内部固定 `true`）。

作者常见需求「图片等比尽量铺满整个框」（CSS `object-fit: cover`）目前做不到。本设计给 `<Image>` 增加两个 fit 值，覆盖 contain / cover 两种纵横比适配。

## 2. 设计决策（已敲定）

1. **折叠进现有 `type=`**，不新增 `fit=` 属性。新增两个值 `contain` / `cover`。理由：这 6 个值在实际使用里互斥（没人会要 `sliced+cover`），作者只选一个，单属性单值最好理解。`type=` 的语义从「Unity 枚举透传」升级成「图片渲染模式」抽象。
2. **cover / contain 都相对父级（对称）**，用 Unity 内置 `AspectRatioFitter`：
   - `contain` → `AspectRatioFitter`，`aspectMode = FitInParent`（等比塞进父框，留白露出父框背景）；
   - `cover` → `AspectRatioFitter`，`aspectMode = EnvelopeParent`（等比撑满父框、溢出）。
   - 「框」= **直接父级的 rect**，由父级 size 决定，不是 Image 自己的 rect。
   - 之所以对称（contain 不走 `preserveAspect` 那条更省事的「贴自己 box」路线）：cover 在 Unity 下没有廉价的「贴自己 box」做法（`AspectRatioFitter` 只能相对父级），若 contain 贴自己 box、cover 贴父级，会造成**同一个 `type=` 里 contain 看自己尺寸、cover 看父级尺寸**的割裂，来回切是坑。统一相对父级、cover↔contain 一致。
3. **裁切由作者负责**，库不自动加 mask。`cover` 的溢出由作者在**父框**上加 `mask="rect"`（`RectMask2D`）裁掉。库只管 `type=` 落到 uGUI 怎么设。
4. **fit 模式下 Image 自己的 `anchor` / `size` / `width` / `height` / `margin` 被 `AspectRatioFitter` 接管（失效）**——`AspectRatioFitter` 的 `FitInParent`/`EnvelopeParent` 会强制 `anchorMin=0`、`anchorMax=1`、`anchoredPosition=0` 并驱动 `sizeDelta`。框由父级决定。
5. **fit 模式不进变体**（见 §6）。

### 推荐写法

```xml
<Frame size="320x180" mask="rect">          <!-- 框 + 裁切都在父级，由作者决定 -->
  <Image type="cover" sprite="ui:banner"/>  <!-- 不写 anchor/size，父级即框 -->
</Frame>
```

## 3. uGUI 映射（6 个 type 值 + 默认）

| `type=`    | `_img.type`                                   | `preserveAspect` | `AspectRatioFitter`        |
| ---------- | --------------------------------------------- | ---------------- | -------------------------- |
| (不写)     | 自动：有 border→`Sliced` 否则 `Simple`（`OnAfterApply`） | `false`          | 无（不创建）               |
| `simple`   | `Simple`                                      | `false`          | 若已存在则 `enabled=false`  |
| `sliced`   | `Sliced`                                      | `false`          | 若已存在则 `enabled=false`  |
| `tiled`    | `Tiled`                                       | `false`          | 若已存在则 `enabled=false`  |
| `filled`   | `Filled`                                      | `false`          | 若已存在则 `enabled=false`  |
| `contain`  | `Simple`                                      | `false`          | `enabled=true`, `FitInParent`   |
| `cover`    | `Simple`                                      | `false`          | `enabled=true`, `EnvelopeParent` |

- fit 模式强制 `_img.type=Simple`（9-slice border 对 contain/cover 无意义，且 `Simple` 才能把 sprite 完整画进 ARF 算好的 rect）。
- 不用 `preserveAspect`：ARF 已把 rect 调成 sprite 比例，`Simple` 画进去无形变，`preserveAspect` 多余。
- fit 模式仍 `_typeExplicit=true`，跳过 `OnAfterApply` 的自动 sliced/simple 判定。

## 4. `AspectRatioFitter` 生命周期

- **懒创建**：`private AspectRatioFitter _arf;`，仅在第一次进入 `contain`/`cover` 时 `AddComponent`，之后靠 `enabled` 开关复用（绝不 `Destroy`）。
- **`aspectRatio` 在 `OnAfterApply` 设**：因为 `Sprite` setter 与 `Type` setter 同在一个属性循环里、顺序不保证，`aspectRatio` 必须在所有 setter 跑完后、用最终 sprite 算：

  ```csharp
  internal override void OnAfterApply()
  {
      if (_arf != null && _arf.enabled && _img.sprite != null)
      {
          var r = _img.sprite.rect;
          _arf.aspectRatio = r.width / r.height;   // 自动跟随 sprite 变化（含 variant 换图）
      }
      if (_typeExplicit) return;
      // ……既有自动 sliced/simple 判定不变
  }
  ```

- **sprite 为空**：fit 模式但 `sprite==null` 时不更新 `aspectRatio`（ARF 退化为默认比例 1）；文档提示 cover/contain 需配 sprite。
- **ReSolve 幂等**：基础值 `type="cover"` 时每次 ReSolve 都重跑 `Type` setter（`EnsureArf` + `enabled=true` + 设 mode），幂等无副作用；`OnAfterApply` 重算 `aspectRatio`。`AspectRatioFitter` 是 `ILayoutSelfController`，父级 resize 时自动重排，无需手动监听。

## 5. 裁切

库不介入。`cover` 溢出靠作者在父框上 `mask="rect"`。Image 自身 `mask="rect"` 对 cover 溢出无效（它 mask 的是自己被撑大的 rect），属作者误用，本期不额外拦截（保持范围聚焦）。

## 6. 变体限制 — `PUI-IMAGE-FIT-VARIANT`

**根因**：`ControlAttributeApplier.Apply` 里 `var v = VariantResolver.ResolveAttribute(...); if (v == null) continue;`——一个属性若**只有变体覆盖、没有基础值**，变体关掉时解算成 `null`，setter **根本不被调用**。于是 `type.mobile="cover"`（无基础 `type`）在 mobile 开时挂了 ARF，mobile 关时 setter 不跑 → ARF 拆不掉，图卡在 cover。这与现有 `mask=` 用 `PUI-MASK-VARIANT` 禁变体是同一类「AddComponent/Destroy 无法随变体回退」问题。

**规则**（仿 `PUI-MASK-VARIANT`，warning，运行期 + CLI 共用一份实现）：

- 触发：`<Image>` 的某个 `type.<variant>` 覆盖值 ∈ {`cover`, `contain`}。
- 不触发：基础 `type="cover"`（稳定，每次 ReSolve 都重应用，正常）；`type.<variant>` 是 `simple`/`sliced`/`tiled`/`filled`（纯枚举，无组件）。
- 刻意采用与 mask 一致的「钝」判定（只看变体值是不是 fit 模式，不分析有没有基础值）：保持规则简单、信息一致——「fit 模式 v1 不支持变体」。
- 文案：fit 模式（cover/contain）切换需挂/拆 `AspectRatioFitter`，v1 不支持放进变体；需要按朝向/设备切 fit 就拆成两个 Screen 或用 `<Add into=...>`。

## 7. 几何属性失效警告 — `PUI-IMAGE-FIT-GEOMETRY`

fit 模式下 Image 自己的几何属性被 ARF 接管、失效（§2.4），作者写了多半是误解。

**规则**（仿 `PUI-MARGIN-INERT-SIDE`，warning，**CLI-only**——纯作者期静态检查，运行期零成本，不从 `ScreenInstantiator` 派发）：

- 触发：`<Image>` 基础 `type` ∈ {`cover`, `contain`} **且**节点带 `anchor` / `size` / `width` / `height` / `margin` 任一（基础或变体形态）。
- **不**含 `pivot`（ARF 不改 pivot，且 stretch 锚下 pivot 影响可忽略，避免误报）。
- 文案：`type="cover"/"contain"` 下框由**父级**决定，`<Image>` 自身的 anchor/size/width/height/margin 会被 `AspectRatioFitter` 接管、无效；把尺寸写到父级容器上。

## 8. 边界 / 非目标

- **LayoutGroup 直接子级**：fit 模式的 Image 直接放进 `<VStack>`/`<HStack>`/`<Grid>` 时，`AspectRatioFitter` 与 LayoutGroup 抢布局，行为未定义。本期**不加 lint**（YAGNI，保持范围），仅在 XML SKILL 文档提示「fit 模式请套一层 `<Frame>`，别直接做 LayoutGroup 子级」。
- **own-box cover**（在 Image 自己 rect 内 render-crop，不靠父级）：非目标——Unity 无廉价做法，需自定义 mesh/UV 渲染，超出「只管 type 怎么设」的范围。
- **径向 / cooldown**：仍不在 `<Image>` 范围（留给未来 `<Cooldown>`）。
- **`GetNativeSize`**：不变（仍返回 sprite 原生尺寸）；fit 模式下几何被 ARF 覆盖，native size 仅在异常/无父级场景兜底，无需特殊处理。
- **XSD**：`type` 是自由 `xs:string`（`XsdGenerator.cs:85`），加值不碰 XSD。

## 9. 测试（EditMode，TDD：先红后绿）

控件行为（`PromptUGUI.Tests.EditMode`）：

1. `type="contain"` → Image 上有 `AspectRatioFitter`、`enabled`、`aspectMode==FitInParent`、`_img.type==Simple`。
2. `type="cover"` → `aspectMode==EnvelopeParent`，其余同上。
3. `aspectRatio` 等于 sprite `rect.width/height`（用一张已知比例 sprite）。
4. fit→普通模式切换（同节点先 cover 后由基础 `type` 解算成 simple 的构造路径，或 mixed base+variant）：ARF `enabled` 关闭。
5. 不写 `type=` / 写 `simple` → 无 `AspectRatioFitter`。
6. sprite 为空 + fit → 不抛异常（aspectRatio 不更新）。

Lint（`PromptUGUI.Tests.EditMode`，仿现有 `MaskAttributeRules` / `MarginAnchorRules` 测试）：

7. `type.mobile="cover"` → 出 `PUI-IMAGE-FIT-VARIANT`；`type="cover"`（基础）不出。
8. `<Image type="cover" size="100x100">` → 出 `PUI-IMAGE-FIT-GEOMETRY`；`<Image type="cover">`（父级定框）不出；`<Image type="simple" size="100x100">` 不出；带 `pivot` 不出。

XSD 测试（`PromptUGUI.Tests.EditorOnly`）：不受影响（`type` 仍 `xs:string`），无需改。

## 10. 文档更新（同 PR）

- **主 spec** `2026-05-07-...-design.md`：`<Image>` 行 `type="sliced|simple|filled|tiled"` 扩为含 `contain`/`cover`，附「相对父级 + 裁切作者负责」一句。
- **XML SKILL** `.claude/skills/authoring-promptugui-xml/SKILL.md`（英文）：
  - `<Image>` 表格 `type` 取值加 `contain` / `cover` + 父级框语义 + 推荐 `<Frame mask="rect">` 包裹写法；
  - 两条新 lint 规则（`PUI-IMAGE-FIT-VARIANT` / `PUI-IMAGE-FIT-GEOMETRY`）入相应规则表；
  - LayoutGroup 直接子级的提示。
- C# / Addressables SKILL：无公开 C# API 变更，不动。
