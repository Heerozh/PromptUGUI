# Carousel peek / 居中选择模式 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给现有 `<Carousel>` 加 `fill="false"` 居中卡片选择器排版——卡片用自身尺寸、邻卡两侧露出（peek）、焦点卡最大最亮、越往边越小越淡（`edgeScale` / `edgeAlpha`）、卡间 `spacing`——不拆控件，复用 v1 全部机制。

**Architecture:** 唯一实质改动在 `CarouselView`：把「卡尺寸」与「步距」从 v1 混用的一个 `_pageWidth` 拆成 `_cardW`/`_cardH`（卡尺寸）+ `_stride`（相邻卡中心距），`Reposition()` 按 `|off|` 给每张卡叠 `localScale`/`CanvasGroup.alpha`。`fill="true"`（默认）是该算法的退化特例（卡=视口、spacing=0、edge*=1），与 v1 逐字等价。lint `PUI-CAROUSEL-CARD-SIZE` 改为仅 fill=true 触发，新增 warning `PUI-CAROUSEL-PEEK-NO-SIZE`。

**Tech Stack:** Unity 6 uGUI, C# 9（Unity Mono，禁用 C# 10+ 语法）, R3, LitMotion, NUnit + Unity Test Framework, Unity MCP 跑测。

**Spec:** `docs~/superpowers/specs/2026-06-15-carousel-peek-mode-design.md`（决策 CAR-D24..D32）。承接 v1：`docs~/superpowers/specs/2026-06-04-carousel-design.md`。

**关键约定（务必遵守）：**
- **分支已建好：`feat/carousel-peek-mode`**（spec 已提交在上面）。全程在此分支，**禁止向 main 提交**。
- 每次写完 `.cs` → `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认编译通过，再跑测。
- 跑测：`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 返回 `job_id`，轮询 `mcp__UnityMCP__get_test_job(job_id=...)`。聚焦单类用 `group_names=["CarouselPeekTests"]`（**没有** `filter` 参数）。
- EditMode 测试类碰 `UI` 必须 `[SetUp]`/`[TearDown]` 调 `UI.ResetForTests()`。
- 所有 `[UIAttr]` 必须配 `[Preserve]`（IL2CPP stripping）。
- 禁用 .NET 线程；用 Unity `Update` / LitMotion / R3。
- 每个 Task 末尾 commit；commit message 末尾加 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。
- lint：改完 `Runtime/Core/Lint/*.cs` 后 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（按需先 `dotnet format whitespace`）。
- **关键事实（已核实，照此实现，别再质疑）：**
  - 卡片在 Carousel 下走**自由定位**（Strip 没有真正的 `LayoutGroup` 组件，`Control.ApplyCommon` 的 LayoutElement 通道按真实组件 `LayoutHost.parent.GetComponent<LayoutGroup>()` 判定 → 走 free-positioning），所以卡片的 `size=` 会落到 `RectTransform.sizeDelta`，`Reposition` 之前就能读到（v1 只是之后用 `_pageWidth` 覆盖）。
  - ReSolve（resize / Variant / Theme）经 `ControlAttributeApplier.Apply`（初次与 ReSolve 共用）在 `ControlAttributeApplier.cs:115` 重跑 `control.OnAfterApply()` → `RelayoutNow()` → `Reposition()`。所以「每次 Reposition 都重写 localScale/alpha」即可自复位，无需额外 ReSolve 钩子。
  - `[UIAttr]` setter 仅在 XML/Variant 写了该属性时才被调用；没写则字段默认值生效（v1 `loop` 默认 true 即靠此）。

**参考实现（照抄模式）：** 现有 `Runtime/Controls/Internal/CarouselView.cs`（`Reposition`/`RelayoutNow`/`OnDrag`/`OnEndDrag`）、`Runtime/Controls/Carousel.cs`（`[UIAttr]` 转发 `_view`）、`Tests/EditMode/Controls/CarouselTests.cs` + `CarouselDragTests.cs`（测试夹具）、`Tests/EditMode/Lint/CarouselRulesTests.cs`（lint 测试）。

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `Runtime/Core/Lint/CarouselRules.cs` | `CheckCard` 改签名（拿父 `fill`）+ fill 门控 CARD-SIZE + 新增 `PUI-CAROUSEL-PEEK-NO-SIZE` | 改 |
| `Runtime/Core/Lint/IRWalker.cs` | `CheckCard(child)` → `CheckCard(node, child)`（`:125`） | 改 |
| `Runtime/Application/ScreenInstantiator.cs` | `CheckCard(c)` → `CheckCard(node, c)`（`:286`） | 改 |
| `Runtime/Controls/Carousel.cs` | 新增 4 个 `[UIAttr]`：`Fill`/`Spacing`/`EdgeScale`/`EdgeAlpha` → 转写 `_view` | 改 |
| `Runtime/Controls/Internal/CarouselView.cs` | `_fill`/`_spacing`/`_edgeScale`/`_edgeAlpha`/`_cardW`/`_cardH`/`_stride` 字段 + setters + `MeasureCard` + 改 `RelayoutNow`/`Reposition`/`OnDrag`/`OnEndDrag` + `ApplyAlpha` | 改 |
| `Tests/EditMode/Lint/CarouselRulesTests.cs` | peek 模式 lint 用例 | 改 |
| `Tests/EditMode/Controls/CarouselPeekTests.cs` | size/stride/edgeScale/edgeAlpha/ReSolve 复位 | 新建 |
| `Tests/EditMode/Controls/CarouselDragTests.cs` | peek 拖动步距 | 改 |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs` | 4 个新属性出现在 XSD 的 substring 断言 | 改 |
| `.claude/skills/authoring-promptugui-xml/reference/controls-carousel.md` | peek 小节 + lint 表 | 改 |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | Carousel 行补 4 属性 | 改 |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 一句：左右箭头 = `<Btn>` 绑 `Previous()`/`Next()` | 改 |
| `docs~/superpowers/specs/2026-06-04-carousel-design.md` | Out-of-Scope 的 peek 项标记 v2 已做 + 反链 | 改 |

---

## Task 1: Lint —— CARD-SIZE 按 fill 门控 + 新增 PEEK-NO-SIZE

**Files:**
- Modify: `Runtime/Core/Lint/CarouselRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs:125`
- Modify: `Runtime/Application/ScreenInstantiator.cs:286`
- Test: `Tests/EditMode/Lint/CarouselRulesTests.cs`

> 先做 lint：peek 卡要写 `size=`，若不先放开门控，后续 peek 布局测试会在 ScreenInstantiator 喷 `PUI-CAROUSEL-CARD-SIZE` 警告（无害但误导）。

- [ ] **Step 1: 写失败测试**（追加到 `CarouselRulesTests.cs`，类内）

```csharp
[Test]
public void Peek_Card_With_Size_Does_Not_Trigger_CardSize()
{
    var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Frame width='120'/></Carousel>")).ToList();
    Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.CardSizeCode),
        "fill=false allows a card to declare its own size");
}

[Test]
public void Peek_Bare_Container_Card_Triggers_PeekNoSize()
{
    var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Frame/></Carousel>")).ToList();
    Assert.That(issues.Any(i => i.Code == CarouselRules.PeekNoSizeCode),
        "bare Frame in peek mode has no resolvable size -> warning");
}

[Test]
public void Peek_Image_Card_Without_Size_Does_Not_Trigger_PeekNoSize()
{
    var issues = IRWalker.Walk(Doc("<Carousel fill='false'><Image/></Carousel>")).ToList();
    Assert.IsFalse(issues.Any(i => i.Code == CarouselRules.PeekNoSizeCode),
        "Image carries a native (sprite) size -> not flagged");
}

[Test]
public void Fill_Mode_Card_With_Size_Still_Triggers_CardSize()
{
    var issues = IRWalker.Walk(Doc("<Carousel><Image width='50'/></Carousel>")).ToList();
    Assert.That(issues.Any(i => i.Code == CarouselRules.CardSizeCode),
        "default fill=true keeps the v1 card-size error");
}
```

- [ ] **Step 2: 跑测确认失败**

`refresh_unity` → `read_console(types=["error"])`（此时编译会因 `PeekNoSizeCode` 不存在而**编译失败** → 预期；下一步实现后再跑）。
`run_tests(EditMode, group_names=["CarouselRulesTests"])`
预期：编译错误 `'CarouselRules' does not contain a definition for 'PeekNoSizeCode'`。

- [ ] **Step 3: 实现** —— 替换 `CarouselRules.cs` 的 `CheckCard`，加常量与 helper

把现有 `public static IEnumerable<LintIssue> CheckCard(ElementNode child)`（`CarouselRules.cs:47-58`）整段替换为：

```csharp
        public const string PeekNoSizeCode = "PUI-CAROUSEL-PEEK-NO-SIZE";

        // 无 GetNativeSize() override 的纯容器（Control 基类返回 null）：peek 模式下这种卡根
        // 不写 size 会兜成视口尺寸（不 peek）。Image/Text/Progress/Icon 等自带原生尺寸，放过。
        private static readonly HashSet<string> NoNativeSizeContainers = new HashSet<string>
        { "Frame", "VStack", "HStack", "Grid" };

        private static bool HasOwnSize(ElementNode n)
            => n.Attributes.ContainsKey("size")
            || n.Attributes.ContainsKey("width")
            || n.Attributes.ContainsKey("height")
            || n.VariantOverrides.ContainsKey("size")
            || n.VariantOverrides.ContainsKey("width")
            || n.VariantOverrides.ContainsKey("height");

        // parent-relative：检查 Carousel 的一个直接子（卡片）。需要父 Carousel 读 `fill`：
        // fill=true（默认）禁止卡片写 size；fill="false"（peek）放开，但对「无原生尺寸容器且没写 size」
        // 的卡给 warning。fill 只读基础属性（base）——peek carousel 的 fill="false" 一定写在 base。
        public static IEnumerable<LintIssue> CheckCard(ElementNode carousel, ElementNode child)
        {
            bool peek = carousel.Attributes.TryGetValue("fill", out var f) && f == "false";
            if (!peek)
            {
                if (HasOwnSize(child))
                    yield return new LintIssue(
                        CardSizeCode, child.Tag, child.Id,
                        $"<{child.Tag} id='{child.Id}'>: a Carousel card is sized to the viewport by the control; " +
                        "remove size/width/height (or set fill=\"false\" for a peek selector).");
            }
            else if (!HasOwnSize(child) && NoNativeSizeContainers.Contains(child.Tag))
            {
                yield return new LintIssue(
                    PeekNoSizeCode, child.Tag, child.Id,
                    $"<{child.Tag} id='{child.Id}'>: fill=\"false\" card has no size and no native size; " +
                    "it will fill the viewport and neighbours won't peek. Add size= on the card root, " +
                    "or use a control with a native size (e.g. <Image>).");
            }
        }
```

更新 `IRWalker.cs:125`：

```csharp
                if (node.Tag == "Carousel")
                    foreach (var issue in CarouselRules.CheckCard(node, child))
                        yield return issue;
```

更新 `ScreenInstantiator.cs:285-287`：

```csharp
                if (node.Tag == "Carousel")
                    foreach (var issue in PromptUGUI.Lint.CarouselRules.CheckCard(node, c))
                        Debug.LogWarning(issue.Message);
```

- [ ] **Step 4: 跑测确认通过**

`refresh_unity` → `read_console(types=["error"])`（应无编译错误）
`run_tests(EditMode, group_names=["CarouselRulesTests"])`
预期：全 PASS（含既有 5 个 + 新 4 个）。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Core/Lint/CarouselRules.cs Runtime/Core/Lint/IRWalker.cs Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Lint/CarouselRulesTests.cs
git commit -m "$(printf 'feat(carousel): gate CARD-SIZE on fill, add PUI-CAROUSEL-PEEK-NO-SIZE\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: peek 布局核心 —— `fill`/`spacing` + 卡尺寸 + 步距

**Files:**
- Modify: `Runtime/Controls/Carousel.cs`
- Modify: `Runtime/Controls/Internal/CarouselView.cs`
- Test: `Tests/EditMode/Controls/CarouselPeekTests.cs`（新建）

- [ ] **Step 1: 写失败测试**（新建 `CarouselPeekTests.cs`）

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class CarouselPeekTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Carousel Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }

        private static RectTransform Card(Carousel car, int i)
            => (RectTransform)car.GameObject.transform.Find("Viewport/Strip").GetChild(i);

        [Test]
        public void Peek_Honors_Card_Size_Not_Viewport()
        {
            var car = Open("<Carousel id='car' size='200x100' fill='false'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            Assert.AreEqual(120f, Card(car, 0).rect.width, 0.5f, "peek card keeps its own width (not viewport 200)");
            Assert.AreEqual(80f, Card(car, 0).rect.height, 0.5f);
        }

        [Test]
        public void Peek_Stride_Is_CardWidth_Plus_Spacing()
        {
            var car = Open("<Carousel id='car' size='200x100' fill='false' spacing='10'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            Assert.AreEqual(0f, Card(car, 0).anchoredPosition.x, 0.5f, "focus card centered at x=0");
            Assert.AreEqual(130f, Card(car, 1).anchoredPosition.x, 0.5f, "neighbour at cardWidth+spacing = 130");
        }

        [Test]
        public void Fill_Mode_Default_Unchanged()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/></Carousel>");
            Assert.AreEqual(200f, Card(car, 0).rect.width, 0.5f, "fill=true (default) still sizes cards to viewport");
        }
    }
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CarouselPeekTests"])`
预期：`Peek_*` FAIL（卡片仍被强制视口尺寸 200、x 间距 200）；`Fill_Mode_Default_Unchanged` PASS。

- [ ] **Step 3: 实现** —— Carousel 加 2 属性 + CarouselView 拆尺寸/步距

`Carousel.cs`：在既有 `Loop`/`Transition` setter 附近（`Carousel.cs:69` 之后）加：

```csharp
        [UIAttr, Preserve]
        public bool Fill { set => _view.SetFill(value); }

        [UIAttr, Preserve]
        public float Spacing { set => _view.SetSpacing(value); }
```

`CarouselView.cs`：

(a) 字段区（`_pageWidth`/`_pageHeight` 旁，`CarouselView.cs:37-38`）加：

```csharp
        private bool _fill = true;          // CAR-D25：默认 v1 全幅
        private float _spacing = 0f;        // CAR-D27
        private float _cardW = 1f;          // 卡槽尺寸（fill=true → 视口；false → resolved 卡尺寸）
        private float _cardH = 1f;
        private float _stride = 1f;          // 相邻卡中心距（fill=true → 视口宽；false → _cardW+_spacing）
```

(b) setter（`SetLoop` 旁，`CarouselView.cs:99`）加：

```csharp
        public void SetFill(bool v) => _fill = v;
        public void SetSpacing(float v) => _spacing = Mathf.Max(0f, v);
```

(c) `MeasureCard`（新方法，放 `RelayoutNow` 上方）：

```csharp
        // peek 假定卡等尺寸：取第 0 张卡的 resolved 尺寸作槽位（在 Reposition 之前调，
        // 此时卡的 size= 已由 apply 落到 sizeDelta）。落空（裸 Frame）兜视口，永不为 0。
        private void MeasureCard(out float w, out float h)
        {
            w = _pageWidth; h = _pageHeight;
            if (_cards.Count > 0 && _cards[0] is Control c0 && c0.GameObject != null)
            {
                var rect = c0.RectTransform.rect;
                if (rect.width > 0f) w = rect.width;
                if (rect.height > 0f) h = rect.height;
            }
        }
```

(d) `RelayoutNow`（`CarouselView.cs:385`）—— 在算完 `_pageWidth`/`_pageHeight` 之后、clamp `_current` 之前插入：

```csharp
            if (_fill) { _cardW = _pageWidth; _cardH = _pageHeight; _stride = _pageWidth; }
            else { MeasureCard(out _cardW, out _cardH); _stride = _cardW + _spacing; }
```

(e) `Reposition`（`CarouselView.cs:378-379`）—— 把这两行：

```csharp
                rt.sizeDelta = new Vector2(_pageWidth, _pageHeight);
                rt.anchoredPosition = new Vector2(off * _pageWidth, 0f);
```

改成：

```csharp
                rt.sizeDelta = new Vector2(_cardW, _cardH);
                rt.anchoredPosition = new Vector2(off * _stride, 0f);
```

- [ ] **Step 4: 跑测确认通过**

`refresh_unity` → `read_console(types=["error"])`
`run_tests(EditMode, group_names=["CarouselPeekTests"])` → 全 PASS
`run_tests(EditMode, group_names=["CarouselTests"])` → 全 PASS（fill=true 回归，§Architecture 的逐字等价）

- [ ] **Step 5: commit**

```bash
git add Runtime/Controls/Carousel.cs Runtime/Controls/Internal/CarouselView.cs Tests/EditMode/Controls/CarouselPeekTests.cs
git commit -m "$(printf 'feat(carousel): fill=false honors card size + spacing stride\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: `edgeScale` —— 焦点卡满、边卡缩（每帧重写自复位）

**Files:**
- Modify: `Runtime/Controls/Carousel.cs`
- Modify: `Runtime/Controls/Internal/CarouselView.cs`
- Test: `Tests/EditMode/Controls/CarouselPeekTests.cs`

- [ ] **Step 1: 写失败测试**（追加到 `CarouselPeekTests.cs`）

```csharp
[Test]
public void Peek_EdgeScale_Shrinks_Neighbours_Focus_Full()
{
    var car = Open("<Carousel id='car' size='400x100' fill='false' edgeScale='0.8' loop='true'>" +
                   "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
    Assert.AreEqual(1f, Card(car, 0).localScale.x, 0.001f, "focus card (off 0) full scale");
    Assert.AreEqual(0.8f, Card(car, 1).localScale.x, 0.001f, "neighbour (off 1) shrunk to edgeScale");
}

[Test]
public void Peek_EdgeScale_Reapplied_On_GoTo_Self_Resets()
{
    var car = Open("<Carousel id='car' size='400x100' fill='false' edgeScale='0.8' loop='true'>" +
                   "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
    Assert.AreEqual(0.8f, Card(car, 1).localScale.x, 0.001f, "initially a neighbour");
    car.GoTo(1, animated: false);   // card1 becomes focus
    Assert.AreEqual(1f, Card(car, 1).localScale.x, 0.001f, "scale re-written every Reposition (self-resets to 1)");
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CarouselPeekTests"])`
预期：编译失败（`EdgeScale` 不存在）→ 实现后再跑。

- [ ] **Step 3: 实现**

`Carousel.cs`：加

```csharp
        [UIAttr, Preserve]
        public float EdgeScale { set => _view.SetEdgeScale(value); }
```

`CarouselView.cs`：
- 字段加 `private float _edgeScale = 1f;`
- setter 加 `public void SetEdgeScale(float v) => _edgeScale = v;`
- `Reposition` 循环里，`anchoredPosition` 那行**之后**加（`localScale` 总是写、不短路 → fill=true / edgeScale=1 时 `s=1` 自复位）：

```csharp
                float t = Mathf.Clamp01(Mathf.Abs(off));          // CAR-D32 线性
                rt.localScale = Vector3.one * Mathf.Lerp(1f, _edgeScale, t);
```

- [ ] **Step 4: 跑测确认通过**

`run_tests(EditMode, group_names=["CarouselPeekTests"])` → 全 PASS
`run_tests(EditMode, group_names=["CarouselTests"])` → 全 PASS（fill=true 下 edgeScale=1 → localScale 恒 1，不破坏 v1）

- [ ] **Step 5: commit**

```bash
git add Runtime/Controls/Carousel.cs Runtime/Controls/Internal/CarouselView.cs Tests/EditMode/Controls/CarouselPeekTests.cs
git commit -m "$(printf 'feat(carousel): edgeScale shrinks peek neighbours toward edges\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: `edgeAlpha` —— 边卡渐隐（懒加 / 复用 CanvasGroup）

**Files:**
- Modify: `Runtime/Controls/Carousel.cs`
- Modify: `Runtime/Controls/Internal/CarouselView.cs`
- Test: `Tests/EditMode/Controls/CarouselPeekTests.cs`

- [ ] **Step 1: 写失败测试**（追加到 `CarouselPeekTests.cs`）

```csharp
[Test]
public void Peek_EdgeAlpha_Fades_Neighbours_Via_CanvasGroup()
{
    var car = Open("<Carousel id='car' size='400x100' fill='false' edgeAlpha='0.4' loop='true'>" +
                   "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
    var cg = Card(car, 1).GetComponent<CanvasGroup>();
    Assert.IsTrue(cg != null, "neighbour gets a CanvasGroup for fading");
    Assert.AreEqual(0.4f, cg.alpha, 0.001f, "neighbour faded to edgeAlpha");
}

[Test]
public void Peek_Focus_Card_Full_Alpha()
{
    var car = Open("<Carousel id='car' size='400x100' fill='false' edgeAlpha='0.4' loop='true'>" +
                   "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
    var cg = Card(car, 0).GetComponent<CanvasGroup>();
    Assert.IsTrue(cg == null || Mathf.Approximately(cg.alpha, 1f), "focus card fully opaque");
}

[Test]
public void Fill_Mode_Adds_No_CanvasGroup()
{
    var car = Open("<Carousel id='car' size='200x100'><Image/><Image/></Carousel>");
    Assert.IsTrue(Card(car, 0).GetComponent<CanvasGroup>() == null,
        "fill-mode cards never get a CanvasGroup (edgeAlpha inert)");
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CarouselPeekTests"])`
预期：编译失败（`EdgeAlpha` 不存在）→ 实现后再跑。

- [ ] **Step 3: 实现**

`Carousel.cs`：加

```csharp
        [UIAttr, Preserve]
        public float EdgeAlpha { set => _view.SetEdgeAlpha(value); }
```

`CarouselView.cs`：
- 字段加 `private float _edgeAlpha = 1f;`
- setter 加 `public void SetEdgeAlpha(float v) => _edgeAlpha = v;`
- `Reposition` 循环里，`localScale` 那行之后加：

```csharp
                ApplyAlpha(card, Mathf.Lerp(1f, _edgeAlpha, t));
```

- 新方法（放 `Reposition` 下方）：

```csharp
        // a<1 才懒加 CanvasGroup 并设 alpha；a==1 时若已有则复位（不新挂）——纯 fill carousel
        // 永不挂 CanvasGroup；peek→fill 切换 / 焦点变化时已有的 group 被复位回不透明。
        private static void ApplyAlpha(Control card, float a)
        {
            var go = card.GameObject;
            var cg = go.GetComponent<CanvasGroup>();
            if (a < 1f)
            {
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = a;
            }
            else if (cg != null)
            {
                cg.alpha = 1f;
            }
        }
```

- [ ] **Step 4: 跑测确认通过**

`run_tests(EditMode, group_names=["CarouselPeekTests"])` → 全 PASS
`run_tests(EditMode, group_names=["CarouselTests"])` → 全 PASS

- [ ] **Step 5: commit**

```bash
git add Runtime/Controls/Carousel.cs Runtime/Controls/Internal/CarouselView.cs Tests/EditMode/Controls/CarouselPeekTests.cs
git commit -m "$(printf 'feat(carousel): edgeAlpha fades peek neighbours via CanvasGroup\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: 拖动 / 吸附改用步距（peek 阈值）

**Files:**
- Modify: `Runtime/Controls/Internal/CarouselView.cs`
- Test: `Tests/EditMode/Controls/CarouselDragTests.cs`

> v1 拖动把本地位移除以 `_pageWidth`、阈值用 `_pageWidth * SnapThreshold`。peek 模式步距是卡宽而非视口宽，必须换成 `_stride`，否则窄卡的拖动手感/翻页阈值全错（按 200 而非卡宽算）。

- [ ] **Step 1: 写失败测试**（追加到 `CarouselDragTests.cs`，类内）

先在类里加一个带尺寸卡的 Open 辅助（放现有 `Open` 旁）：

```csharp
        private static Carousel OpenCards(string attrs, string cards)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' {attrs}>{cards}</Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }
```

再加测试：

```csharp
[Test]
public void Peek_Drag_Threshold_Scales_With_Card_Stride()
{
    // 视口 200，卡宽 100，spacing 0 → 步距 100、阈值 0.2*100 = 20。
    // 拖 -30：> 卡步距阈值（翻页），但 < 旧视口阈值 0.2*200=40（旧逻辑会回弹）。
    var car = OpenCards("fill='false' spacing='0' interval='0'",
        "<Frame size='100x80'/><Frame size='100x80'/><Frame size='100x80'/>");
    DragLocal(car.GameObject.GetComponent<CarouselView>(), -30f);
    Assert.AreEqual(1, car.Current, "drag threshold uses card stride (100), not viewport width (200)");
}
```

- [ ] **Step 2: 跑测确认失败**

`run_tests(EditMode, group_names=["CarouselDragTests"])`
预期：`Peek_Drag_Threshold_Scales_With_Card_Stride` FAIL（`Current==0`，因仍用 `_pageWidth=200`、阈值 40 > 30 → 回弹）。

- [ ] **Step 3: 实现** —— `CarouselView.cs` 把拖动里的 `_pageWidth` 换成 `_stride`

`OnDrag`（`CarouselView.cs:466-467`）：

```csharp
            _dragLocalX = Mathf.Clamp(dxLocal, -_stride, _stride);
            _scroll = _dragStartScroll - _dragLocalX / _stride;
```

`OnEndDrag`（`CarouselView.cs:476-477`）：

```csharp
            if (_dragLocalX <= -_stride * SnapThreshold) target = _current + 1;
            else if (_dragLocalX >= _stride * SnapThreshold) target = _current - 1;
```

> fill=true 时 `_stride == _pageWidth`，v1 拖动测试逐字不变。

- [ ] **Step 4: 跑测确认通过**

`run_tests(EditMode, group_names=["CarouselDragTests"])` → 全 PASS（含 v1 的 6 个 + 新 1 个）

- [ ] **Step 5: commit**

```bash
git add Runtime/Controls/Internal/CarouselView.cs Tests/EditMode/Controls/CarouselDragTests.cs
git commit -m "$(printf 'feat(carousel): drag/snap use card stride in peek mode\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 6: ReSolve 幂等 —— fill 经 Variant 切换复位 scale/alpha

**Files:**
- Test: `Tests/EditMode/Controls/CarouselPeekTests.cs`

> 这是**守护测试**：验证 Task 3/4 的「每帧重写 localScale + ApplyAlpha 复位已有 CanvasGroup」在真实 ReSolve 通道下能把 peek 视觉复位。若全过（无需新生产代码），说明自复位实现正确；若失败，根因必是 `Reposition` 里对 scale/alpha 做了 `if(!_fill)` 短路——删掉短路即可。

- [ ] **Step 1: 写测试**（追加到 `CarouselPeekTests.cs`）

```csharp
[Test]
public void Fill_Toggled_Via_Variant_Resets_Scale_And_Alpha()
{
    var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='400x100' fill='false' fill.big='true'
            edgeScale='0.8' edgeAlpha='0.4' loop='true'>
    <Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/>
  </Carousel>
</Screen></PromptUGUI>";
    UI.LoadDocument("t", xml);
    var car = UI.Open("S").Get<Carousel>("car");
    var card1 = (RectTransform)car.GameObject.transform.Find("Viewport/Strip").GetChild(1);
    Assert.AreEqual(0.8f, card1.localScale.x, 0.001f, "peek: neighbour shrunk");

    UI.Variants.Set("big", true);   // fill -> true -> ReSolve -> Reposition resets

    Assert.AreEqual(1f, card1.localScale.x, 0.001f, "fill=true resets card scale to 1");
    var cg = card1.GetComponent<CanvasGroup>();
    Assert.IsTrue(cg == null || Mathf.Approximately(cg.alpha, 1f), "fill=true resets card alpha to 1");
}

[Test]
public void Resize_Does_Not_Duplicate_CanvasGroup()
{
    var car = Open("<Carousel id='car' size='400x100' fill='false' edgeAlpha='0.4' loop='true'>" +
                   "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
    var card1 = (RectTransform)car.GameObject.transform.Find("Viewport/Strip").GetChild(1);
    var view = car.GameObject.GetComponent<CarouselView>();
    view.InvokeRectChangedForTests();
    view.InvokeRectChangedForTests();
    Assert.AreEqual(1, card1.GetComponents<CanvasGroup>().Length, "no duplicate CanvasGroup across reflows");
}
```

- [ ] **Step 2: 跑测**

`run_tests(EditMode, group_names=["CarouselPeekTests"])`
预期：PASS（Task 3/4 已实现自复位）。**若 FAIL** → 检查 `Reposition`：localScale 必须每帧无条件写、`ApplyAlpha` 的 `a==1` 分支必须复位已有 CanvasGroup（按 Task 3/4 Step 3）。修正后再跑。

- [ ] **Step 3: commit**

```bash
git add Tests/EditMode/Controls/CarouselPeekTests.cs
git commit -m "$(printf 'test(carousel): peek scale/alpha self-reset across ReSolve\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 7: XSD —— 4 个新属性出现在生成的 schema

**Files:**
- Modify: `Tests/EditMode/Editor/XsdGeneratorTests.cs`
- Modify: 已提交的 `.xsd` 工件（如有）

> XSD 生成器反射 `[UIAttr]`，属性加好后生成结果自动含这 4 个；只需加 substring 断言 + 重生成提交工件。

- [ ] **Step 1: 找现有 Carousel 的 XSD 断言锚点**

`mcp__UnityMCP__find_in_file` 或 grep：在 `Tests/EditMode/Editor/XsdGeneratorTests.cs` 里搜 `Carousel` / `interval` 找到既有的针对 Carousel 属性的 `StringAssert.Contains` 测试方法，照它的风格在同一方法（或新方法）加：

```csharp
StringAssert.Contains("fill", xsd);
StringAssert.Contains("spacing", xsd);
StringAssert.Contains("edgeScale", xsd);
StringAssert.Contains("edgeAlpha", xsd);
```

（`xsd` 为该测试里已生成的 schema 字符串变量名；用现有变量名，别新造。）

- [ ] **Step 2: 跑测确认通过**

`run_tests(EditMode, assembly_names=["PromptUGUI.Tests.EditorOnly"], group_names=["XsdGeneratorTests"])`
预期：PASS（生成器已含新属性）。若该测试断言的是某个**固定 .xsd 文件内容**而非内存生成串，则先做 Step 3 重生成，再跑。

- [ ] **Step 3: 重生成并提交 .xsd 工件**

若仓库里有签入的 `.xsd`（grep `*.xsd` 找路径），用菜单重生成：
`mcp__UnityMCP__execute_menu_item(menu_path="Tools/PromptUGUI/Schema/Generate XSD")`（路径以实际菜单为准；**不是**禁用的 Reimport All）。
然后 `git add` 变更的 `.xsd`。

- [ ] **Step 4: commit**

```bash
git add Tests/EditMode/Editor/XsdGeneratorTests.cs
# 若有重生成的 .xsd 一并 add
git commit -m "$(printf 'test(carousel): XSD covers fill/spacing/edgeScale/edgeAlpha\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 8: 文档 —— XML reference + 主 catalog + spec 反链

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/reference/controls-carousel.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-06-04-carousel-design.md`

> CLAUDE.md 硬性要求：功能变更必须同 PR 更新相关 skill。**匹配每个目标文件现有的语言**（`controls-carousel.md` 是中文；`SKILL.md` 看现状跟随）。无测试，纯文档。

- [ ] **Step 1: `reference/controls-carousel.md` 加 peek 小节**

在「卡片布局约束」小节后插入新小节（中文，匹配该文件风格）：

```markdown
## 居中选择 / peek 模式（`fill="false"`）

默认 `fill="true"`：卡片撑满视口、一卡一页（banner）。`fill="false"` 切到**居中卡片选择器**——卡片用自身 `size`、两侧邻卡露出、焦点卡最大最亮、越往边越小越淡：

​```xml
<Carousel id="sel" anchor="center" size="600x360"
          fill="false" spacing="24" edgeScale="0.8" edgeAlpha="0.45"
          interval="0" itemTemplate="LevelCard"/>
​```

- `fill="false"` 下卡片**必须有尺寸**：卡根写 `size=`（裸 `<Frame>`），或用自带原生尺寸的控件（`<Image>`/`<Text>`）。无尺寸的容器卡会兜成视口、不 peek（lint `PUI-CAROUSEL-PEEK-NO-SIZE` 提示）。
- peek 露出多少 = `(carousel 宽 − 卡宽)/2 − spacing`，**由卡尺寸 vs carousel 尺寸隐式决定**，没有单独的 peek 属性。
- `edgeScale`（默认 `1.0`，不缩）/ `edgeAlpha`（默认 `1.0`，不淡）：基准是**选中卡**——中心 = 声明尺寸/不透明，按距中心距离线性插值到边值。
- `spacing`（默认 `0`）：相邻卡间距 px，仅 `fill="false"` 生效。
- `fill="false"` 下 Carousel 占用卡根的 `localScale`（做 edgeScale）；卡根别再写 density `scale=`，要缩放写卡的里层节点。
- autoplay 与 fill 正交：选择器自己写 `interval="0"`。左右翻页箭头 = 放 `<Btn>` 绑 C# `Previous()`/`Next()`。
```

把该文件「Lint 规则」表更新为：`PUI-CAROUSEL-CARD-SIZE` 标注「仅 `fill="true"` 触发」；新增一行 `PUI-CAROUSEL-PEEK-NO-SIZE`（warning，`fill="false"` 卡根是无原生尺寸容器且没写 size）。

- [ ] **Step 2: 主文档 `SKILL.md` 的 Carousel 行补属性**

在内置控件表 `<Carousel>` 行的属性列补 `fill` / `spacing` / `edgeScale` / `edgeAlpha`（一句话各自语义，详情指向 reference）。

- [ ] **Step 3: `scripting-promptugui-csharp/SKILL.md` 补一句**

在 Carousel 的 C# 速查处加：「居中选择器的左右箭头 = `<Btn>` 绑 `car.Previous()` / `car.Next()`（已是 public 方法，无需新 API）」。

- [ ] **Step 4: v1 spec 反链**

`docs~/superpowers/specs/2026-06-04-carousel-design.md` §12 Out of Scope 里「peek 露边」「cross-fade / 缩放转场」「per-card 自有尺寸」三项，各加一句「→ v2 已做，见 `2026-06-15-carousel-peek-mode-design.md`」。

- [ ] **Step 5: 验证 docs 内引用的 XML 能过 lint CLI**

```bash
cd .lint && dotnet run --project UIXmlLint -- ../Runtime/Resources/   # 确认没引入回归（peek 是新增、不影响既有资源）
```

- [ ] **Step 6: commit**

```bash
git add .claude/skills/authoring-promptugui-xml/reference/controls-carousel.md \
        .claude/skills/authoring-promptugui-xml/SKILL.md \
        .claude/skills/scripting-promptugui-csharp/SKILL.md \
        "docs~/superpowers/specs/2026-06-04-carousel-design.md"
git commit -m "$(printf 'docs(carousel): document fill=false peek mode in skills + spec backlink\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## 最终验证（全量，不靠 targeted group 宣称绿）

> 经验教训：别只跑 targeted group 就说全绿——曾漏跑 PlayMode 回归。下面三个全量都要亲跑。

- [ ] `refresh_unity(compile="request", mode="force", scope="all")` → `read_console(types=["error"])` 无错
- [ ] `run_tests(EditMode, assembly_names=["PromptUGUI.Tests.EditMode"])` 全量轮询到完成，全绿
- [ ] `run_tests(EditMode, assembly_names=["PromptUGUI.Tests.EditorOnly"])` 全绿（XSD）
- [ ] `run_tests(PlayMode, assembly_names=["PromptUGUI.Tests.PlayMode"])` 全绿（含 `CarouselPlayTests` 自动播放回归——autoplay 与 fill 正交，应不受影响）
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 干净
- [ ] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 无新增 error
- [ ] 视觉 QA（用户）：在宿主工程摆一个 `fill="false"` 选择器，确认邻卡露边、焦点放大、边缘淡出、拖动吸附、左右按钮（绑 Next/Previous）观感正确

---

## Self-Review（写计划时已核对）

- **Spec 覆盖**：CAR-D25 `fill`→T2；D26 卡尺寸回退+lint→T1/T2；D27 `spacing`→T2；D28 `edgeScale`/`edgeAlpha`→T3/T4；D29 localScale/CanvasGroup→T3/T4；D30 autoplay 正交→无代码（文档 T8）；D31 PEEK-NO-SIZE→T1；D32 线性插值→T3/T4；§6.5 拖动步距→T5；§8 向后兼容→T2/T3/T4 的 CarouselTests 回归 + 最终全量；ReSolve 复位→T6。
- **类型一致**：`SetFill(bool)`/`SetSpacing(float)`/`SetEdgeScale(float)`/`SetEdgeAlpha(float)`；字段 `_fill bool`、`_spacing/_edgeScale/_edgeAlpha/_cardW/_cardH/_stride float`；`MeasureCard(out float,out float)`；`ApplyAlpha(Control,float)`——各 Task 引用一致。
- **占位**：无 TBD；XSD 的「找锚点 / 实际菜单路径」是因生成器实现需现场确认的真实步骤，非占位（给了 grep/find 指令）。
- **lint 双调用点**：`CheckCard` 签名改动同步 IRWalker:125 + ScreenInstantiator:286（T1 Step 3 都列了）。
