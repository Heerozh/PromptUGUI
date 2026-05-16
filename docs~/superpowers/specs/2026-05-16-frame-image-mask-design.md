# Frame / Image mask 属性设计

**日期**: 2026-05-16
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `<Frame>` 新增 `mask` / `maskPadding` 属性 → `RectMask2D`
2. `<Image>` 新增 `mask` / `showMask` / `maskPadding` 属性 → `RectMask2D` 或 stencil `Mask`
3. 新增 `Runtime/Core/Lint/MaskAttributeRules.cs`（与 `ScreenInstantiator` runtime warning 共享），`IRWalker` 按 tag dispatch
4. 新增 `Runtime/Core/Layout/MaskPaddingParser.cs`（T,R,B,L → `Vector4(L,B,R,T)` 翻转）
5. `authoring-promptugui-xml` SKILL.md 更新 `<Frame>` / `<Image>` 行 + 新增 "Mask & clipping" 小节
6. 主 spec `2026-05-07-promptugui-description-language-design.md` §5.3 注脚

**依赖**: 无（独立扩展）

---

## 1. 背景

当前状态:

- `Frame.cs` 是纯空容器（只挂 `RectTransform`）。
- `Image.cs` 只有 `sprite` / `color` / `type` 三个属性。
- stencil `Mask` 和 `RectMask2D` 只在 `ScrollList` / `Dropdown` / `InputField` 内部由控件自身挂载（见 `ScrollList.cs:48`, `Dropdown.cs:76`, `InputField.cs:42`），作者从 XML 层完全感知不到。

作者无法手写"圆角卡片裁剪"、"viewport-style 裁剪区"、"卡片背景但允许角标溢出"这类常见需求 —— 想要圆角裁剪只能套 `<ScrollList>` 然后不用 scroll 功能，或者在 C# 里手动给 GameObject 挂组件。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| FIM-D1 | mask 默认行为 | 不开任何 mask（既不挂 `RectMask2D` 也不挂 `Mask`） | stencil mask 每个一次 SetPass call + 强制拆 batch，"隐式开销"绝不能是默认。RectMask2D 虽便宜但仍是行为变化，也得显式 opt-in |
| FIM-D2 | sprite 出现是否隐式触发 mask | 否 | "装饰背景，子元素故意溢出"（卡片 + 角标 / 阴影 / 徽章）是合法需求，sprite 不能强行裁剪子元素 |
| FIM-D3 | `<Frame>` 是否支持 stencil mask（`mask="self"`） | 否，只支持 `mask="rect"` | Frame 按定义无 Image graphic，stencil 需要 graphic source；硬要 stencil 用 `<Image mask="self">` 即可 |
| FIM-D4 | `<Image mask=>` 取值 | `rect` / `self` 两种 | `rect` = `RectMask2D`，Image 自身仍正常绘制；`self` = stencil `Mask`，Image 当 mask 源 |
| FIM-D5 | `showMask` 默认值 | `true`（mask graphic 默认可见） | 圆角卡片场景（最常见）希望 Image 显示；viewport-style 显式 `showMask="false"`。跟 Unity 默认 `Mask.showMaskGraphic = true` 一致 |
| FIM-D6 | `maskPadding` 语法 | `T,R,B,L` 1/2/4 分量 + `"_"` 占位，跟现有 `<VStack padding=>` 一致 | 已有约定，作者不用学第二套规则 |
| FIM-D7 | `maskPadding` 内部存储 | 解析时翻转成 `Vector4(L,B,R,T)`（Unity 原生顺序） | 作者层 CSS-顺序，运行时 Unity 顺序，解析器一次翻转 |
| FIM-D8 | mask 属性在 Variant 中 override | lint error（v1 禁止） | 切换 mask 模式涉及 `AddComponent` / `Destroy`，性能开销高且语义复杂；变体真要切建议直接拆两个 Screen 或 Add 块。可后续放宽 |
| FIM-D9 | mask 属性在 Variant 中 override + 用户绕过 lint | 运行时一次性创建组件，后续 setter 调用不重建/不销毁组件，最多更新组件 field（如 `showMaskGraphic` / `padding`） | 安全兜底；不引入"按需 add/remove 组件"的复杂状态机 |
| FIM-D10 | `<Image mask="self">` 但没 sprite | 不额外报错（uGUI Mask 在没 graphic 时静默不裁剪），lint 给 warning | 这是用户错误，但运行时无 crash；warning 帮用户定位 |
| FIM-D11 | `<Frame>` 是否也能加 `color` / `sprite` | 否（不在本次范围） | 严格保持 "Frame = 纯容器，Image = 视觉" 的职责分工；想要带背景的容器 = `<Image>` 套子元素 |
| FIM-D12 | mask 属性是否成为"通用属性" | 否，只 `<Frame>` / `<Image>` 暴露 | `<Text>` / `<Btn>` / `<Slider>` 等加 mask 没意义，扩大 API surface 无收益 |
| FIM-D13 | lint 规则地点 | `Runtime/Core/Lint/MaskAttributeRules.cs`，纯 C#，`IRWalker` + `ScreenInstantiator` 共享 | 跟 `LayoutGroupChildRules` 同样模式（CLAUDE.md "Single source of truth"） |
| FIM-D14 | `ControlAttributeApplier` 重排 | 不重排，setter 内部用 `_pendingMaskPadding` / `_pendingShowMask` 字段做"任意顺序到位" | 反射枚举属性顺序不保证；setter 互相独立、最终一致 |
| FIM-D15 | XSD 生成 | 自动（`ControlMeta` 反射 `[UIAttr]`，无需手改） | 现有机制 |
| FIM-D16 | spec / SKILL 同步 | 主 spec §5.3 注脚指向本文；XML SKILL `<Frame>` / `<Image>` 行 + 新增 "Mask & clipping" 小节 | CLAUDE.md: XML 属性增加必须更新 XML SKILL |

---

## 3. 属性表

### 3.1 `<Frame>`

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `mask` | `"rect"` | (none) | 加 `RectMask2D`，矩形裁剪所有子元素 |
| `maskPadding` | `"T,R,B,L"` 1/2/4 分量；`"_"` = 0 占位 | `"0"` | `RectMask2D.padding`，仅 `mask="rect"` 时生效 |

### 3.2 `<Image>`

新加（保留现有 `sprite` / `color` / `type`）:

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `mask` | `"rect"` / `"self"` | (none) | `rect`=加 `RectMask2D`（Image 仍正常绘制）；`self`=加 stencil `Mask`（Image 当 mask 源） |
| `showMask` | `"true"` / `"false"` | `"true"` | 仅 `mask="self"` 生效；写到 `Mask.showMaskGraphic` |
| `maskPadding` | 同 Frame | `"0"` | 同 Frame，仅 `mask="rect"` 生效 |

---

## 4. 五个典型用例

```xml
<!-- 1. 纯容器（无 mask）—— 现状不变 -->
<Frame />

<!-- 2. RectMask2D viewport: 右侧留 16px 给 scrollbar -->
<Frame mask="rect" maskPadding="0,16,0,0" />

<!-- 3. 装饰背景，子元素故意溢出（FIM-D2 想保住的场景） -->
<Image sprite="card-bg" color="#fff">
  <Image id="badge" sprite="badge" anchor="top-right" />
</Image>

<!-- 4. 圆角卡片 + 裁剪 -->
<Image sprite="round-card" color="#1a1a1a" mask="self">
  <Text>clipped content</Text>
</Image>

<!-- 5. mask ≠ 外框（嵌套两层 Image） -->
<Image sprite="card-border" color="#fff">
  <Image sprite="inner-mask" mask="self" showMask="false" margin="8,8,8,8">
    <Text>clipped to inner-mask shape</Text>
  </Image>
</Image>
```

---

## 5. Lint 规则

每条规则在 `Runtime/Core/Lint/MaskAttributeRules.cs`；`IRWalker.WalkNode` dispatch by `node.Tag`；`ScreenInstantiator.InstantiateRecursive` 同源 `Debug.LogWarning`。

| Code | 触发条件 | 信息（节选） |
|---|---|---|
| `PUI-MASK-FRAME-SELF` | `<Frame mask="self">` | "Frame 没有 Image graphic，无法做 stencil mask。改用 `<Image mask=\"self\">`，或者用 `<Frame mask=\"rect\">` 做矩形裁剪。" |
| `PUI-MASK-VALUE` | `mask=` 值不在合法集合 | "Frame 的 mask 合法值: rect。Image 的 mask 合法值: rect, self。" |
| `PUI-MASK-PADDING-NO-RECT` | 写了 `maskPadding=` 但 `mask` 不是 `"rect"` | "maskPadding 仅 mask=\"rect\" (RectMask2D) 时生效。stencil Mask 不支持 padding。" |
| `PUI-MASK-SHOWMASK-NO-SELF` | 写了 `showMask=` 但 `mask` 不是 `"self"` | "showMask 仅 mask=\"self\" (stencil Mask) 时生效。" |
| `PUI-MASK-VARIANT` | `mask` / `showMask` / `maskPadding` 出现在 `VariantOverrides` | "v1 不支持在 variant 间切换 mask 模式（涉及 AddComponent / Destroy）。固定一种 mask 配置；若需差异化裁剪行为，拆两个 Screen 或两个 Add 块。" |
| `PUI-MASK-SELF-NO-SPRITE` | `<Image mask="self">` 但同时没写 `sprite=` | "stencil Mask 没有 sprite 作 mask 源，将不裁剪任何元素。给 Image 加 sprite=，或者改用 mask=\"rect\"。" |

`PUI-MASK-VALUE` / `PUI-MASK-SELF-NO-SPRITE` 是 warning 级（runtime 仍能跑，结果可见）；其余是 error 级（在 UIXmlLint CLI exit code 1）。Runtime 一律 `Debug.LogWarning`（不抛异常，跟 `LayoutGroupChildRules` 同步）。

---

## 6. 实现要点

### 6.1 `Runtime/Core/Layout/MaskPaddingParser.cs`（新文件）

```csharp
using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Layout
{
    /// <summary>
    /// 解析作者层 "T,R,B,L" 1/2/4 分量字符串（"_" = 0 占位），翻转成 Unity 原生
    /// <see cref="UnityEngine.UI.RectMask2D.padding"/> 的 Vector4(L,B,R,T) 顺序。
    /// </summary>
    internal static class MaskPaddingParser
    {
        public static Vector4 Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return Vector4.zero;
            var parts = value.Split(',');
            float t, r, b, l;
            switch (parts.Length)
            {
                case 1:
                    t = r = b = l = ParseOne(parts[0]);
                    break;
                case 2:
                    t = b = ParseOne(parts[0]);
                    r = l = ParseOne(parts[1]);
                    break;
                case 4:
                    t = ParseOne(parts[0]);
                    r = ParseOne(parts[1]);
                    b = ParseOne(parts[2]);
                    l = ParseOne(parts[3]);
                    break;
                default:
                    throw new ArgumentException(
                        $"maskPadding: expected 1, 2, or 4 components, got {parts.Length} in '{value}'");
            }
            return new Vector4(l, b, r, t);
        }

        private static float ParseOne(string s)
        {
            s = s.Trim();
            if (s == "_") return 0f;
            return float.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
```

### 6.2 `Runtime/Controls/Frame.cs`

```csharp
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Frame : Control
    {
        private RectMask2D _rectMask;
        private string _pendingMaskPadding;

        [UIAttr]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                // 其他值: lint 已 error (PUI-MASK-VALUE / PUI-MASK-FRAME-SELF);
                // runtime 静默忽略 (D9 safety net)
            }
        }

        [UIAttr]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = MaskPaddingParser.Parse(value);
            }
        }
    }
}
```

注：`_pendingMaskPadding` 解决"setter 反射顺序不确定"（FIM-D14）—— `MaskPadding` 先于 `Mask` 跑时存起来，等 `Mask="rect"` 来时落盘；反过来 `Mask` 先跑也无所谓，`MaskPadding` 后跑会直接写 `_rectMask.padding`。

### 6.3 `Runtime/Controls/Image.cs`

保留现有 `Sprite` / `Color` / `Type` setter，新增三个：

```csharp
private RectMask2D _rectMask;
private UnityEngine.UI.Mask _stencilMask;
private string _pendingMaskPadding;
private bool? _pendingShowMask;

[UIAttr]
public string Mask
{
    set
    {
        if (value == "rect")
        {
            _rectMask ??= GameObject.AddComponent<RectMask2D>();
            if (!string.IsNullOrEmpty(_pendingMaskPadding))
                _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
        }
        else if (value == "self")
        {
            _stencilMask ??= GameObject.AddComponent<UnityEngine.UI.Mask>();
            if (_pendingShowMask.HasValue)
                _stencilMask.showMaskGraphic = _pendingShowMask.Value;
        }
    }
}

[UIAttr]
public string ShowMask
{
    set
    {
        if (string.IsNullOrEmpty(value)) return;
        _pendingShowMask = bool.Parse(value);
        if (_stencilMask != null)
            _stencilMask.showMaskGraphic = _pendingShowMask.Value;
    }
}

[UIAttr]
public string MaskPadding
{
    set
    {
        _pendingMaskPadding = value;
        if (_rectMask != null)
            _rectMask.padding = MaskPaddingParser.Parse(value);
    }
}
```

### 6.4 `Runtime/Core/Lint/MaskAttributeRules.cs`（新文件）

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for the mask attribute family on `<Frame>` and `<Image>`.
    /// Used by both <c>ScreenInstantiator</c> (runtime warnings) and <c>UIXmlLint</c> (CLI errors).
    /// </summary>
    public static class MaskAttributeRules
    {
        public const string FrameSelfCode       = "PUI-MASK-FRAME-SELF";
        public const string ValueCode           = "PUI-MASK-VALUE";
        public const string PaddingNoRectCode   = "PUI-MASK-PADDING-NO-RECT";
        public const string ShowMaskNoSelfCode  = "PUI-MASK-SHOWMASK-NO-SELF";
        public const string VariantCode         = "PUI-MASK-VARIANT";
        public const string SelfNoSpriteCode    = "PUI-MASK-SELF-NO-SPRITE";

        public static IEnumerable<LintIssue> CheckFrame(ElementNode n) { /* see plan task 2 for body */ }
        public static IEnumerable<LintIssue> CheckImage(ElementNode n) { /* see plan task 2 for body */ }
    }
}
```

### 6.5 `Runtime/Core/Lint/IRWalker.cs` 改动

```csharp
private static IEnumerable<LintIssue> WalkNode(ElementNode node)
{
    // Tag-specific self-checks (new)
    if (node.Tag == "Frame")
        foreach (var issue in MaskAttributeRules.CheckFrame(node))
            yield return issue;
    else if (node.Tag == "Image")
        foreach (var issue in MaskAttributeRules.CheckImage(node))
            yield return issue;

    var isLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
    foreach (var child in node.Children)
    {
        if (isLayoutGroup)
            foreach (var issue in LayoutGroupChildRules.CheckChild(child))
                yield return issue;
        foreach (var issue in WalkNode(child))
            yield return issue;
    }
}
```

注：当前 `WalkNode` 只检查 children 不检查自身（因为 `LayoutGroupChildRules` 是 parent-relative）；mask 规则是 self-relative，必须在 `WalkNode` 入口处先检查 `node` 自己。

### 6.6 `Runtime/Application/ScreenInstantiator.cs` 改动

在 `InstantiateRecursive` 中，layout-group child check 旁边补 tag-specific self check:

```csharp
if (parentIsLayoutGroup)
{
    foreach (var issue in LayoutGroupChildRules.CheckChild(node))
        Debug.LogWarning(issue.Message);
}

// 新增:
if (node.Tag == "Frame")
    foreach (var issue in MaskAttributeRules.CheckFrame(node))
        Debug.LogWarning(issue.Message);
else if (node.Tag == "Image")
    foreach (var issue in MaskAttributeRules.CheckImage(node))
        Debug.LogWarning(issue.Message);
```

---

## 7. 跟现有 spec / SKILL 的整合点

### 7.1 主 spec `2026-05-07-promptugui-description-language-design.md`

§5.3 控件特有属性段 Image 行后追加一个 bullet:
> `<Image mask="rect|self" showMask="true|false" maskPadding="T,R,B,L"/>` — 详见 [`2026-05-16-frame-image-mask-design.md`](2026-05-16-frame-image-mask-design.md)

§5（控件表）`<Frame>` 行作用栏微调：
> `<Frame>` | 纯定位容器；可选 `mask="rect"` 启用 RectMask2D | RectTransform（+ 可选 RectMask2D）

### 7.2 `authoring-promptugui-xml/SKILL.md`

1. Built-in primitives 表 `<Frame>` 行 attributes 列补 `mask="rect"`, `maskPadding`。
2. `<Image>` 行 attributes 列补 `mask`, `showMask`, `maskPadding`。
3. 新增 "Mask & clipping" 小节（紧跟 `<Btn>` 内容自适应那一段），含 5 个用例 + lint 错误代码列表。
4. Quick reference 末尾加一行：
   > `<Image mask="self" sprite="round">` 圆角裁剪；`<Frame mask="rect">` 矩形裁剪；`mask` 永远显式，不会因 sprite 自动开启。

---

## 8. Out of Scope

- soft mask / alpha mask（需要 shader 支持，未来 `<MaskedFrame>` 专用控件再做）
- variant overrides on mask attrs（FIM-D8 决策；阻断在 lint）
- `<Btn>` / `<Text>` / `<Slider>` 等其他控件加 mask（FIM-D12）
- `<Frame>` 加 `sprite` / `color`（FIM-D11；想要带背景容器用 `<Image>`）
- 给 ScrollList / Dropdown / InputField 暴露 mask 给作者（它们内部已用，不打开）
- "mask 形状跟 graphic 不一致的单 Frame 压缩语法"（嵌套两层 Image 已够直观）

---

## 9. 风险与回滚

| 风险 | 缓解 |
|---|---|
| `MaskPaddingParser` 翻转 T,R,B,L ↔ L,B,R,T 写反 | 单元测试 4 个分量分别测；`(top=1,right=2,bottom=3,left=4)` → `Vector4(4,3,2,1)` |
| 用户绕过 lint 在 variant 中切 mask 模式 → 组件越挂越多 | FIM-D9：setter 只 `??=`，重复调用不重建，最坏情况 mask 模式锁死在初次创建那个 |
| `<Image mask="self">` 没 sprite 实际看不到裁剪 | lint warning `PUI-MASK-SELF-NO-SPRITE` |
| 反射枚举属性顺序导致 `MaskPadding` 先于 `Mask` | FIM-D14 `_pendingMaskPadding` 缓存方案，无论顺序最终一致 |
| `RectMask2D` 与 stencil `Mask` 同 GameObject 共存 | 用户只能写一个 `mask=` 值；本地实现上即便两个 setter 都跑过（不该发生），uGUI 行为是 `RectMask2D` 与 `Mask` 互不冲突地共存（RectMask2D 控父级范围 + stencil 控形状），不会 crash |
| XSD 不自动更新（"C# 控件注册变更不自动 pick up"）| 跟 Btn fontSize / 之前任何 [UIAttr] 添加一样：要求用户手动 `Tools → PromptUGUI → Schema → Generate XSD`；SKILL.md "Validation & feedback loop" 段已说明 |
