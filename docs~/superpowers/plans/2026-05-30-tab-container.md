# `<Tab>` 容器化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `<Tab>` 像 `<Btn>` 一样接受子节点(可点击容器),并补齐 Btn 对齐的懒 label / 懒 icon / `color` 属性。

**Architecture:** 引擎本就对所有控件实例化子节点(`ScreenInstantiator.InstantiateRecursive` 把子节点挂到 `control.ChildHostTransform`),所谓"叶子"只是一条执行不一致的 lint。本计划移除 Tab 的 leaf-lint(`PUI-TAB-CHILDREN`),把 Tab 的 label/icon 改成懒创建(不写 `text`/`icon` 就不建对应 GameObject),并新增 `color` 属性(`#00000000` = 透明可点)。点击穿透沿用 uGUI 机制(Tab bg 是 `targetGraphic`+raycast on,子节点 raycast off 即穿透激活),无需新代码。

**Tech Stack:** Unity 6 uGUI + TMP;C# 9;R3(Cysharp);纯 C# lint(`Runtime/Core/Lint`);测试经 UnityMCP 跑(EditMode)。

**Branch:** `feat/tab-container`(已创建,spec + 本 plan 已提交)。**DO NOT commit to main.**

**Spec:** `docs~/superpowers/specs/2026-05-30-tab-container-design.md`

---

## 通用约定(每个 Task 的 run 步骤都这么跑)

本项目**只**经 UnityMCP 跑测试,不用 batch-mode。源码改动后**先 refresh、再读 console 确认无编译错、最后跑测试**。MCP 工具需先 `ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)` 加载。

- 编译刷新:`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- 查编译错:`mcp__UnityMCP__read_console(action="get", types=["error"])` → 期望空
- 跑测试:`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=[".*<ClassName>.*"])`

**run_tests 调用要点(Task 1 实测,务必照做):**
- 部署版 `run_tests` **没有 `filter` 参数**。按类名筛选用 `group_names`,且要传**正则**:`group_names=[".*TabTests.*"]`。⚠️ 用 `test_names=["TabTests"]`(类名)会匹配到 **0 个**测试 → 看似"通过"实则没跑。
- `run_tests` 是**异步**的:返回 `job_id`,需用 `mcp__UnityMCP__get_test_job(job_id=...)` 轮询拿结果(`ToolSearch(query="select:get_test_job", max_results=1)` 加载)。
- **EditMode 测试要求 Editor 不在 Play Mode**。若报 `"Cannot start a test run while the Editor is in or entering Play Mode."`,先 `mcp__UnityMCP__manage_editor(action="stop")`(play-mode 状态读 `mcpforunity://editor/state` 资源,`manage_editor` 无 `get_state`),再重跑。
- **先调一次 `refresh_unity` 确认 MCP 真的连上**;若报错/超时(Unity 没开),停下来报告 "MCP unavailable" + 错误原文,**不要编造测试结果**。可按 CLAUDE.md 尝试重连一次。

**EditMode 测试的日志规则(重要):** Unity 测试遇到**未声明的 `LogError`**会判 FAIL;`LogWarning` 不会让测试失败但会污染输出。现有 Tab 测试用 `LogAssert.Expect(LogType.Warning, regex)` 显式声明 `OnAttached` 在无 `<TabBar>` 祖先时发的告警("Tab ... has no ... TabBar ... ancestor")。**本计划新增的、用 `OpenTab(...)` 直开单个 Tab(无 TabBar)的测试,必须照抄这条 `LogAssert.Expect`,否则 FAIL。** 用 `OpenBar`/含 `<TabBar>` 的测试不需要。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Core/Lint/TabRules.cs` | Tab/TabBar lint 规则 | 删 `CheckTab` 方法 + `TabChildrenCode` 常量 |
| `Runtime/Core/Lint/IRWalker.cs` | CLI lint 遍历(纯 C#) | 删 `else if (node.Tag == "Tab")` dispatch(L51-53) |
| `Runtime/Application/ScreenInstantiator.cs` | runtime 实例化 + 同源 lint warn | 删 `else if (node.Tag == "Tab")` dispatch(L195-197) |
| `Runtime/Controls/Tab.cs` | Tab 控件 | label 懒创建、icon 解依赖、新增 `color` |
| `Tests/EditMode/Lint/TabRulesTests.cs` | lint 单测 | 删 3 个引用 `CheckTab` 的用例,加 1 个"Tab 带子不报错"回归 |
| `Tests/EditMode/Controls/TabTests.cs` | Tab 控件行为单测(**已存在**) | 改 2 个冲突用例,加 接子 / 懒 label / 懒 icon / color 用例 |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | XML 作者指南 | Tab 接子、`color`、删 lint 行 |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | 主 spec §5 控件表 | Tab 行补 `color`、改"容器"措辞 |
| XSD(由 Unity 菜单生成) | schema | 重新生成(新增 `color`) |

---

## Task 1: 移除 Tab 的 leaf-lint(`PUI-TAB-CHILDREN`)

**Files:**
- Modify: `Runtime/Core/Lint/TabRules.cs`(删 `TabChildrenCode` 常量 + `CheckTab` 方法)
- Modify: `Runtime/Core/Lint/IRWalker.cs:51-53`(删 Tab dispatch)
- Modify: `Runtime/Application/ScreenInstantiator.cs:195-197`(删 Tab dispatch)
- Modify: `Tests/EditMode/Lint/TabRulesTests.cs`(删 3 个 `CheckTab` 用例 + 加 1 个回归)

- [ ] **Step 1: 写失败测试 —— CLI walk 不再 flag "Tab 带子"**

在 `Tests/EditMode/Lint/TabRulesTests.cs` 类内**追加**(沿用文件已有的 `UIDocumentParser.Parse` + `IRWalker.Walk` 模式;不要新建文件,不要手搓 `ScreenDef`——它的 `Root` 是只读 ctor 参数):

```csharp
        [Test]
        public void IRWalker_Does_Not_Flag_Tab_With_Children()
        {
            // Container model: <Tab> may now hold children. PUI-TAB-CHILDREN is retired.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar><Tab id='t'><Icon name='ui:gear'/></Tab></TabBar>
</Screen></PromptUGUI>");
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == "PUI-TAB-CHILDREN"),
                "Tab with children must not produce PUI-TAB-CHILDREN anymore");
        }
```

- [ ] **Step 2: 跑测试,确认 FAIL**

`ToolSearch(query="select:refresh_unity,run_tests,read_console", max_results=3)` →
`refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` →
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabRulesTests")`
期望:`IRWalker_Does_Not_Flag_Tab_With_Children` FAIL —— 当前 `CheckTab` 仍报 `PUI-TAB-CHILDREN`。

- [ ] **Step 3: 删 `TabRules.CheckTab` + `TabChildrenCode`**

`Runtime/Core/Lint/TabRules.cs`:
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

保留 `TabParentCode` / `TabBarChildCode` / `DirectionCode` 常量、`CheckTabBar` 方法、`ContainsTabDescendant` 私有方法,均不动。

- [ ] **Step 4: 删两处 `CheckTab` 调用点**

`Runtime/Core/Lint/IRWalker.cs` —— 删 L51-53:

```csharp
            else if (node.Tag == "Tab")
                foreach (var issue in TabRules.CheckTab(node))
                    yield return issue;
```

删后 `Progress` 分支(L48-50)直接接 `else if (node.Tag == "TabBar")`(L54-56,保留)。**注意**:`IRWalker.cs:78-85` 的 inline `PUI-TAB-PARENT` 检查(`TabRules.TabParentCode`)是另一回事,**不动**。

`Runtime/Application/ScreenInstantiator.cs` —— 删 L195-197:

```csharp
            else if (node.Tag == "Tab")
                foreach (var issue in TabRules.CheckTab(node))
                    Debug.LogWarning(issue.Message);
```

删后 `Progress` 分支(L192-194)直接接 `else if (node.Tag == "TabBar")`(L198-200,保留)。

- [ ] **Step 5: 删 3 个引用 `CheckTab` 的旧单测**

`Tests/EditMode/Lint/TabRulesTests.cs` —— 删下面三个用例(它们都调用即将删除的 `TabRules.CheckTab`,不删会编译失败):

1. `Tab_With_Children_Triggers_TabChildren`(约 L15-25)
2. `Tab_Bind_Empty_String_Is_Treated_As_Absent`(约 L27-42)—— 它 `CheckTab(tab)` 断言空;`bind=''` 的"按未设处理"语义在 `Tab.Bind` setter(runtime)里,不是 lint,删掉这个 lint 测试不丢覆盖
3. `IRWalker_Dispatches_Tab_Children_Rule`(约 L68-77)

保留 `TabBar_Direction_Invalid_Triggers_TabBarDirection` / `TabBar_With_NonTab_Child_Triggers_TabBarChild` / `IRWalker_Dispatches_TabBar_Direction_Rule` / `IRWalker_Inline_Tab_Parent_Rule_When_Tab_Outside_TabBar` / `TabBar_With_Template_Wrapper_Child_Does_Not_Trigger_TabBarChild` / `TabBar_With_NonTab_Child_Without_Tab_Descendant_Still_Triggers_TabBarChild` / `IRWalker_Does_Not_Warn_Tab_Parent_When_Wrapped_In_Template_Instance_Root` / `IRWalker_Still_Warns_Tab_Parent_When_Wrapped_In_Plain_Frame`,以及 Step 1 新增的 `IRWalker_Does_Not_Flag_Tab_With_Children`。

- [ ] **Step 6: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])`
期望:空。如有 `CS` 错(如还有别处引用 `CheckTab`/`TabChildrenCode`),按提示清理——全仓引用点仅 `TabRules.cs` / `IRWalker.cs` / `ScreenInstantiator.cs` / `TabRulesTests.cs`,Step 3-5 已覆盖。

- [ ] **Step 7: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabRulesTests")`
期望:全部 PASS(含新回归;旧的 3 个已删)。

- [ ] **Step 8: Commit**

```bash
git add Runtime/Core/Lint/TabRules.cs Runtime/Core/Lint/IRWalker.cs Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Lint/TabRulesTests.cs
git commit -m "feat: <Tab> accepts children — remove PUI-TAB-CHILDREN leaf lint

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Tab label 懒创建(Btn 对齐)

**Files:**
- Modify: `Runtime/Controls/Tab.cs`(OnAttached 去掉无条件 label;新增 `EnsureLabel()`;Text/Font/FontSize setter 改走 EnsureLabel)
- Modify: `Tests/EditMode/Controls/TabTests.cs`(改 2 个冲突用例 + 加 2 个新用例)

- [ ] **Step 1: 改/加测试 —— 不写 text 无 label,写 text 有 label**

`Tests/EditMode/Controls/TabTests.cs`:

(a) **改** `Tab_Has_Bg_Toggle_And_Label_Children`(约 L27-39):它现在用 `<Tab id='t'/>`(无 text)却断言 Label 存在——与懒 label 冲突。改为只断言 bg + toggle,并改名:

```csharp
        [Test]
        public void Tab_Has_Bg_And_Toggle()
        {
            // Suppress the no-ancestor warning fired by OnAttached.
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            Assert.IsNotNull(t.GameObject.GetComponent<UnityImage>(), "bg UnityImage on self");
            Assert.IsNotNull(t.GameObject.GetComponent<UnityToggle>(), "UnityToggle on self");
        }
```

(b) **改** `Tab_With_Empty_Text_Has_Empty_Label`(约 L67-75):前提"无 text 也有空 label"已失效。整体替换为"无 text → 无 label":

```csharp
        [Test]
        public void Tab_NoText_Has_No_Label()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            Assert.IsNull(t.GameObject.transform.Find("Label"),
                "no Label GameObject when text attr absent (lazy label)");
        }
```

(c) `Tab_Text_Sets_Label`(约 L57-65)沿用——它用 `text='Hello'`,懒创建后仍建 label,**不改**。

- [ ] **Step 2: 跑测试,确认 FAIL**

`refresh_unity(...)` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")`
期望:`Tab_NoText_Has_No_Label` FAIL —— 当前 `OnAttached` 无条件建 label。

- [ ] **Step 3: 改 OnAttached + 加 EnsureLabel**

`Runtime/Controls/Tab.cs` —— 把 `OnAttached`(L27-55)中的 label 创建块(L37-45)删掉,新增 `EnsureLabel()`。改后 `OnAttached`:

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

(`ApplyFont` 已有 `if (_label == null) return` 守卫,L100,保留不动。)

- [ ] **Step 4: 改 Text / Font / FontSize setter 走 EnsureLabel**

`Runtime/Controls/Tab.cs` —— 三个 setter(现 L120-145)改为:

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

(`PeekDefaultText` 已是 `_label != null ? _label.text : null`,L129,不动。)

- [ ] **Step 5: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])` 期望空。

- [ ] **Step 6: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")` → 期望 PASS。
回归:`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabBarTests")` → 期望 PASS。

> 注:`TabBarTests` 里若有断言"静态 Tab 有 Label 子物件"的用例,且其 Tab 无 `text`,会因懒 label 变红。届时按同样方式收紧(改为不再断言 label,或给该 Tab 加 `text`)。执行时以 console 实际报错为准。

- [ ] **Step 7: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/Controls/TabTests.cs
git commit -m "feat: Tab label is lazily created (Btn parity)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Tab icon 懒创建解依赖 + icon/text 组合布局

**Files:**
- Modify: `Runtime/Controls/Tab.cs`(Icon setter)
- Modify: `Tests/EditMode/Controls/TabTests.cs`(加 2 个用例)

- [ ] **Step 1: 写失败测试 —— 只写 icon 不崩;icon+text label 右移**

在 `Tests/EditMode/Controls/TabTests.cs` 类内追加。两个用例都用 stub SpriteResolver(返回非 null sprite,避免 `ResolveSprite` 在无 resolver 时打 `LogError` 而需要额外 `LogAssert.Expect`):

```csharp
        [Test]
        public void Tab_IconOnly_NoLabel_NoCrash()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => stub;
            // icon setter must NOT NRE when there is no label (lazy label).
            var t = OpenTab("<Tab id='t' icon='ui:gear'/>");
            Assert.IsNull(t.GameObject.transform.Find("Label"), "no Label when text absent");
            Assert.IsNotNull(t.GameObject.transform.Find("Icon"), "Icon created");
        }

        [Test]
        public void Tab_IconAndText_LabelShiftedRight()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => stub;
            var t = OpenTab("<Tab id='t' icon='ui:gear' text='Hi'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(32f, label.rectTransform.offsetMin.x,
                "label is shifted right to make room for icon, regardless of setter order");
        }
```

> 为何只测这两个不变式:`<Tab>` 的属性存于 `Dictionary`,`text`/`icon` 两个 setter 的 apply 顺序不保证。"icon 在、label 不在(不崩)"覆盖 Icon setter 的 null 安全;"两者都在 → offset=32"覆盖两个 setter 双向兜底后的最终布局。两序殊途同归。

- [ ] **Step 2: 跑测试,确认 FAIL**

`refresh_unity(...)` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")`
期望:`Tab_IconOnly_NoLabel_NoCrash` FAIL/报错 —— 当前 Icon setter 在 `_label` 为 null 时 NRE(现 `Tab.cs:162` 直接访问 `_label.rectTransform`)。

- [ ] **Step 3: 改 Icon setter 解除对 label 的硬依赖**

`Runtime/Controls/Tab.cs` —— Icon setter(现 L147-167)改为(仅 `_label != null` 时才右移):

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
                    // Shift label right to make room for icon — only if label exists.
                    // If text is applied later, EnsureLabel() reads _icon != null and shifts itself.
                    if (_label != null) _label.rectTransform.offsetMin = new Vector2(32f, 0f);
                }
                _icon.sprite = UI.ResolveSprite(value);
            }
        }
```

(Task 2 的 `EnsureLabel()` 已写成 `offsetMin = _icon != null ? (32,0) : zero`,兜住"icon 先于 text"的顺序。此处兜住"text 先于 icon"的顺序。)

- [ ] **Step 4: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])` 期望空。

- [ ] **Step 5: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")` → 期望全部 PASS(含既有 `Tab_With_Icon_Creates_Icon_Child` 仍绿)。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/Controls/TabTests.cs
git commit -m "feat: Tab icon no longer depends on label; order-independent icon+text layout

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Tab 新增 `color` 属性(Btn 对齐)

**Files:**
- Modify: `Runtime/Controls/Tab.cs`(新增 Color 属性)
- Modify: `Tests/EditMode/Controls/TabTests.cs`(加 2 个用例)

- [ ] **Step 1: 写失败测试 —— color 写入 bg;`#00000000` alpha 0**

在 `Tests/EditMode/Controls/TabTests.cs` 类内追加(用 `OpenTab` 直开,无 sprite/icon,只需声明 no-ancestor 告警):

```csharp
        [Test]
        public void Tab_Color_AppliesToBg()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' color='#FF0000'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), bg.color);
        }

        [Test]
        public void Tab_TransparentColor_AlphaZero()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' color='#00000000'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(0f, bg.color.a);
        }
```

- [ ] **Step 2: 跑测试,确认 FAIL**

`refresh_unity(...)` → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")`
期望:两个新用例 FAIL —— Tab 当前无 `color` 属性,attr 被忽略,bg 保持 `DefaultBtnColor`(白)。

- [ ] **Step 3: 新增 Color 属性**

`Runtime/Controls/Tab.cs` —— 在已有 `Sprite` / `SelectedSprite` 属性附近加(`_bg` 在 OnAttached 已无条件建,setter 无需 EnsureXxx;与 `Btn.Color`(`Btn.cs:100-104`)完全同形):

```csharp
        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _bg.color = UI.Theme.Resolve(value);
        }
```

(`IsColor = true` 仅供 XSD 文档生成;runtime 由 setter 自调 `UI.Theme.Resolve`。)

- [ ] **Step 4: refresh + 查编译错**

`refresh_unity(...)` → `read_console(action="get", types=["error"])` 期望空。

- [ ] **Step 5: 跑测试,确认 PASS**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="TabTests")` → 期望全部 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/Controls/TabTests.cs
git commit -m "feat: Tab gains color attribute (transparent-but-clickable via #00000000)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: 文档 + XSD(SKILL / 主 spec / schema)

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`
- 生成:XSD(Unity 菜单 `Tools/PromptUGUI/Schema/Generate XSD`,见 `Editor/XsdMenu.cs:8`)

> CLAUDE.md 强制:任何功能变更必须在同 PR 更新对应 SKILL(英文)。本 Task 无自动化测试,改完用 lint CLI 跑示例验证。

- [ ] **Step 1: 更新 `<Tab>` built-in 表行**

`.claude/skills/authoring-promptugui-xml/SKILL.md` —— `<Tab>` 那一行(约 L102)。属性列追加 `color` (hex / CSS named / theme token; `#00000000` = transparent-but-clickable);描述列把 "No nested XML children allowed." 改为:"Accepts nested XML children (overlaid Frame-style on the Tab bg). For click-through, children must set `raycastTarget=\"false\"` (`<Icon>` already does); a child that keeps `raycastTarget=true` (e.g. a nested `<Btn>`) handles its own clicks instead."

- [ ] **Step 2: 重写 "Custom Tab layout via Template" 一节,补直接子节点写法**

`.claude/skills/authoring-promptugui-xml/SKILL.md`(约 L724)—— 开头补"直接子节点"为首选简单路径;Template 包装保留为"共享样式 / 动态 BindItems"场景。加三个目标用例:

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

补一句:Tab 不是 layout group —— 子节点用各自 `anchor` / `margin` 叠放(Frame 式),不像 TabBar 子项受 layout-group 规则约束。

- [ ] **Step 3: 删 lint 表里的 `PUI-TAB-CHILDREN` 行**

`.claude/skills/authoring-promptugui-xml/SKILL.md`(lint 规则表,约 L761)删掉:

```
| `PUI-TAB-CHILDREN`     | `<Tab>` 包含嵌套 XML children（auto label / icon 由属性驱动）                         | error   |
```

保留 `PUI-TAB-PARENT` / `PUI-TABBAR-CHILD` / `PUI-TABBAR-DIRECTION`。再全文件检查其它处对 Tab "不接子 / leaf / No nested children" 的措辞(如 quick-reference 区块、程序化层级表 L205 附近),改为"接子"。

- [ ] **Step 4: 更新主 spec §5 控件表 Tab 行**

`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` —— §5 控件表 `<Tab>` 行属性补 `color`;"leaf"措辞改为"可点击容器(接子)"。

- [ ] **Step 5: 用 lint CLI 验证示例不再报错**

把 spec §1 用例 2(icon + 两行文字)存成临时 `.ui.xml`(套完整 `<PromptUGUI version='1'><Screen name='S'><TabBar>...</TabBar></Screen></PromptUGUI>`),跑:

```bash
cd .lint && dotnet run --project UIXmlLint -- /tmp/tab_children_example.ui.xml
```

期望:exit 0,输出无 `PUI-TAB-CHILDREN`。验证后删临时文件。

- [ ] **Step 6: 重新生成 XSD**

Unity 编辑器:`Tools → PromptUGUI → Schema → Generate XSD`。经 MCP 时用 `mcp__UnityMCP__execute_menu_item(menu_path="Tools/PromptUGUI/Schema/Generate XSD")`(此菜单不弹模态,安全;**切勿**碰 `Assets/Reimport All`)。确认生成的 XSD 中 `Tab` 含 `color` attr。

- [ ] **Step 7: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md "docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md"
# XSD 若在仓库内(确认路径后再加):
# git add <xsd 路径>
git commit -m "doc: <Tab> container model — SKILL + spec + XSD (color, children, drop PUI-TAB-CHILDREN)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 收尾验证(全部 Task 完成后)

- [ ] `refresh_unity(...)` → `read_console(action="get", types=["error"])` 全空。
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全绿(重点看 `TabTests` / `TabRulesTests` / `TabBarTests` / `TabBarBindItemsTests`)。
- [ ] `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="TabBar")` 绿(`TabBarPlayTests`)。
- [ ] `cd .lint && dotnet restore PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 无改动。
- [ ] `git log --oneline feat/tab-container` 含 5 个功能/文档 commit + spec commit + plan commit;均在分支,**未 push 到 main**。
- [ ] 完成后用 `superpowers:finishing-a-development-branch` 决定合并/PR 方式(交给用户)。

---

## Self-Review(plan 对 spec 覆盖检查)

- **TC-D1 删 leaf-lint** → Task 1 ✓
- **TC-D2 点击穿透(无代码,文档)** → Task 5 Step 1/2(raycast 契约写入 SKILL)✓
- **TC-D3 label 懒创建** → Task 2 ✓(并收紧 2 个冲突的既有测试)
- **TC-D4 icon 懒 + 解依赖** → Task 3 ✓
- **TC-D5 icon+text 顺序无关** → Task 3(EnsureLabel 读 `_icon`,Icon setter 读 `_label`,双向兜底 + 测试)✓
- **TC-D6 color 属性** → Task 4 ✓
- **TC-D7 透明无闪烁** → Task 4 `Tab_TransparentColor_AlphaZero`(ColorTint 从 alpha-0 基色相乘,机制由 uGUI 保证)✓
- **TC-D8 不做向后兼容** → 直接改/删冲突测试断言新行为,无兼容 shim ✓
- **TC-D9 不动其它控件** → 仅改 Tab 相关文件 ✓
- **§7.2 SKILL / §7.3 主 spec / §7.4 XSD** → Task 5 ✓
- **类型/标识一致性**:`EnsureLabel()`(Task 2 定义,Task 2/3 引用一致)、`_icon != null` 判定(Task 2 EnsureLabel 与 Task 3 Icon setter 一致)、`UI.Theme.Resolve`(Task 4,与 `Btn.Color` 同)、测试 helper `OpenTab` / `OpenBar`(沿用既有文件,未自创)、`LogAssert.Expect` 告警声明(全 OpenTab 用例一致)✓
- **Placeholder 扫描**:无 TBD/TODO;每个 code step 含完整代码;每个 run step 含具体 MCP 调用与期望 ✓
- **文件真实性**:测试落点 `Tests/EditMode/Controls/TabTests.cs` / `Tests/EditMode/Lint/TabRulesTests.cs` 均为既有文件;未引入手搓 `ScreenDef`(只读 Root)的写法 ✓
