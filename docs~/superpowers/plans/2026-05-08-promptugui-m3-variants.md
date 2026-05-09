# PromptUGUI M3 变体（Variant）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 spec §8 描述的 Variant 全部能力——`attr.var` 内联属性后缀（last-active-wins）、`<Variant when><Add into at>` 块、运行时切换触发已开 Screen 的属性重解算。跑通"同一 Screen 在 mobile-portrait 与 pc 间切换"完整闭环。

**Architecture:** 在 IR 上加 `ElementNode.VariantOverrides`（按声明顺序的 `(variant, value)` 列表）+ `ScreenDef.Variants`（块定义）。Parser 把 `attr.X="..."` 拆到 overrides；TemplateExpander 透传。运行时引入 `VariantStore`（R3 driven 状态机）+ `VariantResolver`（last-active-wins 解算）。把 instantiator 里"应用 control 属性"的逻辑抽到 `ControlAttributeApplier`，初次实例化与 ReSolve 共用。Screen 订阅 `VariantStore.Changed` 触发 ReSolve（不重建 GameObject，只 reapply ApplyCommon + 控件特定 setter；Add 块走 **Strategy C**——首次激活时实例化并永久挂在 `_byId` / `_nodeMap`，之后 toggle 只切根 GameObject 的 `SetActive`，从不 `Destroy`，让代码侧引用与 R3 订阅跨 toggle 周期保持稳定；只在 Close 时随 RootGameObject 整体销毁）。

**Tech Stack:** Unity 6 (6000.0+), TextMeshPro, R3 (Cysharp), NUnit (Unity Test Framework)。承接 M1/M2 已实现的 IR / Parser / Layout / Registry / Template / Application 各层。

---

## 假设与前置

工程师执行此计划前需要：

1. M1 计划（`docs~/superpowers/plans/2026-05-07-promptugui-m1-core.md`）+ M2 计划（`docs~/superpowers/plans/2026-05-08-promptugui-m2-templates.md`）已全部完成，所有 EditMode + PlayMode 测试 PASS
2. 宿主 Unity 项目位于 `C:\xsoft\PromptUGUIDev`（NuGetForUnity 装有 R3 1.3.0；`com.promptugui.core` 通过 file:// 引用本仓库）
3. UnityMCP 已连接（操作 Unity 用 MCP 工具，不要 batch CLI）
4. 工作目录始终为 PromptUGUI 仓库根 `C:\xsoft\PromptUGUI`

测试运行：用 UnityMCP 的 `mcp__UnityMCP__run_tests(mode="EditMode"|"PlayMode", assembly_names=["PromptUGUI.Tests.EditMode"|"PromptUGUI.Tests.PlayMode"])`，不要 spawn batch-mode Unity。文件改动后调 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` 触发编译并等待 ready。检查编译错误用 `mcp__UnityMCP__read_console(action="get", types=["error"])`。

---

## Spec 漂移说明（不在 M3 范围内）

下列 spec 与代码不一致点是 M1/M2 已落地但 spec 文档未同步的事实，M3 plan **统一以代码现状为准**，spec 同步留作 review 任务：

1. spec §11 速查行 608 仍写 `<UI version="1">`，但 §3 例子与代码均已用 `<PromptUGUI version="1">`。本 plan 所有 XML 例子使用 `<PromptUGUI>`。
2. spec §5 内置原语表列了 6 个，但 M2 已新增 `<Btn>`（通用按钮原语，由模板组合出 PrimaryButton/DangerButton 等）。本 plan 在示例中可使用 `<Btn>`；M3 不新增/移除原语。
3. spec §3 例子里出现 `<CloseButton>` 是自定义控件占位示意，与速查无冲突。
4. spec §3 / §8.2 例子的 `size.mobile-portrait="_,400"` 把 margin 的逗号占位语法借给了 size。size 的值语法是 `WxH`（§6.2），本来没有 `_` 概念。**M3 决策**：`attr.var` 一律走整体替换，不引入按轴/分量的部分覆盖；要单独改一根轴用 `width.var` / `height.var`，或在 base 不放冲突轴的 size/width/height、用显式 `pc` 与 `mobile` 等变体把每条尺寸"分期"声明清楚。本 plan 的所有 XML 例子（含 Task 14 E2E）已按此约束改写。
5. spec §8.4 仅列 `into="#id|@root"` 两种形式。**M3 扩展**：`into` 也支持 `#id/path/to/inner` 路径，语义与 §9.2 `Screen.Get("a/b")` 同义，按 `/` 分段下钻 ScopedIds——这样模板实例内部的 id 也能作为 `<Add>` 的目标父节点（典型用例：`<Add into="#dialog/itemGrid">` 把变体专属项注入到 TitledPanel 内的 Grid）。spec §8.4 / §11 速查待同步。

---

## 文件结构

```
PromptUGUI/                                          # 仓库根（已存在）
├── Runtime/
│   ├── Core/
│   │   ├── IR/
│   │   │   ├── ElementNode.cs                       # Modify (Task 1): 加 VariantOverrides
│   │   │   ├── ScreenDef.cs                         # Modify (Task 1): 加 Variants 列表
│   │   │   ├── AddDirective.cs                      # Create (Task 1)
│   │   │   └── VariantBlock.cs                      # Create (Task 1)
│   │   ├── Parser/
│   │   │   └── UIDocumentParser.cs                  # Modify (Tasks 2,3)
│   │   ├── Template/
│   │   │   └── TemplateExpander.cs                  # Modify (Task 4): 透传 VariantOverrides
│   │   └── Variants/
│   │       └── VariantResolver.cs                   # Create (Task 6)
│   ├── Registry/
│   │   └── ControlMeta.cs                           # Modify (Task 11): 暴露 AttrNames
│   └── Application/
│       ├── VariantStore.cs                          # Create (Task 5)
│       ├── UI.cs                                    # Modify (Tasks 5,7): Variants facade + 注入 store
│       ├── ControlAttributeApplier.cs               # Create (Task 7)
│       ├── ScreenInstantiator.cs                    # Modify (Tasks 7,8,9,10)
│       └── Screen.cs                                # Modify (Tasks 11,12,13)
├── Tests/
│   ├── EditMode/
│   │   ├── Parser/
│   │   │   └── UIDocumentParserTests.cs             # Modify (Tasks 2,3)
│   │   ├── Template/
│   │   │   └── TemplateExpanderTests.cs             # Modify (Task 4)
│   │   ├── Application/
│   │   │   └── VariantStoreTests.cs                 # Create (Task 5)
│   │   └── Variants/
│   │       └── VariantResolverTests.cs              # Create (Task 6)
│   └── PlayMode/
│       ├── Lifecycle/
│       │   ├── ScreenLifecycleTests.cs              # Modify (Tasks 7,11,12)
│       │   └── StackChildWarningTests.cs            # Modify (Task 9)
│       └── E2E/
│           └── VariantSwitchTests.cs                # Create (Task 14)
```

---

## Task 1：IR 扩展——VariantOverrides / VariantBlock / AddDirective

**Files:**
- Modify: `Runtime/Core/IR/ElementNode.cs`
- Create: `Runtime/Core/IR/AddDirective.cs`
- Create: `Runtime/Core/IR/VariantBlock.cs`
- Modify: `Runtime/Core/IR/ScreenDef.cs`

无独立测试——纯数据载体，由 Task 2 起的 parser 测试覆盖。

- [ ] **Step 1: ElementNode 加 VariantOverrides 字段**

把 `Runtime/Core/IR/ElementNode.cs` 整个文件替换为：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class ElementNode {
        public string Tag { get; }
        public string Id { get; set; }
        public Dictionary<string, string> Attributes { get; }
        public string TextContent { get; set; }
        public List<ElementNode> Children { get; }

        /// <summary>
        /// True 表示此节点是某个模板调用展开后产生的"实例根"。
        /// 它内部声明的 id 形成一个独立作用域，由 Control.ScopedIds 持有。
        /// 仅由 TemplateExpander 设置；parser 始终为 false。
        /// </summary>
        public bool IsTemplateInstanceRoot { get; set; }

        /// <summary>
        /// Variant 属性覆盖：原属性名（无后缀）→ 一个有序列表 [(variantName, value), ...]。
        /// 列表顺序就是 XML 中 `attr.varName="..."` 出现的声明顺序；多个后缀可共存。
        /// 仅 parser 写入；instantiator/resolver 只读。
        /// 同一 attrName 在 Attributes 与 VariantOverrides 中可同时存在；前者为基础值，
        /// 后者按 last-active-wins 选取覆盖（spec §8.3）。
        /// </summary>
        public Dictionary<string, List<(string Variant, string Value)>> VariantOverrides { get; }

        public ElementNode(string tag) {
            Tag = tag;
            Attributes = new Dictionary<string, string>();
            Children = new List<ElementNode>();
            VariantOverrides = new Dictionary<string, List<(string, string)>>();
        }
    }
}
```

- [ ] **Step 2: 创建 AddDirective**

`Runtime/Core/IR/AddDirective.cs`：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    /// <summary>
    /// `<Variant when="..."><Add into="#id|@root" at="start|end|N">...</Add></Variant>`
    /// 中的单条 Add 指令。在 Variant 激活时把 Children 实例化到 IntoPath 指向的父节点。
    /// </summary>
    public sealed class AddDirective {
        public string IntoPath { get; set; }      // "#id" 或 "@root"
        public string At { get; set; } = "end";   // "start" / "end" / 整数字符串
        public List<ElementNode> Children { get; } = new();
    }
}
```

- [ ] **Step 3: 创建 VariantBlock**

`Runtime/Core/IR/VariantBlock.cs`：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    /// <summary>
    /// `<Variant when="X">...</Variant>` 块。当变体 X 激活时，依次执行内部的 Adds。
    /// </summary>
    public sealed class VariantBlock {
        public string When { get; }
        public List<AddDirective> Adds { get; } = new();

        public VariantBlock(string when) { When = when; }
    }
}
```

- [ ] **Step 4: ScreenDef 加 Variants 列表**

把 `Runtime/Core/IR/ScreenDef.cs` 整个文件替换为：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class ScreenDef {
        public string Name { get; }
        public ElementNode Root { get; }
        public List<VariantBlock> Variants { get; } = new();

        public ScreenDef(string name, ElementNode root) {
            Name = name;
            Root = root;
        }
    }
}
```

- [ ] **Step 5: 触发 Unity 编译并确认无错**

调用：
- `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- `mcp__UnityMCP__read_console(action="get", types=["error"], count=10)`

预期：无 PromptUGUI 相关 error。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/IR
git commit -m "feat(ir): VariantOverrides on ElementNode; VariantBlock + AddDirective IR"
```

---

## Task 2：Parser 识别 attr.var 后缀

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

- [ ] **Step 1: 写测试**

追加到 `Tests/EditMode/Parser/UIDocumentParserTests.cs`（在最后一个 `}` 之前）：

```csharp
        [Test]
        public void Parses_attr_with_variant_suffix() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' anchor='center' anchor.mobile='bottom-stretch'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var v = doc.Screens[0].Root.Children[0];

            Assert.AreEqual("center", v.Attributes["anchor"]);
            Assert.IsFalse(v.Attributes.ContainsKey("anchor.mobile"));
            Assert.IsTrue(v.VariantOverrides.ContainsKey("anchor"));
            var list = v.VariantOverrides["anchor"];
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("mobile", list[0].Variant);
            Assert.AreEqual("bottom-stretch", list[0].Value);
        }

        [Test]
        public void Multiple_variants_preserve_declaration_order() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' size='100x100'
                            size.mobile='200x200' size.tablet='150x150'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.Children[0].VariantOverrides["size"];
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("mobile", list[0].Variant);
            Assert.AreEqual("tablet", list[1].Variant);
        }

        [Test]
        public void Variant_only_attr_without_base_is_allowed() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' margin.mobile='16'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var v = doc.Screens[0].Root.Children[0];
            Assert.IsFalse(v.Attributes.ContainsKey("margin"));
            Assert.IsTrue(v.VariantOverrides.ContainsKey("margin"));
            Assert.AreEqual(1, v.VariantOverrides["margin"].Count);
        }

        [Test]
        public void Multiple_attrs_with_their_own_variants_dont_interfere() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack anchor='center' anchor.mobile='top-stretch'
                            margin='8'   margin.mobile='16'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var v = doc.Screens[0].Root.Children[0];
            Assert.AreEqual(1, v.VariantOverrides["anchor"].Count);
            Assert.AreEqual("top-stretch", v.VariantOverrides["anchor"][0].Value);
            Assert.AreEqual(1, v.VariantOverrides["margin"].Count);
            Assert.AreEqual("16", v.VariantOverrides["margin"][0].Value);
        }

        [Test]
        public void Throws_on_id_with_variant_suffix() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' id.mobile='other'/>
                </Screen></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_param_default_with_variant_suffix() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='T'>
                    <Param name='x' default='a' default.mobile='b'/>
                    <Frame/>
                </Template></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_attr_with_empty_variant_after_dot() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack anchor.='top-left'/>
                </Screen></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
```

- [ ] **Step 2: 跑测试，确认 FAIL**

`mcp__UnityMCP__refresh_unity(...)` + `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], include_failed_tests=true)`

预期：7 个新测试 FAIL（VariantOverrides 始终为空 / 期待异常未抛）。

- [ ] **Step 3: 改 ParseElement 拆 .var 后缀**

修改 `Runtime/Core/Parser/UIDocumentParser.cs` 的 `ParseElement` 方法。把现有 `ParseElement` 整个替换为：

```csharp
        static ElementNode ParseElement(XmlElement el,
                                        System.Collections.Generic.HashSet<string> idsInScope) {
            var node = new ElementNode(el.Name);

            foreach (XmlAttribute attr in el.Attributes) {
                if (attr.Name == "id") {
                    if (!idsInScope.Add(attr.Value))
                        throw new ParseException(
                            $"Duplicate id='{attr.Value}' within scope");
                    node.Id = attr.Value;
                    continue;
                }

                int dot = attr.Name.IndexOf('.');
                if (dot < 0) {
                    node.Attributes[attr.Name] = attr.Value;
                    continue;
                }

                if (dot == 0 || dot == attr.Name.Length - 1)
                    throw new ParseException(
                        $"<{el.Name}>: malformed attribute '{attr.Name}' (variant suffix must be 'name.variant')");

                var baseName = attr.Name.Substring(0, dot);
                var variant  = attr.Name.Substring(dot + 1);

                if (baseName == "id")
                    throw new ParseException(
                        $"<{el.Name}>: 'id' cannot carry .variant suffix (id='{attr.Value}')");

                if (!node.VariantOverrides.TryGetValue(baseName, out var list)) {
                    list = new System.Collections.Generic.List<(string, string)>();
                    node.VariantOverrides[baseName] = list;
                }
                list.Add((variant, attr.Value));
            }

            // 文本简写
            bool hasElement = false, hasText = false;
            foreach (XmlNode c in el.ChildNodes) {
                if (c is XmlElement) hasElement = true;
                else if (c is XmlText txt && !string.IsNullOrWhiteSpace(txt.Value)) hasText = true;
            }
            if (hasText && hasElement)
                throw new ParseException(
                    $"<{el.Name}> mixes text and child elements; not allowed");
            if (hasText && !hasElement)
                node.TextContent = el.InnerText.Trim();

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el, idsInScope));

            return node;
        }
```

- [ ] **Step 4: 改 ParseTemplate 拒绝 default.var**

`<Param>` 的属性是在 `ParseTemplate` 中循环处理的，不走 `ParseElement`。在原 `ParseTemplate` 方法中处理 Param 的分支里增加属性扫描。把 `if (ce.Name == "Param") { ... }` 整个分支替换为：

```csharp
                if (ce.Name == "Param") {
                    if (sawBody)
                        throw new ParseException(
                            $"<Template name='{name}'>: <Param> must appear before any body element");
                    var pname = ce.GetAttribute("name");
                    if (string.IsNullOrEmpty(pname))
                        throw new ParseException(
                            $"<Template name='{name}'>: <Param> requires name attribute");
                    if (!paramNames.Add(pname))
                        throw new ParseException(
                            $"<Template name='{name}'>: duplicate <Param name='{pname}'>");

                    foreach (XmlAttribute pa in ce.Attributes) {
                        if (pa.Name == "name" || pa.Name == "default") continue;
                        if (pa.Name.StartsWith("default.") || pa.Name.StartsWith("name."))
                            throw new ParseException(
                                $"<Param name='{pname}'>: '{pa.Name}' cannot carry .variant suffix");
                        // 其他属性 M2 行为是隐式忽略，M3 维持
                    }

                    string def = ce.HasAttribute("default") ? ce.GetAttribute("default") : null;
                    tpl.Params.Add(new ParamDef(pname, def));
                }
```

- [ ] **Step 5: 跑测试，全 PASS**

`mcp__UnityMCP__run_tests(mode="EditMode", ...)`。预期：之前所有 EditMode 测试 + 7 个新测试全 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Parser Tests/EditMode/Parser
git commit -m "feat(parser): split attr.var suffix into ElementNode.VariantOverrides"
```

---

## Task 3：Parser 识别 `<Variant>` 与 `<Add>` 块

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

- [ ] **Step 1: 写测试**

追加到 `UIDocumentParserTests.cs`：

```csharp
        [Test]
        public void Parses_variant_block_with_add() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Frame id='root'/>
                    <Variant when='mobile'>
                        <Add into='#root' at='end'>
                            <Image id='joy'/>
                        </Add>
                    </Variant>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var s = doc.Screens[0];
            Assert.AreEqual(1, s.Variants.Count);
            var v = s.Variants[0];
            Assert.AreEqual("mobile", v.When);
            Assert.AreEqual(1, v.Adds.Count);
            Assert.AreEqual("#root", v.Adds[0].IntoPath);
            Assert.AreEqual("end", v.Adds[0].At);
            Assert.AreEqual(1, v.Adds[0].Children.Count);
            Assert.AreEqual("Image", v.Adds[0].Children[0].Tag);
        }

        [Test]
        public void Add_at_defaults_to_end_when_omitted() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Variant when='mobile'>
                        <Add into='@root'><Image/></Add>
                    </Variant>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("end", doc.Screens[0].Variants[0].Adds[0].At);
        }

        [Test]
        public void Add_at_can_be_integer_index_string() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Variant when='m'><Add into='@root' at='2'><Image/></Add></Variant>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("2", doc.Screens[0].Variants[0].Adds[0].At);
        }

        [Test]
        public void Variant_block_can_have_multiple_adds() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Frame id='a'/><Frame id='b'/>
                    <Variant when='m'>
                        <Add into='#a'><Image/></Add>
                        <Add into='#b'><Image/></Add>
                    </Variant>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual(2, doc.Screens[0].Variants[0].Adds.Count);
        }

        [Test]
        public void Throws_on_variant_without_when() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                    <Screen name='X'><Variant/></Screen></PromptUGUI>"));
        }

        [Test]
        public void Throws_on_add_without_into() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                    <Screen name='X'>
                        <Variant when='m'><Add><Image/></Add></Variant>
                    </Screen></PromptUGUI>"));
        }

        [Test]
        public void Throws_when_variant_block_contains_non_add_child() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                    <Screen name='X'>
                        <Variant when='m'><Image/></Variant>
                    </Screen></PromptUGUI>"));
        }

        [Test]
        public void Throws_on_variant_at_top_level_outside_screen() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                    <Variant when='m'><Add into='@root'><Image/></Add></Variant>
                    </PromptUGUI>"));
        }

        [Test]
        public void Variant_block_does_not_appear_in_screen_root_children() {
            // <Variant> 应被解析到 ScreenDef.Variants，而不是出现在根 children 里
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Frame id='a'/>
                    <Variant when='m'><Add into='@root'><Image/></Add></Variant>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual(1, doc.Screens[0].Root.Children.Count);
            Assert.AreEqual("Frame", doc.Screens[0].Root.Children[0].Tag);
        }
```

- [ ] **Step 2: 跑测试，FAIL（Variant 块未被识别）**

预期：8 个新测试中部分 FAIL（解析时把 `<Variant>` 当成普通 element 加入 root.Children，导致 Tag 与计数不符）。

- [ ] **Step 3: 改 ParseScreen + 加 ParseVariantBlock 方法**

修改 `Runtime/Core/Parser/UIDocumentParser.cs`：

(1) 把 `ParseScreen` 整个方法替换为：

```csharp
        static void ParseScreen(XmlElement el, UIDocument doc,
                                System.Collections.Generic.HashSet<string> screenNames) {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Screen> requires name attribute");
            if (!screenNames.Add(name))
                throw new ParseException($"Duplicate <Screen name='{name}'>");

            var idsInScreen = new System.Collections.Generic.HashSet<string>();
            var rootNode = new ElementNode("__screen_root__");
            var screen = new ScreenDef(name, rootNode);

            foreach (XmlNode c in el.ChildNodes) {
                if (c is not XmlElement child_el) continue;
                if (child_el.Name == "Variant") {
                    ParseVariantBlock(child_el, screen, idsInScreen);
                } else {
                    rootNode.Children.Add(ParseElement(child_el, idsInScreen));
                }
            }
            doc.Screens.Add(screen);
        }
```

(2) 新增 `ParseVariantBlock` 静态方法（放在 ParseTemplate 后面）：

```csharp
        static void ParseVariantBlock(XmlElement el, ScreenDef screen,
                                      System.Collections.Generic.HashSet<string> idsInScreen) {
            var when = el.GetAttribute("when");
            if (string.IsNullOrEmpty(when))
                throw new ParseException("<Variant> requires 'when' attribute");

            var block = new VariantBlock(when);

            foreach (XmlNode c in el.ChildNodes) {
                if (c is not XmlElement ce) continue;
                if (ce.Name != "Add")
                    throw new ParseException(
                        $"<Variant when='{when}'>: only <Add> elements allowed (got <{ce.Name}>)");

                var add = new AddDirective();
                var into = ce.GetAttribute("into");
                if (string.IsNullOrEmpty(into))
                    throw new ParseException(
                        $"<Add> inside <Variant when='{when}'>: 'into' attribute is required");
                add.IntoPath = into;
                if (ce.HasAttribute("at")) add.At = ce.GetAttribute("at");

                foreach (XmlNode ac in ce.ChildNodes)
                    if (ac is XmlElement ace)
                        add.Children.Add(ParseElement(ace, idsInScreen));

                block.Adds.Add(add);
            }

            screen.Variants.Add(block);
        }
```

注意：`<Add>` 内部的 children 共享 Screen 的 idsInScreen 集合——这样 Add 块创建的 `id='joy'` 与基础树里的 id 之间也能强制唯一性，避免运行时 Get 歧义。

- [ ] **Step 4: 跑测试，全 PASS**

预期：所有先前测试 + 8 个新增 = 全 PASS。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser Tests/EditMode/Parser
git commit -m "feat(parser): recognize <Variant when><Add into at> blocks within Screen"
```

---

## Task 4：TemplateExpander 透传 VariantOverrides

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`
- Modify: `Tests/EditMode/Template/TemplateExpanderTests.cs`

模板内属性带 `.var` 后缀必须在展开后保留——否则 instantiator 拿不到覆盖。模板调用方在调用上写的 `.var` 后缀（如 `<Box anchor.mobile='top'>`）也必须像基础通用属性那样透传到模板根的 VariantOverrides。

- [ ] **Step 1: 写测试**

追加到 `Tests/EditMode/Template/TemplateExpanderTests.cs`：

```csharp
        [Test]
        public void Variant_overrides_inside_template_body_are_preserved() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame anchor='center' anchor.mobile='top-stretch'/>
                </Template>
                <Screen name='S'>
                    <Box id='b'/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var b = expanded.Screens[0].Root.Children[0];

            Assert.AreEqual("Frame", b.Tag);
            Assert.AreEqual("center", b.Attributes["anchor"]);
            Assert.IsTrue(b.VariantOverrides.ContainsKey("anchor"));
            Assert.AreEqual(1, b.VariantOverrides["anchor"].Count);
            Assert.AreEqual("top-stretch", b.VariantOverrides["anchor"][0].Value);
        }

        [Test]
        public void Variant_overrides_on_invocation_propagate_to_instance_root() {
            // 模板调用上 anchor.mobile=... 应作为通用属性 .var 透传
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame/>
                </Template>
                <Screen name='S'>
                    <Box id='b' anchor='center' anchor.mobile='top-stretch'/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var b = expanded.Screens[0].Root.Children[0];

            Assert.AreEqual("center", b.Attributes["anchor"]);
            Assert.IsTrue(b.VariantOverrides.ContainsKey("anchor"));
            Assert.AreEqual("top-stretch", b.VariantOverrides["anchor"][0].Value);
        }

        [Test]
        public void Variant_overrides_on_template_body_inner_nodes_are_preserved() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Image id='inner' size='10x10' size.mobile='20x20'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box id='b'/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var inner = expanded.Screens[0].Root.Children[0].Children[0];
            Assert.AreEqual("Image", inner.Tag);
            Assert.AreEqual("20x20", inner.VariantOverrides["size"][0].Value);
        }
```

- [ ] **Step 2: 跑测试，FAIL**

预期：3 个新测试 FAIL（VariantOverrides 在 expander 输出中为空）。

- [ ] **Step 3: 改 TemplateExpander 三处复制路径**

修改 `Runtime/Core/Template/TemplateExpander.cs`：

(1) 把 `ExpandTree` 里 pass-through 复制（`var dst = new ElementNode(src.Tag) { ... }` 段）整段替换为：

```csharp
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var kv in src.VariantOverrides)
                dst.VariantOverrides[kv.Key] =
                    new System.Collections.Generic.List<(string, string)>(kv.Value);
            foreach (var c in src.Children) {
                var ec = ExpandTree(c, templates, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
```

(2) 把 `ExpandInvocation` 里"通用属性透传到 instanceRoot"那段（紧接 `if (!string.IsNullOrEmpty(invocation.Id))` 之后的 `foreach (var kv in invocation.Attributes)` 块）整段替换为：

```csharp
                if (!string.IsNullOrEmpty(invocation.Id))
                    instanceRoot.Id = invocation.Id;
                foreach (var kv in invocation.Attributes) {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    instanceRoot.Attributes[kv.Key] = kv.Value;
                }
                foreach (var kv in invocation.VariantOverrides) {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    if (!instanceRoot.VariantOverrides.TryGetValue(kv.Key, out var list)) {
                        list = new System.Collections.Generic.List<(string, string)>();
                        instanceRoot.VariantOverrides[kv.Key] = list;
                    }
                    list.AddRange(kv.Value);
                }
```

(3) 把 `ExpandNode` 里构造 `dst` 的段（紧接 `if (templates.ContainsKey(prepared.Tag))` 之后的 `var dst = new ElementNode(prepared.Tag) { ... }` 块直到 children 循环之前）替换为：

```csharp
            var dst = new ElementNode(prepared.Tag) {
                Id = prepared.Id,
                TextContent = Substitution.Apply(prepared.TextContent, args),
            };
            foreach (var kv in prepared.Attributes) {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = kv.Value;
            }
            foreach (var kv in prepared.VariantOverrides)
                dst.VariantOverrides[kv.Key] =
                    new System.Collections.Generic.List<(string, string)>(kv.Value);
```

(4) 把 `SubstituteAttrs` 整段替换为：

```csharp
        static ElementNode SubstituteAttrs(ElementNode src,
                                           IReadOnlyDictionary<string, string> args) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            foreach (var kv in src.VariantOverrides) {
                var newList = new System.Collections.Generic.List<(string, string)>();
                foreach (var (variant, value) in kv.Value)
                    newList.Add((variant, Substitution.Apply(value, args)));
                dst.VariantOverrides[kv.Key] = newList;
            }
            foreach (var c in src.Children)
                dst.Children.Add(c);
            return dst;
        }
```

(5) 把 `DeepClone` 整段替换为：

```csharp
        static ElementNode DeepClone(ElementNode src) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes) dst.Attributes[kv.Key] = kv.Value;
            foreach (var kv in src.VariantOverrides)
                dst.VariantOverrides[kv.Key] =
                    new System.Collections.Generic.List<(string, string)>(kv.Value);
            foreach (var c in src.Children) dst.Children.Add(DeepClone(c));
            return dst;
        }
```

- [ ] **Step 4: 跑全部 EditMode 测试，全 PASS**

预期：之前测试 + 3 个新增 = 全 PASS。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Template Tests/EditMode/Template
git commit -m "feat(template): TemplateExpander carries VariantOverrides through expansion"
```

---

## Task 5：VariantStore + UI.Variants facade

**Files:**
- Create: `Runtime/Application/VariantStore.cs`
- Modify: `Runtime/Application/UI.cs`
- Create: `Tests/EditMode/Application/VariantStoreTests.cs`

- [ ] **Step 1: 写测试**

`Tests/EditMode/Application/VariantStoreTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application {
    public class VariantStoreTests {
        [Test]
        public void IsActive_default_false() {
            var s = new VariantStore();
            Assert.IsFalse(s.IsActive("mobile"));
        }

        [Test]
        public void Set_true_then_false_toggles_state() {
            var s = new VariantStore();
            s.Set("mobile", true);
            Assert.IsTrue(s.IsActive("mobile"));
            s.Set("mobile", false);
            Assert.IsFalse(s.IsActive("mobile"));
        }

        [Test]
        public void Multiple_variants_active_simultaneously() {
            var s = new VariantStore();
            s.Set("a", true);
            s.Set("b", true);
            Assert.IsTrue(s.IsActive("a"));
            Assert.IsTrue(s.IsActive("b"));
        }

        [Test]
        public void Changed_fires_only_on_state_transition() {
            var s = new VariantStore();
            int events = 0;
            s.Changed.Subscribe(_ => events++);

            s.Set("a", true);
            Assert.AreEqual(1, events);

            s.Set("a", true);   // no-op (already active)
            Assert.AreEqual(1, events);

            s.Set("a", false);
            Assert.AreEqual(2, events);

            s.Set("a", false);  // no-op (already inactive)
            Assert.AreEqual(2, events);
        }

        [Test]
        public void Reset_clears_all_active_variants() {
            var s = new VariantStore();
            s.Set("a", true);
            s.Set("b", true);
            s.Reset();
            Assert.IsFalse(s.IsActive("a"));
            Assert.IsFalse(s.IsActive("b"));
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（VariantStore 不存在）**

- [ ] **Step 3: 实现 VariantStore**

`Runtime/Application/VariantStore.cs`：

```csharp
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Application {
    /// <summary>
    /// 变体激活集合 + 变更事件源。所有 attr.var 后缀解算都查这个 store；
    /// Screen 订阅 Changed 触发 ReSolve。
    /// </summary>
    public sealed class VariantStore {
        readonly HashSet<string> _active = new();
        readonly Subject<Unit> _changed = new();

        public Observable<Unit> Changed => _changed;

        public bool IsActive(string name) => _active.Contains(name);

        public IReadOnlyCollection<string> Active => _active;

        public void Set(string name, bool active) {
            bool changed = active ? _active.Add(name) : _active.Remove(name);
            if (changed) _changed.OnNext(Unit.Default);
        }

        /// <summary>测试用——清空所有激活变体，不发 Changed。</summary>
        public void Reset() => _active.Clear();
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: 在 UI 加 Variants facade**

修改 `Runtime/Application/UI.cs`。把整个文件替换为：

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class UI {
        static ControlRegistry _registry = new();
        static readonly VariantStore _variantStore = new();
        static readonly Dictionary<string, ScreenDef> _docs = new();
        static readonly Dictionary<string, Screen> _open = new();

        public static ControlRegistry Registry => _registry;
        internal static VariantStore VariantStore => _variantStore;

        public static class Variants {
            public static void Set(string name, bool active) =>
                _variantStore.Set(name, active);
            public static bool IsActive(string name) =>
                _variantStore.IsActive(name);
        }

        public static void LoadDocument(string label, string xml) {
            var raw = UIDocumentParser.Parse(xml);
            var doc = PromptUGUI.Template.TemplateExpander.Expand(raw);
            foreach (var s in doc.Screens) {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(
                        $"Screen '{s.Name}' already loaded");
                _docs[s.Name] = s;
            }
        }

        public static Screen Open(string screenName) {
            if (_open.TryGetValue(screenName, out var existing)) return existing;
            if (!_docs.TryGetValue(screenName, out var def))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' not loaded; call LoadDocument first");

            var inst = new ScreenInstantiator(_registry, _variantStore);
            var screen = new Screen(def, inst, _registry, _variantStore);
            screen.Open();
            _open[screenName] = screen;
            return screen;
        }

        public static void Close(string screenName) {
            if (_open.TryGetValue(screenName, out var s)) {
                s.Close();
                _open.Remove(screenName);
            }
        }

        public static Screen Get(string screenName) =>
            _open.TryGetValue(screenName, out var s) ? s : null;

        // 仅测试使用
        internal static void ResetForTests() {
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _variantStore.Reset();
            _registry = new ControlRegistry();
        }
    }
}
```

注意：`ScreenInstantiator` 与 `Screen` 的构造函数将在 Tasks 7/11 扩展。在它们扩展前，编译会因签名不匹配而失败——这是预期，下一步 Task 6 也不动它们，先把 Resolver 写出来；Task 7 起再改 instantiator/screen。所以本 step 5 的代码可以**先注释掉** `Open` 方法里把 store 传进 instantiator/screen 的两行**新参数**，保留旧签名调用，避免编译失败。改回如下：

```csharp
        public static Screen Open(string screenName) {
            if (_open.TryGetValue(screenName, out var existing)) return existing;
            if (!_docs.TryGetValue(screenName, out var def))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' not loaded; call LoadDocument first");

            var inst = new ScreenInstantiator(_registry);
            var screen = new Screen(def, inst);
            screen.Open();
            _open[screenName] = screen;
            return screen;
        }
```

把上面 `UI.cs` 的 `Open` 改回旧形式（保留 instantiator/screen 旧构造），其他改动（VariantStore 字段 / Variants facade / ResetForTests 加 `_variantStore.Reset()`）保留。Task 7 与 Task 11 会再回来扩展。

- [ ] **Step 6: 触发编译，确认无错；跑全 EditMode + PlayMode 测试，全 PASS**

预期：变体相关 5 个 EditMode 测试新 PASS；M2 既有测试无回归。

- [ ] **Step 7: Commit**

```bash
git add Runtime/Application/VariantStore.cs Runtime/Application/UI.cs Tests/EditMode/Application
git commit -m "feat(app): VariantStore (R3 driven) + UI.Variants facade"
```

---

## Task 6：VariantResolver（last-active-wins）

**Files:**
- Create: `Runtime/Core/Variants/VariantResolver.cs`
- Create: `Tests/EditMode/Variants/VariantResolverTests.cs`

- [ ] **Step 1: 写测试**

`Tests/EditMode/Variants/VariantResolverTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Variants;

namespace PromptUGUI.Tests.Variants {
    public class VariantResolverTests {
        ElementNode Node(string baseAttr = null,
                         params (string variant, string value)[] overrides) {
            var n = new ElementNode("X");
            if (baseAttr != null) n.Attributes["size"] = baseAttr;
            if (overrides.Length > 0) {
                var list = new List<(string, string)>();
                foreach (var (v, val) in overrides) list.Add((v, val));
                n.VariantOverrides["size"] = list;
            }
            return n;
        }

        [Test]
        public void Returns_base_when_no_variant_active() {
            var store = new VariantStore();
            Assert.AreEqual("100",
                VariantResolver.ResolveAttribute(Node("100"), "size", store));
        }

        [Test]
        public void Returns_override_when_single_variant_active() {
            var store = new VariantStore();
            store.Set("mobile", true);
            var n = Node("100", ("mobile", "200"));
            Assert.AreEqual("200", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Last_active_wins_when_multiple_variants_active() {
            // 声明顺序：mobile, tablet；都激活 → tablet 胜（声明在后）
            var store = new VariantStore();
            store.Set("mobile", true);
            store.Set("tablet", true);
            var n = Node("100", ("mobile", "200"), ("tablet", "150"));
            Assert.AreEqual("150", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Earlier_declared_wins_when_later_inactive() {
            var store = new VariantStore();
            store.Set("mobile", true);
            // tablet 未激活
            var n = Node("100", ("mobile", "200"), ("tablet", "150"));
            Assert.AreEqual("200", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Returns_base_when_only_inactive_variants_have_overrides() {
            var store = new VariantStore();
            store.Set("tablet", true);
            var n = Node("100", ("mobile", "200"));
            Assert.AreEqual("100", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Returns_null_when_no_base_and_no_active_variant() {
            var store = new VariantStore();
            var n = Node(null, ("mobile", "200"));
            Assert.IsNull(VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Variant_only_attr_returns_override_when_active() {
            var store = new VariantStore();
            store.Set("mobile", true);
            var n = Node(null, ("mobile", "200"));
            Assert.AreEqual("200", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Returns_null_when_attr_not_present_at_all() {
            var store = new VariantStore();
            var n = new ElementNode("X");
            Assert.IsNull(VariantResolver.ResolveAttribute(n, "missing", store));
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（VariantResolver 不存在）**

- [ ] **Step 3: 实现 VariantResolver**

`Runtime/Core/Variants/VariantResolver.cs`：

```csharp
using PromptUGUI.Application;
using PromptUGUI.IR;

namespace PromptUGUI.Variants {
    /// <summary>
    /// 在解析任意属性时调用：先看 VariantOverrides 中按声明顺序"最后一个激活"的变体，
    /// 找到则返回其值；否则回退到基础 Attributes；都没有则返回 null。
    /// 这是 spec §8.3 的 last-active-wins 规则。
    /// </summary>
    public static class VariantResolver {
        public static string ResolveAttribute(
            ElementNode node, string attrName, VariantStore store) {

            if (node.VariantOverrides.TryGetValue(attrName, out var list)) {
                for (int i = list.Count - 1; i >= 0; i--) {
                    if (store.IsActive(list[i].Variant))
                        return list[i].Value;
                }
            }
            return node.Attributes.TryGetValue(attrName, out var v) ? v : null;
        }
    }
}
```

- [ ] **Step 4: 跑测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Variants Tests/EditMode/Variants
git commit -m "feat(variants): VariantResolver implements last-active-wins selection"
```

---

## Task 7：抽离 ControlAttributeApplier 并接入 Instantiator

**Files:**
- Create: `Runtime/Application/ControlAttributeApplier.cs`
- Modify: `Runtime/Application/ScreenInstantiator.cs`
- Modify: `Runtime/Application/UI.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

把"应用 control 属性"逻辑抽到独立类，初次实例化与 Task 11 的 ReSolve 共用同一份代码。Instantiator 改走 VariantResolver。

- [ ] **Step 1: 写 PlayMode 测试**

追加到 `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`（在最后一个 `}` 之前）：

```csharp
        [UnityTest]
        public IEnumerator Variant_attribute_override_applies_when_variant_active_at_open() {
            UI.LoadDocument("v1", @"<PromptUGUI version='1'>
                <Screen name='V1'>
                    <Image id='bg' anchor='top-left' size='100x50'
                           anchor.mobile='top-right' size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            UI.Variants.Set("mobile", true);
            var screen = UI.Open("V1");

            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(200, 100), rt.sizeDelta);
            // anchor=top-right → anchorMin/Max=(1,1)
            Assert.AreEqual(new Vector2(1, 1), rt.anchorMin);
            Assert.AreEqual(new Vector2(1, 1), rt.anchorMax);

            UI.Close("V1");
            UI.Variants.Set("mobile", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Base_attribute_used_when_no_variant_active_at_open() {
            UI.LoadDocument("v2", @"<PromptUGUI version='1'>
                <Screen name='V2'>
                    <Image id='bg' anchor='top-left' size='100x50'
                           size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("V2");

            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Close("V2");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_only_attr_without_base_falls_through_to_default_when_inactive() {
            // margin.mobile 在 mobile 未激活时不应应用，sizeDelta 不受影响
            UI.LoadDocument("v3", @"<PromptUGUI version='1'>
                <Screen name='V3'>
                    <Image id='bg' anchor='top-left' size='100x50' margin.mobile='8'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("V3");
            var rt = screen.Get<PromptImage>("bg").RectTransform;
            // 没 margin → anchoredPosition = (0, 0)
            Assert.AreEqual(new Vector2(0, 0), rt.anchoredPosition);
            UI.Close("V3");
            yield return null;
        }
```

注意：`PromptImage` 是文件已有的 using 别名（`using PromptImage = PromptUGUI.Controls.Image;`）。

- [ ] **Step 2: 跑测试，FAIL（store 没接进 instantiator，attr.mobile 被当陌生属性丢弃）**

- [ ] **Step 3: 创建 ControlAttributeApplier**

`Runtime/Application/ControlAttributeApplier.cs`：

```csharp
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using PromptUGUI.Variants;

namespace PromptUGUI.Application {
    /// <summary>
    /// 把 ElementNode 上的属性（基础值 + Variant 覆盖）解算后应用到一个已构造好的
    /// Control 实例上。被 ScreenInstantiator 在初次实例化与 Screen.ReSolve 共用，
    /// 是 spec §8.1 "切换 Variant 触发已实例化 Screen 的重解算" 的核心算法承载者。
    /// </summary>
    internal static class ControlAttributeApplier {
        public static void Apply(ElementNode node, Control control,
                                 ControlRegistry.Entry entry, VariantStore variants) {

            // 控件特定属性：基础 + 变体 keys 求并集
            var allKeys = new HashSet<string>(node.Attributes.Keys);
            foreach (var k in node.VariantOverrides.Keys) allKeys.Add(k);
            foreach (var attrName in allKeys) {
                if (IsCommonAttribute(attrName)) continue;
                if (!entry.Meta.HasAttribute(attrName)) continue;
                var v = VariantResolver.ResolveAttribute(node, attrName, variants);
                if (v != null) entry.Meta.Apply(control, attrName, v);
            }

            // 文本简写
            if (!string.IsNullOrEmpty(node.TextContent) && entry.DefaultTextAttr != null)
                entry.Meta.Apply(control, entry.DefaultTextAttr, node.TextContent);

            // 通用属性
            var anchor = VariantResolver.ResolveAttribute(node, "anchor", variants);
            var size   = VariantResolver.ResolveAttribute(node, "size",   variants);
            var width  = VariantResolver.ResolveAttribute(node, "width",  variants);
            var height = VariantResolver.ResolveAttribute(node, "height", variants);
            var margin = VariantResolver.ResolveAttribute(node, "margin", variants);
            var pivot  = VariantResolver.ResolveAttribute(node, "pivot",  variants);
            var hiddenStr       = VariantResolver.ResolveAttribute(node, "hidden", variants);
            var interactableStr = VariantResolver.ResolveAttribute(node, "interactable", variants);
            bool hidden       = hiddenStr == "true";
            bool interactable = interactableStr != "false";

            control.ApplyCommon(anchor, size, width, height, margin, pivot, hidden, interactable);
        }

        public static bool IsCommonAttribute(string name) {
            switch (name) {
                case "anchor":
                case "size":
                case "width":
                case "height":
                case "margin":
                case "pivot":
                case "hidden":
                case "interactable":
                    return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 4: 改 ScreenInstantiator 注入 store + 用 Applier**

把 `Runtime/Application/ScreenInstantiator.cs` 整个文件替换为：

```csharp
using System.Collections.Generic;
using System.Reflection;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application {
    public sealed class InstantiationResult {
        public GameObject Root;
        public Dictionary<string, IControl> Controls;
        public Dictionary<ElementNode, Control> NodeToControl;
    }

    public sealed class ScreenInstantiator {
        readonly ControlRegistry _registry;
        readonly VariantStore _variants;

        public ScreenInstantiator(ControlRegistry registry, VariantStore variants) {
            _registry = registry;
            _variants = variants;
        }

        public InstantiationResult Instantiate(ScreenDef def) {
            return InstantiateInto(new GameObject(def.Name, typeof(RectTransform)), def);
        }

        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def) {
            var result = new InstantiationResult {
                Root = root,
                Controls = new Dictionary<string, IControl>(),
                NodeToControl = new Dictionary<ElementNode, Control>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false,
                                     result.Controls, result.NodeToControl);

            return result;
        }

        internal void InstantiateRecursive(ElementNode node, Transform parent,
                                           bool parentIsLayoutGroup,
                                           Dictionary<string, IControl> controls,
                                           Dictionary<ElementNode, Control> nodeMap) {
            if (parentIsLayoutGroup) {
                if (node.Attributes.ContainsKey("anchor")
                    || node.VariantOverrides.ContainsKey("anchor"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: anchor ignored inside layout group");
                if (node.Attributes.ContainsKey("margin")
                    || node.VariantOverrides.ContainsKey("margin"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: margin ignored inside layout group");
            }

            var entry = _registry.Resolve(node.Tag);

            GameObject go;
            Control control;

            if (entry.Prefab != null) {
                go = Object.Instantiate(entry.Prefab, parent);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            } else {
                go = new GameObject(node.Id ?? node.Tag, typeof(RectTransform));
                go.transform.SetParent(parent, worldPositionStays: false);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            }

            if (!string.IsNullOrEmpty(node.Id))
                go.name = node.Id;

            control.Id = node.Id;
            if (entry.Prefab != null)
                BindFields(control, go);
            control.AttachTo(go);

            if (!string.IsNullOrEmpty(node.Id))
                controls[node.Id] = control;
            nodeMap[node] = control;

            ControlAttributeApplier.Apply(node, control, entry, _variants);

            // 子节点的 id 作用域
            Dictionary<string, IControl> childScope = controls;
            if (node.IsTemplateInstanceRoot) {
                childScope = new Dictionary<string, IControl>();
                control.ReplaceScopedIds(childScope);
            }

            bool selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, childScope, nodeMap);
        }

        static void BindFields(Control control, GameObject prefabRoot) {
            var t = control.GetType();
            foreach (var f in t.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var bind = f.GetCustomAttribute<BindAttribute>();
                if (bind == null) continue;

                string childName = bind.ChildName ?? StripUnderscore(f.Name);
                var childTransform = FindChildByName(prefabRoot.transform, childName);
                if (childTransform == null) {
                    Debug.LogWarning(
                        $"[Bind] {t.Name}.{f.Name}: child '{childName}' not found");
                    continue;
                }

                var component = childTransform.GetComponent(f.FieldType);
                if (component == null) {
                    Debug.LogWarning(
                        $"[Bind] {t.Name}.{f.Name}: child '{childName}' " +
                        $"has no {f.FieldType.Name}");
                    continue;
                }

                f.SetValue(control, component);
            }
        }

        static string StripUnderscore(string name) =>
            name.StartsWith("_") ? char.ToUpperInvariant(name[1]) + name.Substring(2) : name;

        static Transform FindChildByName(Transform parent, string name) {
            for (int i = 0; i < parent.childCount; i++) {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
            }
            return null;
        }
    }
}
```

注意：M1 的 `IsCommonAttribute` 静态方法已迁到 `ControlAttributeApplier`；M1 的 `BindFields` / `StripUnderscore` / `FindChildByName` 保持不变。InstantiateRecursive 现在还接收 `nodeMap` 参数。

- [ ] **Step 5: 改 UI.cs 把 store 传给 instantiator**

修改 `Runtime/Application/UI.cs` 的 `Open` 方法（在 Task 5 时已加 store 字段），把构造 instantiator 的行改为：

```csharp
            var inst = new ScreenInstantiator(_registry, _variantStore);
            var screen = new Screen(def, inst);
            screen.Open();
```

注意：Screen 的构造仍是旧形式（`new Screen(def, inst)`），Task 11 才改。

- [ ] **Step 6: 检查所有现有 test 中的 ScreenInstantiator 构造**

`grep -rn "new ScreenInstantiator(" Tests/` 查看哪里直接构造 instantiator 而不是走 UI.Open。修复每一处：把 `new ScreenInstantiator(_reg)` 改为 `new ScreenInstantiator(_reg, UI.VariantStore)`，或者更稳的做法：在测试 SetUp 里走 `var store = new VariantStore(); var inst = new ScreenInstantiator(_reg, store);`。

具体改 `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs` 的 `ScreenInstantiatorTests` 类——它的 `[SetUp]` 已 `_reg = UI.Registry`，加一行 `_store = UI.VariantStore;`，并把所有 `new ScreenInstantiator(_reg)` 改为 `new ScreenInstantiator(_reg, _store)`。类顶部加字段：

```csharp
        VariantStore _store;
```

`[SetUp]` 中加：

```csharp
            _store = UI.VariantStore;
```

替换所有 `new ScreenInstantiator(_reg)` → `new ScreenInstantiator(_reg, _store)`（共 ~5 处）。

类似地检查 `Tests/PlayMode/Controls/`、`Tests/PlayMode/Registry/` 下任何直接 `new ScreenInstantiator(...)` 的地方，按同样模式修复。

`UI.VariantStore` 是 `internal` 的——如果测试 assembly 没访问 internals，给 `Runtime/PromptUGUI.Runtime.asmdef` 同目录或加 `[InternalsVisibleTo]`。M2 plan 已经处理过 internal 访问问题；这里如果遇到访问失败，加 AssemblyInfo.cs 的 `[assembly: InternalsVisibleTo("PromptUGUI.Tests.PlayMode")]` 一行（M1 的 `Runtime/AssemblyInfo.cs` 已存在该机制）。

- [ ] **Step 7: 触发编译；跑全 PlayMode + EditMode 测试，全 PASS**

预期：M1/M2 既有测试无回归；3 个新增 Variant attribute 测试 PASS。

- [ ] **Step 8: Commit**

```bash
git add Runtime/Application Tests/PlayMode/Lifecycle
git commit -m "feat(app): inject VariantStore into instantiator; ControlAttributeApplier shared by initial instantiation"
```

---

## Task 8：Instantiator 在 Open 时执行 `<Variant>/<Add>` 块

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

`<Variant>` 块在 Screen Open 时遍历：当前激活的 Variant 对应的 Add 块需要把子节点实例化到 IntoPath 指向的父节点中、按 At 位置插入。

- [ ] **Step 1: 写测试**

追加到 `ScreenLifecycleTests.cs`：

```csharp
        [UnityTest]
        public IEnumerator Variant_add_block_creates_node_when_active_at_open() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a1", @"<PromptUGUI version='1'>
                <Screen name='A1'>
                    <Frame id='root'/>
                    <Variant when='m'>
                        <Add into='#root'><Image id='joy'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A1");

            var rootGo = screen.Get<PromptUGUI.Controls.Frame>("root").GameObject;
            Assert.AreEqual(1, rootGo.transform.childCount);
            Assert.IsNotNull(screen.Get<PromptImage>("joy"));

            UI.Close("A1");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_block_skipped_when_inactive_at_open() {
            UI.LoadDocument("a2", @"<PromptUGUI version='1'>
                <Screen name='A2'>
                    <Frame id='root'/>
                    <Variant when='m'>
                        <Add into='#root'><Image id='joy'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A2");

            var rootGo = screen.Get<PromptUGUI.Controls.Frame>("root").GameObject;
            Assert.AreEqual(0, rootGo.transform.childCount);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("joy"));

            UI.Close("A2");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_root_creates_at_screen_root() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a3", @"<PromptUGUI version='1'>
                <Screen name='A3'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A3");
            // screen root contains: base, extra
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            UI.Close("A3");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_at_index_inserts_at_position() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a4", @"<PromptUGUI version='1'>
                <Screen name='A4'>
                    <VStack id='v'>
                        <Image id='a'/>
                        <Image id='c'/>
                    </VStack>
                    <Variant when='m'>
                        <Add into='#v' at='1'><Image id='b'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A4");
            var v = screen.Get<PromptVStack>("v").GameObject.transform;
            Assert.AreEqual("a", v.GetChild(0).name);
            Assert.AreEqual("b", v.GetChild(1).name);
            Assert.AreEqual("c", v.GetChild(2).name);
            UI.Close("A4");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_at_start_inserts_first() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a5", @"<PromptUGUI version='1'>
                <Screen name='A5'>
                    <VStack id='v'>
                        <Image id='a'/>
                    </VStack>
                    <Variant when='m'>
                        <Add into='#v' at='start'><Image id='first'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A5");
            var v = screen.Get<PromptVStack>("v").GameObject.transform;
            Assert.AreEqual("first", v.GetChild(0).name);
            Assert.AreEqual("a", v.GetChild(1).name);
            UI.Close("A5");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_template_instance_path_inserts_inside_template() {
            // into="#dialog/inner" → 走模板内部 ScopedIds（spec drift §5 / spec §8.4 扩展）
            UI.Variants.Set("m", true);
            UI.LoadDocument("a6", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Grid id='inner' columns='6'/>
                    </Frame>
                </Template>
                <Screen name='A6'>
                    <Box id='dialog'/>
                    <Variant when='m'>
                        <Add into='#dialog/inner'><Image id='item'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");

            var screen = UI.Open("A6");
            var inner = screen.Get<PromptUGUI.Controls.Grid>("dialog/inner");
            Assert.AreEqual(1, inner.GameObject.transform.childCount);
            Assert.AreEqual("item", inner.GameObject.transform.GetChild(0).name);

            UI.Close("A6");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_unknown_path_segment_throws_at_open() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a7", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame><Grid id='inner' columns='6'/></Frame>
                </Template>
                <Screen name='A7'>
                    <Box id='dialog'/>
                    <Variant when='m'>
                        <Add into='#dialog/missing'><Image id='item'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");

            Assert.Throws<System.InvalidOperationException>(() => UI.Open("A7"));

            UI.Variants.Set("m", false);
            yield return null;
        }
```

注意 `PromptVStack` / `PromptImage` 是该测试文件已有的 using 别名。`PromptUGUI.Controls.Grid` 在文件中尚未 using——可以在测试文件顶部加 `using PromptGrid = PromptUGUI.Controls.Grid;` 然后用 `PromptGrid` 简称（与既有 PlayMode 测试风格一致）。

- [ ] **Step 2: 跑测试，FAIL（Variant 块未被处理）**

- [ ] **Step 3: 实现 Add 块逻辑**

修改 `Runtime/Application/ScreenInstantiator.cs`：用下方代码块**整段替换** `InstantiateInto` 方法体，并在其后**新增** `ApplyAddBlock` + `ResolveAddTarget` 两个方法（都在 `ScreenInstantiator` 类内、`BindFields` 之前）：

```csharp
        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def) {
            var result = new InstantiationResult {
                Root = root,
                Controls = new Dictionary<string, IControl>(),
                NodeToControl = new Dictionary<ElementNode, Control>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false,
                                     result.Controls, result.NodeToControl);

            foreach (var block in def.Variants) {
                if (!_variants.IsActive(block.When)) continue;
                ApplyAddBlock(block, result);
            }

            return result;
        }

        internal List<GameObject> ApplyAddBlock(VariantBlock block, InstantiationResult result) {
            var roots = new List<GameObject>();
            foreach (var add in block.Adds) {
                var parent = ResolveAddTarget(result.Root, result.Controls, add.IntoPath);
                bool parentIsLayoutGroup = parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

                // 实例化前：记下当前 child 数；新增 N 个 child 此时都被追加到末尾
                int prevCount = parent.childCount;
                foreach (var child in add.Children)
                    InstantiateRecursive(child, parent, parentIsLayoutGroup,
                                         result.Controls, result.NodeToControl);
                int addedN = parent.childCount - prevCount;

                // 计算目标基准索引（at='end' 时等于 prevCount，保持新增项原位在末尾）
                int targetBase;
                if (add.At == "start")      targetBase = 0;
                else if (add.At == "end")   targetBase = prevCount;
                else if (int.TryParse(add.At, out var k)) {
                    if (k < 0) k = 0;
                    if (k > prevCount) k = prevCount;  // OOB clamp
                    targetBase = k;
                } else {
                    throw new System.InvalidOperationException(
                        $"<Add at='{add.At}'>: must be 'start' / 'end' / non-negative integer");
                }

                // 把刚加进来的 N 个 child 从末尾移到 targetBase..targetBase+N-1
                if (targetBase != prevCount) {
                    for (int i = 0; i < addedN; i++) {
                        var c = parent.GetChild(prevCount + i);  // 它们仍在末尾
                        c.SetSiblingIndex(targetBase + i);
                    }
                }

                for (int i = 0; i < addedN; i++)
                    roots.Add(parent.GetChild(targetBase + i).gameObject);
            }
            return roots;
        }

        static Transform ResolveAddTarget(GameObject screenRoot,
                                          IReadOnlyDictionary<string, IControl> controls,
                                          string intoPath) {
            if (intoPath == "@root") return screenRoot.transform;
            if (intoPath.StartsWith("#")) {
                var path = intoPath.Substring(1);
                if (string.IsNullOrEmpty(path))
                    throw new System.InvalidOperationException(
                        $"<Add into='{intoPath}'>: id is empty after '#'");

                // 与 Screen.Get(idPath) 同义：首段查 top-level controls，后续段下钻 ScopedIds
                var segs = path.Split('/');
                if (!controls.TryGetValue(segs[0], out var current))
                    throw new System.InvalidOperationException(
                        $"<Add into='{intoPath}'>: id '{segs[0]}' not found in screen");
                for (int i = 1; i < segs.Length; i++) {
                    if (!current.ScopedIds.TryGetValue(segs[i], out var next))
                        throw new System.InvalidOperationException(
                            $"<Add into='{intoPath}'>: '{segs[i]}' not found under " +
                            $"'{string.Join("/", segs, 0, i)}'");
                    current = next;
                }
                return current.GameObject.transform;
            }
            throw new System.InvalidOperationException(
                $"<Add into='{intoPath}'>: must be '@root' or '#id' / '#id/path/...'");
        }
```

最后的 `roots` 列表保留新增 GameObject 引用，给 Task 12 反激活销毁用。

- [ ] **Step 4: 跑测试，全 PASS**

预期：7 个新增 PlayMode 测试 PASS（5 个基础 + 2 个 path 解析）；之前测试无回归。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application Tests/PlayMode/Lifecycle
git commit -m "feat(app): instantiator executes <Variant><Add> blocks at Open; into supports #id/path/to/inner"
```

---

## Task 9：Layout group 子节点 .var 警告（spec §13 R3）

**Files:**
- Modify: `Tests/PlayMode/Lifecycle/StackChildWarningTests.cs`

Task 7 在 InstantiateRecursive 开头的 layout-group 警告分支已经把 `node.VariantOverrides.ContainsKey("anchor")` / `"margin"` 加入判断。现在补上回归测试，确认 .var 形式同样发 warning。

- [ ] **Step 1: 写测试**

追加到 `Tests/PlayMode/Lifecycle/StackChildWarningTests.cs`：

```csharp
        [UnityTest]
        public IEnumerator Variant_anchor_on_VStack_child_logs_warning() {
            LogAssert.Expect(LogType.Warning,
                new Regex("anchor.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("vw1", @"<PromptUGUI version='1'>
                <Screen name='VW1'>
                    <VStack id='v' anchor='center' size='200x200'>
                        <Image id='c' anchor.mobile='top-left'/>
                    </VStack>
                </Screen></PromptUGUI>");
            UI.Open("VW1");

            yield return null;
            UI.Close("VW1");
        }

        [UnityTest]
        public IEnumerator Variant_margin_on_HStack_child_logs_warning() {
            LogAssert.Expect(LogType.Warning,
                new Regex("margin.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("vw2", @"<PromptUGUI version='1'>
                <Screen name='VW2'>
                    <HStack id='h' anchor='center' size='200x100'>
                        <Image id='c' margin.mobile='8'/>
                    </HStack>
                </Screen></PromptUGUI>");
            UI.Open("VW2");

            yield return null;
            UI.Close("VW2");
        }
```

- [ ] **Step 2: 跑测试，全 PASS**

（Task 7 已加判定逻辑，这里只是回归保障；测试应直接 PASS。）

- [ ] **Step 3: Commit**

```bash
git add Tests/PlayMode/Lifecycle/StackChildWarningTests.cs
git commit -m "test(app): warn on .variant anchor/margin inside layout groups (spec §13 R3)"
```

---

## Task 10：Screen 持有 NodeMap（为 ReSolve 做准备）

**Files:**
- Modify: `Runtime/Application/Screen.cs`

无独立测试——基础设施改动，由 Task 11 的 ReSolve 测试覆盖。Task 7 已让 InstantiationResult 暴露 NodeToControl；这一步把它存进 Screen。

- [ ] **Step 1: 修改 Screen 持有 NodeMap + Registry/VariantStore**

把 `Runtime/Application/Screen.cs` 整个文件替换为：

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application {

    public interface IScreen : IDisposable {
        string Name { get; }
        GameObject RootGameObject { get; }
        T Get<T>(string id) where T : class, IControl;
        IControl Get(string id);
    }

    public sealed class Screen : IScreen {
        readonly ScreenDef _def;
        readonly ScreenInstantiator _instantiator;
        readonly ControlRegistry _registry;
        readonly VariantStore _variants;
        readonly Dictionary<string, IControl> _byId = new();
        readonly Dictionary<ElementNode, Control> _nodeMap = new();
        readonly List<IDisposable> _subscriptions = new();

        public string Name => _def.Name;
        public GameObject RootGameObject { get; private set; }

        internal IReadOnlyDictionary<ElementNode, Control> NodeMap => _nodeMap;
        internal ScreenDef Def => _def;
        internal VariantStore Variants => _variants;

        public Screen(ScreenDef def, ScreenInstantiator instantiator,
                      ControlRegistry registry, VariantStore variants) {
            _def = def;
            _instantiator = instantiator;
            _registry = registry;
            _variants = variants;
        }

        public void Open() {
            var root = new GameObject(_def.Name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var result = _instantiator.InstantiateInto(root, _def);
            RootGameObject = result.Root;
            foreach (var kv in result.Controls) _byId[kv.Key] = kv.Value;
            foreach (var kv in result.NodeToControl) _nodeMap[kv.Key] = kv.Value;
        }

        public void Close() {
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            if (RootGameObject != null) {
                UnityEngine.Object.Destroy(RootGameObject);
                RootGameObject = null;
            }
            _byId.Clear();
            _nodeMap.Clear();
        }

        public T Get<T>(string idPath) where T : class, IControl {
            var c = Get(idPath);
            if (c is not T typed)
                throw new InvalidCastException(
                    $"id '{idPath}' is {c.GetType().Name}, not {typeof(T).Name}");
            return typed;
        }

        public IControl Get(string idPath) {
            var segs = idPath.Split('/');
            if (!_byId.TryGetValue(segs[0], out var current))
                throw new KeyNotFoundException(
                    $"id '{segs[0]}' not found in screen '{Name}'");
            for (int i = 1; i < segs.Length; i++) {
                var seg = segs[i];
                if (!current.ScopedIds.TryGetValue(seg, out var next))
                    throw new KeyNotFoundException(
                        $"id '{seg}' not found under '{string.Join("/", segs, 0, i)}' in screen '{Name}'");
                current = next;
            }
            return current;
        }

        public void Track(IDisposable d) => _subscriptions.Add(d);

        public void Dispose() => Close();

        // ReSolve 与 Add 块管理由 Task 11/12 填入下方。
    }
}
```

注意：Screen 构造现在 4 参数。需要更新 UI.cs 的 Open 方法把 registry / store 也传进来：

修改 `Runtime/Application/UI.cs` 的 Open：

```csharp
        public static Screen Open(string screenName) {
            if (_open.TryGetValue(screenName, out var existing)) return existing;
            if (!_docs.TryGetValue(screenName, out var def))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' not loaded; call LoadDocument first");

            var inst = new ScreenInstantiator(_registry, _variantStore);
            var screen = new Screen(def, inst, _registry, _variantStore);
            screen.Open();
            _open[screenName] = screen;
            return screen;
        }
```

- [ ] **Step 2: 修复测试中的 Screen 直接构造**

`grep -rn "new Screen(" Tests/` 找出所有直接 `new Screen(...)` 的地方，把构造改为 4 参数：

例 `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs` 内：

```csharp
var screen = new PromptScreen(doc.Screens[0], inst);
```

改为：

```csharp
var screen = new PromptScreen(doc.Screens[0], inst, _reg, _store);
```

涉及的方法：`Screen_creates_canvas_and_can_get_by_id`、`Screen_get_unknown_id_throws`。

- [ ] **Step 3: 触发编译；跑全测试，全 PASS（无回归）**

- [ ] **Step 4: Commit**

```bash
git add Runtime/Application Tests/PlayMode
git commit -m "feat(app): Screen holds NodeMap + Registry + VariantStore for re-solve"
```

---

## Task 11：Screen.ReSolve() 重解算属性

**Files:**
- Modify: `Runtime/Registry/ControlMeta.cs`（不需要——`ControlAttributeApplier` 已用 Meta.HasAttribute + Meta.Apply，无需新接口）
- Modify: `Runtime/Application/Screen.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

ControlMeta 不需要扩展（之前考虑过暴露 AttrNames 但 ControlAttributeApplier 已通过对 node 的 attrs/overrides keys 求并集覆盖了）。

- [ ] **Step 1: 写测试**

追加到 `ScreenLifecycleTests.cs`：

```csharp
        [UnityTest]
        public IEnumerator ReSolve_updates_size_when_variant_toggles_at_runtime() {
            UI.LoadDocument("rs1", @"<PromptUGUI version='1'>
                <Screen name='RS1'>
                    <Image id='bg' anchor='top-left' size='100x50' size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS1");
            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Variants.Set("mobile", true);
            yield return null;
            Assert.AreEqual(new Vector2(200, 100), rt.sizeDelta);

            UI.Variants.Set("mobile", false);
            yield return null;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Close("RS1");
        }

        [UnityTest]
        public IEnumerator ReSolve_does_not_recreate_GameObjects() {
            UI.LoadDocument("rs2", @"<PromptUGUI version='1'>
                <Screen name='RS2'>
                    <Image id='bg' size='100x50' size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS2");
            var bg1 = screen.Get<PromptImage>("bg").GameObject;

            UI.Variants.Set("mobile", true);
            yield return null;

            var bg2 = screen.Get<PromptImage>("bg").GameObject;
            Assert.AreSame(bg1, bg2);

            UI.Close("RS2");
            UI.Variants.Set("mobile", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_updates_control_specific_attributes() {
            UI.LoadDocument("rs3", @"<PromptUGUI version='1'>
                <Screen name='RS3'>
                    <Text id='t' fontSize='24' fontSize.mobile='48'>Hello</Text>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS3");
            var tmp = screen.Get<PromptText>("t").GameObject.GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual(24, tmp.fontSize);

            UI.Variants.Set("mobile", true);
            yield return null;
            Assert.AreEqual(48, tmp.fontSize);

            UI.Variants.Set("mobile", false);
            UI.Close("RS3");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_with_no_variant_overrides_is_noop() {
            UI.LoadDocument("rs4", @"<PromptUGUI version='1'>
                <Screen name='RS4'>
                    <Image id='bg' anchor='top-left' size='100x50'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS4");
            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            // 切换无关变体不应改变属性
            UI.Variants.Set("mobile", true);
            yield return null;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Variants.Set("mobile", false);
            UI.Close("RS4");
            yield return null;
        }
```

- [ ] **Step 2: 跑测试，FAIL（ReSolve 未实现 / Screen 未订阅 store）**

- [ ] **Step 3: 实现 Screen.ReSolve + 订阅 VariantStore.Changed**

修改 `Runtime/Application/Screen.cs`。在 Open 方法内、`foreach (var kv in result.NodeToControl)` 后加上订阅；并加 ReSolve 方法。把 Screen 类内（`public void Close()` 之前）增加：

```csharp
        IDisposable _variantSub;

        void SubscribeToVariantChanges() {
            _variantSub = _variants.Changed.Subscribe(_ => ReSolve());
        }

        public void ReSolve() {
            foreach (var kv in _nodeMap) {
                var node = kv.Key;
                var control = kv.Value;
                var entry = _registry.Resolve(node.Tag);
                ControlAttributeApplier.Apply(node, control, entry, _variants);
            }
        }
```

并在 `Open` 末尾（`foreach (var kv in result.NodeToControl) _nodeMap[kv.Key] = kv.Value;` 之后）加：

```csharp
            SubscribeToVariantChanges();
```

在 `Close` 内（`foreach (var d in _subscriptions) d.Dispose();` 之前）加：

```csharp
            _variantSub?.Dispose();
            _variantSub = null;
```

注意 `Subscribe` 来自 R3 的 Observable；Screen 文件需要 `using R3;`，加到 using 区。

- [ ] **Step 4: 跑测试，全 PASS**

预期：4 个新增 + M2 测试无回归。注意 `ReSolve_updates_control_specific_attributes` 涉及 fontSize 整数 setter；ControlMeta 已支持 int 类型（M1 task 15）。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application Tests/PlayMode
git commit -m "feat(app): Screen.ReSolve() reapplies attributes on variant change"
```

---

## Task 12：Add 块的动态出现/消失

**Files:**
- Modify: `Runtime/Application/Screen.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

变体首次激活时实例化 `<Add>` 子树并永久挂在 `_byId` / `_nodeMap`；之后 toggle 只切根 GameObject 的 `SetActive`，从不 `Destroy`。这样代码侧持有的引用 / R3 订阅跨 toggle 周期保持稳定；只在 Close 时随 RootGameObject 整体销毁（Strategy C）。

- [ ] **Step 1: 写测试**

追加到 `ScreenLifecycleTests.cs`：

```csharp
        [UnityTest]
        public IEnumerator ReSolve_first_activation_instantiates_add_block() {
            UI.LoadDocument("rsa1", @"<PromptUGUI version='1'>
                <Screen name='RSA1'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA1");

            // 首次激活前：Add 子树未实例化，screen.Get 抛 KeyNotFound
            Assert.AreEqual(1, screen.RootGameObject.transform.childCount);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("extra"));

            UI.Variants.Set("m", true);
            yield return null;

            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            var extra = screen.Get<PromptImage>("extra");
            Assert.IsNotNull(extra);
            Assert.IsTrue(extra.GameObject.activeSelf);

            UI.Close("RSA1");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_deactivation_hides_via_SetActive_keeps_instance() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("rsa2", @"<PromptUGUI version='1'>
                <Screen name='RSA2'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA2");
            var extraGo = screen.Get<PromptImage>("extra").GameObject;
            Assert.IsTrue(extraGo.activeSelf);

            UI.Variants.Set("m", false);
            yield return null;

            // Strategy C：GameObject 仍在场景里、未销毁；id 仍可解析；activeSelf=false
            Assert.IsFalse(extraGo.activeSelf);
            Assert.IsTrue(extraGo != null);  // 与 Strategy A 不同：不是 Unity null
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            Assert.AreSame(extraGo, screen.Get<PromptImage>("extra").GameObject);

            UI.Close("RSA2");
        }

        [UnityTest]
        public IEnumerator ReSolve_re_activation_reuses_same_GameObject_instance() {
            UI.LoadDocument("rsa3", @"<PromptUGUI version='1'>
                <Screen name='RSA3'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA3");

            UI.Variants.Set("m", true);
            yield return null;
            var first = screen.Get<PromptImage>("extra").GameObject;
            Assert.IsTrue(first.activeSelf);

            UI.Variants.Set("m", false);
            yield return null;
            Assert.IsFalse(first.activeSelf);

            UI.Variants.Set("m", true);
            yield return null;

            var second = screen.Get<PromptImage>("extra").GameObject;
            // Strategy C 的核心保证：同一 GameObject 实例
            Assert.AreSame(first, second);
            Assert.IsTrue(second.activeSelf);

            UI.Close("RSA3");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_subscriptions_on_add_controls_survive_toggle_cycle() {
            // Strategy C 的实用价值：在 Add 块的 Btn 上订阅一次 OnClick，跨 toggle 周期仍有效
            UI.LoadDocument("rsa4", @"<PromptUGUI version='1'>
                <Screen name='RSA4'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Btn id='extraBtn'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA4");

            UI.Variants.Set("m", true);
            yield return null;

            int clicks = 0;
            var btn = screen.Get<PromptUGUI.Controls.Btn>("extraBtn");
            btn.OnClick.Subscribe(_ => clicks++).AddTo(screen);

            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(1, clicks);

            UI.Variants.Set("m", false);
            yield return null;
            UI.Variants.Set("m", true);
            yield return null;

            // 同一 Btn 实例、同一 Subject<Unit> → 订阅仍触发
            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(2, clicks);

            UI.Close("RSA4");
            UI.Variants.Set("m", false);
            yield return null;
        }
```

- [ ] **Step 2: 跑测试，FAIL（Add 块在 ReSolve 时不变化）**

- [ ] **Step 3: 在 Screen 维护 Add 块状态 + 在 ReSolve 中处理**

修改 `Runtime/Application/Screen.cs`：

(1) 在 Screen 类字段区加：

```csharp
        // 已实例化的 Add 块（不论当前是否可见）。Strategy C：首次进入激活才实例化；
        // 之后 toggle 仅切根 GameObject 的 SetActive，永不 Destroy/移除字典项；
        // 只在 Close 时随 RootGameObject 整体销毁。
        readonly Dictionary<VariantBlock, AddInstance> _addInstances = new();

        sealed class AddInstance {
            public List<GameObject> Roots = new();
            public List<string> AddedIds = new();
            public List<ElementNode> AddedNodes = new();
        }
```

(2) 把 Add 块的处理从 ScreenInstantiator 迁回 Screen——这样反激活时 Screen 才能精确知道哪些 GameObject / id / node 是某个 Add 块创建的。

先修改 `Runtime/Application/ScreenInstantiator.cs` 的 `InstantiateInto`：把 Task 8 加的"`foreach (var block in def.Variants) { ... ApplyAddBlock(block, result); }`"循环**整段删除**。`ApplyAddBlock` 仍保留为 `internal`，由 Screen 调用。

然后修改 `Runtime/Application/Screen.cs` 的 `Open`：在 `foreach (var kv in result.NodeToControl) _nodeMap[kv.Key] = kv.Value;` 之后、`SubscribeToVariantChanges();` 之前**加上**：

```csharp
            foreach (var block in _def.Variants) {
                if (_variants.IsActive(block.When))
                    ActivateAddBlock(block);
            }
```

(3) 加 `ActivateAddBlock` / `DeactivateAddBlock` 方法（在 Screen 类内）。两者都做成幂等的——`ActivateAddBlock` 在已实例化时只 SetActive(true)；`DeactivateAddBlock` 在未实例化或已隐藏时是 no-op：

```csharp
        void ActivateAddBlock(VariantBlock block) {
            if (_addInstances.TryGetValue(block, out var existing)) {
                // 已实例化过：只重新显示根 GameObject，引用与订阅保持稳定
                foreach (var go in existing.Roots)
                    if (go != null) go.SetActive(true);
                return;
            }

            // 首次激活：实例化并永久挂在 Screen 的 _byId / _nodeMap 里
            var pseudoResult = new InstantiationResult {
                Root = RootGameObject,
                Controls = _byId,
                NodeToControl = _nodeMap,
            };

            // 用 keys 差集追踪 Add 块新增的 ids / nodes（Close 时不需要逐项清理，
            // 但留下来便于诊断与未来扩展，例如 Strategy A 兜底分支）
            var prevIds   = new HashSet<string>(_byId.Keys);
            var prevNodes = new HashSet<ElementNode>(_nodeMap.Keys);

            var inst = new AddInstance();
            inst.Roots.AddRange(_instantiator.ApplyAddBlock(block, pseudoResult));

            foreach (var k in _byId.Keys)
                if (!prevIds.Contains(k)) inst.AddedIds.Add(k);
            foreach (var n in _nodeMap.Keys)
                if (!prevNodes.Contains(n)) inst.AddedNodes.Add(n);

            _addInstances[block] = inst;
        }

        void DeactivateAddBlock(VariantBlock block) {
            if (!_addInstances.TryGetValue(block, out var inst)) return;
            // Strategy C：只 SetActive(false) 隐藏；不 Destroy、不从 _byId/_nodeMap 移除——
            // 让代码侧 cached 引用与 R3 订阅跨 toggle 周期持续有效。
            foreach (var go in inst.Roots)
                if (go != null) go.SetActive(false);
        }
```

(4) 改 ReSolve 加 Add 块同步段。因为 Activate / Deactivate 都已是幂等的，循环里直接根据当前激活态调一次即可，无需对比 `wasActive`。把 ReSolve 整段改为：

```csharp
        public void ReSolve() {
            foreach (var block in _def.Variants) {
                if (_variants.IsActive(block.When)) ActivateAddBlock(block);
                else                                DeactivateAddBlock(block);
            }
            foreach (var kv in _nodeMap) {
                var node = kv.Key;
                var control = kv.Value;
                var entry = _registry.Resolve(node.Tag);
                ControlAttributeApplier.Apply(node, control, entry, _variants);
            }
        }
```

注意：`_nodeMap` 里包含已实例化但当前隐藏的 Add 子树节点；ControlAttributeApplier 会对它们也跑一遍属性 reapply，这是 Strategy C 的预期开销（属性赋值对 inactive GameObject 无副作用，但 RectTransform 等仍会被写入正确值，重新 SetActive(true) 后立即就位）。

(5) Close 方法在销毁 RootGameObject 前清空 `_addInstances.Clear()`（GameObject 都跟随 root 被销毁，无需逐个 Destroy）。

把 Close 修改为：

```csharp
        public void Close() {
            _variantSub?.Dispose();
            _variantSub = null;
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            if (RootGameObject != null) {
                UnityEngine.Object.Destroy(RootGameObject);
                RootGameObject = null;
            }
            _byId.Clear();
            _nodeMap.Clear();
            _addInstances.Clear();
        }
```

注意把 `_variantSub?.Dispose()` 放在 Close 顶端，避免 Dispose 时 store 触发 Changed 又调 ReSolve 而我们正在 teardown。

- [ ] **Step 4: 修复 Task 8 测试与现实现的小冲突**

Task 8 的测试 `Variant_add_block_creates_node_when_active_at_open` 期望 Open 时初次激活的 Add 块也被实例化——上面 Screen.Open 已加 `if (_variants.IsActive(block.When)) ActivateAddBlock(block)`，行为不变。

但 instantiator 测试（如果 Tests/PlayMode/Lifecycle 中有直接 `inst.Instantiate(def)` 而后期望 add 块出现的）会找不到 Add 块——因为 Add 块逻辑已迁到 Screen 层。这是预期：Add 块只在 Screen 生命周期里生效，单独 instantiate 一棵 IR 树不应触发它。所以 Task 8 的测试都走 UI.Open，不会受影响。

- [ ] **Step 5: 跑全测试，全 PASS**

预期：4 个 Strategy C 测试（首次激活实例化 / 反激活只 SetActive / 重激活复用同一实例 / 订阅跨周期持续）+ Task 8 既有 Add 测试 + ReSolve 测试 + M1/M2 全部 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application Tests/PlayMode
git commit -m "feat(app): <Add> blocks lazy-instantiate on first activation; toggle via SetActive (Strategy C)"
```

---

## Task 13：UI.Variants.Set 触发 ReSolve（订阅链路确认）

**Files:**
- 仅运行测试，不改代码

Task 11 已让 Screen 在 Open 时订阅 `VariantStore.Changed.Subscribe(_ => ReSolve())`，Task 12 已让 ReSolve 同时刷新属性与 Add 块。本任务只确认 `UI.Variants.Set` → `VariantStore` → `Changed` → `Screen.ReSolve` 链路完整。

- [ ] **Step 1: 写一个 explicit 的链路测试**

追加到 `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`：

```csharp
        [UnityTest]
        public IEnumerator UI_Variants_Set_propagates_through_to_open_screens() {
            // 双 Screen 同时打开，切一个变体应该让两边都重新解算
            UI.LoadDocument("link", @"<PromptUGUI version='1'>
                <Screen name='LinkA'>
                    <Image id='a' anchor='top-left' size='10x10' size.m='20x20'/>
                </Screen>
                <Screen name='LinkB'>
                    <Image id='b' anchor='top-left' size='30x30' size.m='40x40'/>
                </Screen></PromptUGUI>");

            var sa = UI.Open("LinkA");
            var sb = UI.Open("LinkB");

            UI.Variants.Set("m", true);
            yield return null;

            Assert.AreEqual(new Vector2(20, 20),
                sa.Get<PromptImage>("a").RectTransform.sizeDelta);
            Assert.AreEqual(new Vector2(40, 40),
                sb.Get<PromptImage>("b").RectTransform.sizeDelta);

            UI.Close("LinkA");
            UI.Close("LinkB");
            UI.Variants.Set("m", false);
            yield return null;
        }
```

- [ ] **Step 2: 跑测试，PASS**

- [ ] **Step 3: Commit**

```bash
git add Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs
git commit -m "test(app): UI.Variants.Set fans out to all open screens via VariantStore.Changed"
```

---

## Task 14：E2E — mobile-portrait 与 pc 切换

**Files:**
- Create: `Tests/PlayMode/E2E/VariantSwitchTests.cs`

跑通 spec §12 M3 acceptance：同一 Screen 在 pc 与 mobile-portrait 间切换。

XML 写法约束（呼应顶部 spec drift 第 4 条）：因为我们走整体替换、不接受 `size.var="_,..."` 这种部分覆盖，又因 §6.2 strict 校验禁止 stretch 轴上出现 size/width/height，所以 base 上**不放** anchor/size/width/height，把它们分别写进 `pc` 与 `mobile-portrait` 两个显式变体。该写法另一个隐含约束：`pc` 与 `mobile-portrait` 不能同时激活——同时激活时 `width.pc=480` 会落到 `anchor.mobile-portrait=bottom-stretch` 的 X 轴上触发 strict 校验报错。测试用例显式按"先关再开"的顺序切换。

- [ ] **Step 1: 写 E2E 测试**

`Tests/PlayMode/E2E/VariantSwitchTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using PromptVStack = PromptUGUI.Controls.VStack;
using PromptImage  = PromptUGUI.Controls.Image;
using PromptBtn    = PromptUGUI.Controls.Btn;

namespace PromptUGUI.Tests.E2E {

    public class VariantSwitchTests {

        [SetUp] public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }

        [TearDown] public void TearDown() {
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator MainMenu_switches_between_pc_and_mobile_portrait() {
            UI.LoadDocument("e2e", @"<PromptUGUI version='1'>
                <Screen name='Menu'>
                    <VStack id='menuRoot' spacing='12'
                            anchor.pc='center' width.pc='480' height.pc='320'
                            anchor.mobile-portrait='bottom-stretch'
                            height.mobile-portrait='400'
                            margin.mobile-portrait='_,16,80,16'>
                        <Btn id='play' size='240x64'/>
                    </VStack>
                    <Variant when='mobile-portrait'>
                        <Add into='@root'>
                            <Image id='joystick' anchor='bottom-left'
                                   size='160x160' margin='_,_,40,40'/>
                        </Add>
                    </Variant>
                </Screen></PromptUGUI>");

            // PC：仅 pc 激活
            UI.Variants.Set("pc", true);
            var screen = UI.Open("Menu");
            var rootRT = screen.Get<PromptVStack>("menuRoot").RectTransform;

            Assert.AreEqual(new Vector2(0.5f, 0.5f), rootRT.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rootRT.anchorMax);
            Assert.AreEqual(new Vector2(480, 320), rootRT.sizeDelta);
            // mobile-portrait 还从未激活 → joystick 还未实例化（Strategy C 的首次激活语义）
            Assert.AreEqual(1, screen.RootGameObject.transform.childCount);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("joystick"));

            // 切到 mobile-portrait（必须先关 pc：两者同时激活会让 width.pc=480 落到
            // anchor.mobile-portrait=bottom-stretch 的 X 轴上触发 strict 校验报错）
            UI.Variants.Set("pc", false);
            UI.Variants.Set("mobile-portrait", true);
            yield return null;

            Assert.AreEqual(new Vector2(0, 0), rootRT.anchorMin);
            Assert.AreEqual(new Vector2(1, 0), rootRT.anchorMax);
            Assert.AreEqual(400f, rootRT.sizeDelta.y, 0.001f);
            // joystick 首次激活实例化
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            var joystickGo = screen.Get<PromptImage>("joystick").GameObject;
            Assert.IsTrue(joystickGo.activeSelf);

            // 切回 pc（同样先关再开）
            UI.Variants.Set("mobile-portrait", false);
            UI.Variants.Set("pc", true);
            yield return null;

            Assert.AreEqual(new Vector2(0.5f, 0.5f), rootRT.anchorMin);
            Assert.AreEqual(new Vector2(480, 320), rootRT.sizeDelta);
            // Strategy C：joystick 未销毁，只 SetActive(false) 隐藏；引用与 _byId 表项保持
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            Assert.IsFalse(joystickGo.activeSelf);
            Assert.AreSame(joystickGo, screen.Get<PromptImage>("joystick").GameObject);

            UI.Close("Menu");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameObject_identity_preserved_across_variant_switch() {
            // base 整体替换：base size 与 size.m 都是 anchor=center，无 stretch 轴冲突
            UI.LoadDocument("id_e2e", @"<PromptUGUI version='1'>
                <Screen name='Stable'>
                    <VStack id='root' anchor='center' size='200x200' size.m='300x300'>
                        <Btn id='go'/>
                    </VStack>
                </Screen></PromptUGUI>");

            var screen = UI.Open("Stable");
            var rootGo = screen.Get<PromptVStack>("root").GameObject;
            var btnGo = screen.Get<PromptBtn>("go").GameObject;

            UI.Variants.Set("m", true);
            yield return null;

            Assert.AreSame(rootGo, screen.Get<PromptVStack>("root").GameObject);
            Assert.AreSame(btnGo,  screen.Get<PromptBtn>("go").GameObject);

            UI.Variants.Set("m", false);
            yield return null;

            Assert.AreSame(rootGo, screen.Get<PromptVStack>("root").GameObject);

            UI.Close("Stable");
        }
    }
}
```

- [ ] **Step 2: 跑测试，全 PASS**

`mcp__UnityMCP__refresh_unity(...)` + `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], include_failed_tests=true)`

预期：2 个 E2E 测试 PASS；M1/M2 既有 + Task 7-13 新增 PlayMode 测试全 PASS（应当全绿）。

- [ ] **Step 3: 跑 EditMode 全套确认无回归**

预期：之前所有 EditMode 测试 + Task 2 (7) + Task 3 (8) + Task 4 (3) + Task 5 (5) + Task 6 (8) ≈ 既有 + 31 PASS。

- [ ] **Step 4: Commit**

```bash
git add Tests/PlayMode/E2E/VariantSwitchTests.cs
git commit -m "test(e2e): pc/mobile-portrait variant switch with explicit per-axis overrides + Add joystick"
```

---

## M3 完成验收清单

跑完 14 个 task 后，确认：

- [ ] 内联 `attr.var` 后缀按 last-active-wins 解算（spec §8.3）
- [ ] `<Variant when><Add into at>` 块在变体首次激活时实例化、之后 toggle 仅 `SetActive` 切换（Strategy C：跨 toggle 周期保持代码侧引用与 R3 订阅稳定，仅 Close 时随 RootGameObject 销毁）；`into` 支持 `@root` / `#id` / `#id/path/to/inner`（M3 扩展，详见顶部 spec drift 第 5 条）
- [ ] `UI.Variants.Set(name, bool)` 切换不重建 Screen 的 GameObject，被覆盖的属性即时刷新
- [ ] 不可覆盖字段在 parser 阶段报错：`id.var` / `<Param>` 的 `default.var`（spec §8.5）
- [ ] Layout group 子节点的 `anchor.var` / `margin.var` 输出 warning（spec §13 R3）
- [ ] EditMode 测试套件全 PASS
- [ ] PlayMode 测试套件全 PASS（含 M1/M2 全部回归）
- [ ] M2 的 TitledPanel + MainMenu 示例**不需要任何改动**（向后兼容）
- [ ] Multi-screen 同时打开时，`UI.Variants.Set` 让所有已开 Screen 都重新解算

接下来由 M4 处理 `<Import>` / 跨文件命名空间 / 编辑器内热重载 / XSD 自动生成（spec §12）。

---

## 已知约束与未来工作

- **Add 块的生命周期（Strategy C）**：变体首次激活时实例化其 `<Add>` 子树并永久挂在 `_byId` / `_nodeMap`；之后 toggle 仅切根 GameObject 的 `SetActive`，从不 `Destroy`。代码侧持有的引用（含 R3 订阅）跨 toggle 周期保持稳定。两个有意为之的 API 边角：(a) 从未激活过的变体在当前模式下 `screen.Get('joystick')` 抛 `KeyNotFoundException`——首次激活才会出现；(b) 首次激活后即使 toggle 回 inactive，`Get` 仍返回同一实例（`GameObject.activeSelf == false`），用户应按 `activeSelf` 判可见性而非 `Get` 是否抛错。Close 时 RootGameObject 整体销毁，所有 hidden 子树随之清理，无内存泄漏。如确实需要 lazy-destroy 语义（例如想释放变体专属资源），可在 ControlAttributeApplier 同侧加一个 Strategy A 兜底分支，本 plan 不内置。
- **性能**（spec §13 R2）：当前 ReSolve 是"全 NodeMap reapply"——把每个 Control 的所有属性都重新跑一遍。M3 不内置差分。如出现 profiling 热点，可在 ControlAttributeApplier 里增加"仅 reapply 名字出现在 VariantOverrides 里的 attr"分支；接口签名不需变。
- **Add 块嵌套 / 模板内的 `<Variant>` 块**：spec §8 没明确允许 Template body 内出现 `<Variant>` 块；M3 仅支持 Screen 顶层。模板内属性后缀 `.var` 是允许的（已通过 Task 4 透传）。
- **整数索引超界**：`<Add at='99'>` 当 prevCount=3 时 clamp 到 prevCount——意味着 at='99' 与 at='end' 等效。spec §8.4 没规定 OOB 行为；M3 选择宽松 clamp 而非报错。
- **Spec drift**：spec §11 速查的 root tag 与 §3 不一致（`<UI>` vs `<PromptUGUI>`）；spec §5 内置原语表未列 `<Btn>`；spec §3 / §8.2 的 `size.mobile-portrait="_,400"` 把 margin 的占位语法借给了 size，与 §6.2 的 `WxH` 值语法冲突——M3 决定 `attr.var` 一律整体替换，部分覆盖请用 `width.var` / `height.var` 或显式平台变体（详见顶部 spec drift 第 4 条）；spec §8.4 的 `into` 仅列 `#id|@root`，M3 扩展支持 `#id/path/to/inner` 路径（与 `Screen.Get("a/b")` 同义，详见顶部 spec drift 第 5 条）。本 plan 例子统一使用代码现状。spec 同步任务建议在 M3 完成后单独提一个 PR。

---

_Plan 结束。下一步：由 superpowers:subagent-driven-development 或 superpowers:executing-plans 接手分 task 执行。_
