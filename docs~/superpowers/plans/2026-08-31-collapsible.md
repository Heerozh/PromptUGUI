# `<Collapsible>` 可展开 / 收起的内联面板 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新内置控件 `<Collapsible>`：标题栏（属性三件套或 `<Header>` 槽 + 库画的翻转箭头）+ body（VStack 语义、hug、`maxHeight` 限高滚动）+ 高度 / 淡入 / 箭头三通道过渡 + `expanded` 运行期独占 + `group=` 手风琴 + 键盘可聚焦；`expand` / `collapse` 触发器的源从 `<TabMenu>` 泛化为 `IExpandable`。

**Architecture:** `Collapsible : ProceduralControl`（主表面 = 根），根挂 `VerticalLayoutGroup` 排 Header / Body 两个内部节点，作者子节点进 `Body/Content`（`ChildHostTransform`）。过渡复用 FND 的 `RevealDriver`（body `LayoutElement.preferredHeight`）、`RotateFlipEffect`（箭头 `Rotation`）与 TM 的打断安全 / 完成回调规则；根高度恒为 hug（自由定位下 `ApplyCommon` 自动挂 `ClampFitter(Hug)`）。`<Header>` 是 parser 层的结构元素（照 `<FocusCursor>` 摘出），实例化到 `Header/Host`。TabMenu 的 caption 建节点 / 排版、`TabMenuMarker`、inactive-TMP 测量绕法先抽成共享内部零件（M0），TabMenu 行为不变、由既有测试守门。

**Tech Stack:** Unity 6 / C# (LangVersion 9.0)、uGUI、LitMotion、R3。无新增包。

设计依据：`docs~/superpowers/specs/2026-08-31-collapsible-design.md`（下文 §N 均指该 spec 的节号；决策 `COL-Dn`）。**前置**：`2026-08-31-hug-reveal-flip-checked.md` plan 的 M1–M3 已合并（`ClampFitter.Hug` / `IHugContent` / `HugRules.HugTags` / `RevealDriver` / `RotateFlipEffect`）。

## Global Constraints

- **分支**：全部工作在 `feat/collapsible`（Task 0 建，从合并了 FND 的 `main` 拉）。**绝不提交到 main。**
- **LangVersion 9.0**：无 primary constructor、无 collection expression `[]`、无 `[field: SerializeField]`。
- **不用 System.Threading / Task**。
- **Core 纯 C# 子集**：`Runtime/Core/Lint/CollapsibleRules.cs`、`Core/Parser` / `Core/IR` 的改动 **不得** `using UnityEngine`。
- **lint**：每个 Task 收尾 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`；**不要** `--severity info`。
- **同 PR 必改 SKILL**（英文）：见 Task 9。
- **测试只经 Unity MCP 跑**。**禁止** `execute_menu_item("Assets/Reimport All")`。
- **Red first**。
- **TabMenu 零回归**：M0 每个 Task 末尾跑 `RUN(TabMenuTests)` `RUN(TabMenuBindItemsTests)` `RUN(TabMenuPlacementTests)` `RUN(TabMenuTriggerTests)` `RUN(TabMenuTrapTests)` `RUNPLAY(TabMenuPlayTests)`。
- **`.ui.xml` 改动后跑** `dotnet run --project .lint/UIXmlLint -- <file>`（Task 9 的 SKILL 示例若落成 fixture）。

**RUN(ClassName)** / **RUNPLAY(ClassName)** / **Drain()**：同 FND plan 的定义（`refresh_unity` force → `read_console` 无错 → `run_tests` → 轮询 `get_test_job`、核对 `summary.total` > 0）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Controls/Internal/CaptionBuilder.cs` | icon + label + arrow 的建节点 / 排版 / 属性落点（从 `TabMenu.BuildCaption` / `LayoutCaption` / `arrow*` 抽出） | Create |
| `Runtime/Controls/Internal/IExpandable.cs` | `IsExpanded` / `OnExpanded` / `OnCollapsed`；`ExpandableMarker`（`TabMenuMarker` 更名） | Create（+ 删 `TabMenuMarker.cs`） |
| `Runtime/Controls/Internal/InactiveMeasure.cs` | "inactive 父级下 TMP 报 0"的临时激活 / 还原（从 `TabMenu.BeforeRebuild` / `AfterRebuild` 抽出） | Create |
| `Runtime/Controls/TabMenu.cs` | 改用上面三个零件；箭头翻转改 `RotateFlipEffect.Rotation`；`SetArrowFlip` / `CurrentArrowFlip` 退役 | Modify |
| `Runtime/Controls/Internal/TriggerSourceResolver.cs` | `FindTabMenu` → `FindExpandable`（返回 `IExpandable`） | Modify |
| `Runtime/Controls/Trigger.cs` | `SubscribeMenu` 改按 `IExpandable` 订阅 | Modify |
| `Runtime/Core/Lint/StateTriggerRules.cs` | `IsMenuSourceTag` 加 `"Collapsible"`；`PUI-EXPAND-NO-SOURCE` 消息改 | Modify |
| `Runtime/Controls/Collapsible.cs` | 控件本体 | Create |
| `Runtime/Controls/Internal/CollapsibleGroupRegistry.cs` | Screen 范围命名组 | Create |
| `Runtime/Application/Screen.cs` | `CollapsibleGroups` 属性 + 生命周期 | Modify |
| `Runtime/Core/IR/ElementNode.cs` | `HeaderChildren`（`<Header>` 摘出后的子节点列表；null = 无） | Modify |
| `Runtime/Core/Parser/UIDocumentParser.cs` | `ParseElement` 对 `<Collapsible>` 摘 `<Header>`；`ThemeStyleForbiddenAttrs` 加 `expanded` | Modify |
| `Runtime/Core/Template/TemplateExpander.cs` | `HeaderChildren` 参与 `{{param}}` 替换 / 嵌套展开 / `if=`（与 `Children` 同路径） | Modify |
| `Runtime/Application/ScreenInstantiator.cs` | `HeaderChildren` 实例化到 `Header/Host`（同一 id 作用域）；lint 镜像分发；`selfIsLayoutGroup` 加 `Collapsible` | Modify |
| `Runtime/Application/BuiltinPrimitives.cs` | `Register<Collapsible>("Collapsible", null, runtimeStateAttr: "expanded")` | Modify |
| `Runtime/Core/Lint/BuiltinTags.cs` | 加 `Collapsible` | Modify |
| `Runtime/Core/Lint/CollapsibleRules.cs` | §4.8 前六条 | Create |
| `Runtime/Core/Lint/IRWalker.cs` | 分发 `CollapsibleRules`；`isLayoutGroup` 加 `Collapsible`（`HeaderChildren` 按自由定位规则走） | Modify |
| `Runtime/Core/Lint/HugRules.cs` | `HugTags` 加 `Collapsible` | Modify |
| `Runtime/Core/Lint/ProceduralSurfaceRules.cs` `GradientStopRules.cs` `NavTargetRules.cs` | 标签表各加 `Collapsible` | Modify |
| `Editor/XsdGenerator.cs` | `Collapsible` 元素下手写可选首子元素 `<Header>`（照 `FocusCursor` 在 `Screen` 下的写法） | Modify |
| `Editor/I18n/XmlStringScanner.cs` | `TextHostingTags` 加 `Collapsible`（`text=` 抽 msgid） | Modify |
| `Tests/EditMode/Controls/CaptionBuilderTests.cs` | 抽出零件的直驱测试 | Create |
| `Tests/EditMode/Controls/CollapsibleTests.cs` | 结构 / 属性 / `<Header>` / `expanded` / `transition=0` 终态 | Create |
| `Tests/EditMode/Controls/CollapsibleSizeTests.cs` | hug / `maxHeight` / 布局组内兄弟 / 宽度 | Create |
| `Tests/EditMode/Controls/CollapsibleGroupTests.cs` | 互斥 / 全收起 / 开屏裁决 / Variant / 清表 | Create |
| `Tests/EditMode/Controls/CollapsibleTriggerTests.cs` | `expand` / `collapse` 源解析 | Create |
| `Tests/EditMode/Parser/CollapsibleHeaderParseTests.cs` | `<Header>` 摘出 / 模板展开 | Create |
| `Tests/EditMode/Lint/CollapsibleRulesTests.cs` | 六条 + 布局组子节点规则 + Header 豁免 | Create |
| `Tests/EditMode/Editor/XsdGeneratorTests.cs`（既有） | `Collapsible` / `Header` 元素 | Modify |
| `Tests/EditMode/Lint/BuiltinTagsTests.cs`（既有） | 自动守门（注册 ↔ 表） | — |
| `Tests/PlayMode/Controls/CollapsiblePlayTests.cs` | 过渡 / 打断 / 停用时序 / ReSolve 中途 | Create |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` + `reference/controls-collapsible.md`（新）+ `reference/animations.md` + `reference/controls-tabs.md` + `reference/navigation.md` | 文档（英文） | Modify / Create |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | C# API | Modify |
| `PromptUGUI/CLAUDE.md` | 第 31 行路由表加 `controls-collapsible.md` | Modify |
| `.lint/UIXmlLint/README.md` | 代码表新增 6 条 | Modify |
| `docs~/superpowers/specs/2026-08-31-collapsible-design.md` | §13 实施记录 | Modify |

---

## Task 0: 建分支 + 落 plan

- [ ] 确认 `main` 已含 FND（`git log --oneline | grep hug-reveal`），`git checkout -b feat/collapsible`。
- [ ] 本文件与 spec 已随 FND 分支进 main；若有增补，`git add docs~/` 提交（分支上）。

---

# M0 —— 从 TabMenu 抽共享零件（行为不变）

## Task 1: `CaptionBuilder` + `InactiveMeasure`（spec §6）

**Files:**
- Create: `Runtime/Controls/Internal/CaptionBuilder.cs`、`Runtime/Controls/Internal/InactiveMeasure.cs`
- Modify: `Runtime/Controls/TabMenu.cs`
- Test: `Tests/EditMode/Controls/CaptionBuilderTests.cs`

**Interfaces：**
```csharp
internal sealed class CaptionBuilder {
  public UnityImage Icon; public TMP_Text Label; public UnityImage Arrow;      // 与 TabMenu 今天的三个字段一一对应
  public float PadX = 12f, Gap = 8f, IconSize = 24f, ArrowSize = 16f;
  public CaptionBuilder(RectTransform host, bool arrowAtRight);              // TabMenu: false（箭头跟在文字后）；Collapsible: true（箭头贴右沿）
  public void Layout(float hostWidth);                                       // 原 LayoutCaption；arrowAtRight 时 arrow.x = hostWidth − PadX − ArrowSize，label 宽 = 到箭头区为止
  public Vector2 MeasureText(string text);                                   // 原 MeasureText
  public float ContentWidth();                                               // 原 GetNativeSize 的宽度闭式
  public void SetArrow(string spriteKey); SetArrowColor(string); SetArrowSize(float); SetIcon(string); SetIconColor(string); SetFont(...)
}
internal static class InactiveMeasure {
  public static bool ActivateIfNeeded(GameObject go);   // 返回是否临时激活了
  public static void Restore(GameObject go, bool activated);
}
```
TabMenu：字段改为 `_caption`（`CaptionBuilder`），`BuildCaption` / `LayoutCaption` / `MeasureText` / `arrow*` setter 改为转发；`BeforeRebuild` / `AfterRebuild` 改用 `InactiveMeasure`。`GetNativeSize` 用 `_caption.ContentWidth()`。

- [ ] **Step 1: 红测试** —— `CaptionBuilderTests`：手搭 RT host；`arrowAtRight=false` 时 icon 在 `PadX`、label 紧随、arrow 在 label 后 `Gap`；`arrowAtRight=true` 且 `hostWidth=150` 时 arrow.x == `150 − 12 − 16`、label 宽被截到箭头区前；`SetArrow("")` 隐藏后 label 拿回宽度；`InactiveMeasure` 对 inactive GO 返回 true 且 `Restore` 后回到 inactive。
- [ ] **Step 2: RUN(CaptionBuilderTests)** 红。
- [ ] **Step 3: 实现**（纯搬迁 + 参数化）。
- [ ] **Step 4: 绿**；TabMenu 六个测试类全绿（Global Constraints 列表）。
- [ ] **Step 5: lint**

---

## Task 2: `IExpandable` + `FindExpandable` + 箭头改 `RotateFlipEffect`（spec §4.5 / §6）

**Files:**
- Create: `Runtime/Controls/Internal/IExpandable.cs`（含 `ExpandableMarker : MonoBehaviour { internal IExpandable Owner; }`）
- Delete: `Runtime/Controls/Internal/TabMenuMarker.cs`
- Modify: `Runtime/Controls/TabMenu.cs`、`Runtime/Controls/Internal/TriggerSourceResolver.cs`、`Runtime/Controls/Trigger.cs`、`Runtime/Core/Lint/StateTriggerRules.cs`
- Test: `Tests/EditMode/Controls/TabMenuTriggerTests.cs`（既有，改消息断言）、`Tests/PlayMode/Controls/TabMenuPlayTests.cs`（箭头断言改 `Rotation`）

**Interfaces：**
```csharp
internal interface IExpandable { bool IsExpanded { get; } Observable<Unit> OnExpanded { get; } Observable<Unit> OnCollapsed { get; } }
// TriggerSourceResolver
public static IExpandable FindExpandable(Trigger trigger, string sourceId);
//   空 → GetComponentInParent<ExpandableMarker>(true).Owner；@id → ResolveId + `as IExpandable` 校验
//   错误文本："no <TabMenu>/<Collapsible> ancestor found. Place it inside one, or use expand@<id>."
// Trigger.SubscribeMenu → SubscribeExpandable(kind)：var src = FindExpandable(...); stream = kind == Expand ? src.OnExpanded : src.OnCollapsed
// StateTriggerRules.IsMenuSourceTag(tag) => tag is "TabMenu" or "Collapsible"；NoMenuCode 消息同步
// TabMenu：_arrow 改挂 RotateFlipEffect；PlayTransition 的 _arrowMotion 绑 effect.Rotation 0↔180；CurrentArrowFlip = effect.Rotation / 180
```

- [ ] **Step 1: 红测试** —— `TabMenuTriggerTests` 里"无 TabMenu 祖先"的消息断言改含 `<Collapsible>`（先红）；`TabMenuPlayTests` 里箭头断言改为 `GetComponent<RotateFlipEffect>().Rotation`（0.25s 后 ≈ 180）。
- [ ] **Step 2: RUN / RUNPLAY** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；TabMenu 六个类全绿；`RUN(StateTriggerRulesTests)` / `RUN(DocumentLinterTests)`。
- [ ] **Step 5: lint**

---

# M1 —— `<Collapsible>` 骨架

## Task 3: parser / IR / template：`<Header>` 摘出（spec §4.2 / §6）

**Files:**
- Modify: `Runtime/Core/IR/ElementNode.cs`、`Runtime/Core/Parser/UIDocumentParser.cs`、`Runtime/Core/Template/TemplateExpander.cs`
- Test: `Tests/EditMode/Parser/CollapsibleHeaderParseTests.cs`

**Interfaces：**
```csharp
// ElementNode
public List<ElementNode> HeaderChildren { get; set; }   // null = 无 <Header>；Collapsible 专用
// UIDocumentParser.ParseElement：node.Tag == "Collapsible" 时，若 Children[0].Tag == "Header" → HeaderChildren = 它的 Children，从 Children 移除
//   <Header> 出现在非首位 / 多个 / 非 Collapsible 下 → **不在 parser 报**（留给 lint 的 PUI-COLLAPSIBLE-HEADER-FIRST / -MULTI / PUI-HEADER-OUTSIDE，CLI 与运行时同一份），parser 只摘首位的那一个
//   <Header> 自身属性：v1 拒绝任何属性 → ParseException("<Header> takes no attributes in v1")
// TemplateExpander：遍历 Children 的每处（{{param}} 替换、if=、嵌套调用内联、id 作用域收集）对 HeaderChildren 同样跑一遍
// ThemeStyleForbiddenAttrs 加 "expanded"，ForbiddenAttrReason 的 runtime-owned 分支加它
```

- [ ] **Step 1: 红测试** —— `CollapsibleHeaderParseTests`：`<Collapsible><Header><Text id='c'>x</Text></Header><Btn/></Collapsible>` ⇒ `HeaderChildren.Count == 1 && Children.Count == 1`；无 Header ⇒ `HeaderChildren == null`；`<Header height='24'>` ⇒ `ParseException`；模板体 `<Template name='P'><Param name='t'/><Collapsible><Header><Text>{{t}}</Text></Header><Slot/></Collapsible></Template>` 调用后 Header 里的 `{{t}}` 被替换、`<Slot/>` 注入到 body；`<Style name='s' expanded='false'/>` 在 `<Theme>` 里 ⇒ 错误消息含 "runtime-owned"。
- [ ] **Step 2: RUN(CollapsibleHeaderParseTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(UIDocumentParserTests)` / `RUN(TemplateExpanderTests)`（既有类名以目录为准）回归；`cd .lint && dotnet build UIXmlLint`。
- [ ] **Step 5: lint**

---

## Task 4: `Collapsible` 控件本体 + 注册 + 实例化（spec §4.1 / §4.3 / §5.1 / §5.3 / §5.5）—— `transition=0` 路径

**Files:**
- Create: `Runtime/Controls/Collapsible.cs`
- Modify: `Runtime/Application/BuiltinPrimitives.cs`、`Runtime/Core/Lint/BuiltinTags.cs`、`Runtime/Application/ScreenInstantiator.cs`、`Runtime/Core/Lint/HugRules.cs`、`ProceduralSurfaceRules.cs`、`GradientStopRules.cs`、`NavTargetRules.cs`、`Editor/XsdGenerator.cs`、`Editor/I18n/XmlStringScanner.cs`
- Test: `Tests/EditMode/Controls/CollapsibleTests.cs`、`Tests/EditMode/Controls/CollapsibleSizeTests.cs`、`Tests/EditMode/Editor/XsdGeneratorTests.cs`

**Interfaces（公开面）：**
```csharp
public sealed class Collapsible : ProceduralControl, IStateSource, IExpandable, IHugContent {
  // 内部节点：Header(RT+Image+PuiButton+LE) { Icon/Label/Host/Arrow }  Body(RT+RectMask2D+CanvasGroup+LE [+ScrollRect]) { Content(RT+VLG [+CSF]) }
  protected internal override Transform ChildHostTransform => _content;
  private protected override GameObject SurfaceHost => GameObject;          // 主表面 = 根（COL-D6）
  internal RectTransform HeaderHost => _headerHost;                          // ScreenInstantiator 把 HeaderChildren 挂这里

  [UIAttr, Preserve] public string Text { get; set; }   [UIAttr] Font / FontSize / TextColor / Tr / Ctx
  [UIAttr(IsSprite = true)] public string Icon;  [UIAttr(IsColor = true)] IconColor
  [UIAttr(IsSprite = true)] public string Arrow; [UIAttr(IsColor = true)] ArrowColor; [UIAttr] float ArrowSize
  [UIAttr] public float HeaderHeight = 44f;  [UIAttr(IsColor = true)] HeaderColor; [UIAttr(IsSprite = true)] HeaderSprite
  [UIAttr(IsColor = true)] HoverColor / PressedColor / DisabledColor / HoverModulate / PressedModulate / DisabledModulate; [UIAttr] PressedOffset
  [UIAttr] public bool Expanded { get; set; }            // 运行期独占；setter 走 Expand()/Collapse()（transition 按当前值）
  [UIAttr] public string Group;                          // Screen.CollapsibleGroups
  [UIAttr] public string Transition;                     // AnimationSpec.ParseSeconds；默认 0.2
  [UIAttr] public float MaxHeight;                       // 0 = 无
  [UIAttr] public float Spacing; [UIAttr] public string Padding;   // → _content 的 VLG
  public bool IsExpanded { get; private set; }
  public void Expand(); public void Collapse(); public void Toggle();
  public Observable<Unit> OnExpanded / OnCollapsed; public Observable<bool> OnToggled; public Observable<InteractState> OnState;
  public override bool Interactable { get; set; }        // 同 Btn：级联 PuiButton.interactable
  internal override string PeekRuntimeState() => IsExpanded ? "true" : "false";
  public override Vector2? GetNativeSize();              // 宽：max(caption.ContentWidth(), body preferredWidth)；高：null（恒 hug，由 fitter / 父组量）
  float IHugContent.ContentSize(int axis) => LayoutUtility.GetPreferredSize(RectTransform, axis) via 根 VLG 的 preferred*
}
```
`ApplyCommon` 的钩子：`Collapsible` 覆盖一个 `internal virtual bool ForceHugHeight => true`（`Control` 里默认 false）—— 自由定位分支对该控件在 Y 轴强制走 `SyncClampFitter(Hug)`、忽略 `HasHeight`（lint 已把 `height=` 判为错误；运行时 `ControlAttributeApplier` 对 `PUI-COLLAPSIBLE-HEIGHT` 抛 `ParseException`，见 Task 6）。`ScreenInstantiator.InstantiateRecursive`：`node.HeaderChildren != null` 时先把它们实例化到 `((Collapsible)control).HeaderHost`（`parentIsLayoutGroup: false`、同一 `childScope`、`parentControl: control`），再按常规实例化 `Children` 到 `ChildHostTransform`；`selfIsLayoutGroup` 加 `Collapsible`（只对 Children 生效）。注册：`Register<Collapsible>("Collapsible", null, runtimeStateAttr: "expanded")`。XSD：`Collapsible` 反射生成；`<Header>` 在 `WriteControl` 之后为 `Collapsible` 元素补 `xs:sequence` 的可选首子元素 `Header`（`minOccurs=0 maxOccurs=1`，其内容 = 任意控件序列）。

- [ ] **Step 1: 红测试** ——
  - `CollapsibleTests`：结构（`Header` / `Body` / `Content` / `Arrow` 节点与组件：`PuiButton`、`RectMask2D`、`CanvasGroup`、`VerticalLayoutGroup`、`RotateFlipEffect`）；`text='任务'` 懒建 Label 且 `TMP.text == "任务"`；`<Header>` 子节点挂在 `Header/Host` 下且 `Get<Text>("p/count")` 命中；`icon=` 建 Icon；`arrow=''` 隐藏且 Host 宽 = 标题栏宽；`headerHeight='24'` ⇒ Header `LayoutElement.preferredHeight == 24`；`expanded='false' transition='0'` 开屏 ⇒ `IsExpanded == false`、Body LE 0、`Content.activeSelf == false`、`Rotation == 180`、`alpha == 0`；`Toggle()` ⇒ 全部反过来、`OnExpanded` 计 1、`OnToggled` 收到 true；`PeekRuntimeState()` 随 `Toggle()` 变；`interactable='false'` ⇒ `PuiButton.interactable == false` 且 `Toggle()` 无效；`spacing` / `padding` 落到 `Content` 的 VLG；`OnState` 广播（`ExecuteEvents` 模拟 PointerEnter ⇒ Hover）；`maxHeight='100'` ⇒ Body 有 `ScrollRect(vertical)`、`Content` 有 `ContentSizeFitter`。
  - `CollapsibleSizeTests`（`Drain()`）：自由定位 `<Collapsible anchor='top-right' width='150' headerHeight='24' transition='0'>` 三个 32 高 Btn ⇒ 根高 120；收起 ⇒ 24；`maxHeight='60'` ⇒ 84 且 content 高 96 > viewport；`<VStack>` 里两个 Collapsible + 底部 Btn：收起上面那个 ⇒ 底部 Btn 上移 96；不写 `width` ⇒ 宽 = max(caption, content)；`width='stretch'` 在 VStack 里铺满。
  - `XsdGeneratorTests`：`StringAssert.Contains("name=\"Collapsible\"")`、`Contains("name=\"Header\"")`、`Contains("name=\"expanded\"")`。
- [ ] **Step 2: RUN** 三个类红（`BuiltinTagsTests` 会在注册后、加表前红一次 —— 一起加）。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(BuiltinTagsTests)`、`RUN(ProceduralAttrNamesTests)`、`RUN(HugSizingTests)`、`RUN(NavTargetRulesTests)`、EditorOnly 套件回归。
- [ ] **Step 5: lint**

---

## Task 5: `expand` / `collapse` 触发器指到 Collapsible（spec §4.5）

**Files:**
- Modify: `Runtime/Controls/Collapsible.cs`（挂 `ExpandableMarker`、`OnExpanded` / `OnCollapsed` 的触发时机）
- Test: `Tests/EditMode/Controls/CollapsibleTriggerTests.cs`

- [ ] **Step 1: 红测试** —— body 里 `<Trigger id='t' on='expand'>` 裸形式解析到 Collapsible（`Toggle()` 两次 ⇒ `t.OnFire` 计 1、`on='collapse'` 计 1）；开屏 `expanded='true'` **不**触发 `expand`（计 0）；`expanded='false'` 不触发 `collapse`；TabMenu 里嵌 Collapsible、Collapsible 的 body 里 `on='expand'` 指 Collapsible、TabMenu 行里的 `on='expand'` 指 TabMenu（最近祖先）；`on='expand@tasks'` 从兄弟位置命中（FND 的 `ResolveId`）；`<Animation on='expand' reverse-on='collapse' translate='-12,0:0,0'>` 行：`Expand()` 后 handle 活跃、`Collapse()` 后 `OnReverse` 计 1（EditMode 只断言订阅成对，不等补间）；`expand` 触发时 `Content.activeInHierarchy == true`（先激活再触发）。
- [ ] **Step 2: RUN(CollapsibleTriggerTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(TabMenuTriggerTests)` 回归。
- [ ] **Step 5: lint**

---

## Task 6: lint `CollapsibleRules` + 双分发 + 运行时硬错（spec §4.8）

**Files:**
- Create: `Runtime/Core/Lint/CollapsibleRules.cs`
- Modify: `Runtime/Core/Lint/IRWalker.cs`、`Runtime/Application/ScreenInstantiator.cs`、`Runtime/Application/ControlAttributeApplier.cs`
- Test: `Tests/EditMode/Lint/CollapsibleRulesTests.cs`

**Interfaces（纯 C#）：**
```csharp
public static class CollapsibleRules {
  public const string HeightCode = "PUI-COLLAPSIBLE-HEIGHT", HeaderFirstCode = "PUI-COLLAPSIBLE-HEADER-FIRST",
    HeaderMultiCode = "PUI-COLLAPSIBLE-HEADER-MULTI", HeaderConflictCode = "PUI-COLLAPSIBLE-HEADER-CONFLICT",
    HeaderOutsideCode = "PUI-HEADER-OUTSIDE", GroupMultiExpandedCode = "PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED";
  public static IEnumerable<LintIssue> CheckCollapsible(ElementNode n, StyleAttributeView styles);   // Height（含 .variant、经 class=）/ HeaderFirst / HeaderMulti（看残留在 Children 里的 "Header" 节点）/ HeaderConflict
  public static IEnumerable<LintIssue> CheckHeaderOutside(ElementNode parent, ElementNode child);      // child.Tag == "Header" && parent.Tag != "Collapsible"
  public static IEnumerable<LintIssue> CheckGroups(ElementNode screenRoot);                             // 同 group 内 expanded 有效值为 true 的 > 1（默认 true 也算）
}
```
`IRWalker`：`else if (node.Tag == "Collapsible")` 分发；子节点循环里 `CheckHeaderOutside`；`isLayoutGroup` 对 `Collapsible` 为 true（`HeaderChildren` 不在 `Children` 里，天然不被布局组子节点规则扫到；但要**另行**对 `HeaderChildren` 递归 `WalkNode`，`parentIsLayoutGroup: false`）；Screen 级 `CheckGroups`（在 `WalkScreen` 处一次）。`ControlAttributeApplier`：`PUI-COLLAPSIBLE-HEIGHT` 抛 `ParseException`。

- [ ] **Step 1: 红测试** —— 六条各一正一反；`<Collapsible><Btn anchor='center'/></Collapsible>` ⇒ `PUI-LAYOUT-ANCHOR`，`<Collapsible><Header><Text anchor='center-left'/></Header>…` **不**报；`<Collapsible class='h'>` + `<Style name='h' height='100'/>` ⇒ `PUI-COLLAPSIBLE-HEIGHT`；`UI.Open` 含 `height='100'` 的 Collapsible ⇒ `ParseException` 含 `[PUI-COLLAPSIBLE-HEIGHT]`。
- [ ] **Step 2: RUN(CollapsibleRulesTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(DocumentLinterTests)`、`RUN(LayoutGroupChildRules*)` 回归；`cd .lint && dotnet build UIXmlLint`。
- [ ] **Step 5: lint**

---

# M2 —— 过渡

## Task 7: 三通道过渡 + 打断安全 + 停用 / 激活时序（spec §5.2）

**Files:**
- Modify: `Runtime/Controls/Collapsible.cs`
- Test: `Tests/PlayMode/Controls/CollapsiblePlayTests.cs`

**Interfaces（私有实现要点）：**
```csharp
private MotionHandle _heightMotion, _fadeMotion, _arrowMotion;
private void PlayTransition(bool expanding) {
  CancelMotions();
  if (expanding) { _content.gameObject.SetActive(true); }
  var target = expanding ? Mathf.Min(RevealDriver.Measure(_content, 1), MaxHeightOrInf) : 0f;   // Measure 内含 InactiveMeasure
  if (_transition <= 0f || !Application.isPlaying) { 写终态；if (!expanding) _content.SetActive(false); return; }
  _heightMotion = LMotion.Create(_bodyLe.preferredHeight, target, _transition).WithEase(OutCubic).Bind(...RevealDriver.ApplyBox(_body, 1, v, inLayoutGroup: true)).AddTo(_body.gameObject);
  _fadeMotion   = LMotion.Create(_bodyCg.alpha, expanding ? 1f : 0f, _transition)…
  _arrowMotion  = LMotion.Create(_arrowFx.Rotation, expanding ? 0f : 180f, _transition)…
  if (!expanding) { var captured = _heightMotion; _heightMotion.GetAwaiter().OnCompleted(() => { if (!IsExpanded && captured.Equals(_heightMotion)) _content.gameObject.SetActive(false); }); }
  RevealDriver.SetClip(_body.gameObject, true);   // 到达展开终态时 SetClip(false)
}
// Expand(): if (IsExpanded || !Interactable || !GameObject.activeInHierarchy) return; group 互斥（Task 8）; IsExpanded = true; PlayTransition(true); _expanded.OnNext; _toggled.OnNext(true)
// OnAfterApply(): base; 若 expanded 属性与 IsExpanded 不一致且未被运行时锁 → 走 Expand/Collapse（transition 生效）；开屏首次 expanded=false → DeferDuringOpen(HideBodyIfCollapsed)；重断言 body LE 当前值
```

- [ ] **Step 1: 红测试** —— `CollapsiblePlayTests`（`transition='0.2s'`）：`Collapse()` 后逐帧采样 Body LE 单调递减、下方兄弟 `anchoredPosition.y` 单调上移；0.3s 后 `Content.activeSelf == false`、`Rotation ≈ 180`、`alpha ≈ 0`；`Collapse()` 0.1s 后 `Expand()` ⇒ 相邻两帧 LE 差 < 20（无跳变）且 0.3s 后 `Content.activeSelf == true`（上一次收起的停用回调被 handle 比对拦下）；`Expand()` 时 `expand` 触发在 `Content` 已激活之后；`Collapse()` 中途 `UI.Variants.Set("x", true)` 触发 ReSolve ⇒ LE 不被重置回终态（连续性断言）；`hidden='true'` 的 Collapsible `Expand()` 直接置终态。
- [ ] **Step 2: RUNPLAY(CollapsiblePlayTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(CollapsibleTests)`（transition=0 路径不变）、`RUNPLAY(TabMenuPlayTests)`、`RUNPLAY(AnimationRevealPlayTests)` 回归。
- [ ] **Step 5: lint**

---

# M3 —— `group=`、运行期独占、文档

## Task 8: `group=` 手风琴 + Variant 裁决（spec §4.6 / §4.7）

**Files:**
- Create: `Runtime/Controls/Internal/CollapsibleGroupRegistry.cs`
- Modify: `Runtime/Application/Screen.cs`、`Runtime/Controls/Collapsible.cs`
- Test: `Tests/EditMode/Controls/CollapsibleGroupTests.cs`

**Interfaces：**
```csharp
internal sealed class CollapsibleGroupRegistry {
  public void Add(string group, Collapsible c);   public void Remove(string group, Collapsible c);
  public void NotifyExpanding(string group, Collapsible c);            // 同组其它 IsExpanded 的 → Collapse()
  public Collapsible FirstExpanded(string group);                       // 开屏裁决用
  public void Clear();
}
// Screen: internal CollapsibleGroupRegistry CollapsibleGroups { get; private set; }（与 ToggleGroups 同处构造 / Close 时 Clear）
// Collapsible.Group setter：换组时 Remove 旧、Add 新；Dispose 时 Remove
// 开屏裁决：Screen 在 Open 的 apply 结束、DeferDuringOpen 排空前，对每个 group 保留 FirstExpanded（文档顺序 = 实例化顺序），其余 Collapse(instant)；运行时 warning 镜像 PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED
// Variant：expanded.variant 切到 true 时走 Expand() → NotifyExpanding 自然互斥
```

- [ ] **Step 1: 红测试** —— 三个同组 `transition='0'`：`b.Expand()` ⇒ `a.IsExpanded == false`；`b.Collapse()` ⇒ 全收起合法；开屏 `a` `b` 都 `expanded='true'` ⇒ 只有 `a` 展开且 console 有 warning（`LogAssert.Expect`）；`expanded.portrait='true'` 于 `c` + 切 `portrait` ⇒ `c` 展开、其余收起；跨组互不影响；`Screen.Close()` 后 registry 空；Add 块动态加入的成员按到达顺序参与。
- [ ] **Step 2: RUN(CollapsibleGroupTests)** 红。
- [ ] **Step 3: 实现**。
- [ ] **Step 4: 绿**；`RUN(CollapsibleTests)`、`RUN(ScreenTests)`（既有）回归。
- [ ] **Step 5: lint**

---

## Task 9: 文档（英文 SKILL；同 PR）

**Files:**
- Create: `.claude/skills/authoring-promptugui-xml/reference/controls-collapsible.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`、`reference/animations.md`、`reference/controls-tabs.md`、`reference/navigation.md`、`.claude/skills/scripting-promptugui-csharp/SKILL.md`、`PromptUGUI/CLAUDE.md`、`.lint/UIXmlLint/README.md`

- [ ] **SKILL.md**：内置目录表加 `<Collapsible>` 行；新 `### <Collapsible>` 小节（属性表全量、`<Header>` 规则、body = VStack 语义、`expanded` 运行期独占、`group=`、`transition`、`maxHeight`、高度恒 hug 的 `PUI-COLLAPSIBLE-HEIGHT`、动态行用 `<ScrollList height="clamp(_, hug, N)">`）；程序化表面标签列表（三处）、`focus=` 列表、native-size 列表、`tint=` 列表、nav 列表各加一项；uGUI 对照表加 `<Collapsible>` 一行（Header / Body / Content 结构 + 事件源）；Quick reference 一行；Templates 段补一句"`<Header>` 在模板体内同样参与 `{{param}}` 与 `<Slot/>`"。
- [ ] **reference/controls-collapsible.md**：结构图（§5.1）、属性表、三个配方（HUD 任务面板 / 自定义标题 / 手风琴）、与 `<TabMenu>` 的取舍表、过渡与打断语义、lint 表、C# 片段。
- [ ] **reference/animations.md**：`expand` / `collapse` 两行的源改为 "`<TabMenu>` or `<Collapsible>`"；错误消息更新；行动画 pattern 改 `reverse-on="collapse"`。
- [ ] **reference/controls-tabs.md**：`<TabMenu>` 小节开头加 "For an inline, always-visible fold use `<Collapsible>`"。
- [ ] **reference/navigation.md**：Selectable 列表加 `<Collapsible>`；一句"collapsed body is inactive → skipped by the nav graph"。
- [ ] **scripting SKILL**：§5.5 的 API 块 + "`Expanded` setter 走 Expand/Collapse，播过渡"。
- [ ] **CLAUDE.md** 第 31 行路由表加 `controls-collapsible.md`。
- [ ] **README 代码表**：六条 `PUI-COLLAPSIBLE-*` / `PUI-HEADER-OUTSIDE`（`HEIGHT` 标 runtime throw）。
- [ ] 若 SKILL 示例落成 fixture，跑 UIXmlLint。

---

## Task 10: 全套件 + 收尾

- [ ] `RUN` 全量 EditMode、EditorOnly、`RUNPLAY` 全量（每次 job 前 force refresh，核对 `total`）。
- [ ] `read_console(types=["error"])` 为空；`warning` 里无 "layout rebuild while we are already inside a layout rebuild loop"、无 TabMenu 的 "no <TabBar> ancestor" 之外的新 warning。
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`；`dotnet build .lint/UIXmlLint`。
- [ ] spec §13 写实施记录：TabMenu 抽零件的实际边界、`ForceHugHeight` 钩子的最终形态、过渡打断的最大帧间跳变实测、开屏裁决落在 Open 的哪个阶段、箭头 180° 的观感结论（§11 开放问题 1）。
- [ ] `git add -A && git commit`（分支上），推 `feat/collapsible`，开 PR（正文引用 spec / plan）。

## Self-Review 注记（写计划时已核）

- `TabMenu` 的 caption 是"选中项 icon + text"**动态**的（`RefreshCaption` 订阅 `Tab.ContentChanged`），`CaptionBuilder` 只抽"建节点 + 排版 + 属性落点"，数据来源留在各自控件里（spec §11 开放问题 5 的既定边界）。
- `TabMenu.SetArrowFlip` 用负缩放是因为箭头 pivot 在左沿（`TabMenu.cs:655-665`）；`RotateFlipEffect` 绕 rect 中心改顶点、不碰 transform，同一个箭头节点换实现后位置不动 —— `TabMenuPlayTests` 的位置断言（如有）应保持通过。
- `ScreenInstantiator.InstantiateRecursive` 对 `Children` 的实例化用 `control.ChildHostTransform`；`HeaderChildren` 要走 `HeaderHost`，且 `parentIsLayoutGroup` 传 false（Header 子节点是自由定位）—— 否则 `ApplyCommon` 会把它们当布局组子节点写 `LayoutElement`。
- `Collapsible` 是 `ProceduralControl`，`SurfaceHost` = 根 GO：`radius` / `glass` 圈整块；`headerColor` 是 Header 节点上独立 `Image` 的颜色，与 `hoverColor` 等同层（`StateTintInstaller` 的 `targetGraphic` = 那张 Image）。
- 根 VLG `childForceExpandWidth = true` 让 Header / Body 铺满宽度；Body 的 LE 只写高度，宽度随根。
- `expanded` 注册为 `runtimeStateAttr`，`ControlAttributeApplier` 在运行时锁定后跳过重放；但 `expanded.variant` 的切换仍会进 setter（applier 的既有规则）—— Task 8 的 Variant 用例钉它。
- `FND` 的 `HugRules.HugTags` 与 `IRWalker.isLayoutGroup` 都要加 `Collapsible`；`BuiltinTagsTests` 守注册 ↔ 表，`ProceduralAttrNamesTests` 守 `SurfaceTags` ↔ `ProceduralControl` 子类，两者都会在漏加时自动红。
- `<Header>` 不进 `BuiltinTags`（不是控件）：`IRWalker` 遇到残留的 `Header` 节点（非首位 / 多个 / 非 Collapsible 下）只报 `CollapsibleRules` 的码，不走"未知标签"路径 —— 需要在未知标签检查前短路。
- 开屏 `expanded='false'` 的 body 停用走 `DeferDuringOpen`（`Screen.cs:216`），与 `Tab.bind` / `TabMenu` 同一条路，TMP 在 active 时先量一次。
