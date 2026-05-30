# `<Tab>` 容器化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `<Tab>` 像 `<Btn>` 一样接受子节点(可点击容器),并补齐 Btn 对齐的懒 label / 懒 icon / `color` 属性。

**Architecture:** 引擎本就对所有控件实例化子节点(`ScreenInstantiator.InstantiateRecursive` 把子节点挂到 `control.ChildHostTransform`),所谓"叶子"只是一条执行不一致的 lint。本计划移除 Tab 的 leaf-lint(`PUI-TAB-CHILDREN`),把 Tab 的 label/icon 改成懒创建(不写 `text`/`icon` 就不建对应 GameObject),并新增 `color` 属性(`#00000000` = 透明可点)。点击穿透沿用 uGUI 机制(Tab bg 是 `targetGraphic`+raycast on,子节点 raycast off 即穿透激活),无需新代码。

**Tech Stack:** Unity 6 uGUI + TMP;C# 9;R3(Cysharp);纯 C# lint(`Runtime/Core/Lint`);测试经 UnityMCP 跑(EditMode)。

**Branch:** `feat/tab-container`(已创建,spec 已提交 `dce1a47`)。**DO NOT commit to main.**

**Spec:** `docs~/superpowers/specs/2026-05-30-tab-container-design.md`

---

## 通用约定(每个 Task 的 run 步骤都这么跑)

本项目**只**经 UnityMCP 跑测试,不用 batch-mode。源码改动后**先 refresh、再读 console 确认无编译错、最后跑测试**。MCP 工具需先 `ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)` 加载。

- 编译刷新:`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- 查编译错:`mcp__UnityMCP__read_console(action="get", types=["error"])` → 期望空
- 跑测试:`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="<ClassName>")`

若 MCP 不可用,按 CLAUDE.md:尝试重连或让用户重启 MCP。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Core/Lint/TabRules.cs` | Tab/TabBar lint 规则 | 删 `CheckTab` + `TabChildrenCode` |
| `Runtime/Core/Lint/IRWalker.cs` | CLI lint 遍历(纯 C#) | 删 `Tab` dispatch 分支(L51-53) |
| `Runtime/Application/ScreenInstantiator.cs` | runtime 实例化 + 同源 lint warn | 删 `Tab` dispatch 分支(L194-195) |
| `Runtime/Controls/Tab.cs` | Tab 控件 | label 懒创建、icon 解依赖、新增 `color` |
| `Tests/EditMode/Lint/TabRulesTests.cs` | lint 单测 | 删两个 `CheckTab` 断言用例 |
| `Tests/EditMode/Lint/IRWalkerTabChildrenTests.cs` | CLI walk 回归(新) | 断言 Tab-带子 不再被 flag |
| `Tests/EditMode/TabControlTests.cs` | Tab 控件行为(新) | 接子 / 懒 label / 懒 icon / color |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | XML 作者指南 | Tab 接子、`color`、删 lint 行 |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | 主 spec §5 控件表 | Tab 行补 `color`、改"容器"措辞 |
| XSD(由 Unity 菜单生成) | schema | 重新生成(新增 `color`) |

---

## Task 1: 移除 Tab 的 leaf-lint(`PUI-TAB-CHILDREN`)

**Files:**
- Create: `Tests/EditMode/Lint/IRWalkerTabChildrenTests.cs`
- Modify: `Runtime/Core/Lint/TabRules.cs:16,19-26`
- Modify: `Runtime/Core/Lint/IRWalker.cs:51-53`
- Modify: `Runtime/Application/ScreenInstantiator.cs:194-195`
- Modify: `Tests/EditMode/Lint/TabRulesTests.cs:11-26`

- [ ] **Step 1: 写失败测试 —— CLI walk 不再 flag "Tab 带子"**

新建 `Tests/EditMode/Lint/IRWalkerTabChildrenTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    [TestFixture]
    public class IRWalkerTabChildrenTests
    {
        // A <Tab> with nested children is now legal (container model).
        // The old PUI-TAB-CHILDREN lint must no longer fire from the CLI walker.
        [Test]
        public void TabWithChildren_NotFlagged()
        {
            var tab = new ElementNode("Tab") { Id = "t" };
            tab.Children.Add(new ElementNode("Icon") { Id = "ic" });

            var bar = new ElementNode("TabBar") { Id = "bar" };
            bar.Children.Add(tab);

            var screen = new ScreenDef { Name = "S", Root = bar };
            var doc = new UIDocument();
            doc.Screens.Add(screen);

            var issues = IRWalker.Walk(doc).ToList();
            Assert.That(issues.Any(i => i.Code == "PUI-TAB-CHILDREN"), Is.False);
        }
    }
}
```

- [ ] **Step 2: 跑测试,确认 FAIL**

`ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)` →
`refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` →
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerTabChildrenTests")`
期望:FAIL —— 当前 `CheckTab` 仍对带子 Tab 报 `PUI-TAB-CHILDREN`。

- [ ] **Step 3: 删 `TabRules.CheckTab` + `TabChildrenCode`**

在 `Runtime/Core/Lint/TabRules.cs`:
1. 删常量行(L16):`public const string TabChildrenCode = "PUI-TAB-CHILDREN";`
2. 删整个方法(L19-26):

```csharp
        public static IEnumerable<LintIssue> CheckTab(ElementNode n)
        {
            if (n.Children.Count > 0)
                yield return new LintIssue(
                    TabChildrenCode, n.Tag, n.Id,
                    $"<Tab id='{n.Id}'>: Tab is a leaf control; nested children are not allowed. " +
                    "Use text / icon attributes to express content.");
        }
```

保留 `TabParentCode` / `TabBarChildCode` / `DirectionCode` 常量与 `CheckTabBar` 方法不动。

- [ ] **Step 4: 删两处 `CheckTab` 调用点**

`Runtime/Core/Lint/IRWalker.cs` —— 删 L51-53 这个分支:

```csharp
            else if (node.Tag == "Tab")
                foreach (var issue in TabRules.CheckTab(node))
                    yield return issue;
```

删后,其上的 `Progress` 分支直接接 `else if (node.Tag == "TabBar")`(L54-56 的 TabBar 分支保留)。

`Runtime/Application/ScreenInstantiator.cs` —— 删 L194-195 这个分支:

```csharp
            else if (node.Tag == "Tab")
                LogLintWarnings(TabRules.CheckTab(node));
```

删后,`Progress` 分支(L192-193)直接接 `else if (node.Tag == "TabBar")`(L196-197 保留)。

- [ ] **Step 5: 删旧的 `CheckTab` 单测**

`Tests/EditMode/Lint/TabRulesTests.cs` —— 删这两个用例(它们引用已删除的 `CheckTab` / `TabChildrenCode`,否则编译失败):

```csharp
        [Test]
        public void Tab_WithChildren_Flagged()
        {
            var tab = new ElementNode("Tab");
            tab.Children.Add(new ElementNode("Text"));
            var issues = TabRules.CheckTab(tab).ToList();
            Assert.That(issues.Any(i => i.Code == TabRules.TabChildrenCode));
        }

        [Test]
        public void Tab_NoChildren_Ok()
        {
            var tab = new ElementNode("Tab");
            var issues = TabRules.CheckTab(tab).ToList();
            Assert.That(issues, Is.Empty);
        }
```

保留 `TabBar_NonTabChild_Flagged` / `TabBar_Direction_Invalid_Flagged` / `TabBar_NonTabChild_TemplateWrapper_Suppressed`。

- [ ] **Step 6: refresh + 查编译错**

`refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` →
`read_console(action="get", types=["error"])`
期望:空(无 CS 编译错;确认没遗漏对 `CheckTab`/`TabChildrenCode` 的引用)。

- [ ] **Step 7: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabRules")` →
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IRWalkerTabChildrenTests")`
期望:全部 PASS。

- [ ] **Step 8: Commit**

```bash
git add Runtime/Core/Lint/TabRules.cs Runtime/Core/Lint/IRWalker.cs Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Lint/TabRulesTests.cs Tests/EditMode/Lint/IRWalkerTabChildrenTests.cs
git commit -m "feat: <Tab> accepts children — remove PUI-TAB-CHILDREN leaf lint

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Tab label 懒创建(Btn 对齐)

**Files:**
- Create: `Tests/EditMode/TabControlTests.cs`
- Modify: `Runtime/Controls/Tab.cs:27-55`(OnAttached + 新增 EnsureLabel)、`Tab.cs:136-161`(Text/Font/FontSize setter)

- [ ] **Step 1: 写失败测试 —— 不写 text 无 label,写 text 有 label**

新建 `Tests/EditMode/TabControlTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode
{
    [TestFixture]
    public class TabControlTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static Tab LoadTab(string tabXml)
        {
            var screen = UI.LoadDocument(
                "test",
                $"<Screen name=\"S\"><TabBar id=\"bar\">{tabXml}</TabBar></Screen>");
            return screen.Get<Tab>("t");
        }

        [Test]
        public void Tab_WithText_CreatesLabel()
        {
            var tab = LoadTab("<Tab id=\"t\" text=\"Hi\"/>");
            var label = tab.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.That(label, Is.Not.Null);
            Assert.That(label.text, Is.EqualTo("Hi"));
        }

        [Test]
        public void Tab_NoText_NoLabel()
        {
            var tab = LoadTab("<Tab id=\"t\"/>");
            var label = tab.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.That(label, Is.Null);
        }
    }
}
```

- [ ] **Step 2: 跑测试,确认 FAIL**

`refresh_unity(...)` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabControlTests")`
期望:`Tab_NoText_NoLabel` FAIL —— 当前 `OnAttached` 无条件建 label。

- [ ] **Step 3: 改 OnAttached + 加 EnsureLabel**

`Runtime/Controls/Tab.cs` —— 把 `OnAttached`(L27-55)中的 label 创建块删掉,新增 `EnsureLabel()`。改后 `OnAttached`:

```csharp
        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultBtnColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
            _toggle.targetGraphic = _bg;
            _toggle.transition = Selectable.Transition.ColorTint;

            var group = FindAncestorToggleGroup();
            if (group == null)
                Debug.LogWarning($"Tab '{Id}' has no <TabBar> ancestor; mutual exclusion disabled.");
            else
                _toggle.group = group;

            _toggle.onValueChanged.AddListener(OnIsOnChanged);
            UI.Locale.Changed += ApplyFont;
        }

        private TMP_Text EnsureLabel()
        {
            if (_label != null) return _label;
            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.alignment = TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            _label.fontSize = 24;
            _label.text = "";
            var lrt = _label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = _icon != null ? new Vector2(32f, 0f) : Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            ApplyFont();
            return _label;
        }
```

(`ApplyFont` 已有 `if (_label == null) return` 守卫,不变。)

- [ ] **Step 4: 改 Text / Font / FontSize setter 走 EnsureLabel**

`Runtime/Controls/Tab.cs` —— 三个 setter 改为:

```csharp
        [UIAttr, Preserve]
        public string Text
        {
            set
            {
                if (string.IsNullOrEmpty(value) && _label == null) return;
                EnsureLabel().text = value ?? "";
            }
        }

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                if (_label != null) ApplyFont();
            }
        }

        [UIAttr("fontSize"), Preserve]
        public int FontSize
        {
            set => EnsureLabel().fontSize = value;
        }
```

(`PeekDefaultText` 已是 `_label != null ? _label.text : null`,不变。)

- [ ] **Step 5: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])` 期望空。

- [ ] **Step 6: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabControlTests")` → 期望 PASS。
回归:`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabBar")` → 期望 PASS(`StaticTabs_AutoSelectFirst` / `Tab_Bind_TogglesFrame` 仍过)。

- [ ] **Step 7: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/TabControlTests.cs
git commit -m "feat: Tab label is lazily created (Btn parity)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Tab icon 懒创建解依赖 + icon/text 组合布局

**Files:**
- Modify: `Runtime/Controls/Tab.cs`(Icon setter)
- Modify: `Tests/EditMode/TabControlTests.cs`(加用例)

- [ ] **Step 1: 写失败测试 —— 只写 icon 不崩;icon+text label 右移**

在 `Tests/EditMode/TabControlTests.cs` 的类内追加:

```csharp
        [Test]
        public void Tab_IconOnly_NoLabel_NoCrash()
        {
            // icon setter must not NRE when there is no label.
            var tab = LoadTab("<Tab id=\"t\" icon=\"ui:gear\"/>");
            var label = tab.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.That(label, Is.Null);
            var icon = tab.GameObject.transform.Find("Icon");
            Assert.That(icon, Is.Not.Null);
        }

        [Test]
        public void Tab_IconAndText_LabelShiftedRight()
        {
            var tab = LoadTab("<Tab id=\"t\" icon=\"ui:gear\" text=\"Hi\"/>");
            var label = tab.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.That(label, Is.Not.Null);
            Assert.That(label.rectTransform.offsetMin.x, Is.EqualTo(32f));
        }
```

> 注:`<Tab>` 的 XML 属性存于 `Dictionary`,apply 顺序不保证。这两个测试覆盖"icon 在、label 不在(不崩)"和"两者都在 → 最终 offset=32"的不变式,等价覆盖两个 setter 的 null 安全与组合结果。

- [ ] **Step 2: 跑测试,确认 FAIL**

`refresh_unity(...)` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabControlTests")`
期望:`Tab_IconOnly_NoLabel_NoCrash` FAIL/报错 —— 当前 Icon setter 在 `_label` 为 null 时 NRE(`Tab.cs:177` 直接访问 `_label.rectTransform`)。
> 注:`icon="ui:gear"` 在无 sprite resolver 的 EditMode 下,`UI.ResolveSprite` 返回 null + `Debug.LogWarning("No sprite for key ...")`。这是**预期日志噪音**,不影响测试结果(Unity 测试只因 LogError/异常失败,warning 不失败);Icon GameObject 仍会创建,结构断言成立。

- [ ] **Step 3: 改 Icon setter 解除对 label 的硬依赖**

`Runtime/Controls/Tab.cs` —— Icon setter 改为(仅 `_label != null` 时才右移):

```csharp
        [UIAttr(IsSprite = true), Preserve]
        public string Icon
        {
            set
            {
                if (_icon == null)
                {
                    _icon = ProceduralBuilders.AddImage(RectTransform, "Icon", raycast: false);
                    var rt = _icon.rectTransform;
                    rt.anchorMin = new Vector2(0f, 0.5f);
                    rt.anchorMax = new Vector2(0f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(24f, 24f);
                    rt.anchoredPosition = new Vector2(16f, 0f);     // 4px gap from left edge then center of 24
                    // Shift label right to make room for icon — only if label already exists.
                    // If text is applied later, EnsureLabel() reads _icon != null and shifts itself.
                    if (_label != null) _label.rectTransform.offsetMin = new Vector2(32f, 0f);
                }
                _icon.sprite = UI.ResolveSprite(value);
            }
        }
```

(`EnsureLabel()` 已在 Task 2 写成 `offsetMin = _icon != null ? (32,0) : zero`,兜住"icon 先于 text"的顺序。)

- [ ] **Step 4: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])` 期望空。

- [ ] **Step 5: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabControlTests")` → 期望全部 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/TabControlTests.cs
git commit -m "feat: Tab icon no longer depends on label; order-independent icon+text layout

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Tab 新增 `color` 属性(Btn 对齐)

**Files:**
- Modify: `Runtime/Controls/Tab.cs`(新增 Color 属性)
- Modify: `Tests/EditMode/TabControlTests.cs`(加用例)

- [ ] **Step 1: 写失败测试 —— color 写入 bg;`#00000000` alpha 0**

在 `Tests/EditMode/TabControlTests.cs` 类内追加:

```csharp
        [Test]
        public void Tab_Color_AppliesToBg()
        {
            var tab = LoadTab("<Tab id=\"t\" color=\"#FF0000\"/>");
            var bg = tab.GameObject.GetComponent<Image>();
            Assert.That(bg.color, Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
        }

        [Test]
        public void Tab_TransparentColor_AlphaZero()
        {
            var tab = LoadTab("<Tab id=\"t\" color=\"#00000000\"/>");
            var bg = tab.GameObject.GetComponent<Image>();
            Assert.That(bg.color.a, Is.EqualTo(0f));
        }
```

- [ ] **Step 2: 跑测试,确认 FAIL**

`refresh_unity(...)` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabControlTests")`
期望:两个新用例 FAIL —— Tab 当前无 `color` 属性,attr 被忽略,bg 保持 `DefaultBtnColor`。

- [ ] **Step 3: 新增 Color 属性**

`Runtime/Controls/Tab.cs` —— 在已有的 `Sprite` / `SelectedSprite` 属性附近加(`_bg` 在 OnAttached 已无条件建,setter 无需 EnsureXxx):

```csharp
        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _bg.color = UI.Theme.Resolve(value);
        }
```

(`IsColor = true` 仅用于 XSD 文档生成;runtime 由 setter 自己调 `UI.Theme.Resolve`,与 `Btn.Color` 完全一致。)

- [ ] **Step 4: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])` 期望空。

- [ ] **Step 5: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabControlTests")` → 期望全部 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/TabControlTests.cs
git commit -m "feat: Tab gains color attribute (transparent-but-clickable via #00000000)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: 文档 + XSD(SKILL / 主 spec / schema)

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`
- 生成:XSD(Unity 菜单)

> CLAUDE.md 强制:任何功能变更必须在同 PR 更新对应 SKILL(英文)。本 Task 无自动化测试,改完用 lint CLI 跑一个示例做验证。

- [ ] **Step 1: 更新 `<Tab>` built-in 表行**

`.claude/skills/authoring-promptugui-xml/SKILL.md` —— 找到 `<Tab>` 那一行(约 L102)。属性列追加 `color` (hex / CSS named / theme token; `#00000000` = 透明可点);描述列把 "No nested XML children allowed." 改为 "Accepts nested XML children (overlaid Frame-style on the Tab bg). For click-through, children must set `raycastTarget="false"` (`<Icon>` already does); a child that keeps `raycastTarget=true` (e.g. a nested `<Btn>`) handles its own clicks instead."

- [ ] **Step 2: 重写 "Custom Tab layout via Template" 一节,补直接子节点写法**

`.claude/skills/authoring-promptugui-xml/SKILL.md` —— 在该节(约 L724)开头补"直接子节点"为首选简单路径,Template 包装保留为"共享样式 / 动态 BindItems"场景。加三个目标用例(对应 spec §1):

```xml
<!-- 1. Icon-only tab -->
<Tab bind="panel1"><Icon name="ui:gear"/></Tab>

<!-- 2. Icon + two lines of text -->
<Tab bind="panel2">
  <HStack>
    <Icon name="ui:file"/>
    <VStack>
      <Text raycastTarget="false">Title</Text>
      <Text raycastTarget="false">Subtitle</Text>
    </VStack>
  </HStack>
</Tab>

<!-- 3. Transparent tab: no chrome, only the icon, click switches page -->
<Tab color="#00000000" bind="panel3"><Icon name="ui:gear"/></Tab>
```

并补一句:Tab 不是 layout group —— 子节点用各自 `anchor` / `margin` 叠放(Frame 式),不像 TabBar 子项受 layout-group 规则约束。

- [ ] **Step 3: 删 lint 表里的 `PUI-TAB-CHILDREN` 行**

`.claude/skills/authoring-promptugui-xml/SKILL.md` —— 在 lint 规则表(约 L761)删掉:

```
| `PUI-TAB-CHILDREN`     | `<Tab>` 包含嵌套 XML children（auto label / icon 由属性驱动）                         | error   |
```

保留 `PUI-TAB-PARENT` / `PUI-TABBAR-CHILD` / `PUI-TABBAR-DIRECTION`。同时检查本文件其它处(如 quick-reference / 程序化层级表)对"Tab 不接子 / leaf"的措辞,改为"接子"。

- [ ] **Step 4: 更新主 spec §5 控件表 Tab 行**

`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` —— §5 控件表 `<Tab>` 行属性补 `color`;把"leaf"措辞改为"可点击容器(接子)"。

- [ ] **Step 5: 用 lint CLI 验证示例不再报错**

把 spec §1 用例 2(icon + 两行文字)存成一个临时 `.ui.xml`,跑:

```bash
cd .lint && dotnet run --project UIXmlLint -- /tmp/tab_children_example.ui.xml
```

期望:exit 0,无 `PUI-TAB-CHILDREN`。验证后删临时文件。

- [ ] **Step 6: 重新生成 XSD**

在 Unity 编辑器:`Tools → PromptUGUI → Schema → Generate XSD`(新增 `color` attr 进入 schema)。
> 这是 Unity 菜单操作,非 batch;若经 MCP 触发,用 `mcp__UnityMCP__execute_menu_item` 跑对应非模态菜单路径(确认不是 `Assets/Reimport All` 那类弹模态的——本菜单不弹框)。确认生成的 XSD 含 `color`。

- [ ] **Step 7: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md "docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md"
# 若 XSD 文件在仓库内(确认路径后):
# git add <xsd 路径>
git commit -m "doc: <Tab> container model — SKILL + spec + XSD (color, children, drop PUI-TAB-CHILDREN)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 收尾验证(全部 Task 完成后)

- [ ] `refresh_unity(...)` → `read_console(action="get", types=["error"])` 全空。
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全绿(尤其 `TabControlTests` / `TabRulesTests` / `IRWalkerTabChildrenTests` / `TabBarTests`)。
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 无改动。
- [ ] `git log --oneline feat/tab-container` 含 5 个功能 commit + spec commit;均在分支上,**未 push 到 main**。
- [ ] 完成后用 `superpowers:finishing-a-development-branch` 决定合并/PR 方式(交给用户)。

---

## Self-Review(plan 对 spec 覆盖检查)

- **TC-D1 删 leaf-lint** → Task 1 ✓
- **TC-D2 点击穿透(无代码,文档)** → Task 5 Step 1/2(raycast 契约写入 SKILL)✓
- **TC-D3 label 懒创建** → Task 2 ✓
- **TC-D4 icon 懒 + 解依赖** → Task 3 ✓
- **TC-D5 icon+text 顺序无关** → Task 3(EnsureLabel 读 `_icon`,Icon setter 读 `_label`,双向兜底 + 测试)✓
- **TC-D6 color 属性** → Task 4 ✓
- **TC-D7 透明无闪烁** → Task 4 `Tab_TransparentColor_AlphaZero`(ColorTint 从 alpha-0 基色相乘,机制由 uGUI 保证)✓
- **TC-D8 不做向后兼容** → 计划未加任何兼容 shim;`Tab_NoText_NoLabel` 直接断言新行为 ✓
- **TC-D9 不动其它控件** → 仅改 Tab 相关文件 ✓
- **§7.2 SKILL / §7.3 主 spec / §7.4 XSD** → Task 5 ✓
- **类型一致性**:`EnsureLabel()`(Task 2 定义,Task 2/3 引用)、`_icon != null` 判定(Task 2 EnsureLabel 与 Task 3 Icon setter 一致)、`UI.Theme.Resolve`(Task 4,与 Btn.Color 同)、测试 helper `LoadTab` / `LoadDocument("test", xml)` / `screen.Get<T>("id")`(全 Task 一致,与现有 BtnTests/TabBarTests 同模式)✓
- **Placeholder 扫描**:无 TBD/TODO;每个 code step 含完整代码;每个 run step 含具体 MCP 调用与期望 ✓
