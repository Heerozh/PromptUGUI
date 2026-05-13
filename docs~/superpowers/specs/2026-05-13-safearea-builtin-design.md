# `<SafeArea>` 内置控件设计

**日期**：2026-05-13
**状态**：设计阶段（待 review，未进入实施）
**作用域**：新增 `<SafeArea>` 内置控件，作为显式安全区容器；运行时按 `Screen.safeArea` 计算 anchor 分数，自动响应屏幕旋转 / Device Simulator 切换 / Variant ReSolve。背景图等需要 bleed 到屏幕边缘的元素继续作为 SafeArea 的兄弟节点放在 `<Screen>` 根下。
**依赖**：[`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5（内置原语）、§6.2（anchor stretch 结构约束）；`Runtime/Application/RectDimensionsRelay.cs`（已有的 RectTransform dimensions 事件桥）

---

## 1. 背景与目标

iOS notch / 灵动岛 / 安卓打孔屏 / 手势条 / Android 系统状态栏在不同设备和屏幕方向下产生不规则的 unsafe inset。Unity 的 `UnityEngine.Screen.safeArea` 已经把这些差异收敛成单一 `Rect`（设备坐标，像素单位），但 PromptUGUI 当前**没有任何**封装让 XML 作者声明"这个容器要待在安全区内"。

调研结果：
- 仓库内 grep `safeArea` / `SafeArea` 零命中。
- `Runtime/Application/RectDimensionsRelay.cs` 已经把 `OnRectTransformDimensionsChange` magic method 接成 C# event，`Screen.RectTransformDimensionsChanged` 在 `Screen.Open`（Runtime/Application/Screen.cs:100）里被订阅出去。屏幕旋转 / 窗口缩放 / Device Simulator 切换都会触发它。
- 14 个现存 built-in 控件全部继承自 `Control`（非 MonoBehaviour），通过 `BuiltinPrimitives.Register` 在 `UI.ResetForTests` / `UI.Initialize` 时统一注册。`Frame.cs` 是无视觉、无 `[UIAttr]` 的最小样板，是 SafeArea 的最佳参考。

### 触发场景

```xml
<Screen name="Login">
  <Image id="bg" anchor="stretch" color="#0B1828"/>
  <HStack id="brandBar" anchor="top-left" width="320" height="56" margin="24,_,_,24">
    ...
  </HStack>
</Screen>
```

期望：背景 bleed 到屏幕物理边缘；`brandBar` 距 notch / 状态栏底沿 24px。
实际：`brandBar` 的 24px 是相对 canvas 顶部、不是相对 safe area 顶部，notch 设备上会被刘海吃掉。

### 目标

1. 新增 `<SafeArea>` 内置控件，把"安全区"做成显式 XML 容器，组合性优于"Screen 默认安全区+背景 opt-out"。
2. 运行时使用 anchor-fraction 方案（Unity 官方推荐路径）：`anchorMin/Max` = safeArea 在 `Screen.width/height` 上的占比，`offsetMin/Max = 0`。这是 resolution-independent 的写法，跟 `CanvasScaler` 配合不需要换算。
3. 自动响应三类变化：
   - 屏幕旋转 / 窗口缩放 / Device Simulator → `OnRectTransformDimensionsChange` magic method 触发重算
   - Variant `ReSolve` → 在 `ControlAttributeApplier.Apply` 之后自动补一次
   - GameObject `OnEnable` → 进入显示状态时计算一次
4. 不引入任何作者可调参数。`<SafeArea>` 的所有几何行为完全由 `Screen.safeArea` 决定，无 `edges=` 之类的精细控制。
5. 解析期强制锁定 `<SafeArea>` 不接受 layout 类属性，从语义层避免歧义。

### 显式不做

- ❌ `edges="top,bottom"` 之类的"只对特定边 inset"参数（`Screen.safeArea` 在无 notch 的边自动为 0，已经隐含正确行为；加 attr 是徒增复杂度）
- ❌ 在 `<Screen>` 上做"默认安全区"开关（背景 bleed 是更高频的需求，opt-in 容器更直观且组合性好）
- ❌ 允许 `<SafeArea margin="...">`（保持单一职责：要 padding 用 `<Frame>` 嵌套一层就行）
- ❌ Editor-only 的 fake safe area 注入机制（Unity 6 Device Simulator 已经会重写 `Screen.safeArea`，是 Editor 内验布局的标准路径）
- ❌ 自定义"非全屏"SafeArea（譬如 `<SafeArea anchor="bottom-stretch">` 只在底部 inset 手势条）；嵌套 `<Frame>` 可表达同等意图
- ❌ 把 SafeArea 做成 layout group（它不是布局算法，只是一层 RectTransform 重整）

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| SA-D1 | 作为 built-in 控件（vs 作为属性 `safeArea="true"` on any control） | built-in `<SafeArea>` | 跟 `<Frame>` 同形式，组合性强；不污染所有控件的属性表；XSD 生成器（M4）自动包含新 tag |
| SA-D2 | inset 算法 | anchor-fraction：`anchorMin/Max = safeArea / Vector2(Screen.width, Screen.height)`，`offsetMin/Max = 0` | Unity 官方 SafeArea 示例的路径；resolution / CanvasScaler 无关 |
| SA-D3 | 应用时机 | (a) `OnEnable`、(b) `OnRectTransformDimensionsChange` magic method、(c) `Control.OnAfterApply` 钩子 | 三类变化分别走三个触发点；语义清晰、避免轮询 |
| SA-D4 | 内部组件挂载方式 | 在 SafeArea.OnAttached 里 `AddComponent<SafeAreaTracker>()`，tracker 自己持有 RectTransform 引用 | tracker = MonoBehaviour，承担 Unity magic method；Control 子类保持非 MonoBehaviour 路线 |
| SA-D5 | `Control.OnAfterApply` 钩子 | **新增** internal virtual hook，在 `ControlAttributeApplier.Apply` 末尾调一次；默认实现为空 | Variant ReSolve 会重写 `anchorMin/Max`，tracker 必须在 ApplyCommon 之后补一次；不加钩子就只能在 LateUpdate 轮询，更脏 |
| SA-D6 | 测试可注入 | `SafeAreaTracker` 暴露 `internal static Func<Rect> SafeAreaOverride`（默认 null → 走 `Screen.safeArea`） | `Screen.safeArea` 是 static get-only，测试无法直接 mock；提供静态注入点是最小代价的可测设计 |
| SA-D7 | 禁止属性集 | `anchor`、`size`、`width`、`height`、`margin`、`pivot` 出现在 `<SafeArea>` 上 → ParseException | 跟 §6.2 "stretch 上不允许 size" 同精神：避免作者写出会被运行时无视的属性，让违反语义的事在解析期就炸 |
| SA-D8 | 允许属性集 | `id`、`hidden`、`interactable`、`if=` | 跟其他 built-in 完全对齐 |
| SA-D9 | 嵌套 SafeArea 处理 | 不报错也不警告 | 第二层 SafeArea 的 `Screen.safeArea` 计算结果跟第一层一致 → 第二层的 anchorMin/Max=(0,0)/(1,1)，等价于一个 stretch Frame；行为是"无害冗余"。SKILL.md 提示"通常一个 Screen 只放一个"就够了 |
| SA-D10 | parent 不是 `<Screen>` 根时怎么办 | 仍然按 `Screen.safeArea / Screen.width|height` 计算 anchor fraction | SafeArea 的 anchor 是相对 parent 的，但 `Screen.safeArea` 是设备级别的全屏比例。当 SafeArea 不在 Screen 根、parent 自己被 inset 过时，行为可能不直观——这在 v1 是预期外用法。SKILL.md 提示"SafeArea 期望直接放在 `<Screen>` 根下"，不做运行时校验（成本高、收益小） |
| SA-D11 | 注册位置 | `BuiltinPrimitives.Register` 加一行 `reg.Register<SafeArea>("SafeArea", null);` | 跟其他 built-in 同路径 |
| SA-D12 | 文件位置 | `Runtime/Controls/SafeArea.cs`（Control 子类）+ `Runtime/Controls/Internal/SafeAreaTracker.cs`（MonoBehaviour） | tracker 放 `Runtime/Controls/Internal/`（目录已存在，跟其他 internal helpers 同处） |
| SA-D13 | Tracker 取得 parent canvas 尺寸 | 不需要——anchor-fraction 算法只依赖 `Screen.width/Screen.height`（设备像素），跟 canvas reference resolution 无关 | CanvasScaler 把 `Screen.width` 像素 → canvas 单位的换算对 anchor 比例不影响（比例不变量） |
| SA-D14 | SafeArea 是否作为 LayoutGroup 子节点工作 | 不需特殊处理 | LayoutGroup 接管子节点 anchor → ApplyCommon 走 LayoutElement 分支，SafeArea 在 LayoutGroup 子节点位置时 anchor-fraction 写入会被 layout pass 覆盖。SKILL 提示"SafeArea 不要放进 VStack/HStack/Grid"，运行时不强制校验（同 SA-D10 理由） |
| SA-D15 | 测试位置 | EditMode：`Tests/EditMode/Controls/SafeAreaTests.cs`（parse-time + structure + 静态 override 注入数值断言）；PlayMode：`Tests/PlayMode/Controls/SafeAreaTests.cs`（真 Canvas + Variant ReSolve 路径 + RectTransformDimensionsChange 触发） | 跟其他 built-in 测试位置对齐 |
| SA-D16 | XSD 生成器同步 | M4 的 XSD 生成器是反射驱动 → 新增控件自动出现在生成的 schema 里；不需要额外改动 | 顺路验证一遍：跑 M4 `XsdGeneratorTests`，确认 `SafeArea` 在生成结果里 |
| SA-D17 | SKILL.md 更新范围 | 新增 "Safe area" 小节：XML 模板 + 限制属性集 + 嵌套提示 + "通常放在 `<Screen>` 根下" | CLAUDE.md 触发条件：新增 XML 元素 → 强制 SKILL 更新 |
| SA-D18 | spec §5 / §6 是否同 PR 改 | 同 PR 在 master spec §5 末尾追加一小段 `<SafeArea>` 描述，引用本设计文档 | 维持"master spec 是入口、详细设计在 dated doc"的 workflow |

---

## 3. 改动面

### 3.1 新文件 `Runtime/Controls/SafeArea.cs`

```csharp
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public sealed class SafeArea : Control
    {
        private SafeAreaTracker _tracker;

        public override void OnAttached()
        {
            _tracker = GameObject.AddComponent<SafeAreaTracker>();
        }

        internal override void OnAfterApply()
        {
            // ApplyCommon 在变体 ReSolve 时会重写 anchorMin/Max；让 tracker 立刻补一次
            if (_tracker != null) _tracker.Apply();
        }
    }
}
```

### 3.2 新文件 `Runtime/Controls/Internal/SafeAreaTracker.cs`

```csharp
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    [DisallowMultipleComponent]
    internal sealed class SafeAreaTracker : MonoBehaviour
    {
        // Tests inject a custom provider; default = real device safe area
        internal static System.Func<Rect> SafeAreaOverride;

        private RectTransform _rt;

        private void OnEnable()
        {
            _rt = (RectTransform)transform;
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_rt == null) return;
            Apply();
        }

        internal void Apply()
        {
            var safe = SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);
            if (screenSize.x <= 0f || screenSize.y <= 0f) return;

            var aMin = safe.position;
            var aMax = safe.position + safe.size;
            aMin.x /= screenSize.x; aMin.y /= screenSize.y;
            aMax.x /= screenSize.x; aMax.y /= screenSize.y;

            _rt.anchorMin = aMin;
            _rt.anchorMax = aMax;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
```

### 3.3 `Control.cs` 新增 internal virtual `OnAfterApply`

```csharp
// 现有 OnAttached 下方添加：
internal virtual void OnAfterApply() { }
```

零开销默认实现；其他 13 个 built-in 不需要重写。

### 3.4 `ControlAttributeApplier.Apply` 末尾调用 `OnAfterApply`

在现有 `control.ApplyCommon(...)` 调用之后追加一行：

```csharp
control.OnAfterApply();
```

（位置在 ControlAttributeApplier.cs:65 当前 ApplyCommon 调用语句之后）

### 3.5 `BuiltinPrimitives.cs` 注册

`Runtime/Application/BuiltinPrimitives.cs:7-24` 的列表中追加：

```csharp
reg.Register<SafeArea>("SafeArea", null);
```

放在 `Frame` 之后，作为另一个"无视觉容器"。

### 3.6 `UIDocumentParser.cs` 解析期校验

在 `ParseElement`（line 229-317）中，识别 `tag == "SafeArea"` 时遍历 attributes，命中禁止集（`anchor` / `size` / `width` / `height` / `margin` / `pivot`）即抛：

```
ParseException: <SafeArea> does not accept attribute '{attr}';
SafeArea is always stretched to Screen.safeArea. To add inner padding,
wrap the content in a <Frame margin="..."/> inside the SafeArea.
(line {n}, col {m})
```

实现上跟 §6.2 "stretch axis 上不允许 size" 的校验放同一处（或新建一个 `SafeAreaValidator` static helper，被 `ParseElement` 调用）。

### 3.7 `Tests/EditMode/Controls/SafeAreaTests.cs` 新文件

见 §6。

### 3.8 `Tests/PlayMode/Controls/SafeAreaTests.cs` 新文件

见 §6。

### 3.9 `.claude/skills/authoring-promptugui-xml/SKILL.md`

新增小节，见 §7。

### 3.10 `2026-05-07-promptugui-description-language-design.md` §5 末尾

追加一段（约 5 行）说明 `<SafeArea>` 存在 + 引用本 design 文档。

---

## 4. 触发与重算流

```
Author writes <SafeArea> in XML
        │
        ▼
Parser validates禁止属性集 (SA-D7)
        │
        ▼
ScreenInstantiator creates GameObject + AttachTo
        │
        ├─ OnAttached → tracker = AddComponent<SafeAreaTracker>
        │      └─ tracker.OnEnable → tracker.Apply (1st write)
        │
        ▼
ControlAttributeApplier.Apply
        ├─ ApplyCommon writes anchor=stretch / sizeDelta=0 (the standard path)
        └─ OnAfterApply → tracker.Apply (overrides anchor with safe-area fractions)
        │
        ▼
Screen.Open completes. Steady state.

Variant change:
        VariantStore.Changed → Screen.ReSolve
            → ControlAttributeApplier.Apply on every node
                → ApplyCommon overwrites anchor (back to stretch)
                → OnAfterApply → tracker.Apply (re-applies safe area fractions)

Screen rotation / window resize / Device Simulator switch:
        Unity fires OnRectTransformDimensionsChange on tracker
            → tracker.Apply (idempotent: writes same values if unchanged)
```

无轮询、无每帧 LateUpdate、无线程问题。

---

## 5. 公开 API 表

| 状态 | 签名 / 行为 | 说明 |
|---|---|---|
| 新内置控件 | `<SafeArea id="..." hidden="..." interactable="..." if="..."/>` | 可包含任意子节点；作者面 |
| 新 C# 类 | `public sealed class PromptUGUI.Controls.SafeArea : Control` | 作者可通过 `screen.Get<SafeArea>("id")` 取到 |
| 新 internal | `SafeAreaTracker` 在 `PromptUGUI.Controls.Internal` 命名空间 | 不暴露 |
| 新 internal | `Control.OnAfterApply()` virtual | InternalsVisibleTo 范围内可见；其他 control 默认实现为空 |
| 新 internal 静态 | `SafeAreaTracker.SafeAreaOverride : Func<Rect>` | 仅用于测试 mock；非测试代码不应触碰 |
| 新解析期错误 | `<SafeArea>` 上出现禁止属性 → `ParseException` | 错误文案要可读 |
| 不变 | 其他所有控件 / Variant / Template / Hot reload / XSD 生成器外部行为 | XSD 自动收录新 tag（反射驱动），但生成结果不需要手动改 |

---

## 6. 测试矩阵

### EditMode (`Tests/EditMode/Controls/SafeAreaTests.cs`)

| 用例 | 期望 |
|---|---|
| `<SafeArea/>` 实例化 | GameObject 上挂着 `SafeAreaTracker`；`screen.Get<SafeArea>("id")` 取得回 |
| `<SafeArea anchor="stretch"/>` | `ParseException`，message 含 `"anchor"` |
| `<SafeArea size="100x100"/>` | `ParseException`，message 含 `"size"` |
| `<SafeArea width="100"/>` | `ParseException`，message 含 `"width"` |
| `<SafeArea height="100"/>` | `ParseException`，message 含 `"height"` |
| `<SafeArea margin="10"/>` | `ParseException`，message 含 `"margin"` |
| `<SafeArea pivot="0.5,0.5"/>` | `ParseException`，message 含 `"pivot"` |
| 注入 `SafeAreaOverride = () => new Rect(0, 100, 1080, 1820)`, `Screen.width=1080, Screen.height=1920` | tracker.Apply 后 RectTransform: anchorMin=(0, 0.0521)、anchorMax=(1, 1)（容差 0.001） |
| 注入 `SafeAreaOverride = () => 全屏 rect` | anchorMin=(0,0), anchorMax=(1,1) |
| Variant `ReSolve` 后 anchor 仍是 safe area 分数 | 切 variant → 触发 ReSolve → 再读 tracker.RectTransform 仍是 safe area 分数（OnAfterApply 重新补上） |
| `<SafeArea hidden="true">` | `GameObject.activeSelf == false`；tracker 不触发（OnEnable 不跑） |
| 子节点继承 SafeArea rect | SafeArea 内放 `<Frame anchor="stretch"/>`，layout pass 后 frame rect == SafeArea rect |

### PlayMode (`Tests/PlayMode/Controls/SafeAreaTests.cs`)

| 用例 | 期望 |
|---|---|
| 真 Canvas + 注入 mock safe area，手动触发 `LayoutRebuilder.ForceRebuildLayoutImmediate` | SafeArea 子节点的 `rect.size` 缩小至 safe area 区域 |
| `Screen.RectTransformDimensionsChanged` 触发后 tracker 重新 Apply | 改 mock provider 返回值 → 触发 dimension change → 新值生效 |
| SafeArea 在 Variant `Add` 块里、变体切换时 instantiation + tracker 正常工作 | 第一次激活时 tracker 计算正确；deactivate 后 tracker 不报错；再激活后还是正确 |

### Editor 验证（手工）

打开 Device Simulator → 切到 iPhone 14 Pro / iPad / Pixel → SafeArea 内容自动避开 notch / 状态栏 / 手势条。

### 兼容性回归

- 跑现有所有 EditMode + PlayMode 测试 → 全绿
- M4 `XsdGeneratorTests`：确认生成的 `.xsd` 包含 `<xs:element name="SafeArea">`，且属性列表里**没有** `anchor` / `size` / `width` / `height` / `margin` / `pivot`（生成器应当识别 SafeArea 的 attribute 限制，可能需要小改 generator；如果太烦琐，spec 上允许 generator 输出"完整通用 attribute 列表 + parser 在运行时把关"的折中方案）

> **Generator 改动权衡留到 plan 阶段**：如果改 generator 识别"per-tag 禁止属性"成本高，回退到"XSD 允许任意 attribute、parser 拒绝禁止集"。决策点放在 plan 评审。

---

## 7. SKILL.md 同步

在 `.claude/skills/authoring-promptugui-xml/SKILL.md` 中加新章节，位置紧跟 "Built-in primitives" 那节之后或单列一节 "Safe area"：

````markdown
## Safe area

Mobile devices have unsafe insets — notch, status bar, home indicator, gesture
bar. Wrap your UI in `<SafeArea>` to stay inside `Screen.safeArea`. Backgrounds
that should bleed to the device edges stay outside, as siblings of `<SafeArea>`:

```xml
<Screen name="Login">
  <Image id="bg" anchor="stretch" color="#0B1828"/>
  <SafeArea>
    <HStack id="brandBar" anchor="top-left" width="320" height="56" margin="24,_,_,24">
      ...
    </HStack>
  </SafeArea>
</Screen>
```

Rules:

- `<SafeArea>` is always stretched to the safe area; it does **not** accept
  `anchor`, `size`, `width`, `height`, `margin`, or `pivot`. Writing any of
  those is a parse error.
- To add inner padding inside the safe area, wrap content in
  `<Frame anchor="stretch" margin="..."/>` *inside* the `<SafeArea>`.
- Place `<SafeArea>` as a direct child of `<Screen>`. Nesting another
  `<SafeArea>` inside one is harmless but redundant (the inner one collapses
  to the outer one's rect).
- Don't put `<SafeArea>` inside `<VStack>` / `<HStack>` / `<Grid>` — the
  layout group will override its anchor math.
- Reacts automatically to screen rotation, window resize, and Unity 6's
  Device Simulator. No code-side wiring needed.
````

---

## 8. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 作者把 `<SafeArea>` 放在 LayoutGroup 子节点位置 | LayoutGroup 接管 anchor → tracker 写入被覆盖、行为静默错误 | SKILL.md 提示；后续 milestone 可考虑运行时 LogWarning |
| `OnRectTransformDimensionsChange` 重入 | tracker.Apply 写 RectTransform → 触发自己 → 再 Apply | Apply 是幂等的（同输入 → 同输出），第二轮写相同值不再触发；最坏 1 次冗余 |
| `Control.OnAfterApply` 是新公开钩子（即便 internal），未来扩展可能引入"per-control after-apply 顺序"问题 | 设计 surface 微膨胀 | v1 只有 SafeArea 用；约束"OnAfterApply 内不要修改其他控件"，类似 Control 内其他 hook 的契约 |
| `Screen.safeArea` 在 Editor 非 Device Simulator 模式下 = 全屏，tracker 计算结果 = (0,0)-(1,1) | EditMode 测试看不出错位（这是 design 而非 bug） | EditMode 测试用 `SafeAreaOverride` 注入；Editor 视觉验证靠 Device Simulator |
| Variant `Add` 块里第一次激活时 OnAttached 跑了，之后 deactivate 不卸 tracker；再激活时 tracker 已存在 | OnEnable 仍能触发 → 没问题 | `[DisallowMultipleComponent]` 防止重复挂；激活 / deactivate 都不动 tracker 生命周期 |
| XSD generator 不识别 SafeArea 的属性禁止集 | Editor 自动补全可能建议 `anchor=` 等被禁属性 | plan 阶段决定：要么改 generator 透传 per-tag deny list；要么 XSD 全宽放、靠 parser 兜底（见 §6 测试备注） |

---

## 9. 实施顺序（plan 时细化）

1. EditMode red 测：写一个 `<SafeArea anchor="stretch"/>` 期望 `ParseException`；同时一个 mock-injected 测期望 anchorMin/Max 计算正确
2. 新增 `Control.OnAfterApply` virtual + `ControlAttributeApplier` 末尾调用（含其他 control 不受影响的回归测）
3. 实现 `SafeArea.cs` + `SafeAreaTracker.cs`，注册到 `BuiltinPrimitives`
4. `UIDocumentParser` 加 SafeArea 属性校验逻辑（独立 helper / inline TBD by plan）
5. EditMode 全套用例补完 → 全绿
6. PlayMode 三条用例 → 全绿
7. SKILL.md 增加 "Safe area" 章节
8. master spec §5 末尾追加 `<SafeArea>` 引用本设计文档
9. host Unity 项目用 Device Simulator 切到 notch 设备目视验证

---

## 10. 开放问题

| 问题 | 处置 |
|---|---|
| 是否要让 `<SafeArea>` 接受 `id` 路径下的命名查询（`screen.Get<SafeArea>("path")` 配 ScopedIds）？ | 默认走 `Control` 基类的 `Id` + `ScopedIds`，无需特殊处理 |
| 是否需要给作者一个 `<UnsafeArea>` 配对元素，明确"我要 bleed"？ | 不加。bleed 是默认行为，加 `<UnsafeArea>` 是冗余语法 |
| 未来如果要做"只 inset 指定边" | 留作 v2；本 spec §1 显式不做 |
| 是否应同时为 `Screen.safeArea` 之外的"键盘弹出 inset"考虑？ | 不做。Android `SoftInputMode` / iOS keyboard inset 跟 safeArea 不是一个概念，未来单独立项 |
