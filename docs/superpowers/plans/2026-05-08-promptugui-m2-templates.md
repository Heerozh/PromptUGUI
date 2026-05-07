# PromptUGUI M2 模板 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 spec §7 描述的 `<Template>` / `<Param>` / `<Slot>` / `{{}}` / `if=` 全部能力，并交付 spec §13 R5 提到的 ID 路径访问，跑通"用 TitledPanel 包背包 Grid"完整闭环。

**Architecture:** 在 M1 的 `Parser → Instantiator` 之间插入新的 **TemplateExpander** 阶段，把所有模板调用展开为纯原语树，下游不感知模板存在。Template 的 `id` 作用域通过在 ElementNode 上打 `IsTemplateInstanceRoot` 标记 + Control 持有 `ScopedIds` 字典实现，`Screen.Get("a/b")` 用 `/` 分隔逐层下钻。

**Tech Stack:** Unity 6 (6000.0+), TextMeshPro, R3 (Cysharp), NUnit (Unity Test Framework)。承接 M1 已实现的 IR / Parser / Layout / Registry / Controls / Application 各层。

---

## 假设与前置

工程师执行此计划前需要：

1. M1 计划（`docs/superpowers/plans/2026-05-07-promptugui-m1-core.md`）已全部完成，53 EditMode + 23 PlayMode 测试 PASS
2. 宿主 Unity 项目位于 `C:\xsoft\PromptUGUIDev`（NuGetForUnity 装有 R3 1.3.0；`com.promptugui.core` 通过 file:// 引用本仓库）
3. UnityMCP 已连接（操作 Unity 用 MCP 工具，不要 batch CLI）
4. 工作目录始终为 PromptUGUI 仓库根 `C:\xsoft\PromptUGUI`

测试运行：用 UnityMCP 的 `mcp__UnityMCP__run_tests(mode="EditMode"|"PlayMode", assembly_names=["PromptUGUI.Tests.EditMode"|"PromptUGUI.Tests.PlayMode"])`，不要 spawn batch-mode Unity。文件改动后调 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` 触发编译并等待 ready。检查编译错误用 `mcp__UnityMCP__read_console(action="get", types=["error"])`。

---

## 文件结构

```
PromptUGUI/                                    # 仓库根（已存在）
├── Runtime/
│   ├── Core/
│   │   ├── IR/
│   │   │   ├── UIDocument.cs                  # Modify: 加 Templates 字典
│   │   │   ├── ElementNode.cs                 # Modify: 加 IsTemplateInstanceRoot 标记
│   │   │   ├── TemplateDef.cs                 # Task 1
│   │   │   └── ParamDef.cs                    # Task 1
│   │   ├── Parser/
│   │   │   └── UIDocumentParser.cs            # Modify: 识别 <Template>/<Param>/<Slot>/if
│   │   └── Template/
│   │       ├── Truthy.cs                      # Task 5
│   │       ├── Substitution.cs                # Task 7
│   │       └── TemplateExpander.cs            # Task 6/8/9/10/11/12
│   ├── Controls/
│   │   ├── IControl.cs                        # Modify: 加 ScopedIds + GetChildById
│   │   └── Control.cs                         # Modify: 实现 ScopedIds
│   └── Application/
│       ├── ScreenInstantiator.cs              # Modify: 处理 IsTemplateInstanceRoot
│       ├── Screen.cs                          # Modify: Get(path) 走 / 分段
│       └── UI.cs                              # Modify: LoadDocument 跑 expander
├── Tests/
│   ├── EditMode/
│   │   ├── Parser/
│   │   │   └── UIDocumentParserTests.cs       # Modify: 加 Template/Param/Slot/if 解析测试
│   │   └── Template/
│   │       ├── TruthyTests.cs                 # Task 5
│   │       ├── SubstitutionTests.cs           # Task 7
│   │       └── TemplateExpanderTests.cs       # Tasks 6/8/9/10/11/12
│   └── PlayMode/
│       ├── Lifecycle/
│       │   └── ScreenLifecycleTests.cs        # Modify: 加 path Get + 模板 instantiation 测试
│       └── E2E/
│           └── TitledPanelInventoryTests.cs   # Task 18
```

---

## Task 1：TemplateDef + ParamDef + UIDocument 扩展

**Files:**
- Create: `Runtime/Core/IR/ParamDef.cs`
- Create: `Runtime/Core/IR/TemplateDef.cs`
- Modify: `Runtime/Core/IR/UIDocument.cs`

无独立测试——这些类是数据载体，由 Task 2 起的 parser 测试覆盖。

- [ ] **Step 1: 创建 ParamDef**

`Runtime/Core/IR/ParamDef.cs`：

```csharp
namespace PromptUGUI.IR {
    public sealed class ParamDef {
        public string Name { get; }
        public string DefaultValue { get; }       // null = 必填
        public bool   HasDefault   => DefaultValue != null;

        public ParamDef(string name, string defaultValue) {
            Name = name;
            DefaultValue = defaultValue;
        }
    }
}
```

- [ ] **Step 2: 创建 TemplateDef**

`Runtime/Core/IR/TemplateDef.cs`：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class TemplateDef {
        public string Name { get; }
        public List<ParamDef> Params { get; } = new();
        public ElementNode Body { get; set; }    // 必须有且仅有一个根元素

        public TemplateDef(string name) { Name = name; }
    }
}
```

- [ ] **Step 3: 扩展 UIDocument 加 Templates 字典**

修改 `Runtime/Core/IR/UIDocument.cs`：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class UIDocument {
        public int Version { get; set; } = 1;
        public List<ScreenDef> Screens { get; } = new();
        public Dictionary<string, TemplateDef> Templates { get; } = new();
    }
}
```

- [ ] **Step 4: 触发 Unity 编译并确认无错**

调用：
- `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- `mcp__UnityMCP__read_console(action="get", types=["error"], count=10)`

预期：除 `Samples~/MainMenu does not exist` 这条已知 warning 之外，无 PromptUGUI 相关 error。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/IR
git commit -m "feat(ir): TemplateDef + ParamDef; UIDocument.Templates dict"
```

---

## Task 2：Parser — 识别 <Template> 与 <Param>

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

- [ ] **Step 1: 写测试**

追加到 `Tests/EditMode/Parser/UIDocumentParserTests.cs`（在最后一个 `}` 之前）：

```csharp
        [Test]
        public void Parses_template_with_typed_params() {
            const string xml = @"<UI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <VStack padding='16'>
                        <Text>{{title}}</Text>
                    </VStack>
                </Template>
            </UI>";

            var doc = UIDocumentParser.Parse(xml);

            Assert.AreEqual(1, doc.Templates.Count);
            var tpl = doc.Templates[""TitledPanel""];
            Assert.AreEqual(""TitledPanel"", tpl.Name);
            Assert.AreEqual(2, tpl.Params.Count);

            Assert.AreEqual(""title"",    tpl.Params[0].Name);
            Assert.IsFalse(tpl.Params[0].HasDefault);

            Assert.AreEqual(""closable"", tpl.Params[1].Name);
            Assert.IsTrue(tpl.Params[1].HasDefault);
            Assert.AreEqual(""true"", tpl.Params[1].DefaultValue);

            Assert.IsNotNull(tpl.Body);
            Assert.AreEqual(""VStack"", tpl.Body.Tag);
        }

        [Test]
        public void Throws_on_template_without_name() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse(""<UI version='1'><Template><VStack/></Template></UI>""));
        }

        [Test]
        public void Throws_on_template_with_zero_root_elements() {
            const string xml = @""<UI version='1'>
                <Template name='Empty'>
                    <Param name='x'/>
                </Template>
            </UI>"";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_template_with_multiple_root_elements() {
            const string xml = @""<UI version='1'>
                <Template name='Two'>
                    <VStack/>
                    <HStack/>
                </Template>
            </UI>"";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_template_name() {
            const string xml = @""<UI version='1'>
                <Template name='Same'><Frame/></Template>
                <Template name='Same'><Frame/></Template>
            </UI>"";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_param_after_first_body_element() {
            const string xml = @""<UI version='1'>
                <Template name='Bad'>
                    <Frame/>
                    <Param name='late'/>
                </Template>
            </UI>"";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
```

注意：上面 `@""..."""` 是 C# 中的 verbatim-string 转义。在你写到文件里时把外层 `"""` 改回普通 `""`，把行内的 `""` 也改回 `"`。本步骤的实际写入示例（已正确转义）：

```csharp
        [Test]
        public void Parses_template_with_typed_params() {
            const string xml = @"<UI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <VStack padding='16'>
                        <Text>{{title}}</Text>
                    </VStack>
                </Template>
            </UI>";

            var doc = UIDocumentParser.Parse(xml);

            Assert.AreEqual(1, doc.Templates.Count);
            var tpl = doc.Templates["TitledPanel"];
            Assert.AreEqual("TitledPanel", tpl.Name);
            Assert.AreEqual(2, tpl.Params.Count);

            Assert.AreEqual("title",    tpl.Params[0].Name);
            Assert.IsFalse(tpl.Params[0].HasDefault);

            Assert.AreEqual("closable", tpl.Params[1].Name);
            Assert.IsTrue(tpl.Params[1].HasDefault);
            Assert.AreEqual("true", tpl.Params[1].DefaultValue);

            Assert.IsNotNull(tpl.Body);
            Assert.AreEqual("VStack", tpl.Body.Tag);
        }
```

（其余 5 个测试同样把外层 `@"...". ` 与内嵌 `"..."` 写成普通 C# 字符串。）

- [ ] **Step 2: 跑测试，确认 FAIL（Parser 还不识别 Template）**

`mcp__UnityMCP__refresh_unity(...)` + `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], include_failed_tests=true)`

预期：6 个新测试 FAIL（Templates 字典为空 / 期待异常未抛）。

- [ ] **Step 3: 在 UIDocumentParser 处理 Template/Param**

修改 `Runtime/Core/Parser/UIDocumentParser.cs`，在原 `Parse` 方法 `foreach (XmlNode child in root.ChildNodes)` 循环中，增加 `Template` 分支；同时增加一个 `ParseTemplate` 静态方法。

把整个文件替换为：

```csharp
using System.Xml;
using PromptUGUI.IR;

namespace PromptUGUI.Parser {
    public static class UIDocumentParser {
        public static UIDocument Parse(string xml) {
            var xdoc = new XmlDocument();
            xdoc.LoadXml(xml);

            var root = xdoc.DocumentElement;
            if (root == null || root.Name != "UI")
                throw new ParseException("Root element must be <UI>");

            var versionAttr = root.GetAttribute("version");
            if (string.IsNullOrEmpty(versionAttr))
                throw new ParseException("<UI> requires version attribute");

            var doc = new UIDocument { Version = int.Parse(versionAttr) };
            var screenNames = new System.Collections.Generic.HashSet<string>();

            foreach (XmlNode child in root.ChildNodes) {
                if (child is not XmlElement el) continue;
                switch (el.Name) {
                    case "Screen":
                        ParseScreen(el, doc, screenNames);
                        break;
                    case "Template":
                        ParseTemplate(el, doc);
                        break;
                    default:
                        // M2 仅 Screen/Template；Import 留 M4
                        throw new ParseException(
                            $"unexpected top-level element <{el.Name}>");
                }
            }

            return doc;
        }

        static void ParseScreen(XmlElement el, UIDocument doc,
                                System.Collections.Generic.HashSet<string> screenNames) {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Screen> requires name attribute");
            if (!screenNames.Add(name))
                throw new ParseException($"Duplicate <Screen name='{name}'>");

            var idsInScreen = new System.Collections.Generic.HashSet<string>();
            var rootNode = new ElementNode("__screen_root__");
            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    rootNode.Children.Add(ParseElement(child_el, idsInScreen));
            doc.Screens.Add(new ScreenDef(name, rootNode));
        }

        static void ParseTemplate(XmlElement el, UIDocument doc) {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Template> requires name attribute");
            if (doc.Templates.ContainsKey(name))
                throw new ParseException($"Duplicate <Template name='{name}'>");

            var tpl = new TemplateDef(name);
            bool sawBody = false;
            ElementNode body = null;

            foreach (XmlNode c in el.ChildNodes) {
                if (c is not XmlElement ce) continue;
                if (ce.Name == "Param") {
                    if (sawBody)
                        throw new ParseException(
                            $"<Template name='{name}'>: <Param> must appear before any body element");
                    var pname = ce.GetAttribute("name");
                    if (string.IsNullOrEmpty(pname))
                        throw new ParseException(
                            $"<Template name='{name}'>: <Param> requires name attribute");
                    string def = ce.HasAttribute("default") ? ce.GetAttribute("default") : null;
                    tpl.Params.Add(new ParamDef(pname, def));
                } else {
                    if (sawBody)
                        throw new ParseException(
                            $"<Template name='{name}'> must have exactly one root element");
                    sawBody = true;
                    var tplIds = new System.Collections.Generic.HashSet<string>();
                    body = ParseElement(ce, tplIds);
                }
            }
            if (!sawBody)
                throw new ParseException(
                    $"<Template name='{name}'> must have one root element after <Param>s");

            tpl.Body = body;
            doc.Templates[name] = tpl;
        }

        static ElementNode ParseElement(XmlElement el,
                                        System.Collections.Generic.HashSet<string> idsInScope) {
            var node = new ElementNode(el.Name);

            foreach (XmlAttribute attr in el.Attributes) {
                if (attr.Name == "id") {
                    if (!idsInScope.Add(attr.Value))
                        throw new ParseException(
                            $"Duplicate id='{attr.Value}' within scope");
                    node.Id = attr.Value;
                } else {
                    node.Attributes[attr.Name] = attr.Value;
                }
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
    }
}
```

注意：M1 的 ParseElement 完整保留；新增的 ParseScreen / ParseTemplate 共用它。M1 的 "duplicate screen name" 与 "id 重复" 行为不变。Template 的 id 作用域用一个**独立** HashSet，所以 Screen 的 id 与 Template 的 id 不冲突。

- [ ] **Step 4: 跑全部 EditMode 测试，全 PASS**

`mcp__UnityMCP__run_tests(mode="EditMode", ...)`。预期：全部之前 53 个 + 6 个新增 = 59 PASS。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser Tests/EditMode/Parser
git commit -m "feat(parser): recognize <Template> and <Param> at top level"
```

---

## Task 3：Parser — 识别 <Slot/>

**Files:**
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

Slot 在 parser 阶段不需要特殊处理——`<Slot/>` 会以 `Tag="Slot"` 的普通 ElementNode 形式出现在 Template body 内。Expander 在 Task 10 才依赖此 tag 名。

唯一要保证的是：Slot 在非 Template 内出现时**不**报错（parser 不知道上下文，让 expander 处理）；Slot 出现 ≥2 次也由 expander 检查。

- [ ] **Step 1: 写一个回归测试，确认 Slot 解析为普通节点**

追加到 `UIDocumentParserTests.cs`：

```csharp
        [Test]
        public void Parses_Slot_as_ordinary_element_node() {
            const string xml = @"<UI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Slot/>
                    </Frame>
                </Template>
            </UI>";

            var doc = UIDocumentParser.Parse(xml);
            var body = doc.Templates["Box"].Body;
            Assert.AreEqual("Frame", body.Tag);
            Assert.AreEqual(1, body.Children.Count);
            Assert.AreEqual("Slot", body.Children[0].Tag);
        }
```

- [ ] **Step 2: 跑测试，PASS**（无需改 parser）

- [ ] **Step 3: Commit**

```bash
git add Tests/EditMode/Parser
git commit -m "test(parser): confirm <Slot/> parses as ordinary node"
```

---

## Task 4：ElementNode 加 IsTemplateInstanceRoot 标记

**Files:**
- Modify: `Runtime/Core/IR/ElementNode.cs`

无测试——纯字段添加，由 Task 11/16 的功能测试覆盖。

- [ ] **Step 1: 加字段**

修改 `Runtime/Core/IR/ElementNode.cs`：

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

        public ElementNode(string tag) {
            Tag = tag;
            Attributes = new Dictionary<string, string>();
            Children = new List<ElementNode>();
        }
    }
}
```

- [ ] **Step 2: 触发编译，确认无错**

`mcp__UnityMCP__refresh_unity(...)` + `mcp__UnityMCP__read_console(...)`。

- [ ] **Step 3: Commit**

```bash
git add Runtime/Core/IR/ElementNode.cs
git commit -m "feat(ir): ElementNode.IsTemplateInstanceRoot marker for scoped ids"
```

---

## Task 5：Truthy 评估器

**Files:**
- Create: `Runtime/Core/Template/Truthy.cs`
- Create: `Tests/EditMode/Template/TruthyTests.cs`

`if="{{p}}"` 的真值判定：参数串经过 `{{}}` 替换后，按下列规则判 truthy/falsy。Spec §7.3 明确规则：`非空串、非 false、非 0、非 null` 为 truthy。

- [ ] **Step 1: 写测试**

`Tests/EditMode/Template/TruthyTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class TruthyTests {
        [TestCase("true",  true)]
        [TestCase("True",  true)]   // 大小写不敏感
        [TestCase("TRUE",  true)]
        [TestCase("yes",   true)]
        [TestCase("foo",   true)]
        [TestCase("1",     true)]
        [TestCase("42",    true)]
        [TestCase("-1",    true)]
        [TestCase("0.5",   true)]
        public void Truthy_values(string s, bool expected) {
            Assert.AreEqual(expected, Truthy.Eval(s));
        }

        [TestCase("",      false)]
        [TestCase(null,    false)]
        [TestCase("false", false)]
        [TestCase("False", false)]
        [TestCase("FALSE", false)]
        [TestCase("0",     false)]
        [TestCase("0.0",   false)]
        [TestCase("null",  false)]
        [TestCase("NULL",  false)]
        public void Falsy_values(string s, bool expected) {
            Assert.AreEqual(expected, Truthy.Eval(s));
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（Truthy 类不存在）**

- [ ] **Step 3: 实现 Truthy**

`Runtime/Core/Template/Truthy.cs`：

```csharp
using System.Globalization;

namespace PromptUGUI.Template {
    public static class Truthy {
        public static bool Eval(string s) {
            if (string.IsNullOrEmpty(s)) return false;

            // case-insensitive 关键字
            var lower = s.ToLowerInvariant();
            if (lower == "false" || lower == "null") return false;

            // 数字 0 / 0.0 等
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d != 0.0;

            // 其他非空串
            return true;
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Template/Truthy.cs Tests/EditMode/Template/TruthyTests.cs
git commit -m "feat(template): Truthy evaluator (non-empty / non-false / non-0 / non-null)"
```

---

## Task 6：TemplateExpander 框架（pass-through）

**Files:**
- Create: `Runtime/Core/Template/TemplateExpander.cs`
- Create: `Tests/EditMode/Template/TemplateExpanderTests.cs`

先建一个空壳 `TemplateExpander.Expand(UIDocument)` 返回深拷贝（不解析模板调用）。后续 Task 7-12 逐步加功能。

- [ ] **Step 1: 写测试**

`Tests/EditMode/Template/TemplateExpanderTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class TemplateExpanderTests {
        [Test]
        public void Pass_through_screen_with_no_template_invocation() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Screen name='X'>
                    <VStack id='v'>
                        <Image id='a'/>
                    </VStack>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);

            Assert.AreEqual(1, expanded.Screens.Count);
            var screen = expanded.Screens[0];
            Assert.AreEqual("X", screen.Name);
            Assert.AreEqual(1, screen.Root.Children.Count);
            var v = screen.Root.Children[0];
            Assert.AreEqual("VStack", v.Tag);
            Assert.AreEqual("v", v.Id);
            Assert.AreEqual(1, v.Children.Count);
            Assert.AreEqual("a", v.Children[0].Id);
        }

        [Test]
        public void Templates_dictionary_carries_through() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'><Frame/></Template>
                <Screen name='X'/>
            </UI>");

            var expanded = TemplateExpander.Expand(doc);
            // Templates 仍然在 expanded 文档里也行（M2 后续 task 会决定要不要清空）
            // 这里只验证 Screen 数与原始一致
            Assert.AreEqual(1, expanded.Screens.Count);
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（TemplateExpander 不存在）**

- [ ] **Step 3: 实现 TemplateExpander 空壳**

`Runtime/Core/Template/TemplateExpander.cs`：

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template {
    public static class TemplateExpander {
        public static UIDocument Expand(UIDocument doc) {
            var result = new UIDocument { Version = doc.Version };
            // Templates 不参与下游 instantiation，但保留以便诊断
            foreach (var kv in doc.Templates)
                result.Templates[kv.Key] = kv.Value;

            foreach (var s in doc.Screens) {
                var newRoot = CloneNode(s.Root);
                result.Screens.Add(new ScreenDef(s.Name, newRoot));
            }
            return result;
        }

        static ElementNode CloneNode(ElementNode src) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children)
                dst.Children.Add(CloneNode(c));
            return dst;
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Template/TemplateExpander.cs Tests/EditMode/Template/TemplateExpanderTests.cs
git commit -m "feat(template): TemplateExpander skeleton (deep clone, no expansion yet)"
```

---

## Task 7：{{}} 替换工具

**Files:**
- Create: `Runtime/Core/Template/Substitution.cs`
- Create: `Tests/EditMode/Template/SubstitutionTests.cs`

实现 `{{paramName}}` 在字符串中的替换。规则：
- 匹配 `\{\{ \s* identifier \s* \}\}`
- identifier 是 `[A-Za-z_][A-Za-z0-9_]*`
- 未在 params 字典中的引用 → throw（避免静默失败）
- 同一字符串内多个 `{{}}` 全部替换
- 字面 `{` `}` 单独出现不视为占位符

- [ ] **Step 1: 写测试**

`Tests/EditMode/Template/SubstitutionTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class SubstitutionTests {
        Dictionary<string, string> P(params (string k, string v)[] kv) {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        [Test]
        public void Replaces_single_placeholder() {
            Assert.AreEqual("背包",
                Substitution.Apply("{{title}}", P(("title", "背包"))));
        }

        [Test]
        public void Replaces_with_surrounding_text() {
            Assert.AreEqual("icons/sword.png",
                Substitution.Apply("icons/{{icon}}.png", P(("icon", "sword"))));
        }

        [Test]
        public void Replaces_multiple_placeholders() {
            Assert.AreEqual("a-b-c",
                Substitution.Apply("{{x}}-{{y}}-{{z}}",
                    P(("x", "a"), ("y", "b"), ("z", "c"))));
        }

        [Test]
        public void Whitespace_inside_braces_allowed() {
            Assert.AreEqual("foo",
                Substitution.Apply("{{  name  }}", P(("name", "foo"))));
        }

        [Test]
        public void Throws_on_unknown_param() {
            Assert.Throws<TemplateException>(() =>
                Substitution.Apply("{{missing}}", P(("other", "x"))));
        }

        [Test]
        public void Returns_input_when_no_placeholders() {
            Assert.AreEqual("plain text",
                Substitution.Apply("plain text", P(("title", "x"))));
        }

        [Test]
        public void Null_input_returns_null() {
            Assert.IsNull(Substitution.Apply(null, P(("x", "y"))));
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（Substitution + TemplateException 不存在）**

- [ ] **Step 3: 实现 TemplateException**

`Runtime/Core/Template/TemplateException.cs`：

```csharp
using System;

namespace PromptUGUI.Template {
    public sealed class TemplateException : Exception {
        public TemplateException(string message) : base(message) { }
    }
}
```

- [ ] **Step 4: 实现 Substitution**

`Runtime/Core/Template/Substitution.cs`：

```csharp
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PromptUGUI.Template {
    public static class Substitution {
        static readonly Regex Placeholder =
            new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

        public static string Apply(string raw, IReadOnlyDictionary<string, string> args) {
            if (raw == null) return null;
            return Placeholder.Replace(raw, m => {
                var name = m.Groups[1].Value;
                if (!args.TryGetValue(name, out var val))
                    throw new TemplateException(
                        $"unknown template parameter '{{{{{name}}}}}'");
                return val ?? "";
            });
        }
    }
}
```

- [ ] **Step 5: 跑测试，PASS**

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Template Tests/EditMode/Template/SubstitutionTests.cs
git commit -m "feat(template): {{}} substitution + TemplateException"
```

---

## Task 8：Expander — 文本节点 / 属性内的 {{}} 替换

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`
- Modify: `Tests/EditMode/Template/TemplateExpanderTests.cs`

让 Expander 在展开 Template body 时，对每个 attribute value 与 TextContent 调用 `Substitution.Apply`。但首先得真的"展开"——Task 6 的 expander 只是 pass-through。本 task 只引入"展开 + 替换"的最小路径，**先不**做参数收集（用空 args 演示替换工具被串起来）；下一 task 才做完整的 invocation 展开。

更现实的安排：把"识别 invocation + 收集 params + 替换"一次写完。本 task 与 Task 9-11 高度耦合；为遵守"小步 commit"，本 task 仅做 helper 补充：增加私有 `ExpandNode(ElementNode src, IReadOnlyDictionary<string,string> args, IReadOnlyList<ElementNode> slot)` 函数（递归 clone + 替换 attr/text，不识别 invocation 也不识别 if/Slot），由后续 task 串起。

- [ ] **Step 1: 写测试（直接调 ExpandNode helper 不可行——它是 internal——所以测 invocation 实际行为）**

跳过 internal helper 测试；本 task 的行为由 Task 11 的 invocation 测试间接覆盖。

直接进入下一步。

- [ ] **Step 2: 在 TemplateExpander 加 internal helper（不暴露）**

修改 `Runtime/Core/Template/TemplateExpander.cs`，加私有方法：

```csharp
        // 展开模板 body 的一个节点：
        //   - 替换 attr / text 中的 {{}}
        //   - **本步骤不**处理 if / Slot / invocation；后续 task 接力
        static ElementNode ExpandNode(ElementNode src,
                                      System.Collections.Generic.IReadOnlyDictionary<string, string> args) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = Substitution.Apply(src.TextContent, args),
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            foreach (var c in src.Children)
                dst.Children.Add(ExpandNode(c, args));
            return dst;
        }
```

注意要 `using PromptUGUI.Template;`（同命名空间已存在，import 已有）。

- [ ] **Step 3: 触发编译，确认无错**

- [ ] **Step 4: Commit**

```bash
git add Runtime/Core/Template/TemplateExpander.cs
git commit -m "feat(template): expander helper applies {{}} to attr + text"
```

---

## Task 9：Expander — `if` 条件元素

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`

`if="..."` 出现在元素的属性上，经 `{{}}` 替换后用 `Truthy.Eval` 判断。Falsy 时整个元素（含子树）被丢弃。

注意：`if` 也可以出现在**非**模板内的节点上吗？Spec §7.3 明确"替换规则**仅 Template 内有效**"。所以 `if` 只对从 Template body 展开出来的节点生效；Screen 直接写的 `if` 应当被忽略（视作普通自定义控件属性，由代码侧处理）。

为保持 expander 简洁：`if` 仅由 `ExpandNode` 处理（即 template body 内）。Screen 直接写的 `if` 会原样保留，instantiator 会把它当成 ControlMeta 的未知属性而忽略（因为内置控件没有 `if` UIAttr）。Spec 没禁止此行为，但保险起见可在 expander 加一句"若节点的 if 在替换后存在，仍是模板内才考虑"——本 task 实现仅模板内生效。

- [ ] **Step 1: 改 ExpandNode 处理 if**

```csharp
        // 返回 null 表示该节点被 if 排除
        static ElementNode ExpandNode(ElementNode src,
                                      System.Collections.Generic.IReadOnlyDictionary<string, string> args) {
            // if 检查（先于其他属性替换；本来就只看自身 attr）
            if (src.Attributes.TryGetValue("if", out var rawIf)) {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = Substitution.Apply(src.TextContent, args),
            };
            foreach (var kv in src.Attributes) {
                if (kv.Key == "if") continue;     // 不要把 if 写入产物
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            }
            foreach (var c in src.Children) {
                var ec = ExpandNode(c, args);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }
```

- [ ] **Step 2: 触发编译，确认无错**

- [ ] **Step 3: Commit**

```bash
git add Runtime/Core/Template/TemplateExpander.cs
git commit -m "feat(template): if=... drops node when falsy after substitution"
```

---

## Task 10：Expander — `<Slot/>` 替换为调用方子节点

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`

在 template body 展开时遇到 `<Slot/>` 节点（Tag="Slot"），用调用方传入的子节点列表替换。注意：调用方子节点是**外部**作用域，不参与本模板的 `{{}}` 替换；它们应当在被嵌入前已经在外部作用域被自身展开过。

约束（spec §7.1）：每个 Template body 内 `<Slot/>` 出现 **0 或 1 次**；多次出现报错（由 Task 12 的错误检查覆盖；本 task 先实现单 slot 行为）。

- [ ] **Step 1: 给 ExpandNode 增 slot 参数；改造 children 遍历**

```csharp
        static ElementNode ExpandNode(
            ElementNode src,
            System.Collections.Generic.IReadOnlyDictionary<string, string> args,
            System.Collections.Generic.IReadOnlyList<ElementNode> slotContent) {

            if (src.Attributes.TryGetValue("if", out var rawIf)) {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = Substitution.Apply(src.TextContent, args),
            };
            foreach (var kv in src.Attributes) {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            }

            foreach (var c in src.Children) {
                if (c.Tag == "Slot") {
                    // 已在外部展开好的 slot 内容，整体放进来
                    if (slotContent != null)
                        foreach (var sc in slotContent)
                            dst.Children.Add(CloneNode(sc));
                    continue;
                }
                var ec = ExpandNode(c, args, slotContent);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }
```

注意：之前 Task 8/9 的两参数 `ExpandNode(src, args)` 签名被本 task 替换为三参数。其他地方暂未调用 ExpandNode（外部入口在 Task 11 才接进来），所以不会破坏编译。

- [ ] **Step 2: 触发编译，确认无错**

- [ ] **Step 3: Commit**

```bash
git add Runtime/Core/Template/TemplateExpander.cs
git commit -m "feat(template): <Slot/> replaced by caller-provided child nodes"
```

---

## Task 11：Expander — 解析模板调用并展开

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`
- Modify: `Tests/EditMode/Template/TemplateExpanderTests.cs`

Expander 入口 `Expand(doc)` 已经在 Task 6 把 Screen 节点 deep-clone。现在改造为：
- Screen 走 `ExpandTree(node, doc)` 而非 CloneNode
- ExpandTree 检测 `node.Tag` 是否在 `doc.Templates` 中
  - 是 → 解析为模板调用：build params dict，递归展开 invocation 的 children（在外部作用域），调 `ExpandNode(template.Body, params, expandedChildren)` 得到模板根，标记 `IsTemplateInstanceRoot=true`，把 invocation 的 `id` 转移给模板根
  - 否 → 普通节点 deep-clone（仍可包含模板调用作为子节点）
- 模板内嵌套调用（template body 中包含其他 template tag）也必须展开：每次 ExpandNode 内部当遇到子节点 tag 是模板时，转交给 ExpandTree

- [ ] **Step 1: 写测试**

追加到 `TemplateExpanderTests.cs`：

```csharp
        [Test]
        public void Expands_template_invocation_with_params() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Greet'>
                    <Param name='who'/>
                    <Text>Hello {{who}}</Text>
                </Template>
                <Screen name='S'>
                    <Greet who='World'/>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            var screen = expanded.Screens[0];
            Assert.AreEqual(1, screen.Root.Children.Count);
            var text = screen.Root.Children[0];
            Assert.AreEqual("Text", text.Tag);
            Assert.AreEqual("Hello World", text.TextContent);
        }

        [Test]
        public void Param_default_used_when_invocation_omits_attr() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Param name='label' default='默认'/>
                    <Text>{{label}}</Text>
                </Template>
                <Screen name='S'>
                    <Box/>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            Assert.AreEqual("默认", expanded.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Required_param_missing_throws() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Param name='must'/>
                    <Text>{{must}}</Text>
                </Template>
                <Screen name='S'><Box/></Screen></UI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Unknown_param_passed_throws() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Param name='a'/>
                    <Text>{{a}}</Text>
                </Template>
                <Screen name='S'>
                    <Box a='1' b='2'/>
                </Screen></UI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Slot_receives_invocation_children() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Slot/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box>
                        <Image id='inside'/>
                    </Box>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            var box = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("Frame", box.Tag);
            Assert.AreEqual(1, box.Children.Count);
            Assert.AreEqual("inside", box.Children[0].Id);
        }

        [Test]
        public void If_drops_element_when_falsy() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Param name='show' default='false'/>
                    <Frame>
                        <Image if='{{show}}' id='maybe'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box/>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            var frame = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual(0, frame.Children.Count);
        }

        [Test]
        public void Invocation_id_transfers_to_instance_root() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Image id='inside'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box id='outer'/>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            var box = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("Frame", box.Tag);
            Assert.AreEqual("outer", box.Id);
            Assert.IsTrue(box.IsTemplateInstanceRoot);
            Assert.AreEqual("inside", box.Children[0].Id);
        }

        [Test]
        public void Invocation_attributes_other_than_params_passthrough_to_root() {
            // anchor/size 是通用属性，不是 Param；它们应当被透传到模板根上
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <Frame/>
                </Template>
                <Screen name='S'>
                    <Box anchor='center' size='100x100'/>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            var box = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("center", box.Attributes["anchor"]);
            Assert.AreEqual("100x100", box.Attributes["size"]);
        }

        [Test]
        public void Nested_template_invocation_expands() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Inner'>
                    <Param name='msg'/>
                    <Text>{{msg}}</Text>
                </Template>
                <Template name='Outer'>
                    <Frame>
                        <Inner msg='from outer'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Outer/>
                </Screen></UI>");

            var expanded = TemplateExpander.Expand(doc);
            var frame = expanded.Screens[0].Root.Children[0];
            var text = frame.Children[0];
            Assert.AreEqual("Text", text.Tag);
            Assert.AreEqual("from outer", text.TextContent);
        }
```

- [ ] **Step 2: 跑测试，全 FAIL（expander 还未实现 invocation）**

- [ ] **Step 3: 在 TemplateExpander 实现 invocation 解析**

把 TemplateExpander.cs 的 `Expand`、`CloneNode` 删掉，整体替换为：

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template {
    public static class TemplateExpander {
        // 通用属性集合：模板调用上写的这些不算 Param
        static readonly HashSet<string> CommonAttrs = new() {
            "anchor", "size", "width", "height", "margin", "pivot",
            "padding", "spacing",
            "hidden", "interactable",
        };

        public static UIDocument Expand(UIDocument doc) {
            var result = new UIDocument { Version = doc.Version };
            foreach (var kv in doc.Templates)
                result.Templates[kv.Key] = kv.Value;

            foreach (var s in doc.Screens) {
                var newRoot = new ElementNode(s.Root.Tag);
                foreach (var c in s.Root.Children) {
                    var ec = ExpandTree(c, doc.Templates);
                    if (ec != null) newRoot.Children.Add(ec);
                }
                result.Screens.Add(new ScreenDef(s.Name, newRoot));
            }
            return result;
        }

        // 处理任意上下文节点：可能是模板调用，也可能是普通节点。
        // 模板调用 → 解析 params + 展开 body
        // 普通节点 → deep-clone，但子节点内若有模板调用同样递归
        static ElementNode ExpandTree(ElementNode src,
                                      IReadOnlyDictionary<string, TemplateDef> templates) {
            if (templates.TryGetValue(src.Tag, out var tpl))
                return ExpandInvocation(src, tpl, templates);

            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children) {
                var ec = ExpandTree(c, templates);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        static ElementNode ExpandInvocation(
            ElementNode invocation,
            TemplateDef tpl,
            IReadOnlyDictionary<string, TemplateDef> templates) {

            // 1. 收集 params
            var args = new Dictionary<string, string>();
            foreach (var p in tpl.Params) {
                if (invocation.Attributes.TryGetValue(p.Name, out var v))
                    args[p.Name] = v;
                else if (p.HasDefault)
                    args[p.Name] = p.DefaultValue;
                else
                    throw new TemplateException(
                        $"<{tpl.Name}>: required <Param name='{p.Name}'> not provided");
            }

            // 2. 调用方多余 attr 检查（但允许通用属性透传）
            //    通用属性会在 Step 4 被透传到 instance root；非通用、非 Param 的 attr 都报错
            foreach (var kv in invocation.Attributes) {
                if (CommonAttrs.Contains(kv.Key)) continue;
                if (args.ContainsKey(kv.Key)) continue;
                throw new TemplateException(
                    $"<{tpl.Name}>: unknown attribute '{kv.Key}'");
            }

            // 3. 先在外部作用域展开 invocation 的子节点（slot 内容）
            var slotContent = new List<ElementNode>();
            foreach (var c in invocation.Children) {
                var ec = ExpandTree(c, templates);
                if (ec != null) slotContent.Add(ec);
            }

            // 4. 展开模板 body
            var instanceRoot = ExpandNode(tpl.Body, args, slotContent, templates);
            if (instanceRoot == null)
                throw new TemplateException(
                    $"<{tpl.Name}>: template body root was excluded by if; not allowed");

            // 5. 标记为 template instance root
            instanceRoot.IsTemplateInstanceRoot = true;

            // 6. 调用方 id 覆盖 body 根 id
            if (!string.IsNullOrEmpty(invocation.Id))
                instanceRoot.Id = invocation.Id;

            // 7. 通用属性透传到 instance root
            foreach (var kv in invocation.Attributes) {
                if (!CommonAttrs.Contains(kv.Key)) continue;
                instanceRoot.Attributes[kv.Key] = kv.Value;
            }

            return instanceRoot;
        }

        // 展开 template body 内的节点：
        //   - {{}} 替换 attr/text
        //   - if 决定保留/丢弃
        //   - <Slot/> 替换为 slotContent
        //   - 子节点若是其他模板调用，递归 ExpandTree
        static ElementNode ExpandNode(
            ElementNode src,
            IReadOnlyDictionary<string, string> args,
            IReadOnlyList<ElementNode> slotContent,
            IReadOnlyDictionary<string, TemplateDef> templates) {

            if (src.Attributes.TryGetValue("if", out var rawIf)) {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            // 如果 src 自身是另一个模板调用：先 attr-substitute，然后转给 ExpandTree
            // （但 ExpandTree 不会做 {{}} 替换；invocation 的 attr 值也可能含 {{}}）
            // 解决方案：先用 args 替换 src 的 attr，再走 ExpandTree
            ElementNode prepared = SubstituteAttrs(src, args);

            if (templates.ContainsKey(prepared.Tag))
                return ExpandTree(prepared, templates);

            // 普通元素
            var dst = new ElementNode(prepared.Tag) {
                Id = prepared.Id,
                TextContent = Substitution.Apply(prepared.TextContent, args),
            };
            foreach (var kv in prepared.Attributes) {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = kv.Value;   // 已经在 SubstituteAttrs 里替换过
            }
            foreach (var c in src.Children) {        // 注意：children 仍来自 src
                if (c.Tag == "Slot") {
                    if (slotContent != null)
                        foreach (var sc in slotContent)
                            dst.Children.Add(DeepClone(sc));
                    continue;
                }
                var ec = ExpandNode(c, args, slotContent, templates);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        // 浅拷贝 src 节点，但所有 attr value 经过 {{}} 替换。
        // 用于把 invocation 节点（在外层模板 body 内）做参数替换后再交给 ExpandTree。
        static ElementNode SubstituteAttrs(ElementNode src,
                                           IReadOnlyDictionary<string, string> args) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = Substitution.Apply(kv.Value, args);
            foreach (var c in src.Children)
                dst.Children.Add(c);   // children 不深拷贝；ExpandTree 会处理
            return dst;
        }

        static ElementNode DeepClone(ElementNode src) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes) dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children) dst.Children.Add(DeepClone(c));
            return dst;
        }
    }
}
```

注意：上面把 Task 6 的 CloneNode、Task 8/9/10 的 ExpandNode 全部整合替换，因为这一步要求一致的接口签名。前面 task 写的旧 helper 不再使用。

- [ ] **Step 4: 跑全部 EditMode 测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Template Tests/EditMode/Template
git commit -m "feat(template): expand template invocations with params/slot/if/nested"
```

---

## Task 12：Expander — 错误路径（cyclic / Slot 多次 / Slot 在 Screen）

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`
- Modify: `Tests/EditMode/Template/TemplateExpanderTests.cs`

补充错误检测：

1. **Cyclic 引用**：A 调 B 调 A 死循环 → 检测后报错。实现：在 ExpandInvocation 维护 visiting 集合（线程局部或参数链）。
2. **`<Slot/>` 出现在 Screen 直接 body**：Slot 仅在 Template body 内合法。Screen 里出现 Slot → 报错。
3. **`<Slot/>` 在同一 Template body 内出现 ≥2 次**：报错（spec §7.1 单 slot）。

- [ ] **Step 1: 写测试**

追加到 `TemplateExpanderTests.cs`：

```csharp
        [Test]
        public void Cyclic_template_reference_throws() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='A'><B/></Template>
                <Template name='B'><A/></Template>
                <Screen name='S'><A/></Screen></UI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Slot_in_Screen_body_throws() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Screen name='S'>
                    <Slot/>
                </Screen></UI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Two_slots_in_template_body_throws() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'>
                    <VStack>
                        <Slot/>
                        <Slot/>
                    </VStack>
                </Template>
                <Screen name='S'><Box/></Screen></UI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }
```

- [ ] **Step 2: 跑测试，FAIL（错误路径未实现）**

- [ ] **Step 3: 在 TemplateExpander 加 cycle / slot 检查**

修改 `Runtime/Core/Template/TemplateExpander.cs`：

把 `ExpandTree` 与 `ExpandInvocation` 改为带一个 `HashSet<string> visiting` 参数；`Expand(doc)` 入口传一个新的空 set。

```csharp
        public static UIDocument Expand(UIDocument doc) {
            // 模板内 Slot 计数检查（一次性、与 instance 数无关）
            foreach (var t in doc.Templates.Values)
                ValidateSlotCount(t);

            var result = new UIDocument { Version = doc.Version };
            foreach (var kv in doc.Templates)
                result.Templates[kv.Key] = kv.Value;

            foreach (var s in doc.Screens) {
                var newRoot = new ElementNode(s.Root.Tag);
                foreach (var c in s.Root.Children) {
                    EnsureNoSlot(c, $"Screen '{s.Name}'");
                    var ec = ExpandTree(c, doc.Templates, new HashSet<string>());
                    if (ec != null) newRoot.Children.Add(ec);
                }
                result.Screens.Add(new ScreenDef(s.Name, newRoot));
            }
            return result;
        }

        static void ValidateSlotCount(TemplateDef tpl) {
            int count = 0;
            CountSlots(tpl.Body, ref count);
            if (count > 1)
                throw new TemplateException(
                    $"<Template name='{tpl.Name}'>: at most one <Slot/> allowed (found {count})");
        }
        static void CountSlots(ElementNode n, ref int count) {
            if (n.Tag == "Slot") count++;
            foreach (var c in n.Children) CountSlots(c, ref count);
        }

        static void EnsureNoSlot(ElementNode n, string contextLabel) {
            if (n.Tag == "Slot")
                throw new TemplateException(
                    $"<Slot/> is only allowed inside <Template>, but found in {contextLabel}");
            foreach (var c in n.Children) EnsureNoSlot(c, contextLabel);
        }

        static ElementNode ExpandTree(ElementNode src,
                                      IReadOnlyDictionary<string, TemplateDef> templates,
                                      HashSet<string> visiting) {
            if (templates.TryGetValue(src.Tag, out var tpl))
                return ExpandInvocation(src, tpl, templates, visiting);

            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children) {
                var ec = ExpandTree(c, templates, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }

        static ElementNode ExpandInvocation(
            ElementNode invocation,
            TemplateDef tpl,
            IReadOnlyDictionary<string, TemplateDef> templates,
            HashSet<string> visiting) {

            if (!visiting.Add(tpl.Name))
                throw new TemplateException(
                    $"cyclic template reference detected: {string.Join(" → ", visiting)} → {tpl.Name}");

            try {
                var args = new Dictionary<string, string>();
                foreach (var p in tpl.Params) {
                    if (invocation.Attributes.TryGetValue(p.Name, out var v))
                        args[p.Name] = v;
                    else if (p.HasDefault)
                        args[p.Name] = p.DefaultValue;
                    else
                        throw new TemplateException(
                            $"<{tpl.Name}>: required <Param name='{p.Name}'> not provided");
                }

                foreach (var kv in invocation.Attributes) {
                    if (CommonAttrs.Contains(kv.Key)) continue;
                    if (args.ContainsKey(kv.Key)) continue;
                    throw new TemplateException(
                        $"<{tpl.Name}>: unknown attribute '{kv.Key}'");
                }

                var slotContent = new List<ElementNode>();
                foreach (var c in invocation.Children) {
                    var ec = ExpandTree(c, templates, visiting);
                    if (ec != null) slotContent.Add(ec);
                }

                var instanceRoot = ExpandNode(tpl.Body, args, slotContent, templates, visiting);
                if (instanceRoot == null)
                    throw new TemplateException(
                        $"<{tpl.Name}>: template body root was excluded by if; not allowed");

                instanceRoot.IsTemplateInstanceRoot = true;
                if (!string.IsNullOrEmpty(invocation.Id))
                    instanceRoot.Id = invocation.Id;
                foreach (var kv in invocation.Attributes) {
                    if (!CommonAttrs.Contains(kv.Key)) continue;
                    instanceRoot.Attributes[kv.Key] = kv.Value;
                }

                return instanceRoot;
            } finally {
                visiting.Remove(tpl.Name);
            }
        }

        static ElementNode ExpandNode(
            ElementNode src,
            IReadOnlyDictionary<string, string> args,
            IReadOnlyList<ElementNode> slotContent,
            IReadOnlyDictionary<string, TemplateDef> templates,
            HashSet<string> visiting) {

            if (src.Attributes.TryGetValue("if", out var rawIf)) {
                var resolved = Substitution.Apply(rawIf, args);
                if (!Truthy.Eval(resolved)) return null;
            }

            ElementNode prepared = SubstituteAttrs(src, args);

            if (templates.ContainsKey(prepared.Tag))
                return ExpandTree(prepared, templates, visiting);

            var dst = new ElementNode(prepared.Tag) {
                Id = prepared.Id,
                TextContent = Substitution.Apply(prepared.TextContent, args),
            };
            foreach (var kv in prepared.Attributes) {
                if (kv.Key == "if") continue;
                dst.Attributes[kv.Key] = kv.Value;
            }
            foreach (var c in src.Children) {
                if (c.Tag == "Slot") {
                    if (slotContent != null)
                        foreach (var sc in slotContent)
                            dst.Children.Add(DeepClone(sc));
                    continue;
                }
                var ec = ExpandNode(c, args, slotContent, templates, visiting);
                if (ec != null) dst.Children.Add(ec);
            }
            return dst;
        }
```

`SubstituteAttrs`、`DeepClone`、`CommonAttrs` 不变。

- [ ] **Step 4: 跑全部 EditMode 测试，全 PASS（之前 Task 11 测试不应回归）**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Template Tests/EditMode/Template
git commit -m "feat(template): cyclic + multi-Slot + Slot-in-Screen error checks"
```

---

## Task 13：UI.LoadDocument 串入 expander

**Files:**
- Modify: `Runtime/Application/UI.cs`

让 `UI.LoadDocument` 在 parser 之后立即跑 expander，把展开后的 ScreenDef 放入 `_docs`。这样 ScreenInstantiator 看到的永远是纯原语树（除 IsTemplateInstanceRoot 标记之外）。

- [ ] **Step 1: 修改 UI.LoadDocument**

把 `Runtime/Application/UI.cs` 的 `LoadDocument` 方法改为：

```csharp
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
```

- [ ] **Step 2: 触发编译，确认无错**

- [ ] **Step 3: 跑全部 PlayMode 测试，确认无回归（Lifecycle / E2E 仍 PASS）**

`mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], include_failed_tests=true)`。

预期：M1 留下的 24 个 PlayMode 测试仍全 PASS。

- [ ] **Step 4: Commit**

```bash
git add Runtime/Application/UI.cs
git commit -m "feat(app): UI.LoadDocument runs TemplateExpander after parsing"
```

---

## Task 14：IControl + Control 加 ScopedIds + GetChildById

**Files:**
- Modify: `Runtime/Controls/IControl.cs`
- Modify: `Runtime/Controls/Control.cs`

ScopedIds 是模板实例根专用的局部 id 表。其他 Control 这个表为空。新增 `GetChildById(string)` 方法：返回该 id 在本作用域里的 IControl，找不到返回 null。

- [ ] **Step 1: 改 IControl**

修改 `Runtime/Controls/IControl.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls {
    public interface IControl : IDisposable {
        string Id { get; }
        GameObject GameObject { get; }
        RectTransform RectTransform { get; }
        bool Hidden { get; set; }
        bool Interactable { get; set; }

        /// <summary>
        /// 模板实例根才会有非空的字典；其他 Control 返回空只读字典。
        /// 用于 Screen.Get("a/b") 路径解析。
        /// </summary>
        IReadOnlyDictionary<string, IControl> ScopedIds { get; }
    }
}
```

- [ ] **Step 2: 改 Control 基类**

在 `Runtime/Controls/Control.cs` 加：

```csharp
        Dictionary<string, IControl> _scopedIds;

        public IReadOnlyDictionary<string, IControl> ScopedIds =>
            _scopedIds ?? (IReadOnlyDictionary<string, IControl>)
                System.Collections.Immutable.ImmutableDictionary<string, IControl>.Empty;

        // 由 ScreenInstantiator 调用：把一对 (id, control) 加入此节点的局部作用域
        internal void AddScopedId(string id, IControl c) {
            _scopedIds ??= new Dictionary<string, IControl>();
            _scopedIds[id] = c;
        }
```

注意：`System.Collections.Immutable` 在 Unity 6 中默认可用（.NET Standard 2.1）。如果编译报缺包，改用一个 static readonly 字段：

```csharp
        static readonly IReadOnlyDictionary<string, IControl> EmptyDict =
            new Dictionary<string, IControl>();

        public IReadOnlyDictionary<string, IControl> ScopedIds => _scopedIds ?? EmptyDict;
```

按 fallback 写法保险起见使用第二版。

- [ ] **Step 3: 触发编译，确认无错**

- [ ] **Step 4: 跑全部 PlayMode 测试无回归**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls
git commit -m "feat(controls): IControl.ScopedIds + Control.AddScopedId for template id scoping"
```

---

## Task 15：ScreenInstantiator 处理 IsTemplateInstanceRoot

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`

当 InstantiateRecursive 处理一个 `IsTemplateInstanceRoot=true` 的节点：
- 该节点的 Control 自身的 id 仍按现状放入 outer scope（即 Screen 的 flat dict 或上一层 instance 的 ScopedIds）
- 该节点**子树**内的 id（直到下一个 IsTemplateInstanceRoot）应当放入**该节点的 ScopedIds**，而不是当前 outer scope

实现策略：把 `Dictionary<string, IControl> controls` 参数改为"当前作用域字典"——Instantiate 入口传 Screen-level dict；遇到 instance root 节点时，递归子节点时切换到该 Control 的 _scopedIds。

- [ ] **Step 1: 改 ScreenInstantiator**

修改 `Runtime/Application/ScreenInstantiator.cs`：

把 `InstantiateRecursive` 内部"放 id 到 controls"那一行的逻辑替换为：

```csharp
            if (!string.IsNullOrEmpty(node.Id))
                controls[node.Id] = control;
```

改为：

```csharp
            // 该节点的 id 入当前作用域
            if (!string.IsNullOrEmpty(node.Id))
                currentScopeIds[node.Id] = control;
```

并把 `controls` 重命名为 `currentScopeIds`。

之后 children 递归段：

```csharp
            bool selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, controls);
```

改为：

```csharp
            bool selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            // 子节点的 id 作用域：若本节点是模板实例根，则切到本 Control 的 ScopedIds
            Dictionary<string, IControl> childScope = currentScopeIds;
            if (node.IsTemplateInstanceRoot) {
                childScope = new Dictionary<string, IControl>();
                // 让 Control 持有这个字典
                foreach (var _ in childScope) { } // no-op，给读者一个语义提示
                // 把 childScope 关联到 control
                AttachScopedIds(control, childScope);
            }
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, childScope);
```

并增加：

```csharp
        // 反射桥接：把字典挂到 Control._scopedIds（internal AddScopedId 一次一对，这里直接灌一份）
        static void AttachScopedIds(Control control, Dictionary<string, IControl> dict) {
            // dict 在递归中会被填充；Control 需要一个引用而不是 snapshot
            // 用 internal 入口逐个 add 显然不行（递归还没填）。改为 Control 暴露 internal ReplaceScopedIds(dict)
            control.ReplaceScopedIds(dict);
        }
```

并在 `Control.cs` 加 internal 方法：

```csharp
        internal void ReplaceScopedIds(Dictionary<string, IControl> dict) {
            _scopedIds = dict;
        }
```

注意：`AttachScopedIds` 把字典本体交给 Control，后续 InstantiateRecursive 继续往同一字典 add，所以 Control 看到的就是完整的 ScopedIds。

也要相应更新 `Instantiate` 入口的字段名 `result.Controls`（保留原名作为 Screen 顶层字典）：

```csharp
        public InstantiationResult Instantiate(ScreenDef def) {
            return InstantiateInto(new GameObject(def.Name, typeof(RectTransform)), def);
        }

        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def) {
            var result = new InstantiationResult {
                Root = root,
                Controls = new Dictionary<string, IControl>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false, result.Controls);

            return result;
        }
```

`result.Controls` 就是 Screen 顶层 scope。

- [ ] **Step 2: 触发编译，确认无错**

- [ ] **Step 3: 跑全部 PlayMode 测试无回归**

- [ ] **Step 4: Commit**

```bash
git add Runtime/Application/ScreenInstantiator.cs Runtime/Controls/Control.cs
git commit -m "feat(app): instantiator routes child ids into ScopedIds when crossing template root"
```

---

## Task 16：Screen.Get("a/b") 路径解析

**Files:**
- Modify: `Runtime/Application/Screen.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

支持 `screen.Get("dialog/close")`：split on `/`，先在 _byId 找第一段，再依次到下一段的 ScopedIds。

- [ ] **Step 1: 写测试**

追加到 `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs` 类内部（与现有测试同级）：

```csharp
        [UnityTest]
        public IEnumerator Path_Get_walks_template_scope() {
            UI.LoadDocument("path_doc", @"<UI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Image id='inside'/>
                    </Frame>
                </Template>
                <Screen name='PathTest'>
                    <Box id='outer'/>
                </Screen></UI>");

            var screen = UI.Open("PathTest");

            // 顶层 id 仍工作
            var outer = screen.Get<PromptUGUI.Controls.Frame>("outer");
            Assert.IsNotNull(outer);

            // 模板内 id 通过路径访问
            var inside = screen.Get<PromptUGUI.Controls.Image>("outer/inside");
            Assert.IsNotNull(inside);

            UI.Close("PathTest");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Path_Get_throws_on_unknown_segment() {
            UI.LoadDocument("path_doc2", @"<UI version='1'>
                <Template name='Box'>
                    <Frame><Image id='inside'/></Frame>
                </Template>
                <Screen name='PathTest2'>
                    <Box id='outer'/>
                </Screen></UI>");

            var screen = UI.Open("PathTest2");
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("outer/nope"));

            UI.Close("PathTest2");
            yield return null;
        }
```

- [ ] **Step 2: 跑测试，FAIL（路径解析未实现）**

- [ ] **Step 3: 改 Screen.Get**

修改 `Runtime/Application/Screen.cs` 中的 `Get<T>` 与 `Get(string)`：

```csharp
        public T Get<T>(string id) where T : class, IControl {
            var c = Get(id);
            if (c is not T typed)
                throw new InvalidCastException(
                    $"id '{id}' is {c.GetType().Name}, not {typeof(T).Name}");
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
```

- [ ] **Step 4: 跑全 PlayMode 测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Screen.cs Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs
git commit -m "feat(app): Screen.Get('a/b') walks ScopedIds for template instance access"
```

---

## Task 17：[Bind] 字段在模板内仍可工作（回归保障）

**Files:**
- 仅运行原 BindWiringTests，不改代码

`Bind` 是 prefab→component 字段 wiring。模板调用展开后，模板根可能本身是个非 prefab 内置控件（Frame）也可能是其他自定义控件。如果是后者，`[Bind]` 应当照常工作。

- [ ] **Step 1: 跑全 PlayMode 测试套件，确认 BindWiringTests 仍 PASS**

如果 PASS，本 task 完成无 commit。

如果 FAIL：分析失败原因，回到 Task 15 修复 instantiator——大概率是 instance root 节点的 control 创建路径出了问题（prefab 实例化与 BindFields 的相对顺序）。

---

## Task 18：E2E — TitledPanel 包 Inventory Grid

**Files:**
- Create: `Tests/PlayMode/E2E/TitledPanelInventoryTests.cs`

跑通 spec §12 M2 acceptance：用 TitledPanel 包背包 Grid。验证：
- 模板展开后 Grid 在 Frame 内
- 模板内的 close button id 通过 `screen.Get("dialog/close")` 可访问
- 通用属性透传到模板根（anchor/size 等可设定）

- [ ] **Step 1: 写 E2E 测试**

`Tests/PlayMode/E2E/TitledPanelInventoryTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PromptText = PromptUGUI.Controls.Text;
using PromptVStack = PromptUGUI.Controls.VStack;
using PromptGrid = PromptUGUI.Controls.Grid;
using PromptFrame = PromptUGUI.Controls.Frame;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.E2E {

    public sealed class CloseBtn : Control {
        Button _btn;
        readonly Subject<Unit> _click = new();
        public override void OnAttached() {
            _btn = GameObject.GetComponent<Button>();
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
        }
        public Observable<Unit> OnClick => _click;
        public override void Dispose() { _click.Dispose(); base.Dispose(); }
    }

    public class TitledPanelInventoryTests {

        GameObject _btnPrefab;

        [SetUp] public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);

            _btnPrefab = new GameObject("CloseBtnPrefab", typeof(RectTransform));
            _btnPrefab.AddComponent<UnityImage>();
            _btnPrefab.AddComponent<Button>();

            UI.Registry.Register<CloseBtn>("CloseBtn", _btnPrefab);
        }

        [TearDown] public void TearDown() {
            if (_btnPrefab != null) Object.Destroy(_btnPrefab);
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator TitledPanel_wraps_inventory_grid_with_path_access() {
            UI.LoadDocument("inv", @"<UI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <Frame>
                        <Text id='titleLabel'>{{title}}</Text>
                        <CloseBtn if='{{closable}}' id='close'/>
                        <Slot/>
                    </Frame>
                </Template>
                <Screen name='Inventory'>
                    <TitledPanel id='dialog' anchor='center' size='600x400' title='背包'>
                        <Grid id='itemGrid' columns='6'/>
                    </TitledPanel>
                </Screen></UI>");

            var screen = UI.Open("Inventory");

            // dialog 在顶层 scope
            var dialog = screen.Get<PromptFrame>("dialog");
            Assert.IsNotNull(dialog);

            // 通用属性透传：模板根有 anchor=center / size=600x400
            var rt = dialog.RectTransform;
            Assert.AreEqual(new Vector2(600, 400), rt.sizeDelta);

            // Slot 内容已注入：Grid 是 dialog 的子节点之一
            // dialog 的子节点：titleLabel, close, itemGrid
            Assert.AreEqual(3, dialog.GameObject.transform.childCount);

            // 模板内 id 通过路径访问
            var title = screen.Get<PromptText>("dialog/titleLabel");
            Assert.AreEqual("背包", title.GameObject.GetComponent<TMP_Text>().text);

            var close = screen.Get<CloseBtn>("dialog/close");
            Assert.IsNotNull(close);

            var grid = screen.Get<PromptGrid>("dialog/itemGrid");
            Assert.IsNotNull(grid);

            // close 可订阅
            int clicks = 0;
            close.OnClick.Subscribe(_ => clicks++).AddTo(screen);
            close.GameObject.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(1, clicks);

            UI.Close("Inventory");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TitledPanel_with_closable_false_omits_CloseBtn() {
            UI.LoadDocument("inv2", @"<UI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <Frame>
                        <Text id='titleLabel'>{{title}}</Text>
                        <CloseBtn if='{{closable}}' id='close'/>
                        <Slot/>
                    </Frame>
                </Template>
                <Screen name='InventoryNoClose'>
                    <TitledPanel id='dialog' title='背包' closable='false'>
                        <Grid id='itemGrid' columns='6'/>
                    </TitledPanel>
                </Screen></UI>");

            var screen = UI.Open("InventoryNoClose");
            var dialog = screen.Get<PromptFrame>("dialog");

            // 没有 close 子节点：titleLabel + itemGrid
            Assert.AreEqual(2, dialog.GameObject.transform.childCount);

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("dialog/close"));

            UI.Close("InventoryNoClose");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TitledPanel_can_be_instantiated_twice_with_independent_ids() {
            UI.LoadDocument("inv3", @"<UI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Frame>
                        <Text id='titleLabel'>{{title}}</Text>
                    </Frame>
                </Template>
                <Screen name='Twin'>
                    <VStack id='root'>
                        <TitledPanel id='left'  title='Left'/>
                        <TitledPanel id='right' title='Right'/>
                    </VStack>
                </Screen></UI>");

            var screen = UI.Open("Twin");

            var leftTitle  = screen.Get<PromptText>("left/titleLabel");
            var rightTitle = screen.Get<PromptText>("right/titleLabel");

            Assert.AreEqual("Left",  leftTitle.GameObject.GetComponent<TMP_Text>().text);
            Assert.AreEqual("Right", rightTitle.GameObject.GetComponent<TMP_Text>().text);

            UI.Close("Twin");
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，PASS**

`mcp__UnityMCP__refresh_unity(...)` + `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], include_failed_tests=true)`。

预期：之前 24 PlayMode + 2 path Get + 3 TitledPanel = 29 PASS（数字大概；以实际为准，应当全绿）。

- [ ] **Step 3: 跑 EditMode 全套确认无回归**

预期：之前 53 + 6 (Task 2) + 1 (Task 3 slot 解析) + 17 (Task 5 Truthy 9+8 = 17) + 7 (Task 7 Substitution) + 2 (Task 6 expander pass-through) + 9 (Task 11 expander invocation) + 3 (Task 12 errors) ≈ 98 PASS。以实际为准，应全绿。

- [ ] **Step 4: Commit**

```bash
git add Tests/PlayMode/E2E/TitledPanelInventoryTests.cs
git commit -m "test(e2e): TitledPanel template wraps inventory Grid; path Get; double-instance"
```

---

## M2 完成验收清单

跑完 18 个 task 后，确认：

- [ ] `screen.Get("dialog/close")` 类的 `/` 路径访问对模板内 id 工作
- [ ] `<Template>` + `<Param default="">` + `<Slot/>` + `{{}}` + `if=` 全部按 spec §7 行为
- [ ] 同一模板可在同 Screen 内多次调用，id 互不污染
- [ ] EditMode 测试套件全 PASS
- [ ] PlayMode 测试套件全 PASS（含 M1 全部 + path Get + TitledPanel E2E）
- [ ] 错误路径覆盖：cyclic / Slot 多次 / Slot 在 Screen / 必填 Param 缺失 / 未知 Param 传入
- [ ] M1 的 MainMenu 示例**不需要任何改动**（向后兼容）

接下来由 M3 处理 `<Variant>` / `attr.var` / 运行时切换重解算（spec §8）。

---

## 已知约束与未来工作

- **`<Import>`**：M2 仍**不支持**跨文件引用模板；留 M4
- **多 Slot 命名**：spec §10 列为 v1 显式不做；保持单匿名 Slot
- **模板调用属性 `attr.var` 形态**：M2 不引入 Variant 后缀；遇到带 `.` 的 attr → expander 当作未知属性报错（M3 会改）
- **模板 body 多根**：M2 强制单根；后续若需多根可考虑引入隐式 Frame 包裹
- **模板内的 `[Bind]` 字段**：当模板根本身是 prefab-based 自定义控件时，BindFields 走当前 M1 路径；若需要从模板调用方传 Component → 不支持，建议改设计成传字符串 id 后代码侧 Get
