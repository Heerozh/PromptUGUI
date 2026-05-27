# `<Progress>` 控件设计

**日期**: 2026-05-27
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. 新增 `Runtime/Controls/Progress.cs`（程序化建子节点，复用 `ProceduralBuilders`）
2. `Runtime/Application/BuiltinPrimitives.cs` 注册 `Progress`
3. 新增 `Runtime/Core/Lint/ProgressAttributeRules.cs`（与 `ScreenInstantiator` 共享）
4. `authoring-promptugui-xml` SKILL.md 新增 `<Progress>` 行 + 一节用例
5. `scripting-promptugui-csharp` SKILL.md 加上 `Get<Progress>().Value = ...` 的提示
6. 主 spec `2026-05-07-promptugui-description-language-design.md` §5（控件表）追加行

**依赖**: 无（独立扩展，复用 `UI.ResolveSprite` / `ProceduralBuilders` / Image auto-Sliced 等已有机制）

---

## 1. 背景

当前作者要做进度条只能用现有原语手糊：

```xml
<Frame>
  <Image sprite="ui:track"/>              <!-- 底 -->
  <Image sprite="ui:mask" mask="self">    <!-- 形状裁剪 -->
    <Image sprite="ui:fill" anchor="stretch" margin="..."/>  <!-- 填充 -->
  </Image>
  <Image sprite="ui:frame"/>              <!-- 边框 -->
</Frame>
```

问题：

- 4 层手写，padding / anchor / margin 容易写错。
- 进度变化要 author 自己改 fill 的 `width` 或 `margin`，不直观。
- `Image.Type.Filled` 截断渲染模式作者基本不知道怎么搭。
- 一旦想换"mask 不设时 mask sprite 兼任可见底"这种轻量场景，又得改结构。

需要一个**显示型**控件 `<Progress>`，把"边框 + 形状遮罩 + 底/轨道 + 填充 + 填充模式 + 进度值"打包成一行 XML，attr 命名跟现有 `<Image>` / `<Slider>` 词汇对齐。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| PB-D1 | 控件类型 | 显示型（只读），不暴露 `Observable` / `OnValueChanged` | Progress 是 view，不接 input；C# 侧直接 `Get<Progress>().Value = x` 或 `Bind` 数据源即可 |
| PB-D2 | 值模型 | 归一化 `[0..1]`，无 `min`/`max`/`wholeNumbers` | Progress 不是 Slider；调用方自行归一化更简单。Clamp 到 [0,1]，超界静默裁剪 |
| PB-D3 | `bg` vs `back` 命名 | `bg` + `bgColor` | `fill`/`fillColor` 已成对；`bg`/`bgColor` 闭环。Slider 内部字段 `_bg` 同名。CSS / Tailwind 通用词汇 |
| PB-D4 | `scale` vs `fill` 术语 | `mode="scale"` = 改 Fill 的 RectTransform 拉伸；`mode="fill"` = `UnityImage.Type.Filled` 截断渲染 | 跟 Unity Image 行为绑死；作者一看就知道是 RectTransform 动还是 Image fillAmount 动 |
| PB-D5 | 方向词汇 | `direction="horizontal\|vertical\|reverse-horizontal\|reverse-vertical"` | 跟现有 `Slider.direction` 完全一致（`Runtime/Controls/Slider.cs:100`），author 心智复用 |
| PB-D6 | radial 模式 | **不在 v1**，未来开 `<Cooldown>` 控件单独做 | radial 需要 `origin` (90/180/360) + `clockwise`，attr 表会膨胀；冷却环的语义跟"线性进度"也分得开 |
| PB-D7 | 层级始终统一（固定结构） | `Progress → [MaskWrapper → [Bg, Fill], Frame]` 永远 4 个 RT | 避免"mask 设没设导致 Fill 父节点不同"的结构分支；`Mask` / `UnityImage` 组件按需 `AddComponent`，但 RT 树形不变 |
| PB-D8 | mask 没设、bg 没设 | MaskWrapper 不挂 `Mask` / `UnityImage`，仅作 stretch wrapper | 零开销；语义上"没 mask 没底"就是空 |
| PB-D9 | mask 设了、bg 没设 | MaskWrapper.Mask.showMaskGraphic = `true`，mask sprite 兼任可见底（用户提出的优化路径） | 一个 sprite 干两件事，最常见的"圆角胶囊条 + 单色 fill"零冗余 |
| PB-D10 | mask 设了、bg 设了 | MaskWrapper.Mask.showMaskGraphic = `false`；Bg 作为 MaskWrapper 第一个子节点（被裁剪到 mask 形状） | bg 跟 fill 同处 mask 内部，共享形状；mask sprite 当 stencil 不可见 |
| PB-D11 | mask 没设、bg 设了 | Bg 仍是 MaskWrapper 第一个子节点（MaskWrapper 此时是透明 wrapper） | 结构一致；bg 占满整个 Progress rect 时就是普通矩形底 |
| PB-D12 | Progress 是否接 XML 子元素 | 否（leaf control） | 跟 Slider / Btn 一致；Progress 内部 4 个图层全部程序化建 |
| PB-D13 | `value` 变化的代价 | 只动 Fill 的 anchor (scale 模式) 或 `UnityImage.fillAmount` (fill 模式)，不重建 GO，不改 Bg/Frame/Mask | 跟 Variant 规则一致；可被 R3 高频 `Bind` |
| PB-D14 | 模式/方向变化代价 | OnAfterApply 一次性 reconcile：算出目标 (anchor, image.type, fillMethod, fillOrigin, fillAmount) 写入 Fill | setter 顺序无关；mode + direction + value 任意 setter 跑过都触发同一次 reconcile |
| PB-D15 | sprite 9-slice 自动 Sliced | Bg / Fill / Frame / Mask 四个 UnityImage 都跑 Image 现有"sprite.border != 0 → Type.Sliced"逻辑 | 复用 `Image.OnAfterApply` 的思路；fill 模式下 fill 这层例外（强制 Filled） |
| PB-D16 | Frame 层的 raycast | `raycastTarget = false` | 装饰层，不能挡 Progress 父级或同级控件的事件 |
| PB-D17 | `GetNativeSize()` | frame sprite 设了 → frame.nativeSize；否则 bg 设了 → bg.nativeSize；否则 `(160, 16)` | 跟 `Image.GetNativeSize` 同样按 `sprite.rect / pixelsPerUnit`；细长条是最合理默认 |
| PB-D18 | variant override 范围 | 允许 override `value` / `fill` / `fillColor` / `bg` / `bgColor` / `frame` / `mode` / `direction`；**禁止** override `mask` | mask 切换需要 `AddComponent`/`Destroy`（同 FIM-D8 逻辑）；其他 attr 都是值写入，安全。lint 阻断 mask 出现在 Variant |
| PB-D19 | clamp 行为 | value < 0 → 0；value > 1 → 1；NaN → 0 | 显示控件，宁可显示边界值也不让 fillAmount/anchor 进入未定义区间 |
| PB-D20 | lint 规则地点 | `Runtime/Core/Lint/ProgressAttributeRules.cs`，跟 `MaskAttributeRules` / `LayoutGroupChildRules` 同模式 | Single source of truth：CLI exit code + runtime `Debug.LogWarning` 共享 |

---

## 3. 属性表

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `value` | `[0..1]` 浮点 | `0` | 进度；超界 clamp。改它只动 Fill |
| `fill` | sprite key | (none) | 填充层 sprite；走 `UI.ResolveSprite` |
| `fillColor` | `#rrggbb` / `#rrggbbaa` / 命名色 | white | 填充层 tint |
| `bg` | sprite key | (none) | 底/轨道 sprite |
| `bgColor` | 颜色 | white | 底色 tint；`bg` 没设但 `bgColor` 设了 → MaskWrapper 子节点画纯色矩形当底 |
| `frame` | sprite key | (none) | 装饰边框 sprite；画在最上层，`raycastTarget=false` |
| `mask` | sprite key | (none) | 形状遮罩 sprite，挂 `UnityEngine.UI.Mask` 做 stencil 裁剪 |
| `mode` | `scale` \| `fill` | `scale` | `scale` = 改 Fill RectTransform 拉伸；`fill` = `UnityImage.Type.Filled` 截断渲染 |
| `direction` | `horizontal` \| `vertical` \| `reverse-horizontal` \| `reverse-vertical` | `horizontal` | 进度增长方向（决定 anchor 原点 / fillOrigin） |

约束：
- 不接受 XML 子元素（PB-D12）；写了会被 `ScreenInstantiator` 当 unknown control 忽略 + 一条 warning。
- `mask` 不能出现在 `Variant` 覆盖里（PB-D18，lint error）。

---

## 4. 六个典型用例

```xml
<!-- 1. 最简：纯色 bg + 单色 fill；scale 横向 -->
<Progress value="0.6" bgColor="#222" fillColor="#3cf"/>

<!-- 2. 单 sprite 填充；scale 横向 -->
<Progress value="0.6" fill="ui:bar_red"/>

<!-- 3. 圆角胶囊：mask sprite 兼当底 (PB-D9) -->
<Progress value="0.4" mask="ui:pill" fill="ui:bar_blue"/>

<!-- 4. 全套装饰：frame + mask + bg + fill -->
<Progress value="0.6" frame="ui:gold_border" mask="ui:pill" bg="ui:track" fill="ui:bar_red"/>

<!-- 5. Unity Image.Type.Filled, 反向纵向（液体从顶部往下空） -->
<Progress value="0.3" fill="ui:liquid" mode="fill" direction="reverse-vertical"/>

<!-- 6. 在 Variant 中切换 value / colors (frame / bg / fill sprite 允许；mask 完全禁止 — PUI-PROG-MASK-VARIANT) -->
<Progress id="hp"
          value="1.0" value.low="0.2"
          fill="ui:bar" fillColor.low="#f44"
          bgColor="#000"/>
```

---

## 5. C# 侧用法

```csharp
var p = screen.Get<Progress>("hp");
p.Value = 0.42f;                              // 直接赋值
healthStream.Subscribe(v => p.Value = v);     // R3 推送
```

`Progress.Value` 是 `public float` getter+setter（与 `Slider.Value` 镜像）。无 `OnValueChanged`（PB-D1）。`[Bind]` 是用于把 child control 字段注入到 parent 的（参见 `Runtime/Registry/BindAttribute.cs`），不是数据流绑定 —— Progress 这种基础数值控件直接用 `Get<Progress>("id").Value = v` 即可。

---

## 6. 程序化层级（固定）

```
Progress (RectTransform, 无 Graphic)
├── MaskWrapper (RectTransform; 按需挂 UnityImage + UI.Mask)
│   ├── Bg   (RectTransform + UnityImage; 按需 SetActive)
│   └── Fill (RectTransform + UnityImage; 永远存在)
└── Frame (RectTransform + UnityImage; 按需 SetActive, raycastTarget=false)
```

组件按 `mask` / `bg` / `bgColor` / `frame` 是否设值条件性 `AddComponent`：

| 条件 | MaskWrapper.UnityImage | MaskWrapper.Mask | MaskWrapper.showMaskGraphic | Bg.SetActive | Frame.SetActive |
|---|---|---|---|---|---|
| 无 mask、无 bg/bgColor | 不挂 | 不挂 | — | false | (按 frame) |
| 无 mask、有 bg/bgColor | 不挂 | 不挂 | — | true | (按 frame) |
| 有 mask、无 bg/bgColor | 挂（sprite=mask） | 挂 | true | false | (按 frame) |
| 有 mask、有 bg/bgColor | 挂（sprite=mask） | 挂 | false | true | (按 frame) |

Frame 行：`frame` 设了 → SetActive(true) + sprite/Sliced；没设 → SetActive(false)。

---

## 7. 行为细节

### 7.1 `mode="scale"` (默认)

Fill 的 RT 根据 value + direction 设 anchorMin/anchorMax：

| direction | anchorMin | anchorMax |
|---|---|---|
| `horizontal` (forward) | `(0, 0)` | `(value, 1)` |
| `reverse-horizontal` | `(1-value, 0)` | `(1, 1)` |
| `vertical` | `(0, 0)` | `(1, value)` |
| `reverse-vertical` | `(0, 1-value)` | `(1, 1)` |

`offsetMin = offsetMax = Vector2.zero`。`UnityImage.type` 走 sprite border 自动 Sliced/Simple（与 `Image.OnAfterApply` 同逻辑）。

### 7.2 `mode="fill"`

Fill 的 RT 满铺 `(0,0)..(1,1)`；UnityImage 配置：

| direction | type | fillMethod | fillOrigin |
|---|---|---|---|
| `horizontal` | Filled | Horizontal | Left (0) |
| `reverse-horizontal` | Filled | Horizontal | Right (1) |
| `vertical` | Filled | Vertical | Bottom (0) |
| `reverse-vertical` | Filled | Vertical | Top (1) |

`fillAmount = value`. 切换 `mode` 时反转 type 回 Simple/Sliced (按 sprite border) + 重置 RT 到 value 模式。

### 7.3 reconcile 时机

`value` / `fill` / `mode` / `direction` 任一 setter 跑过 → 在 `OnAfterApply` 里跑一次 `ReconcileFill()`，集中处理上面表 7.1 / 7.2 的写入。setter 顺序无关。

---

## 8. Lint 规则

`Runtime/Core/Lint/ProgressAttributeRules.cs`；`IRWalker.WalkNode` 增 `node.Tag == "Progress"` 分支；`ScreenInstantiator.InstantiateRecursive` 同源 `Debug.LogWarning`。

| Code | 触发条件 | 信息（节选） | 级别 |
|---|---|---|---|
| `PUI-PROG-VALUE-RANGE` | `value` 字面量解析出 < 0 或 > 1 | "Progress.value 期望 [0..1]，当前 '{v}' 会被 clamp。如果是动态绑定可忽略；字面量请改成合法范围。" | warning |
| `PUI-PROG-MODE` | `mode` 不在 `scale` / `fill` | "Progress.mode 合法值: scale, fill。" | error |
| `PUI-PROG-DIRECTION` | `direction` 不在四值集合 | "Progress.direction 合法值: horizontal, vertical, reverse-horizontal, reverse-vertical。" | error |
| `PUI-PROG-CHILDREN` | `<Progress>` 包含子元素 | "Progress 是 leaf 控件，不接受子元素。把装饰图层用 frame / mask / bg / fill 属性表达。" | error |
| `PUI-PROG-MASK-VARIANT` | `mask` 出现在 `Variant` 覆盖里 | "Progress.mask 不支持 variant override（涉及 AddComponent/Destroy）。把 mask 固定在主声明；其他 attr 可以在 variant 中改。" | error |
| `PUI-PROG-NO-FILL` | `fill` / `fillColor` 都没设 且 `value` 有值 | "Progress 设了 value 但没设 fill 也没设 fillColor，看不到任何填充。" | warning |

runtime 一律 `Debug.LogWarning`（跟 `LayoutGroupChildRules` 同步），不抛异常；CLI `UIXmlLint` 用规则级别决定 exit code。

---

## 9. 实现要点

### 9.1 `Runtime/Controls/Progress.cs`（新文件）

骨架（详细 reconcile 逻辑放 plan 里）：

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnityMask = UnityEngine.UI.Mask;

namespace PromptUGUI.Controls
{
    // Progress 是显示型线性进度条 (horizontal / vertical, scale / Image.Type.Filled).
    // Radial fill (cooldown ring) 不在范围; 未来需要时新增 <Cooldown> 控件, 不要扩这里. (PB-D6)
    public sealed class Progress : Control
    {
        private UnityImage _bg;          // 可能为 null (无 bg 且无 bgColor)
        private UnityImage _maskGraphic; // 可能为 null (无 mask)
        private UnityMask _stencilMask;  // 可能为 null
        private UnityImage _fill;        // 永远非 null
        private UnityImage _frame;       // 可能为 null

        private float _value;
        private string _mode = "scale";
        private string _direction = "horizontal";
        private bool _hasBgColor;
        private bool _fillTypeExplicit; // 跟 Image 一样

        public override void OnAttached()
        {
            // MaskWrapper
            var maskRt = ProceduralBuilders.AddChild(RectTransform, "MaskWrapper");
            // Bg (lazy via setter; 总是先创建禁用)
            var bgRt = ProceduralBuilders.AddChild(maskRt, "Bg");
            bgRt.gameObject.SetActive(false);
            _bg = bgRt.gameObject.AddComponent<UnityImage>();
            _bg.raycastTarget = false;
            // Fill (永远存在)
            _fill = ProceduralBuilders.AddImage(maskRt, "Fill", raycast: false);
            // Frame (lazy via setter; 总是先创建禁用)
            var frameRt = ProceduralBuilders.AddChild(RectTransform, "Frame");
            frameRt.gameObject.SetActive(false);
            _frame = frameRt.gameObject.AddComponent<UnityImage>();
            _frame.raycastTarget = false;
        }

        [UIAttr, Preserve] public float Value { get => _value; set => _value = Mathf.Clamp01(value); }
        [UIAttr, Preserve] public string Mode      { set => _mode = value; }
        [UIAttr, Preserve] public string Direction { set => _direction = value; }

        [UIAttr, Preserve]
        public string Fill { set { _fill.sprite = UI.ResolveSprite(value); } }
        [UIAttr, Preserve]
        public string FillColor { set { if (ColorUtility.TryParseHtmlString(value, out var c)) _fill.color = c; } }

        [UIAttr, Preserve]
        public string Bg
        {
            set
            {
                _bg.sprite = UI.ResolveSprite(value);
                _bg.gameObject.SetActive(true);
            }
        }
        [UIAttr, Preserve]
        public string BgColor
        {
            set
            {
                if (!ColorUtility.TryParseHtmlString(value, out var c)) return;
                _bg.color = c;
                _hasBgColor = true;
                _bg.gameObject.SetActive(true);
            }
        }

        [UIAttr, Preserve]
        public string Frame
        {
            set
            {
                _frame.sprite = UI.ResolveSprite(value);
                _frame.gameObject.SetActive(true);
            }
        }

        [UIAttr, Preserve]
        public string Mask
        {
            set
            {
                if (_maskGraphic == null)
                {
                    var maskRt = (RectTransform)_fill.transform.parent;
                    _maskGraphic = maskRt.gameObject.AddComponent<UnityImage>();
                    _maskGraphic.raycastTarget = false;
                    _stencilMask = maskRt.gameObject.AddComponent<UnityMask>();
                }
                _maskGraphic.sprite = UI.ResolveSprite(value);
                _stencilMask.showMaskGraphic = !_bg.gameObject.activeSelf;
            }
        }

        internal override void OnAfterApply()
        {
            // bg/frame/mask 各自 sprite border 自动 Sliced
            AutoSlice(_bg);
            AutoSlice(_frame);
            AutoSlice(_maskGraphic);
            // 如果 bg 在 Mask 设值之后才打开, 同步 showMaskGraphic
            if (_stencilMask != null) _stencilMask.showMaskGraphic = !_bg.gameObject.activeSelf;
            ReconcileFill();
        }

        private void ReconcileFill()
        {
            var rt = _fill.rectTransform;
            if (_mode == "fill")
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                _fill.type = UnityImage.Type.Filled;
                (_fill.fillMethod, _fill.fillOrigin) = _direction switch
                {
                    "horizontal" => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Left),
                    "reverse-horizontal" => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Right),
                    "vertical" => (UnityImage.FillMethod.Vertical, (int)UnityImage.OriginVertical.Bottom),
                    "reverse-vertical" => (UnityImage.FillMethod.Vertical, (int)UnityImage.OriginVertical.Top),
                    _ => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Left),
                };
                _fill.fillAmount = _value;
            }
            else // scale (默认)
            {
                // 回到 Simple/Sliced (按 sprite border)
                _fill.type = (_fill.sprite != null && _fill.sprite.border != Vector4.zero)
                    ? UnityImage.Type.Sliced
                    : UnityImage.Type.Simple;
                _fill.fillAmount = 1f;
                (rt.anchorMin, rt.anchorMax) = _direction switch
                {
                    "horizontal" => (new Vector2(0f, 0f), new Vector2(_value, 1f)),
                    "reverse-horizontal" => (new Vector2(1f - _value, 0f), new Vector2(1f, 1f)),
                    "vertical" => (new Vector2(0f, 0f), new Vector2(1f, _value)),
                    "reverse-vertical" => (new Vector2(0f, 1f - _value), new Vector2(1f, 1f)),
                    _ => (new Vector2(0f, 0f), new Vector2(_value, 1f)),
                };
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
        }

        private static void AutoSlice(UnityImage img)
        {
            if (img == null || img.sprite == null) return;
            img.type = img.sprite.border != Vector4.zero ? UnityImage.Type.Sliced : UnityImage.Type.Simple;
        }

        public override Vector2? GetNativeSize()
        {
            if (_frame != null && _frame.sprite != null) return Native(_frame);
            if (_bg != null && _bg.sprite != null) return Native(_bg);
            return new Vector2(160f, 16f);
        }

        private static Vector2 Native(UnityImage img)
        {
            var ppu = img.pixelsPerUnit;
            return new Vector2(img.sprite.rect.width / ppu, img.sprite.rect.height / ppu);
        }
    }
}
```

> 注：上面 `OnAfterApply` 用 `internal override` —— 跟 `Image.OnAfterApply` 同签名（`Runtime/Controls/Image.cs:111`）。`ControlAttributeApplier` 在所有 setter 跑完后调一次。

### 9.2 `Runtime/Application/BuiltinPrimitives.cs`

```csharp
reg.Register<Progress>("Progress", null);  // 跟 Slider 一行同位置
```

### 9.3 `Runtime/Core/Lint/ProgressAttributeRules.cs`（新文件）

跟 `MaskAttributeRules` 同模式：static class，几个 `Check*` 方法返回 `IEnumerable<LintIssue>`。

```csharp
public static class ProgressAttributeRules
{
    public const string ValueRangeCode    = "PUI-PROG-VALUE-RANGE";
    public const string ModeCode          = "PUI-PROG-MODE";
    public const string DirectionCode     = "PUI-PROG-DIRECTION";
    public const string ChildrenCode      = "PUI-PROG-CHILDREN";
    public const string MaskVariantCode   = "PUI-PROG-MASK-VARIANT";
    public const string NoFillCode        = "PUI-PROG-NO-FILL";

    public static IEnumerable<LintIssue> CheckProgress(ElementNode n) { /* see plan */ }
}
```

### 9.4 `Runtime/Core/Lint/IRWalker.cs` 改动

`WalkNode` 入口 self-check 追加：

```csharp
else if (node.Tag == "Progress")
    foreach (var issue in ProgressAttributeRules.CheckProgress(node))
        yield return issue;
```

### 9.5 `Runtime/Application/ScreenInstantiator.cs` 改动

self-check 块同步追加 `Progress` 分支（与 `Image` / `Frame` 同位置）。

---

## 10. 跟现有 spec / SKILL 的整合点

### 10.1 主 spec `2026-05-07-promptugui-description-language-design.md`

§5（控件表）追加一行：

> `<Progress>` | 线性进度条 (scale / Image.Type.Filled, horizontal / vertical, +可选 frame / mask / bg 装饰) | RectTransform（+ 内部 4 个图层；详见 [`2026-05-27-progress-control-design.md`](2026-05-27-progress-control-design.md)）

### 10.2 `authoring-promptugui-xml/SKILL.md`

1. Built-in primitives 表追加 `<Progress>` 行（attrs：`value`, `fill`, `fillColor`, `bg`, `bgColor`, `frame`, `mask`, `mode`, `direction`）。
2. 新增 "Progress" 小节，含 6 个用例（§4）+ lint codes 列表（§8）+ "mask 配 bg 的四种组合表"（§6）。
3. Quick reference 末尾加一行：
   > `<Progress value="0.6" fill="ui:bar"/>` 最简；`mask=` + 不设 `bg` → mask sprite 自动可见兼当底；radial 进度环不在 `<Progress>` 范围。

### 10.3 `scripting-promptugui-csharp/SKILL.md`

R3 / `Get<T>` 段加一句：
> `screen.Get<Progress>("hp").Value = 0.42f;` — Progress 是只读显示控件，无 `OnValueChanged`；用 `Bind`-属性或直接 setter 推值。

---

## 11. Out of Scope

- **Radial fill (cooldown 环)** —— 未来 `<Cooldown>` 控件（PB-D6）。
- **Value tween / 动画过渡** —— 用现有 `<Animation>` 控件或调用方自己缓动；Progress 本身只写当前值。
- **`min` / `max` / `wholeNumbers`** —— Progress 不是 Slider（PB-D2）；要离散段数自己量化。
- **多段进度条 (segmented bar)** —— 暂用多个 `<Progress>` 横排或 Variant 模拟。
- **soft / alpha mask** —— 跟 FIM 同 out-of-scope；stencil Mask 足够。
- **Variant 切换 mask 模式** —— PB-D18 / PB-D20 阻断。

---

## 12. 风险与回滚

| 风险 | 缓解 |
|---|---|
| `mode="scale"` ↔ `mode="fill"` 切换后 fillAmount / RT 残值导致首帧错位 | `ReconcileFill()` 永远把两边都写一遍（scale 模式重置 fillAmount=1，fill 模式重置 RT 到满铺）；OnAfterApply 每次都跑 |
| 反射 setter 顺序：`Mask` 先跑、`Bg` 后跑 → `showMaskGraphic` 计算错 | `OnAfterApply` 末尾再写一次 `_stencilMask.showMaskGraphic = !_bg.gameObject.activeSelf` |
| Variant override `mask=` 绕过 lint | runtime `Mask` setter 用 `??=`（同 FIM-D9），不重建组件；只更新 sprite |
| `direction` 拼写错（如 `"reverse_horizontal"` 下划线） | lint error `PUI-PROG-DIRECTION`；runtime 落入 default 分支按 horizontal 兜底 |
| `value` 高频赋值（每帧）→ 每次 ReconcileFill 跑完整 switch | switch 极轻；fillAmount / anchor 是直接字段赋值，UI rebatch 只在 RT 改时触发，跟 Slider 相同量级 |
| Frame 层 raycast 漏关 → 挡掉父级点击 | `OnAttached` 时直接 `raycastTarget = false`（PB-D16），无 attr 暴露不让 author 改 |
| `<Progress>` 节点写了子元素被静默忽略 | lint error `PUI-PROG-CHILDREN`；ScreenInstantiator 已有"未知子节点 warning"路径 |
| XSD 不自动更新 | 跟所有新 `[UIAttr]` 一样手动 `Tools → PromptUGUI → Schema → Generate XSD`；SKILL.md 已说明 |
