# `<SafeArea>` 自身 margin 吸收 (max) v2 设计

**日期**：2026-05-26
**状态**：设计阶段（待 review，未进入实施）
**作用域**：让 `<SafeArea>` 接受自身 `margin` 属性，每条边解析成 `max(designMargin_i, deviceSafeAreaInset_i)`。SafeArea 自己缩进到合成 rect 内，所有子节点正常排版（不感知 inset）。
**依赖**：[`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5.5（SafeArea）、§6.2（anchor stretch）；[`2026-05-13-safearea-builtin-design.md`](2026-05-13-safearea-builtin-design.md)（SafeArea built-in v0 设计）

---

## 1. 背景与目标

### 1.1 问题

当前 `<SafeArea>` 把自己 anchor 写成 safe-area 分数、offsets=0，正好填满 safe area。如果作者还想给"距屏幕边的呼吸感" 6px，必须在子节点上加 `margin="6,6,_,_"`，结果在刘海设备上变成 `inset + 6 = 50px`，空白过大。

### 1.2 业内做法

- **CSS** `padding: max(16px, env(safe-area-inset-left))` — 设计 padding 是底线，safe area 触底取大不取和。
- **iOS SwiftUI** `.safeAreaPadding()` — 语义类似。
- **Android `WindowInsetsCompat.consume()`** — scope isolation：root 容器吸收掉，子元素不再看到。

主流是 `max()` 模型：设计 margin 是"我至少要这么多"，inset 是"实际可能比这更多"，不叠加。

### 1.3 目标

1. `<SafeArea>` 接受 `margin` 属性，含义是"SafeArea 自己跟父级（通常是 Screen）之间的 inset，要求至少这么多，但会被 device safe area 吸收"。
2. 子节点排版完全不感知 inset，SafeArea 缩进结果 = 子的 parent rect。
3. 不引入新 XML 属性、新 control、新 struct。改动局限在 SafeArea 自己 + parser 解禁。
4. 不动 `MarginResolver` / `Control.ApplyCommon` / `ControlAttributeApplier` / `Screen` 的代码或签名。

### 1.4 显式不做

- ❌ 子节点穿透吸收 — v1 已经放弃，v2 不重新引入。
- ❌ 后代 SafeArea 智能去重 — 嵌套行为 well-defined（doubly inset，因 inner 仍读 device 级 `Screen.safeArea`），SKILL 提示"一个 Screen 一个"。
- ❌ `safe-margin` / `margin.safe` 等新属性名 — 直接用 `margin` 跟其他控件一致的语法，文档说明吸收语义。
- ❌ "additive" 模式 / escape hatch attribute — 想要 "safe area + 16px 固定 padding"用嵌套 `<Frame margin="16,_,_,_"/>` 表达。

### 1.5 跟 v1 的关系

v1（experimental，分支 `feat/safearea-margin-absorb`）在子节点层做 max() 吸收，需要：`SafeAreaInsets` struct、`SafeAreaInsetsResolver`、`MarginResolver.Resolve` 加可选参、`Control.ApplyCommon` 加可选参、`ControlAttributeApplier` 每个 control apply 前查父级 tracker、`Screen.Open` 订阅 tracker → 全 Screen ReSolve。v2 把 max() 上移到 SafeArea 容器自己一层，全部废除。v1 分支保留为历史参考，不合并、不 cherry-pick。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| MA2-D1 | max() 计算位置 | SafeArea 容器自己一层 | 子不感知 inset；MarginResolver/ApplyCommon/ControlAttributeApplier 完全不动 |
| MA2-D2 | SafeArea 几何写法 | tracker 写 `anchorMin/Max=(0,0)/(1,1)` + `offsetMin/Max = max(designMargin_i, inset_i_designPx)` | 跟普通 stretched Frame 同形式；inset 表达完全在 offsets 里；polling 重新 blend 时不需要重新算 anchor |
| MA2-D3 | tracker 怎么知道 design margin | snapshot `RectTransform.offsetMin/Max`（在 `OnAfterApply` 里、即 `ApplyCommon` 刚写完之后），存进 `_designOffsetMin/Max` 字段 | 不解析 margin 字符串、不读 `[UIAttr]`；Variant ReSolve 路径自动支持（每次 ApplyCommon 重写纯 design offsets，OnAfterApply 重新 snapshot） |
| MA2-D4 | inset 单位换算 | `inset_design_px = inset_device_px / canvas.scaleFactor`，canvas 通过 `GetComponentInParent<Canvas>()` 获取 | scaleFactor 由 CanvasScaler 维护，是 device → design 的活动比例；非整数 inset 不影响渲染 |
| MA2-D5 | scaleFactor 找不到 | 1:1 换算 + `Debug.LogWarning` | 跟现有 tracker null-RectTransform 容错精神一致 |
| MA2-D6 | inset 变化触发 | tracker 自己 Update poll（已有）扩展对比项加 `_lastScaleFactor` | 不需要 v1 那种 `Screen.ReSolve` 路径；v2 inset 变化只影响 SafeArea 自己几何 |
| MA2-D7 | SafeArea 禁止属性集 | `anchor` / `size` / `width` / `height` / `pivot` 仍禁；**`margin` 解禁** | SA-D7 减一项；仍保留"SafeArea 形状固定"语义 |
| MA2-D8 | `margin` Variant override（`margin.var="..."`） | 自动支持 | 走现有 ControlAttributeApplier.Apply → ApplyCommon → OnAfterApply 链路，无特例代码 |
| MA2-D9 | `<SafeArea/>`（无 margin）行为 | `_designOffsetMin/Max = (0,0)/(0,0)`，max-blend 得 `(insetL, insetB, -insetR, -insetT)` → SafeArea 正好 fit safe area | 跟 v0 视觉等价；但 RectTransform 内部数值变了（v0 anchor=fraction+offset=0；v2 anchor=stretch+offset=inset） |
| MA2-D10 | 嵌套 SafeArea | 内层 doubly inset；不警告 | SKILL 已有"一个 Screen 一个"提示；运行时不强校验 |
| MA2-D11 | SafeArea 不在 Screen 根（如 Modal 注入） | `GetComponentInParent<Canvas>` 仍能找到 modal canvas；scaleFactor 取该 canvas | Modal 注入场景兼容 |
| MA2-D12 | 测试位置 | EditMode `Tests/EditMode/Controls/SafeAreaTests.cs` 重写 + 扩 margin 矩阵；PlayMode 保留一两条 end-to-end | 不另开文件；旧 anchor-fraction 断言改成 offset 断言 |
| MA2-D13 | tracker EditMode 测试注入 | 现有 `SafeAreaOverride : Func<Rect>` + `ScreenSizeOverride : Func<Vector2>` 保留；scaleFactor 通过测试时直接设 `Canvas.scaleFactor` 或加 `ScaleFactorOverride : Func<float>`（plan 时择简） | 跟现有注入风格一致 |
| MA2-D14 | SKILL.md 改写 | `authoring-promptugui-xml` 的 Safe area 章节完全重写，强调 max() + `<SafeArea margin>` 用法 + 旧"嵌套 Frame"模式留作"想要 fixed gap below safe area"的 escape hatch | CLAUDE.md 触发条件：XML 属性语义改 → 强制更新 |
| MA2-D15 | master spec §5.5 | 同 PR 更新，去掉对 v1 spec 的前向引用，加入 max() 语义说明 + 引用本文档 | 维持 "master spec 入口、详细设计在 dated doc" workflow |
| MA2-D16 | 已有 `.ui.xml` 迁移 | 仅 `UnityProjects~/PromptUGUIDev` 下游戏只有 Lobby 一个界面，作者手工迁移 | 不写自动 migration / lint codemod |

---

## 3. 改动面

### 3.1 `Runtime/Core/Parser/UIDocumentParser.cs:410`

把 `margin` 从 SafeArea 禁止集移除：

```csharp
foreach (var key in new[] { "anchor", "size", "width", "height", "pivot" })  // 删除 "margin"
{
    // 现有 ParseException 文案保持，但删掉末尾"To add inner padding, wrap content in <Frame...>"那句
    // （那是 v0 的引导，v2 直接用 SafeArea margin）
    ...
}
```

错误文案改成（v2 上下文）：

```
<SafeArea> does not accept attribute '{key}';
SafeArea is always stretched to its parent.
Use <SafeArea margin="..."> for inset (absorbed by device safe area).
```

### 3.2 `Runtime/Controls/SafeArea.cs`

```csharp
public sealed class SafeArea : Control
{
    private SafeAreaTracker _tracker;

    public override void OnAttached()
    {
        _tracker = GameObject.AddComponent<SafeAreaTracker>();
    }

    internal override void OnAfterApply()
    {
        if (_tracker == null) return;
        _tracker.CaptureDesignMargin(RectTransform);  // 新增：snapshot ApplyCommon 刚写的 offsets
        _tracker.Apply();                              // 用 snapshot + device inset 重 blend
    }
}
```

### 3.3 `Runtime/Controls/Internal/SafeAreaTracker.cs`

```csharp
[DisallowMultipleComponent]
internal sealed class SafeAreaTracker : MonoBehaviour
{
    internal static Func<Rect> SafeAreaOverride;
    internal static Func<Vector2> ScreenSizeOverride;
    // 测试可选注入；不注入则走真 canvas.scaleFactor
    internal static Func<float> ScaleFactorOverride;

    private RectTransform _rt;
    private Canvas _canvas;

    private Vector2 _designOffsetMin;  // 新：纯 design margin（由 SafeArea.OnAfterApply 写）
    private Vector2 _designOffsetMax;
    private bool _hasDesignMargin;     // 防 OnEnable 先于第一次 Apply 时用错值

    private Rect _lastSafe;
    private Vector2 _lastScreenSize;
    private float _lastScaleFactor;
    private bool _hasApplied;

    private void OnEnable()
    {
        _rt = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        Apply();
    }

    private void Update()
    {
        var safe = ResolveSafeArea();
        var screenSize = ResolveScreenSize();
        var sf = ResolveScaleFactor();

        if (!_hasApplied || safe != _lastSafe
            || screenSize != _lastScreenSize
            || !Mathf.Approximately(sf, _lastScaleFactor))
        {
            Apply();
        }
    }

    internal void CaptureDesignMargin(RectTransform rt)
    {
        _designOffsetMin = rt.offsetMin;
        _designOffsetMax = rt.offsetMax;
        _hasDesignMargin = true;
    }

    internal void Apply()
    {
        if (_rt == null) _rt = transform as RectTransform;
        if (_rt == null) return;

        var safe = ResolveSafeArea();
        var screenSize = ResolveScreenSize();
        if (screenSize.x <= 0f || screenSize.y <= 0f) return;
        var sf = ResolveScaleFactor();

        _lastSafe = safe;
        _lastScreenSize = screenSize;
        _lastScaleFactor = sf;
        _hasApplied = true;

        // device-px inset → design-px
        var insetL = safe.xMin / sf;
        var insetR = (screenSize.x - safe.xMax) / sf;
        var insetB = safe.yMin / sf;
        var insetT = (screenSize.y - safe.yMax) / sf;

        // design margin: offsetMin = (l, b), offsetMax = (-r, -t)
        // _designOffsetMin/Max 未初始化（OnEnable 先于 OnAfterApply）按 0 取
        var desL = _hasDesignMargin ? _designOffsetMin.x : 0f;
        var desB = _hasDesignMargin ? _designOffsetMin.y : 0f;
        var desR = _hasDesignMargin ? -_designOffsetMax.x : 0f;
        var desT = _hasDesignMargin ? -_designOffsetMax.y : 0f;

        var finL = Mathf.Max(desL, insetL);
        var finR = Mathf.Max(desR, insetR);
        var finB = Mathf.Max(desB, insetB);
        var finT = Mathf.Max(desT, insetT);

        _rt.anchorMin = new Vector2(0f, 0f);
        _rt.anchorMax = new Vector2(1f, 1f);
        _rt.offsetMin = new Vector2(finL, finB);
        _rt.offsetMax = new Vector2(-finR, -finT);
    }

    private Rect ResolveSafeArea() =>
        SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;

    private Vector2 ResolveScreenSize() =>
        ScreenSizeOverride != null
            ? ScreenSizeOverride()
            : new Vector2(Screen.width, Screen.height);

    private float ResolveScaleFactor()
    {
        if (ScaleFactorOverride != null) return ScaleFactorOverride();
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            Debug.LogWarning("[SafeAreaTracker] no Canvas in parent chain; using 1:1 device→design scale");
            return 1f;
        }
        return _canvas.scaleFactor;
    }
}
```

### 3.4 测试

见 §6。

### 3.5 SKILL.md

见 §7。

### 3.6 master spec §5.5

见 §8。

---

## 4. 触发与重算流

```
Screen 打开
  ScreenInstantiator 创建 SafeArea GameObject
    SafeArea.OnAttached → tracker = AddComponent<SafeAreaTracker>
      tracker.OnEnable → Apply（_hasDesignMargin=false → 走 0 design margin → SafeArea 正好 fit safe area）

  ControlAttributeApplier.Apply（SafeArea node）
    ApplyCommon(anchor=stretch, margin="6,6,6,6", ...)
      → 跟普通 stretched Frame 同代码路径，写 anchorMin/Max=(0,0)/(1,1), offsetMin=(6,6), offsetMax=(-6,-6)
    SafeArea.OnAfterApply
      → CaptureDesignMargin: _designOffsetMin=(6,6), _designOffsetMax=(-6,-6), _hasDesignMargin=true
      → tracker.Apply: 4 inset_design_px + max-blend → 写最终 offsets

  ControlAttributeApplier.Apply 子节点
    → 完全照常，子的 parent rect = SafeArea 最终 rect

steady state.

Screen rotation / Device Simulator / 灵动岛动画
  tracker.Update poll → 检测 safe / screenSize / scaleFactor 变化 → Apply（_designOffsetMin/Max 沿用 snapshot）

Variant ReSolve
  ControlAttributeApplier.Apply 重跑 → ApplyCommon 重写纯 design offsets → OnAfterApply 重 snapshot → tracker.Apply 重 blend
```

无每帧轮询子树、无写入循环（tracker 写 anchor 不会触发 RectTransformDimensionsChanged 回写循环，因为 v2 不订阅 RectTransformDimensionsChanged，跟 v0 一致）、无线程问题。

---

## 5. 公开 API 表

| 状态 | 签名 / 行为 | 说明 |
|---|---|---|
| 新作者面 | `<SafeArea margin="...">` | 支持 1/2/4 分量 margin，跟其他控件 margin 语法一致；`_`=0 |
| 不变 | `<SafeArea>` 拒绝 `anchor`/`size`/`width`/`height`/`pivot`（MA2-D7） | 错误文案微调（删除"嵌套 Frame"引导） |
| 不变 | `SafeArea` 公开类型签名 | 仍 `public sealed class SafeArea : Control` |
| 内部 | `SafeAreaTracker.CaptureDesignMargin(RectTransform)` | OnAfterApply 调用入口；InternalsVisibleTo 范围 |
| 内部 | `SafeAreaTracker.DesignOffsetMin/Max`（测试可选用读断言）| 不暴露 setter |
| 新内部静态（可选）| `SafeAreaTracker.ScaleFactorOverride : Func<float>` | 测试注入 scaleFactor；不注入走真 canvas |
| 不变 | `MarginResolver.Resolve` 签名 / 行为 | v1 加的可选参不需要 |
| 不变 | `Control.ApplyCommon` 签名 / 行为 | v1 加的可选参不需要 |
| 不变 | `ControlAttributeApplier.Apply` | 不查父级 tracker、不传 inset |
| 不变 | `Screen.Open` / `Screen.ReSolve` | 不订阅 tracker 事件 |
| 视觉等价但内部 RectTransform 数值变 | `<SafeArea/>`（无 margin）的 anchor/offset | v0: anchor=safe-fraction, offset=0；v2: anchor=(0,0)/(1,1), offset=device-inset-design-px |

---

## 6. 测试矩阵

### 6.1 EditMode `Tests/EditMode/Controls/SafeAreaTests.cs`

**重写**（旧 anchor-fraction 断言全部废）+ 新增 margin 矩阵。

inset / margin 注入路径：`SafeAreaTracker.SafeAreaOverride` + `ScreenSizeOverride`；scaleFactor 由测试直接设 `canvas.scaleFactor` 或 `ScaleFactorOverride`（plan 阶段择简）。

| 用例 | screen | safe(device) | sf | SafeArea margin | 期望 offsetMin / offsetMax（design px） |
|---|---|---|---|---|---|
| PC，无 margin | 1920×1080 | 0..1920, 0..1080 | 1 | _ | (0,0) / (0,0) |
| PC，margin=6 | 1920×1080 | 0..1920, 0..1080 | 1 | "6,6,6,6" | (6,6) / (-6,-6) |
| iPhone 14 Pro 竖屏 | 1170×2532 | 0..1170, 132..2398 | 1 | _ | (0,132)（即 insetB=132）/ (0, -134)（insetT=134）|
| 同上，margin=6 | 同上 | 同上 | 1 | "6,6,6,6" | (6,132) / (-6,-134) |
| 同上，margin=50,_,_,_（仅顶部 50） | 同上 | 同上 | 1 | "50,_,_,_" | (0,132)（左右底无 margin）/ (0, -134)（top=max(50,134)）|
| 同上，margin=200,_,_,_（top design 设计胜出）| 同上 | 同上 | 1 | "200,_,_,_" | (0,132) / (0, -200) |
| Retina HiDPI 设备：device 1170×2532, safe (l=0, r=0, b=68 device px, t=134 device px), scaleFactor=2（design 585×1266）| 585×1266 design | device 0..1170, 68..2398 | 2 | "6,_,_,_" | (0, 68/2=34) / (0, -max(6, 134/2)=-67)（即 top design inset = 67、bottom 34、左右 0）|
| `<SafeArea anchor="stretch"/>` | — | — | — | — | ParseException |
| `<SafeArea margin="6"/>`（解禁后）| — | — | — | — | 不抛 |
| Variant override `margin.test="20"` | 1920×1080 | 0..1920, 0..1080 | 1 | base="6" → test 切换 → "20" | base ReSolve: (6,6)/(-6,-6)；切到 test：(20,20)/(-20,-20) |
| inset 运行时变化 | 1920×1080 | 旧 0..1920, 0..1080 → 新 0..1920, 100..1080 | 1 | "6" | poll 触发 Apply 二次执行，offsetMin.y=max(6,100)=100 |

### 6.2 PlayMode `Tests/PlayMode/Controls/SafeAreaTests.cs`

保留一两条 end-to-end：真 Canvas + LayoutRebuilder.ForceRebuildLayoutImmediate → 验证 SafeArea `rect.size` 缩小到期望值，子节点 rect 在 SafeArea 内。

### 6.3 兼容性回归

跑现有所有 EditMode + PlayMode 测试 → 全绿；`XsdGeneratorTests`：确认 `<SafeArea>` 的属性表里 `margin` 不再被排除（plan 阶段如发现 generator 仍硬编码排除，同步小改）。

### 6.4 Editor 手工验证

Device Simulator 切到 iPhone 14 Pro / Pixel 8：SafeArea 自动 inset 到 safe area 范围；加 margin 后视觉等于 max。

---

## 7. SKILL.md 同步

`.claude/skills/authoring-promptugui-xml/SKILL.md` 的 Safe area 章节完全重写：

````markdown
## Safe area

Wrap UI in `<SafeArea>` and put a `margin` on it to control inset:

```xml
<Screen name="Lobby">
  <Image anchor="stretch" color="#08152C"/>     <!-- bleed background, sibling of SafeArea -->
  <SafeArea margin="6,6,6,6">
    <HStack id="topIcons" anchor="top-stretch" height="24"
            margin="0,0,_,_" spacing="4" childAlign="middle-right">...</HStack>
  </SafeArea>
</Screen>
```

- Per-edge inset = `max(designMargin, deviceSafeAreaInset)`.
- PC (no inset): you get exactly the `margin` you wrote.
- Notched device: the safe-area inset absorbs the margin on edges where the inset is bigger; edges past it keep your design value (e.g. `margin="200,_,_,_"` on iPhone 14 Pro gives top=200, not 134).
- Unspecified edges (`_` or omitted from the 2/4-component form) default to 0 → that edge fully absorbs the device inset.

Other notes:
- `<SafeArea>` still rejects `anchor`/`size`/`width`/`height`/`pivot`. It's always stretched to its parent.
- One `<SafeArea>` per Screen. Backgrounds that need to bleed past the safe area stay as siblings, not children.
- For "fixed gap below the safe area" (e.g. always 16px below the notch, never flush), nest a `<Frame anchor="stretch" margin="16,_,_,_"/>` inside.
- Reacts automatically to screen rotation, Device Simulator, and Dynamic Island animations.
````

---

## 8. master spec §5.5 同步

去掉对"safearea-margin-absorb v2 semantics"的前向引用（那是 v1 分支留下的占位指针），直接展开 max() 语义：

````markdown
### 5.5 `<SafeArea>`（安全区容器）

显式安全区包裹层；运行时每条边 `inset = max(designMargin_i, Screen.safeArea_i)`，自动响应屏幕旋转 / Device Simulator / Dynamic Island。完整设计见 [`2026-05-26-safearea-margin-absorb-v2-design.md`](2026-05-26-safearea-margin-absorb-v2-design.md)。

- `margin` 属性：`<SafeArea margin="6,6,6,6">` 表示"至少 6px 距父级边"，被 device inset 吸收（取大）。
- 拒绝 `anchor`/`size`/`width`/`height`/`pivot`。
- 典型用法：作为 `<Screen>` 直接子节点；要 bleed 的背景图作为 SafeArea 的兄弟节点。
````

---

## 9. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 已有 `.ui.xml` 中 SafeArea 子节点的 margin 行为变化（v0 是 inset+margin，v2 因为 SafeArea 不再 fit safe area，子节点的 margin 不再被 inset 抬高，但 SafeArea 自己的 0 margin 仍 fit safe area，所以子节点视觉位置**不变**）| 实际无视觉 breaking，但作者如果之前刻意写"利用 SafeArea fit 后再加 margin 叠加"的间距，迁移时要把那个 margin 上移到 SafeArea 自己 | MA2-D16：作者手工 audit；本仓库目前只有 Lobby 一个 .ui.xml |
| `_designOffsetMin/Max` 与 `Apply()` 时序：OnEnable 先于第一次 OnAfterApply 调用 Apply 时 `_hasDesignMargin=false` → 用 0 design margin | 第一次 Apply 跟"v0 + 0 margin" 视觉等价，无错位；OnAfterApply 跑完再 Apply 一次拿到正确值 | 同帧内（Screen.Open 同帧 instantiate + apply）肉眼不可见 |
| Canvas.scaleFactor 不准（CanvasScaler 在 LateUpdate 才更新）| 旋转瞬间 scaleFactor 滞后一帧，导致 inset 换算偏差 | Update poll 下一帧捕捉到 scaleFactor 变化，触发二次 Apply；整体旋转动画期间 layout 都在重排，视觉不可见 |
| 嵌套 SafeArea：内层 doubly inset | 实际视觉是 inset×2，明显偏离作者预期 | SKILL.md 提示"一个 Screen 一个"；MA2-D10 不强校验 |
| 用户在子节点上仍写 `margin="6,6,_,_"` 期望被 SafeArea 吸收 | v2 不会吸收（吸收语义只在 SafeArea 自己上）→ 实际效果就是 SafeArea inset + 6，跟 v0 一样 | SKILL 更新强调"吸收发生在 SafeArea 自身的 margin 上" |
| `SafeAreaTracker.ScaleFactorOverride` 引入测试钩 | 增加内部静态可变量 | plan 阶段决定是否真的需要——简单做法：测试时直接设 `Canvas.scaleFactor = ...`（属性可写），跳过 Override |

---

## 10. 实施顺序（plan 时细化）

1. EditMode red 测先写一条：`<SafeArea margin="6"/>` + inset(top=44 device, sf=1) → 期望 offsetMax.y = -44
2. 修 parser 把 `margin` 从 SafeArea 禁止集移除（红测对应一条：parse `<SafeArea margin>` 不再抛）
3. SafeAreaTracker：加 `_designOffsetMin/Max` + `CaptureDesignMargin` + Apply 改写
4. SafeArea.OnAfterApply 调 `CaptureDesignMargin` 后再 `Apply`
5. EditMode 测试矩阵 §6.1 全套补完 → 全绿
6. 老 anchor-fraction 断言改成 offset 断言
7. PlayMode 测试 §6.2 → 全绿
8. SKILL.md 改写 Safe area 章节
9. master spec §5.5 改写
10. host Unity 项目 Device Simulator 切到 notch 设备目视验证

---

## 11. 开放问题

| 问题 | 处置 |
|---|---|
| ScaleFactorOverride 是否引入 | plan 阶段决；若测试直接设 `Canvas.scaleFactor` 能跑通则不引入 |
| `XsdGeneratorTests` 是否需要改 | 如 generator 硬编码"SafeArea 不出 margin"，plan 阶段同步小改；如 generator 反射驱动则自动跟上 |
| `_designOffsetMin/Max` 是否需要在 SafeArea 被 deactivate 后清零 | 否：tracker 跟 SafeArea 共生命周期，deactivate 时不卸 tracker；下次 activate 由 OnAfterApply 重新 capture |
| 是否在 SafeAreaTracker.Apply 里加 `Mathf.Round` 让 offset 取整 | 否（MA2-D5 / v1 MA-D5 决策一致）：layout 没视觉伪影 |
