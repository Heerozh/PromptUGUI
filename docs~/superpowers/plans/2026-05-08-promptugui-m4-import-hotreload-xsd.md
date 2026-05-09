# PromptUGUI M4 实施计划：Import / Auto-import / Hot reload / XSD

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落实 M4 设计 spec（`docs~/superpowers/specs/2026-05-08-m4-import-autoimport-hotreload-xsd-design.md`）。新增能力：

1. `<Import src="..." [as="ns"]/>` 跨文件 Template 复用
2. `UI.LoadCommonLibrary(src, [as])` 启动期常驻模板池（auto-import）
3. `UI.Reload(name)` / `UI.ReloadCommonLibrary(src)` 运行时热重载 + Editor `AssetPostprocessor` 自动通路
4. `Tools/PromptUGUI/Generate XSD` 菜单驱动 + 反射注册控件输出项目级 XSD
5. Sample 迁移到 `LoadDocumentFromSrc` 写法以享受热重载

**Architecture:** 在 IR 上加 `ElementNode.Namespace` + `ImportRef`；新增 `DocumentLoader`（递归 Import + cycle detection + 合并 Templates）和 `DepGraph`（commons sources / src→deps / screen→deps 三张表）。`SourceResolver: Func<string,string>` 全局一次注册，库本身永不直接读文件系统。`UI.LoadDocumentFromSrc` 走 DocumentLoader；现有 `UI.LoadDocument(label, xml)` 保留为低层 raw 入口（不进 depGraph、不可 reload）。Hot reload = `Close → 解析 → Open`，VariantStore 自然存活；Reload 解析失败保留旧状态（spec D8 软失败例外）。XSD 生成器用 `XmlWriter` 输出，对每个 `ControlRegistry` 注册项重新反射 `[UIAttr]` 属性。Editor 相关代码进新 `PromptUGUI.Editor` asmdef。

**Tech Stack:** Unity 6 (6000.0+), TextMeshPro, R3 (Cysharp), NUnit (Unity Test Framework)。承接 M1/M2/M3 已落地的 IR / Parser / Layout / Registry / Template / Application / Variants 各层。

---

## 假设与前置

工程师执行此计划前需要：

1. M1/M2/M3 计划全部完成；所有 EditMode + PlayMode 测试 PASS；`main` 与 `origin/main` 同步
2. 宿主 Unity 项目位于 `C:\xsoft\PromptUGUIDev`（NuGetForUnity 装有 R3 1.3.0；`com.promptugui.core` 通过 file:// 引用本仓库）
3. UnityMCP 已连接；操作 Unity 一律走 MCP 工具，不要 spawn batch-mode Unity
4. 工作目录始终为 PromptUGUI 仓库根 `C:\xsoft\PromptUGUI`

测试运行约定：
- `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
- `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`
- 文件改动后调 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- 检查编译错误：`mcp__UnityMCP__read_console(action="get", types=["error"])`

**Spec 引用速查（不重抄；实施时按行号查证）：**

- 决策表：spec §2（M4-D1..D12）
- 数据结构：spec §4.4（DepGraph）
- 错误矩阵：spec §9
- 测试集合：spec §10
- 实施分期：spec §12

---

## 与主 spec 的轻量漂移说明

本 plan 一处与 spec 主文件（`2026-05-07-promptugui-description-language-design.md`）不一致点，按 plan 实施、Task 30 同步主 spec：

1. 主 spec §7.6 "as= 仅在两个 Import 暴露同名 Template 时强制；常态不需要" 在 M4 硬报错矩阵下应为"as= 是唯一显式消歧手段；多源同名时必填"。这是 spec 演进，不是本 plan 行为漂移。
2. 主 spec §11 速查与 §12 M4 行需要回填本 plan 落地的能力。

---

## 文件结构

```
PromptUGUI/                                                    # 仓库根
├── Runtime/
│   ├── Core/
│   │   ├── IR/
│   │   │   ├── ElementNode.cs                                 # Modify (T3): 加 Namespace
│   │   │   ├── TemplateDef.cs                                 # Modify (T11): 加 OriginSrc
│   │   │   ├── UIDocument.cs                                  # Modify (T1): 加 Imports
│   │   │   └── ImportRef.cs                                   # Create (T1)
│   │   ├── Parser/
│   │   │   └── UIDocumentParser.cs                            # Modify (T2,T3,T14)
│   │   └── Template/
│   │       └── TemplateExpander.cs                            # Modify (T7): (ns,name) 查找
│   ├── Registry/
│   │   └── ControlRegistry.cs                                 # Modify (T25): 加 All 枚举
│   └── Application/
│       ├── UI.cs                                              # Modify (T4,T8,T11,T17,T18,T23): 全 facade 增量
│       ├── DocumentLoader.cs                                  # Create (T5,T6,T12,T15)
│       ├── DepGraph.cs                                        # Create (T10)
│       └── ResourcesResolverHelper.cs                         # Create (T23)
├── Editor/                                                    # NEW —— Editor-only asmdef
│   ├── PromptUGUI.Editor.asmdef                               # Create (T21)
│   ├── UIAssetPostprocessor.cs                                # Create (T22)
│   ├── XsdGenerator.cs                                        # Create (T26,T27)
│   └── XsdMenu.cs                                             # Create (T28)
├── Tests/EditMode/
│   ├── Application/
│   │   ├── DocumentLoaderTests.cs                             # Create (T6,T13)
│   │   ├── DepGraphTests.cs                                   # Create (T10)
│   │   ├── ImportSemanticsTests.cs                            # Create (T9): 跨入口冒烟
│   │   ├── HotReloadTests.cs                                  # Create (T20)
│   │   └── CommonLibraryTests.cs                              # Create (T13)
│   ├── Parser/
│   │   └── ImportParserTests.cs                               # Create (T2,T14)
│   ├── Template/
│   │   └── NamespaceLookupTests.cs                            # Create (T16)
│   ├── Registry/
│   │   └── ControlRegistryAllTests.cs                         # Create (T25)
│   ├── Editor/                                                # NEW
│   │   ├── PromptUGUI.Tests.EditorOnly.asmdef                 # Create (T29)
│   │   ├── XsdGeneratorTests.cs                               # Create (T29)
│   │   └── Fixtures/
│   │       └── expected.xsd                                   # Create (T29)
│   └── PromptUGUI.Tests.EditMode.asmdef                       # Modify (T19): 加 UNITY_EDITOR define
└── Samples~/MainMenu/
    ├── MainMenuRunner.cs                                      # Modify (T24)
    ├── MainMenu.ui.xml                                        # Move → Resources/UI/MainMenu.ui.xml
    └── Resources/UI/MainMenu.ui.xml                           # Create (T24)
```

---

# 段 M4.1 —— Import parser + 跨文件 Template 合并

## Task 1：IR——`ImportRef` + `UIDocument.Imports`

**Files:**
- Create: `Runtime/Core/IR/ImportRef.cs`
- Modify: `Runtime/Core/IR/UIDocument.cs`

- [ ] **Step 1：创建 `ImportRef.cs`**

```csharp
// Runtime/Core/IR/ImportRef.cs
namespace PromptUGUI.IR {
    public sealed class ImportRef {
        public string Src { get; }
        public string Namespace { get; }   // null = 无命名空间
        public ImportRef(string src, string ns) {
            Src = src;
            Namespace = ns;
        }
    }
}
```

- [ ] **Step 2：修改 `UIDocument.cs` 加 Imports 列表**

```csharp
// Runtime/Core/IR/UIDocument.cs
using System.Collections.Generic;
namespace PromptUGUI.IR {
    public sealed class UIDocument {
        public int Version { get; set; } = 1;
        public List<ScreenDef> Screens { get; } = new();
        public Dictionary<string, TemplateDef> Templates { get; } = new();
        public List<ImportRef> Imports { get; } = new();
    }
}
```

- [ ] **Step 3：编译并跑现有测试，确认无回归**

调 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`。`mcp__UnityMCP__read_console(action="get", types=["error"])` 应无错误。`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全 PASS。

- [ ] **Step 4：commit**

```bash
git add Runtime/Core/IR/ImportRef.cs Runtime/Core/IR/UIDocument.cs Runtime/Core/IR/ImportRef.cs.meta
git commit -m "feat(ir): add ImportRef + UIDocument.Imports for M4"
```

---

## Task 2：Parser——识别顶层 `<Import>`，禁止嵌套与同 src 重复

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Create: `Tests/EditMode/Parser/ImportParserTests.cs`

- [ ] **Step 1：写 4 个失败测试**

```csharp
// Tests/EditMode/Parser/ImportParserTests.cs
using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class ImportParserTests {
        const string Header = @"<?xml version='1.0'?><PromptUGUI version='1'>";
        const string Footer = @"</PromptUGUI>";

        [Test]
        public void TopLevel_Import_collected_in_doc() {
            var xml = Header + @"<Import src='common/Buttons'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual(1, doc.Imports.Count);
            Assert.AreEqual("common/Buttons", doc.Imports[0].Src);
            Assert.IsNull(doc.Imports[0].Namespace);
        }

        [Test]
        public void Import_inside_Screen_throws() {
            var xml = Header + @"<Screen name='X'><Import src='y'/></Screen>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Import_inside_Template_throws() {
            var xml = Header + @"<Template name='T'><Import src='y'/><Frame/></Template>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Duplicate_src_in_same_file_throws() {
            var xml = Header + @"<Import src='a'/><Import src='a'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("duplicate", ex.Message.ToLowerInvariant());
        }
    }
}
```

- [ ] **Step 2：跑测试确认 FAIL**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ImportParserTests")` 4 个失败。

- [ ] **Step 3：修改 parser 顶层 switch 加 Import case**

在 `UIDocumentParser.Parse` 的 `foreach (XmlNode child in root.ChildNodes)` 的 switch 块中（紧挨现有 `case "Screen"` / `case "Template"`）加：

```csharp
case "Import":
    ParseImport(el, doc);
    break;
```

并新增 `ParseImport`：

```csharp
static void ParseImport(System.Xml.XmlElement el, UIDocument doc) {
    var src = el.GetAttribute("src");
    if (string.IsNullOrEmpty(src))
        throw new ParseException("<Import> requires src attribute");

    foreach (var existing in doc.Imports) {
        if (existing.Src == src)
            throw new ParseException(
                $"<Import>: duplicate src='{src}' in same file");
    }

    var ns = el.HasAttribute("as") ? el.GetAttribute("as") : null;
    if (ns != null && string.IsNullOrEmpty(ns))
        throw new ParseException(
            $"<Import src='{src}'>: as attribute cannot be empty");

    doc.Imports.Add(new IR.ImportRef(src, ns));
}
```

- [ ] **Step 4：拒绝嵌套——在 `ParseScreen` 与 `ParseTemplate` 内的子元素 switch 显式拒绝 Import**

在 `ParseScreen` 现有处理子元素的循环里：

```csharp
foreach (XmlNode c in el.ChildNodes) {
    if (c is not XmlElement child_el) continue;
    if (child_el.Name == "Import")
        throw new ParseException(
            $"<Screen name='{name}'>: <Import> only allowed as top-level element");
    if (child_el.Name == "Variant") {
        // ... 现有
    } else {
        rootNode.Children.Add(ParseElement(child_el, idsInScreen));
    }
}
```

在 `ParseTemplate` 子循环里类似，紧跟 `if (ce.Name == "Param")` 之前加：

```csharp
if (ce.Name == "Import")
    throw new ParseException(
        $"<Template name='{name}'>: <Import> only allowed as top-level element");
```

- [ ] **Step 5：跑测试确认 PASS**

4 个 ImportParserTests 全 PASS；现有 EditMode 全套也 PASS（M3 测试不受影响）。

- [ ] **Step 6：commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs \
        Tests/EditMode/Parser/ImportParserTests.cs \
        Tests/EditMode/Parser/ImportParserTests.cs.meta
git commit -m "feat(parser): recognize top-level <Import>, reject nested + duplicate src"
```

---

## Task 3：IR + Parser——`<ns.Tag/>` 命名空间拆分

**Files:**
- Modify: `Runtime/Core/IR/ElementNode.cs`
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Create: 新增 `ImportParserTests` 命名空间用例

- [ ] **Step 1：在 `ElementNode` 上加 `Namespace` 字段**

```csharp
// Runtime/Core/IR/ElementNode.cs
public sealed class ElementNode {
    public string Tag { get; }
    public string Namespace { get; }    // null = 无命名空间；如 "ml" 表示 <ml.Foo/>
    public string Id { get; set; }
    // ... 其余字段不变

    public ElementNode(string tag, string ns = null) {
        Tag = tag;
        Namespace = ns;
        Attributes = new Dictionary<string, string>();
        Children = new List<ElementNode>();
        VariantOverrides = new Dictionary<string, List<(string Variant, string Value)>>();
    }
}
```

- [ ] **Step 2：写 3 个失败测试（追加到 ImportParserTests.cs）**

```csharp
[Test]
public void Namespaced_tag_split_into_ns_and_name() {
    var xml = Header +
        @"<Screen name='S'><ml.Foo id='x'/></Screen>" + Footer;
    var doc = UIDocumentParser.Parse(xml);
    var foo = doc.Screens[0].Root.Children[0];
    Assert.AreEqual("Foo", foo.Tag);
    Assert.AreEqual("ml", foo.Namespace);
}

[Test]
public void Plain_tag_has_null_namespace() {
    var xml = Header + @"<Screen name='S'><Frame/></Screen>" + Footer;
    var doc = UIDocumentParser.Parse(xml);
    var frame = doc.Screens[0].Root.Children[0];
    Assert.AreEqual("Frame", frame.Tag);
    Assert.IsNull(frame.Namespace);
}

[Test]
public void Multiple_dots_in_tag_throws() {
    var xml = Header + @"<Screen name='S'><a.b.c/></Screen>" + Footer;
    var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
    StringAssert.Contains("namespace", ex.Message.ToLowerInvariant());
}
```

- [ ] **Step 3：跑确认 FAIL**

3 个失败：`Tag == "ml.Foo"` 等。

- [ ] **Step 4：在 `ParseElement` 处头部拆 namespace**

```csharp
static ElementNode ParseElement(XmlElement el,
                                System.Collections.Generic.HashSet<string> idsInScope) {
    string ns = null;
    string tag = el.Name;
    int dot = tag.IndexOf('.');
    if (dot >= 0) {
        if (dot == 0 || dot == tag.Length - 1)
            throw new ParseException(
                $"malformed namespaced tag '{tag}'");
        if (tag.IndexOf('.', dot + 1) >= 0)
            throw new ParseException(
                $"tag '{tag}' has multiple dots; namespace tags must be 'ns.Name' (one dot)");
        ns = tag.Substring(0, dot);
        tag = tag.Substring(dot + 1);
    }
    var node = new ElementNode(tag, ns);
    // ... 现有属性循环不变
}
```

- [ ] **Step 5：跑测试 PASS**

3 个新测试 PASS；现有全部 PASS（既有 ElementNode 调用都用单参构造函数，新加的 `ns` 是默认参数）。

- [ ] **Step 6：commit**

```bash
git add Runtime/Core/IR/ElementNode.cs \
        Runtime/Core/Parser/UIDocumentParser.cs \
        Tests/EditMode/Parser/ImportParserTests.cs
git commit -m "feat(parser): support <ns.Tag/> namespace syntax in IR + parser"
```

---

## Task 4：UI——SourceResolver 字段 + 未设置错误处理

**Files:**
- Modify: `Runtime/Application/UI.cs`

- [ ] **Step 1：在 `UI.cs` 顶部加字段**

```csharp
public static class UI {
    public static System.Func<string, string> SourceResolver { get; set; }
    // ... 现有字段
```

- [ ] **Step 2：写测试（追加到 `Tests/EditMode/Application/CommonLibraryTests.cs`，先建文件）**

```csharp
// Tests/EditMode/Application/CommonLibraryTests.cs
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application {
    public class CommonLibraryTests {
        [SetUp]   public void Setup()    => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void LoadDocumentFromSrc_without_resolver_throws() {
            UI.SourceResolver = null;
            Assert.Throws<InvalidOperationException>(() =>
                UI.LoadDocumentFromSrc("X"));
        }
    }
}
```

- [ ] **Step 3：跑确认 FAIL**

测试失败原因：`UI.LoadDocumentFromSrc` 不存在（编译错）。这是 expected——下一 Task 引入。

为先打通 Task 4 单步交付，本任务**仅引入 SourceResolver 字段 + 在 ResetForTests 里把它清回 null**。把上面的 `LoadDocumentFromSrc_without_resolver_throws` 用例**先注释掉**，等 Task 8 实现后再启用。

修改 `ResetForTests`：

```csharp
internal static void ResetForTests() {
    foreach (var s in _open.Values) s.Close();
    _open.Clear();
    _docs.Clear();
    _variantStore.Reset();
    _registry = new ControlRegistry();
    SourceResolver = null;
}
```

- [ ] **Step 4：编译 + 全 EditMode 测试 PASS**

无新测试通过，但既有全套不应出错。

- [ ] **Step 5：commit**

```bash
git add Runtime/Application/UI.cs \
        Tests/EditMode/Application/CommonLibraryTests.cs \
        Tests/EditMode/Application/CommonLibraryTests.cs.meta
git commit -m "feat(ui): add SourceResolver field + CommonLibraryTests scaffold"
```

---

## Task 5：DocumentLoader——递归 Import + cycle detection

**Files:**
- Create: `Runtime/Application/DocumentLoader.cs`

- [ ] **Step 1：建文件骨架**

```csharp
// Runtime/Application/DocumentLoader.cs
using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Application {
    /// <summary>
    /// 把一个 src 解析成"已合并 Templates 与 Screens 的 IR 文档"。
    /// 递归解析其 Import 链；同 src 在一次 Load 内只解析一次（cache）；A→B→A 循环报错。
    /// 不接触 commons pool；不入 depGraph。这两件事由 UI 上层负责。
    /// </summary>
    internal static class DocumentLoader {
        internal sealed class LoadedDoc {
            public string EntrySrc;                             // 顶层调用的 src（可能是 commons 或 screen）
            public HashSet<string> AllSrcs = new();             // 含 entry 在内的所有解析过的 src
            public List<ScreenDef> Screens = new();             // 顶层 screens（来自 entry doc 自身；嵌套 imports 中的 Screens 视为非法）
            public Dictionary<TemplateKey, TemplateDef> Templates = new();
        }

        internal readonly struct TemplateKey : IEquatable<TemplateKey> {
            public readonly string Namespace;   // null = 裸名
            public readonly string Name;
            public TemplateKey(string ns, string name) { Namespace = ns; Name = name; }
            public bool Equals(TemplateKey o) => Namespace == o.Namespace && Name == o.Name;
            public override bool Equals(object o) => o is TemplateKey k && Equals(k);
            public override int GetHashCode() =>
                System.HashCode.Combine(Namespace, Name);
            public override string ToString() =>
                Namespace == null ? Name : $"{Namespace}.{Name}";
        }

        internal static LoadedDoc Load(string src,
                                       Func<string, string> resolver,
                                       bool allowScreens) {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");

            var loaded = new LoadedDoc { EntrySrc = src };
            var visiting = new Stack<string>();
            LoadInternal(src, resolver, allowScreens, loaded, visiting,
                         applyNamespace: null);
            return loaded;
        }

        static void LoadInternal(
            string src,
            Func<string, string> resolver,
            bool allowScreens,
            LoadedDoc agg,
            Stack<string> visiting,
            string applyNamespace) {

            if (visiting.Contains(src)) {
                var chain = string.Join(" → ", visiting);
                throw new ParseException(
                    $"cyclic Import detected: {chain} → {src}");
            }
            if (!agg.AllSrcs.Add(src)) return;   // already loaded once during this call

            var xml = resolver(src);
            if (string.IsNullOrEmpty(xml))
                throw new System.IO.IOException(
                    $"SourceResolver returned null/empty for src='{src}'");

            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xml); }
            catch (Exception e) {
                throw new ParseException($"parsing src='{src}' failed: {e.Message}", e);
            }

            if (!allowScreens && doc.Screens.Count > 0)
                throw new ParseException(
                    $"src='{src}' is loaded as common library / nested import; <Screen> not allowed");

            // 当前文件的 Screens（仅 entry 允许）
            if (allowScreens) {
                foreach (var s in doc.Screens) agg.Screens.Add(s);
            }

            // 当前文件的 Templates 入合并表，按 applyNamespace 决定 ns
            foreach (var kv in doc.Templates) {
                var key = new TemplateKey(applyNamespace, kv.Key);
                if (agg.Templates.ContainsKey(key))
                    throw new TemplateException(
                        $"duplicate template '{key}' (loaded from src='{src}')");
                agg.Templates[key] = kv.Value;
            }

            // 递归解析 Imports
            visiting.Push(src);
            try {
                foreach (var imp in doc.Imports) {
                    var childNs = imp.Namespace ?? applyNamespace;
                    LoadInternal(imp.Src, resolver, allowScreens: false,
                                 agg, visiting, childNs);
                }
            } finally { visiting.Pop(); }
        }
    }
}
```

- [ ] **Step 2：写测试 `DocumentLoaderTests.cs`**

```csharp
// Tests/EditMode/Application/DocumentLoaderTests.cs
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Application {
    public class DocumentLoaderTests {
        sealed class FakeFiles {
            public Dictionary<string, string> Map = new();
            public Func<string, string> Resolver => s =>
                Map.TryGetValue(s, out var v) ? v : null;
        }

        const string Wrap = @"<?xml version='1.0'?><PromptUGUI version='1'>{0}</PromptUGUI>";

        string W(string body) => string.Format(Wrap, body);

        [Test]
        public void Single_file_no_imports_loads() {
            var ff = new FakeFiles { Map = {
                ["main"] = W(@"<Template name='X'><Frame/></Template>"),
            }};
            var loaded = DocumentLoader.Load("main", ff.Resolver, allowScreens: true);
            Assert.AreEqual(1, loaded.Templates.Count);
            CollectionAssert.AreEquivalent(new[] { "main" }, loaded.AllSrcs);
        }

        [Test]
        public void Nested_imports_resolved_recursively() {
            var ff = new FakeFiles { Map = {
                ["main"] = W(@"<Import src='a'/><Screen name='S'><Frame/></Screen>"),
                ["a"]    = W(@"<Import src='b'/><Template name='Ta'><Frame/></Template>"),
                ["b"]    = W(@"<Template name='Tb'><Frame/></Template>"),
            }};
            var loaded = DocumentLoader.Load("main", ff.Resolver, allowScreens: true);
            CollectionAssert.AreEquivalent(new[] { "main", "a", "b" }, loaded.AllSrcs);
            Assert.AreEqual(2, loaded.Templates.Count);
        }

        [Test]
        public void Cycle_detected_with_path_in_message() {
            var ff = new FakeFiles { Map = {
                ["main"] = W(@"<Import src='a'/>"),
                ["a"]    = W(@"<Import src='b'/>"),
                ["b"]    = W(@"<Import src='a'/>"),
            }};
            var ex = Assert.Throws<ParseException>(() =>
                DocumentLoader.Load("main", ff.Resolver, allowScreens: true));
            StringAssert.Contains("cyclic", ex.Message.ToLowerInvariant());
            StringAssert.Contains("a", ex.Message);
            StringAssert.Contains("b", ex.Message);
        }

        [Test]
        public void Allow_screens_false_rejects_Screen_in_entry() {
            var ff = new FakeFiles { Map = {
                ["common"] = W(@"<Screen name='S'><Frame/></Screen>"),
            }};
            Assert.Throws<ParseException>(() =>
                DocumentLoader.Load("common", ff.Resolver, allowScreens: false));
        }

        [Test]
        public void Same_src_imported_by_multiple_files_loaded_once() {
            var ff = new FakeFiles { Map = {
                ["main"] = W(@"<Import src='a'/><Import src='b'/>"),
                ["a"]    = W(@"<Import src='shared'/>"),
                ["b"]    = W(@"<Import src='shared'/>"),
                ["shared"] = W(@"<Template name='Sh'><Frame/></Template>"),
            }};
            var loaded = DocumentLoader.Load("main", ff.Resolver, allowScreens: false);
            Assert.AreEqual(1, loaded.Templates.Count);   // Sh 不重复
        }

        [Test]
        public void Resolver_null_throws_InvalidOperation() {
            Assert.Throws<InvalidOperationException>(() =>
                DocumentLoader.Load("x", null, allowScreens: true));
        }

        [Test]
        public void Resolver_returns_null_throws_IOException() {
            Assert.Throws<System.IO.IOException>(() =>
                DocumentLoader.Load("x", _ => null, allowScreens: true));
        }
    }
}
```

注意：`DocumentLoader` 是 `internal`；测试通过既有 `InternalsVisibleTo` 路径访问。检查 `Runtime/AssemblyInfo.cs` 是否暴露给 `PromptUGUI.Tests.EditMode`：

```csharp
// Runtime/AssemblyInfo.cs（应已存在）
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PromptUGUI.Tests.EditMode")]
```

如果没有，本 Task 顺手加上。

- [ ] **Step 3：跑测试 PASS**

7 个 DocumentLoaderTests 全 PASS。

- [ ] **Step 4：commit**

```bash
git add Runtime/Application/DocumentLoader.cs \
        Runtime/Application/DocumentLoader.cs.meta \
        Tests/EditMode/Application/DocumentLoaderTests.cs \
        Tests/EditMode/Application/DocumentLoaderTests.cs.meta \
        Runtime/AssemblyInfo.cs
git commit -m "feat(loader): DocumentLoader recursive Import + cycle detection"
```

---

## Task 6：DocumentLoader——同名冲突报错

**Files:**
- Modify: `Runtime/Application/DocumentLoader.cs`（已有冲突逻辑，本任务加针对性测试 + 补全错误信息）
- Modify: `Tests/EditMode/Application/DocumentLoaderTests.cs`

- [ ] **Step 1：写 3 个冲突测试**

```csharp
[Test]
public void Two_imports_define_same_template_name_throws() {
    var ff = new FakeFiles { Map = {
        ["main"] = W(@"<Import src='a'/><Import src='b'/>"),
        ["a"]    = W(@"<Template name='Foo'><Frame/></Template>"),
        ["b"]    = W(@"<Template name='Foo'><Frame/></Template>"),
    }};
    var ex = Assert.Throws<TemplateException>(() =>
        DocumentLoader.Load("main", ff.Resolver, allowScreens: false));
    StringAssert.Contains("Foo", ex.Message);
    StringAssert.Contains("duplicate", ex.Message.ToLowerInvariant());
}

[Test]
public void Entry_doc_template_collides_with_import_throws() {
    var ff = new FakeFiles { Map = {
        ["main"] = W(@"<Import src='a'/><Template name='Foo'><Frame/></Template>"),
        ["a"]    = W(@"<Template name='Foo'><Frame/></Template>"),
    }};
    Assert.Throws<TemplateException>(() =>
        DocumentLoader.Load("main", ff.Resolver, allowScreens: false));
}

[Test]
public void Same_template_name_in_different_namespaces_OK() {
    var ff = new FakeFiles { Map = {
        ["main"] = W(@"<Import src='a'/><Import src='b' as='ns'/>"),
        ["a"]    = W(@"<Template name='Foo'><Frame/></Template>"),
        ["b"]    = W(@"<Template name='Foo'><Frame/></Template>"),
    }};
    var loaded = DocumentLoader.Load("main", ff.Resolver, allowScreens: false);
    Assert.AreEqual(2, loaded.Templates.Count);
}
```

- [ ] **Step 2：跑确认前 2 个 PASS（Task 5 已实现），第 3 个 FAIL（DocumentLoader 还没看 ImportRef.Namespace）**

- [ ] **Step 3：在 LoadInternal 内已经处理 applyNamespace 参数了——但 Task 5 的递归调用传的是 `imp.Namespace ?? applyNamespace`，按嵌套 import 的 ns 优先生效**

确认这条路径在 Task 5 已写入；运行第 3 个测试应 PASS。

如果 FAIL，检查是不是 Step 2 中的"`as=` 解析"还没对齐：parser 是否正确把 `<Import src='b' as='ns'/>` 的 `ns` 写到 `ImportRef.Namespace`？回看 Task 2 Step 3：是。

- [ ] **Step 4：跑测试 PASS**

3 个新测试 + Task 5 全部 PASS。

- [ ] **Step 5：commit**

```bash
git add Tests/EditMode/Application/DocumentLoaderTests.cs
git commit -m "test(loader): cover template conflict + namespace disambiguation"
```

---

## Task 7：TemplateExpander——`(ns, name)` 查找

**Files:**
- Modify: `Runtime/Core/Template/TemplateExpander.cs`

- [ ] **Step 1：把 Expand 签名改为接受 `(ns,name) → TemplateDef` 字典**

把现有 `IReadOnlyDictionary<string, TemplateDef> templates` 参数改为：

```csharp
using TemplateMap = System.Collections.Generic.IReadOnlyDictionary<
    PromptUGUI.Application.DocumentLoader.TemplateKey,
    PromptUGUI.IR.TemplateDef>;
```

或者直接用具体类型签名。把 `TemplateExpander.Expand(UIDocument doc)` 重写为接收 `LoadedDoc`：

```csharp
public static UIDocument Expand(DocumentLoader.LoadedDoc loaded) {
    foreach (var t in loaded.Templates.Values) ValidateSlotCount(t);

    var result = new UIDocument { Version = 1 };
    foreach (var kv in loaded.Templates)
        result.Templates[kv.Key.ToString()] = kv.Value;     // 调试可读，不再被运行时使用

    foreach (var s in loaded.Screens) {
        var newRoot = new ElementNode(s.Root.Tag);
        foreach (var c in s.Root.Children) {
            EnsureNoSlot(c, $"Screen '{s.Name}'");
            var ec = ExpandTree(c, loaded.Templates, new HashSet<DocumentLoader.TemplateKey>());
            if (ec != null) newRoot.Children.Add(ec);
        }
        var newScreen = new ScreenDef(s.Name, newRoot);
        foreach (var block in s.Variants) {
            // ... 与现有 Expand 同
        }
        result.Screens.Add(newScreen);
    }
    return result;
}
```

- [ ] **Step 2：把 `ExpandTree` / `ExpandInvocation` / `ExpandNode` 内部所有 `templates.TryGetValue(src.Tag, out var tpl)` 改为按 `(Namespace, Name)` 查找**

```csharp
static ElementNode ExpandTree(ElementNode src,
        IReadOnlyDictionary<DocumentLoader.TemplateKey, TemplateDef> templates,
        HashSet<DocumentLoader.TemplateKey> visiting) {
    var key = new DocumentLoader.TemplateKey(src.Namespace, src.Tag);
    if (templates.TryGetValue(key, out var tpl))
        return ExpandInvocation(src, tpl, templates, visiting);
    // 命名空间存在但找不到对应名 → 报错
    if (src.Namespace != null)
        throw new TemplateException(
            $"unknown template '{src.Namespace}.{src.Tag}'");
    // 否则视为非模板（控件 / 原语），按现有 fall-through 路径继续
    // ... 现有 dst clone 路径
}
```

- [ ] **Step 3：保留 `Expand(UIDocument doc)` 兼容入口（M1/M2/M3 测试仍调用它）**

```csharp
public static UIDocument Expand(UIDocument doc) {
    // Adapter：把单 doc 包成 LoadedDoc 走新路径
    var loaded = new DocumentLoader.LoadedDoc {
        EntrySrc = "<inline>",
        Screens = doc.Screens,
    };
    foreach (var kv in doc.Templates)
        loaded.Templates[new DocumentLoader.TemplateKey(null, kv.Key)] = kv.Value;
    return Expand(loaded);
}
```

- [ ] **Step 4：跑全部 EditMode 测试 PASS（M1/M2/M3 不应受影响）**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__run_tests(mode="EditMode")
```

- [ ] **Step 5：写新增测试 `NamespaceLookupTests.cs`**

```csharp
// Tests/EditMode/Template/NamespaceLookupTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class NamespaceLookupTests {

        DocumentLoader.LoadedDoc Make(
            ElementNode rootChild,
            (string ns, string name, ElementNode body)[] templates) {
            var l = new DocumentLoader.LoadedDoc {
                EntrySrc = "test",
                Screens = { new ScreenDef("S", new ElementNode("__root__")) }
            };
            l.Screens[0].Root.Children.Add(rootChild);
            foreach (var (ns, name, body) in templates) {
                var t = new TemplateDef(name) { Body = body };
                l.Templates[new DocumentLoader.TemplateKey(ns, name)] = t;
            }
            return l;
        }

        [Test]
        public void Namespaced_template_invocation_resolved() {
            var body = new ElementNode("Frame");
            var inv = new ElementNode("Foo", "ns");
            var loaded = Make(inv, new[] {
                (ns: "ns", name: "Foo", body: body)
            });
            var expanded = TemplateExpander.Expand(loaded);
            Assert.AreEqual("Frame", expanded.Screens[0].Root.Children[0].Tag);
        }

        [Test]
        public void Plain_invocation_doesnt_match_namespaced_template() {
            // <Foo/> 应不命中 (ns="ns", name="Foo")，被当作未注册控件——
            // expander 不抛错，下游 instantiator 才会抛"未注册"。
            var body = new ElementNode("Frame");
            var inv = new ElementNode("Foo");   // ns=null
            var loaded = Make(inv, new[] {
                (ns: "ns", name: "Foo", body: body)
            });
            var expanded = TemplateExpander.Expand(loaded);
            Assert.AreEqual("Foo", expanded.Screens[0].Root.Children[0].Tag);
            Assert.IsNull(expanded.Screens[0].Root.Children[0].Namespace);
        }

        [Test]
        public void Unknown_namespace_throws() {
            var inv = new ElementNode("Foo", "missing");
            var loaded = Make(inv, System.Array.Empty<(string, string, ElementNode)>());
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(loaded));
        }
    }
}
```

- [ ] **Step 6：跑测试 PASS**

3 个新测试 + 已有全 PASS。

- [ ] **Step 7：commit**

```bash
git add Runtime/Core/Template/TemplateExpander.cs \
        Tests/EditMode/Template/NamespaceLookupTests.cs \
        Tests/EditMode/Template/NamespaceLookupTests.cs.meta
git commit -m "feat(expander): lookup by (namespace, name); preserve UIDocument adapter"
```

---

## Task 8：UI——`LoadDocumentFromSrc` API + 集成

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Modify: `Tests/EditMode/Application/CommonLibraryTests.cs`

- [ ] **Step 1：在 UI 加 `LoadDocumentFromSrc`**

```csharp
public static IReadOnlyList<string> LoadDocumentFromSrc(string src) {
    if (SourceResolver == null)
        throw new InvalidOperationException(
            "UI.SourceResolver must be set before LoadDocumentFromSrc");

    var loaded = DocumentLoader.Load(src, SourceResolver, allowScreens: true);
    var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

    var added = new List<string>();
    foreach (var s in expanded.Screens) {
        if (_docs.ContainsKey(s.Name))
            throw new InvalidOperationException(
                $"Screen '{s.Name}' already loaded");
        _docs[s.Name] = s;
        added.Add(s.Name);
    }
    // depGraph 注册留 Task 11 加（commons 依赖图就位后一起）
    return added;
}
```

- [ ] **Step 2：取消 Task 4 注释掉的测试，再追加几条**

```csharp
[Test]
public void LoadDocumentFromSrc_without_resolver_throws() {
    UI.SourceResolver = null;
    Assert.Throws<InvalidOperationException>(() =>
        UI.LoadDocumentFromSrc("X"));
}

[Test]
public void LoadDocumentFromSrc_returns_screen_names() {
    UI.SourceResolver = src => src == "main"
        ? @"<?xml version='1.0'?><PromptUGUI version='1'>
              <Screen name='S1'><Frame/></Screen>
              <Screen name='S2'><Frame/></Screen>
           </PromptUGUI>"
        : null;
    var names = UI.LoadDocumentFromSrc("main");
    CollectionAssert.AreEquivalent(new[] { "S1", "S2" }, names);
}

[Test]
public void LoadDocumentFromSrc_imports_resolved() {
    var files = new Dictionary<string, string> {
        ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                      <Import src='shared'/>
                      <Screen name='S'><Frame/></Screen>
                    </PromptUGUI>",
        ["shared"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                        <Template name='T'><Frame/></Template>
                      </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    UI.LoadDocumentFromSrc("main");
    Assert.NotNull(UI.Get("S") == null ? null : UI.Get("S"));   // 仅断言无异常
}
```

- [ ] **Step 3：跑测试 PASS**

3 个新测试 PASS。

- [ ] **Step 4：commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/CommonLibraryTests.cs
git commit -m "feat(ui): LoadDocumentFromSrc through DocumentLoader"
```

---

## Task 9：Import 端到端冒烟

**Files:**
- Create: `Tests/EditMode/Application/ImportSemanticsTests.cs`

- [ ] **Step 1：写一个完整 E2E 用例**

```csharp
// Tests/EditMode/Application/ImportSemanticsTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application {
    public class ImportSemanticsTests {
        [SetUp]   public void Setup()    { UI.ResetForTests(); BuiltinPrimitives.Register(UI.Registry); }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Imported_template_usable_in_screen_file() {
            var files = new Dictionary<string, string> {
                ["panels"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                 <Template name='Card'>
                                   <Frame><Slot/></Frame>
                                 </Template>
                               </PromptUGUI>",
                ["main"]   = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                 <Import src='panels'/>
                                 <Screen name='Main'>
                                   <Card id='c'><Text>hi</Text></Card>
                                 </Screen>
                               </PromptUGUI>",
            };
            UI.SourceResolver = src => files.GetValueOrDefault(src);
            UI.LoadDocumentFromSrc("main");
            var screen = UI.Open("Main");
            Assert.IsNotNull(screen.Get<Frame>("c"));
        }
    }
}
```

- [ ] **Step 2：跑测试 PASS**

`mcp__UnityMCP__run_tests(mode="EditMode", filter="ImportSemanticsTests")` PASS。

- [ ] **Step 3：commit**

```bash
git add Tests/EditMode/Application/ImportSemanticsTests.cs Tests/EditMode/Application/ImportSemanticsTests.cs.meta
git commit -m "test: end-to-end Import semantics — Card template across files"
```

---

# 段 M4.2 —— LoadCommonLibrary + commons 池 + DepGraph

## Task 10：DepGraph 数据结构

**Files:**
- Create: `Runtime/Application/DepGraph.cs`
- Create: `Tests/EditMode/Application/DepGraphTests.cs`

- [ ] **Step 1：建 `DepGraph.cs`**

```csharp
// Runtime/Application/DepGraph.cs
using System.Collections.Generic;

namespace PromptUGUI.Application {
    internal sealed class DepGraph {
        public readonly HashSet<string> CommonsSources = new();
        public readonly Dictionary<string, HashSet<string>> SrcToDeps = new();
        public readonly Dictionary<string, ScreenDep> ScreenDeps = new();

        public sealed class ScreenDep {
            public string EntrySrc;
            public HashSet<string> AllDeps;
        }

        public void Clear() {
            CommonsSources.Clear();
            SrcToDeps.Clear();
            ScreenDeps.Clear();
        }

        public bool IsCommons(string src) => CommonsSources.Contains(src);

        public IEnumerable<string> ScreensDependingOn(string src) {
            foreach (var kv in ScreenDeps)
                if (kv.Value.AllDeps.Contains(src))
                    yield return kv.Key;
        }
    }
}
```

- [ ] **Step 2：写 `DepGraphTests.cs`（小，只验数据结构 plain 行为）**

```csharp
// Tests/EditMode/Application/DepGraphTests.cs
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application {
    public class DepGraphTests {
        [Test]
        public void IsCommons_reflects_set() {
            var g = new DepGraph();
            g.CommonsSources.Add("c");
            Assert.IsTrue(g.IsCommons("c"));
            Assert.IsFalse(g.IsCommons("d"));
        }

        [Test]
        public void ScreensDependingOn_returns_matches() {
            var g = new DepGraph();
            g.ScreenDeps["A"] = new DepGraph.ScreenDep {
                EntrySrc = "a",
                AllDeps = new() { "a", "x" },
            };
            g.ScreenDeps["B"] = new DepGraph.ScreenDep {
                EntrySrc = "b",
                AllDeps = new() { "b", "y" },
            };
            CollectionAssert.AreEquivalent(new[] { "A" },
                System.Linq.Enumerable.ToArray(g.ScreensDependingOn("x")));
        }

        [Test]
        public void Clear_resets() {
            var g = new DepGraph();
            g.CommonsSources.Add("c");
            g.ScreenDeps["A"] = new DepGraph.ScreenDep();
            g.SrcToDeps["c"] = new();
            g.Clear();
            Assert.AreEqual(0, g.CommonsSources.Count);
            Assert.AreEqual(0, g.ScreenDeps.Count);
            Assert.AreEqual(0, g.SrcToDeps.Count);
        }
    }
}
```

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Runtime/Application/DepGraph.cs Runtime/Application/DepGraph.cs.meta \
        Tests/EditMode/Application/DepGraphTests.cs Tests/EditMode/Application/DepGraphTests.cs.meta
git commit -m "feat(ui): DepGraph data structure for hot reload"
```

---

## Task 11：UI——commons 池 + `LoadCommonLibrary`

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Modify: `Runtime/Core/IR/TemplateDef.cs`（加 OriginSrc 字段）
- Modify: `Tests/EditMode/Application/CommonLibraryTests.cs`

- [ ] **Step 1：在 `TemplateDef` 加 OriginSrc**

```csharp
public sealed class TemplateDef {
    public string Name { get; }
    public List<ParamDef> Params { get; } = new();
    public ElementNode Body { get; set; }
    public string OriginSrc { get; set; }   // 仅 commons reload 时使用；其他场景 null

    public TemplateDef(string name) { Name = name; }
}
```

- [ ] **Step 2：在 `UI.cs` 加 commons 池 + DepGraph + LoadCommonLibrary**

```csharp
public static class UI {
    static readonly Dictionary<DocumentLoader.TemplateKey, TemplateDef> _commonsPool = new();
    static readonly DepGraph _depGraph = new();

    public static void LoadCommonLibrary(string src, string @as = null) {
        if (SourceResolver == null)
            throw new InvalidOperationException(
                "UI.SourceResolver must be set before LoadCommonLibrary");

        var loaded = DocumentLoader.Load(src, SourceResolver, allowScreens: false);

        // 与已有 commons 冲突检查（先于落盘）
        var stagedKeys = new List<DocumentLoader.TemplateKey>();
        foreach (var kv in loaded.Templates) {
            var rebasedKey = @as == null
                ? kv.Key
                : new DocumentLoader.TemplateKey(@as, kv.Key.Name);
            if (_commonsPool.ContainsKey(rebasedKey))
                throw new TemplateException(
                    $"common library conflict: '{rebasedKey}' already in commons pool");
            stagedKeys.Add(rebasedKey);
        }

        // 真正塞入
        foreach (var kv in loaded.Templates) {
            var rebasedKey = @as == null
                ? kv.Key
                : new DocumentLoader.TemplateKey(@as, kv.Key.Name);
            kv.Value.OriginSrc = src;
            _commonsPool[rebasedKey] = kv.Value;
        }

        _depGraph.CommonsSources.Add(src);
        _depGraph.SrcToDeps[src] = new HashSet<string>(loaded.AllSrcs);
    }

    // ResetForTests 增量
    internal static void ResetForTests() {
        // ... 现有
        _commonsPool.Clear();
        _depGraph.Clear();
    }
}
```

- [ ] **Step 3：在 `LoadDocumentFromSrc` 内合并 commons**

```csharp
public static IReadOnlyList<string> LoadDocumentFromSrc(string src) {
    if (SourceResolver == null)
        throw new InvalidOperationException(
            "UI.SourceResolver must be set before LoadDocumentFromSrc");

    var loaded = DocumentLoader.Load(src, SourceResolver, allowScreens: true);

    // 把 commons 与 entry 合并
    var merged = new Dictionary<DocumentLoader.TemplateKey, TemplateDef>();
    foreach (var kv in _commonsPool) merged[kv.Key] = kv.Value;
    foreach (var kv in loaded.Templates) {
        if (merged.ContainsKey(kv.Key))
            throw new TemplateException(
                $"template '{kv.Key}' conflicts with commons pool");
        merged[kv.Key] = kv.Value;
    }
    loaded.Templates.Clear();
    foreach (var kv in merged) loaded.Templates[kv.Key] = kv.Value;

    var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

    var added = new List<string>();
    foreach (var s in expanded.Screens) {
        if (_docs.ContainsKey(s.Name))
            throw new InvalidOperationException(
                $"Screen '{s.Name}' already loaded");
        _docs[s.Name] = s;
        added.Add(s.Name);
        _depGraph.ScreenDeps[s.Name] = new DepGraph.ScreenDep {
            EntrySrc = src,
            AllDeps = new HashSet<string>(loaded.AllSrcs),
        };
    }
    _depGraph.SrcToDeps[src] = new HashSet<string>(loaded.AllSrcs);
    return added;
}
```

- [ ] **Step 4：测试**

```csharp
[Test]
public void LoadCommonLibrary_makes_template_visible_to_screen() {
    var files = new Dictionary<string, string> {
        ["common/btns"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Template name='PrimaryButton'>
                                <Btn><Slot/></Btn>
                              </Template>
                            </PromptUGUI>",
        ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                       <Screen name='M'>
                         <PrimaryButton id='play'>开始</PrimaryButton>
                       </Screen>
                     </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    BuiltinPrimitives.Register(UI.Registry);

    UI.LoadCommonLibrary("common/btns");
    UI.LoadDocumentFromSrc("main");
    var screen = UI.Open("M");
    Assert.IsNotNull(screen.Get<Btn>("play"));
}

[Test]
public void Commons_with_screen_throws() {
    UI.SourceResolver = src =>
        @"<?xml version='1.0'?><PromptUGUI version='1'>
            <Screen name='X'><Frame/></Screen>
          </PromptUGUI>";
    Assert.Throws<ParseException>(() => UI.LoadCommonLibrary("any"));
}

[Test]
public void Commons_conflict_throws_on_second_register() {
    var xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
                  <Template name='Foo'><Frame/></Template>
                </PromptUGUI>";
    UI.SourceResolver = _ => xml;
    UI.LoadCommonLibrary("a");
    Assert.Throws<TemplateException>(() => UI.LoadCommonLibrary("b"));
}

[Test]
public void Commons_conflict_with_screen_local_throws() {
    var files = new Dictionary<string, string> {
        ["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='X'><Frame/></Template>
                  </PromptUGUI>",
        ["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='X'><Frame/></Template>
                    <Screen name='S'><Frame/></Screen>
                  </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    UI.LoadCommonLibrary("c");
    Assert.Throws<TemplateException>(() => UI.LoadDocumentFromSrc("m"));
}

[Test]
public void Commons_with_as_namespace_isolates() {
    var files = new Dictionary<string, string> {
        ["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='X'><Frame/></Template>
                  </PromptUGUI>",
        ["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='X'><Frame/></Template>
                    <Screen name='S'><X id='a'/></Screen>
                  </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    UI.LoadCommonLibrary("c", @as: "std");   // commons 进 std 命名空间 → 不冲突
    UI.LoadDocumentFromSrc("m");             // 应不抛
    Assert.Pass();
}
```

- [ ] **Step 5：跑测试 PASS + commit**

```bash
git add Runtime/Application/UI.cs Runtime/Core/IR/TemplateDef.cs \
        Tests/EditMode/Application/CommonLibraryTests.cs
git commit -m "feat(ui): LoadCommonLibrary commons pool with as= namespace + dep graph"
```

---

## Task 12：DocumentLoader——把 commons 注入合并字典的代码下沉到一处

**Files:**
- Modify: `Runtime/Application/DocumentLoader.cs`
- Modify: `Runtime/Application/UI.cs`

> **目的**：Task 11 的合并代码留在 UI 里有点啰嗦；把它下沉成 `DocumentLoader.LoadAndMerge` 助手便于 Reload 路径复用。重构性 task，无新行为。

- [ ] **Step 1：添加 helper**

```csharp
// DocumentLoader.cs 末尾
internal static LoadedDoc LoadAndMerge(
    string src,
    Func<string, string> resolver,
    IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool) {

    var loaded = Load(src, resolver, allowScreens: true);

    foreach (var kv in commonsPool) {
        if (loaded.Templates.ContainsKey(kv.Key))
            throw new TemplateException(
                $"template '{kv.Key}' conflicts with commons pool");
        loaded.Templates[kv.Key] = kv.Value;
    }
    return loaded;
}
```

- [ ] **Step 2：UI.LoadDocumentFromSrc 调它**

```csharp
public static IReadOnlyList<string> LoadDocumentFromSrc(string src) {
    if (SourceResolver == null)
        throw new InvalidOperationException(
            "UI.SourceResolver must be set before LoadDocumentFromSrc");

    var loaded = DocumentLoader.LoadAndMerge(src, SourceResolver, _commonsPool);
    var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

    var added = new List<string>();
    foreach (var s in expanded.Screens) {
        if (_docs.ContainsKey(s.Name))
            throw new InvalidOperationException(
                $"Screen '{s.Name}' already loaded");
        _docs[s.Name] = s;
        added.Add(s.Name);
        _depGraph.ScreenDeps[s.Name] = new DepGraph.ScreenDep {
            EntrySrc = src,
            AllDeps = new HashSet<string>(loaded.AllSrcs),
        };
    }
    _depGraph.SrcToDeps[src] = new HashSet<string>(loaded.AllSrcs);
    return added;
}
```

- [ ] **Step 3：跑全套 EditMode PASS（重构无回归）+ commit**

```bash
git add Runtime/Application/DocumentLoader.cs Runtime/Application/UI.cs
git commit -m "refactor(loader): extract LoadAndMerge for reuse by reload paths"
```

---

## Task 13：CommonLibraryTests 补完——递归 Import + 命名空间

**Files:**
- Modify: `Tests/EditMode/Application/CommonLibraryTests.cs`

- [ ] **Step 1：写 2 个测试**

```csharp
[Test]
public void Common_library_can_import_other_files() {
    var files = new Dictionary<string, string> {
        ["base"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                       <Template name='B'><Frame/></Template>
                     </PromptUGUI>",
        ["ext"]  = @"<?xml version='1.0'?><PromptUGUI version='1'>
                       <Import src='base'/>
                       <Template name='E'><Frame/></Template>
                     </PromptUGUI>",
        ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                       <Screen name='M'><B id='b'/><E id='e'/></Screen>
                     </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    BuiltinPrimitives.Register(UI.Registry);

    UI.LoadCommonLibrary("ext");      // ext 内有 <Import src='base'/>
    UI.LoadDocumentFromSrc("main");
    var s = UI.Open("M");
    Assert.IsNotNull(s.Get<Frame>("b"));
    Assert.IsNotNull(s.Get<Frame>("e"));
}

[Test]
public void Two_commons_distinct_names_OK() {
    var files = new Dictionary<string, string> {
        ["a"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='A'><Frame/></Template>
                  </PromptUGUI>",
        ["b"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='B'><Frame/></Template>
                  </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    UI.LoadCommonLibrary("a");
    UI.LoadCommonLibrary("b");
    Assert.Pass();   // 不抛即可
}
```

- [ ] **Step 2：跑 PASS + commit**

```bash
git add Tests/EditMode/Application/CommonLibraryTests.cs
git commit -m "test(commons): cover transitive import + multi-commons coexistence"
```

---

# 段 M4.3 —— `as="ns"` 完成度补强

> Task 2/3/6/11 已经把 `as=` 落地，本段补它在 Screen 文件 `<Import as=>` 路径上的端到端用例。

## Task 14：Parser——`<Import as=>` 错误情形

**Files:**
- Modify: `Tests/EditMode/Parser/ImportParserTests.cs`

- [ ] **Step 1：写 3 个测试**

```csharp
[Test]
public void Import_with_as_recorded() {
    var xml = Header + @"<Import src='a' as='ns'/>" + Footer;
    var doc = UIDocumentParser.Parse(xml);
    Assert.AreEqual("ns", doc.Imports[0].Namespace);
}

[Test]
public void Import_with_empty_as_throws() {
    var xml = Header + @"<Import src='a' as=''/>" + Footer;
    Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
}

[Test]
public void Import_with_dot_in_as_throws() {
    var xml = Header + @"<Import src='a' as='x.y'/>" + Footer;
    Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
}
```

- [ ] **Step 2：在 `ParseImport` 加 `as=` 字符校验**

```csharp
if (ns != null) {
    if (string.IsNullOrEmpty(ns))
        throw new ParseException(
            $"<Import src='{src}'>: as attribute cannot be empty");
    if (ns.Contains('.'))
        throw new ParseException(
            $"<Import src='{src}'>: as='{ns}' must not contain '.'");
}
```

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Parser/ImportParserTests.cs
git commit -m "feat(parser): validate <Import as=> attribute (non-empty, no dot)"
```

---

## Task 15：DocumentLoader——`as=` 在 Screen Import 端到端

**Files:**
- Modify: `Tests/EditMode/Application/ImportSemanticsTests.cs`

- [ ] **Step 1：补 1 个 E2E**

```csharp
[Test]
public void Imports_with_namespace_can_coexist_with_same_template_name() {
    var files = new Dictionary<string, string> {
        ["a"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='Btn'><Frame/></Template>
                  </PromptUGUI>",
        ["b"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Template name='Btn'><Frame/></Template>
                  </PromptUGUI>",
        ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                       <Import src='a'/>
                       <Import src='b' as='b'/>
                       <Screen name='M'>
                         <Btn id='one'/>
                         <b.Btn id='two'/>
                       </Screen>
                     </PromptUGUI>",
    };
    UI.SourceResolver = src => files.GetValueOrDefault(src);
    BuiltinPrimitives.Register(UI.Registry);
    UI.LoadDocumentFromSrc("main");
    var s = UI.Open("M");
    Assert.IsNotNull(s.Get<Frame>("one"));
    Assert.IsNotNull(s.Get<Frame>("two"));
}
```

- [ ] **Step 2：跑 PASS + commit**

```bash
git add Tests/EditMode/Application/ImportSemanticsTests.cs
git commit -m "test(import): namespaced Import disambiguates same-named templates"
```

---

## Task 16：未声明 ns 错误信息可读性

**Files:**
- Modify: `Tests/EditMode/Template/NamespaceLookupTests.cs`

- [ ] **Step 1：补错误用例**

```csharp
[Test]
public void Unknown_namespace_message_lists_namespace() {
    var inv = new ElementNode("Foo", "missing");
    var loaded = Make(inv, System.Array.Empty<(string, string, ElementNode)>());
    var ex = Assert.Throws<TemplateException>(() => TemplateExpander.Expand(loaded));
    StringAssert.Contains("missing", ex.Message);
    StringAssert.Contains("Foo", ex.Message);
}
```

- [ ] **Step 2：必要时调整 TemplateExpander 错误信息使包含 ns 与 name**

确认 Task 7 已写为 `$"unknown template '{src.Namespace}.{src.Tag}'"`，包含两者。

- [ ] **Step 3：commit**

```bash
git add Tests/EditMode/Template/NamespaceLookupTests.cs
git commit -m "test(expander): assert namespace + tag in unknown-template error"
```

---

# 段 M4.4 —— Reload + HotReload entry point

## Task 17：UI——`Reload(name)` 运行时 API

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Create: `Tests/EditMode/Application/HotReloadTests.cs`

- [ ] **Step 1：在 UI 加 Reload**

```csharp
public static void Reload(string screenName) {
    if (!_depGraph.ScreenDeps.TryGetValue(screenName, out var dep))
        throw new InvalidOperationException(
            $"Screen '{screenName}' was not loaded by src; cannot reload " +
            $"(use LoadDocumentFromSrc instead of LoadDocument(label, xml))");

    // 1) 先解析（失败保留旧状态）
    var loaded = DocumentLoader.LoadAndMerge(dep.EntrySrc, SourceResolver, _commonsPool);
    var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

    // 找新 ScreenDef
    ScreenDef newDef = null;
    foreach (var s in expanded.Screens)
        if (s.Name == screenName) { newDef = s; break; }
    if (newDef == null)
        throw new InvalidOperationException(
            $"Screen '{screenName}' no longer present in src='{dep.EntrySrc}' after reload");

    // 2) 销毁旧
    bool wasOpen = _open.ContainsKey(screenName);
    if (wasOpen) Close(screenName);

    // 3) 替换 _docs + depGraph
    _docs[screenName] = newDef;
    _depGraph.ScreenDeps[screenName] = new DepGraph.ScreenDep {
        EntrySrc = dep.EntrySrc,
        AllDeps = new HashSet<string>(loaded.AllSrcs),
    };

    // 4) 如此前是开着的就重开
    if (wasOpen) Open(screenName);
}
```

- [ ] **Step 2：写测试**

```csharp
// Tests/EditMode/Application/HotReloadTests.cs
using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application {
    public class HotReloadTests {
        Dictionary<string, string> _files;

        [SetUp] public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
            _files = new Dictionary<string, string>();
            UI.SourceResolver = src => _files.GetValueOrDefault(src);
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Reload_replaces_screen_def() {
            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentFromSrc("main");
            UI.Open("S");

            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='b'/></Screen>
              </PromptUGUI>";
            UI.Reload("S");

            var s = UI.Get("S");
            Assert.IsNotNull(s.Get<Frame>("b"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => s.Get<Frame>("a"));
        }

        [Test]
        public void Reload_failed_parse_preserves_old_state() {
            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentFromSrc("main");
            var oldScreen = UI.Open("S");

            _files["main"] = "<<<not xml>>>";
            Assert.Throws<Exception>(() => UI.Reload("S"));

            // 旧 Screen 仍可用
            Assert.IsNotNull(oldScreen.Get<Frame>("a"));
            Assert.AreSame(oldScreen, UI.Get("S"));
        }

        [Test]
        public void Reload_raw_loaded_doc_throws() {
            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame/></Screen>
                  </PromptUGUI>");
            UI.Open("S");
            Assert.Throws<InvalidOperationException>(() => UI.Reload("S"));
        }

        [Test]
        public void Reload_preserves_VariantStore_state() {
            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentFromSrc("main");
            UI.Variants.Set("mobile", true);
            UI.Open("S");

            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='b'/></Screen>
              </PromptUGUI>";
            UI.Reload("S");

            Assert.IsTrue(UI.Variants.IsActive("mobile"));
        }
    }
}
```

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Runtime/Application/UI.cs \
        Tests/EditMode/Application/HotReloadTests.cs Tests/EditMode/Application/HotReloadTests.cs.meta
git commit -m "feat(ui): UI.Reload(name) — close→reparse→open with rollback on failure"
```

---

## Task 18：UI——`ReloadCommonLibrary(src)` 与全 Screen 重 reload

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Modify: `Tests/EditMode/Application/HotReloadTests.cs`

- [ ] **Step 1：在 UI 加 ReloadCommonLibrary**

```csharp
public static void ReloadCommonLibrary(string src) {
    if (!_depGraph.CommonsSources.Contains(src))
        throw new InvalidOperationException(
            $"src='{src}' is not a registered common library");

    // 暂存：把这批 commons 撤出来，便于失败回滚
    var stashed = new List<KeyValuePair<DocumentLoader.TemplateKey, TemplateDef>>();
    foreach (var kv in _commonsPool)
        if (kv.Value.OriginSrc == src) stashed.Add(kv);
    foreach (var kv in stashed) _commonsPool.Remove(kv.Key);
    var prevDeps = _depGraph.SrcToDeps.TryGetValue(src, out var d) ? d : null;

    try {
        // 复用 LoadCommonLibrary 重新装载（因为我们已经把旧的撤了，不会冲突）
        // 注意 as= 在 commons 上的语义：LoadCommonLibrary 当时传的 as 我们没存。
        // M4 v1 简化：commons 重 reload 时 as= 信息丢失——只支持裸 commons。
        // 如果某 commons 用了 as=，reload 后冲突会报错；那时改回手动 UnloadAll + 重 bootstrap。
        // 这条限制写进 Task 18 的 README/注释，并在 spec 风险表登记。
        LoadCommonLibrary(src);
    } catch {
        // 回滚
        foreach (var kv in stashed) _commonsPool[kv.Key] = kv.Value;
        if (prevDeps != null) _depGraph.SrcToDeps[src] = prevDeps;
        _depGraph.CommonsSources.Add(src);
        throw;
    }

    // 一律 reload 所有 _screenDeps（M4 v1 简化策略）
    var names = new List<string>(_depGraph.ScreenDeps.Keys);
    foreach (var name in names) Reload(name);
}
```

- [ ] **Step 2：测试**

```csharp
[Test]
public void ReloadCommonLibrary_picks_up_template_changes() {
    _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Template name='V'><Frame id='old'/></Template>
      </PromptUGUI>";
    _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Screen name='S'><V id='holder'/></Screen>
      </PromptUGUI>";
    UI.LoadCommonLibrary("c");
    UI.LoadDocumentFromSrc("m");
    UI.Open("S");

    // 改 commons：
    _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Template name='V'><Frame id='new'/></Template>
      </PromptUGUI>";
    UI.ReloadCommonLibrary("c");

    var s = UI.Get("S");
    Assert.IsNotNull(s.Get<Frame>("holder/new"));
}

[Test]
public void ReloadCommonLibrary_failed_parse_rolls_back() {
    _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Template name='V'><Frame/></Template>
      </PromptUGUI>";
    UI.LoadCommonLibrary("c");

    _files["c"] = "<<<bad>>>";
    Assert.Throws<Exception>(() => UI.ReloadCommonLibrary("c"));

    // 旧 commons 仍在
    var canReload = false;
    try {
        _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
            <Template name='V'><Frame/></Template>
          </PromptUGUI>";
        UI.ReloadCommonLibrary("c");
        canReload = true;
    } catch { }
    Assert.IsTrue(canReload, "expected commons pool intact after failed reload");
}
```

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/HotReloadTests.cs
git commit -m "feat(ui): UI.ReloadCommonLibrary — refresh commons + cascade screen reloads"
```

---

## Task 19：UI.HotReload 静态嵌套类（仅 Editor）

**Files:**
- Modify: `Runtime/Application/UI.cs`
- Modify: `Tests/EditMode/PromptUGUI.Tests.EditMode.asmdef`（确认默认 includePlatforms=Editor，使 UNITY_EDITOR define 在 EditMode 测试编译期生效）

- [ ] **Step 1：在 UI 末尾加 HotReload 嵌套类**

```csharp
#if UNITY_EDITOR
public static class HotReload {
    public static System.Func<string, string> AssetPathToSrc { get; set; }
    public static bool Enabled { get; set; } = true;

    public static void NotifyAssetChanged(string assetPath) {
        if (!Enabled || AssetPathToSrc == null) return;
        var src = AssetPathToSrc(assetPath);
        if (string.IsNullOrEmpty(src)) return;

        if (_depGraph.IsCommons(src)) {
            ReloadCommonLibrary(src);
            return;
        }

        var affected = new List<string>();
        foreach (var name in _depGraph.ScreensDependingOn(src))
            affected.Add(name);
        foreach (var name in affected) Reload(name);
    }
}
#endif
```

- [ ] **Step 2：在 ResetForTests 内重置 HotReload 配置**

```csharp
internal static void ResetForTests() {
    // ... 现有
#if UNITY_EDITOR
    HotReload.AssetPathToSrc = null;
    HotReload.Enabled = true;
#endif
}
```

- [ ] **Step 3：测试 HotReload.NotifyAssetChanged 路径分流**

```csharp
[Test]
public void NotifyAssetChanged_for_screen_src_reloads() {
    _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Screen name='S'><Frame id='a'/></Screen>
      </PromptUGUI>";
    UI.LoadDocumentFromSrc("m");
    UI.Open("S");

    UI.HotReload.AssetPathToSrc = path => path == "fakepath/m.ui.xml" ? "m" : null;
    _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Screen name='S'><Frame id='b'/></Screen>
      </PromptUGUI>";
    UI.HotReload.NotifyAssetChanged("fakepath/m.ui.xml");

    Assert.IsNotNull(UI.Get("S").Get<Frame>("b"));
}

[Test]
public void NotifyAssetChanged_for_commons_src_reloads_screens() {
    _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Template name='T'><Frame id='inner_v1'/></Template>
      </PromptUGUI>";
    _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Screen name='S'><T id='outer'/></Screen>
      </PromptUGUI>";
    UI.LoadCommonLibrary("c");
    UI.LoadDocumentFromSrc("m");
    UI.Open("S");

    UI.HotReload.AssetPathToSrc = path => path == "p/c.ui.xml" ? "c" : null;
    _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Template name='T'><Frame id='inner_v2'/></Template>
      </PromptUGUI>";
    UI.HotReload.NotifyAssetChanged("p/c.ui.xml");

    Assert.IsNotNull(UI.Get("S").Get<Frame>("outer/inner_v2"));
}

[Test]
public void NotifyAssetChanged_unknown_path_silently_ignored() {
    UI.HotReload.AssetPathToSrc = _ => null;
    Assert.DoesNotThrow(() => UI.HotReload.NotifyAssetChanged("foo"));
}

[Test]
public void NotifyAssetChanged_when_disabled_noops() {
    _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Screen name='S'><Frame id='a'/></Screen>
      </PromptUGUI>";
    UI.LoadDocumentFromSrc("m");
    UI.Open("S");

    UI.HotReload.AssetPathToSrc = _ => "m";
    UI.HotReload.Enabled = false;
    _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
        <Screen name='S'><Frame id='b'/></Screen>
      </PromptUGUI>";
    UI.HotReload.NotifyAssetChanged("p");

    Assert.IsNotNull(UI.Get("S").Get<Frame>("a"));   // 没 reload
}
```

- [ ] **Step 4：跑 PASS + commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/HotReloadTests.cs
git commit -m "feat(ui): HotReload.NotifyAssetChanged entry — Editor-only dispatcher"
```

---

## Task 20：HotReloadTests——补 unload / commons-as= 限制

**Files:**
- Modify: `Tests/EditMode/Application/HotReloadTests.cs`

- [ ] **Step 1：补两条**

```csharp
[Test]
public void Reload_unknown_screen_throws() {
    Assert.Throws<InvalidOperationException>(() => UI.Reload("Nonexistent"));
}

[Test]
public void ReloadCommonLibrary_unknown_src_throws() {
    Assert.Throws<InvalidOperationException>(() => UI.ReloadCommonLibrary("not-a-commons"));
}
```

- [ ] **Step 2：跑 PASS + commit**

```bash
git add Tests/EditMode/Application/HotReloadTests.cs
git commit -m "test(reload): assert error paths for unknown screen / commons src"
```

---

# 段 M4.5 —— Editor asmdef + AssetPostprocessor + Sample 迁移

## Task 21：建 `PromptUGUI.Editor` asmdef

**Files:**
- Create: `Editor/PromptUGUI.Editor.asmdef`
- Create: `Editor/PromptUGUI.Editor.asmdef.meta`

- [ ] **Step 1：建 asmdef**

```json
{
    "name": "PromptUGUI.Editor",
    "rootNamespace": "PromptUGUI.Editor",
    "references": ["PromptUGUI.Runtime"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

注意 `includePlatforms: Editor` —— 这个 asmdef 里的代码不会被编译进 Player build。

- [ ] **Step 2：建 meta（让 Unity 识别 asmdef）**

```yaml
# Editor/PromptUGUI.Editor.asmdef.meta
fileFormatVersion: 2
guid: 00000000000000000000000000000000   # 占位；保存时 Unity 自动生成
AssemblyDefinitionImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
```

> 实际用 Unity 创建 asmdef 时它会自动写 meta；本步骤里写文本是为了让 git 跟踪它。生成完成后用 `mcp__UnityMCP__manage_asset(action="create_folder", path="Editor")` 与 `manage_script` 操作创建会更稳。**首选用 MCP 创建 asmdef**：
>
> ```python
> mcp__UnityMCP__manage_asset(action="create_folder", path="Assets/")  # 仅在宿主项目；本仓库下直接 git add 文件即可
> ```
>
> 由于本仓库以 UPM package 形式分发，`Editor/` 目录下放 asmdef + cs 即可，meta 由 Unity 自动生成。

- [ ] **Step 3：editor 端 hello 测试**（验证 asmdef 编译通过）

新建 `Editor/_HelloEditor.cs`：

```csharp
namespace PromptUGUI.Editor {
    internal static class _HelloEditor {}
}
```

在宿主项目 `C:\xsoft\PromptUGUIDev` 调 `mcp__UnityMCP__refresh_unity(...)` + `mcp__UnityMCP__read_console(action="get", types=["error"])`，应无 asmdef 错误。

- [ ] **Step 4：删 _HelloEditor.cs（仅探针），commit asmdef**

```bash
git add Editor/PromptUGUI.Editor.asmdef Editor/PromptUGUI.Editor.asmdef.meta
git commit -m "chore(editor): add PromptUGUI.Editor asmdef"
```

---

## Task 22：`UIAssetPostprocessor`

**Files:**
- Create: `Editor/UIAssetPostprocessor.cs`

- [ ] **Step 1：实现**

```csharp
// Editor/UIAssetPostprocessor.cs
using System.Linq;
using UnityEditor;
using PromptUGUI.Application;

namespace PromptUGUI.Editor {
    internal sealed class UIAssetPostprocessor : AssetPostprocessor {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths) {

            if (!UI.HotReload.Enabled || UI.HotReload.AssetPathToSrc == null) return;

            foreach (var p in importedAssets.Concat(movedAssets)) {
                if (!p.EndsWith(".ui.xml")) continue;
                try { UI.HotReload.NotifyAssetChanged(p); }
                catch (System.Exception e) {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] hot reload failed for {p}: {e.Message}");
                }
            }
        }
    }
}
```

注意 `try/catch` 把 reload 异常捕获并打 LogError——避免 AssetPostprocessor 异常把 Unity Asset import 流程整个搞崩。这是 Editor 流程友好考虑，与 spec D8 "保留旧状态" 一致。

- [ ] **Step 2：编译通过（无 Asset 改动可触发，本任务无新测试，留 Task 24 Sample 迁移做手测）**

```
mcp__UnityMCP__refresh_unity(...)
mcp__UnityMCP__read_console(action="get", types=["error"])  # 无错
```

- [ ] **Step 3：commit**

```bash
git add Editor/UIAssetPostprocessor.cs Editor/UIAssetPostprocessor.cs.meta
git commit -m "feat(editor): AssetPostprocessor → UI.HotReload.NotifyAssetChanged"
```

---

## Task 23：`UseResourcesResolver` helper

**Files:**
- Create: `Runtime/Application/ResourcesResolverHelper.cs`

> 即便此 helper 在 Runtime asmdef，其内部 Editor-only 部分用 `#if UNITY_EDITOR` 包裹。

- [ ] **Step 1：实现**

```csharp
// Runtime/Application/ResourcesResolverHelper.cs
using System;
using System.IO;
using UnityEngine;

namespace PromptUGUI.Application {
    public static partial class UI {
        public static void UseResourcesResolver(string rootPath) {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("rootPath must be non-empty");
            var root = rootPath.TrimEnd('/');

            SourceResolver = src => {
                if (string.IsNullOrEmpty(src))
                    throw new IOException("Resources lookup with empty src");
                var ta = Resources.Load<TextAsset>($"{root}/{src}");
                if (ta == null)
                    throw new IOException(
                        $"Resources lookup failed: {root}/{src}");
                return ta.text;
            };

#if UNITY_EDITOR
            HotReload.AssetPathToSrc = assetPath => {
                if (string.IsNullOrEmpty(assetPath)) return null;
                var marker = $"/Resources/{root}/";
                int idx = assetPath.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) return null;
                var rel = assetPath.Substring(idx + marker.Length);
                return rel.EndsWith(".ui.xml")
                    ? rel.Substring(0, rel.Length - ".ui.xml".Length)
                    : null;
            };
#endif
        }
    }
}
```

注意：`UI` 类需要标 `partial` 才能跨文件扩展。回到 `Runtime/Application/UI.cs` 把 `public static class UI` 改为 `public static partial class UI`（一行变更，无逻辑影响）。

- [ ] **Step 2：测试 helper 路径反映射**

```csharp
// Tests/EditMode/Application/CommonLibraryTests.cs 末尾追加
[Test]
public void UseResourcesResolver_AssetPathToSrc_strips_prefix() {
    UI.UseResourcesResolver("UI");
    var fn = UI.HotReload.AssetPathToSrc;
    Assert.IsNotNull(fn);
    Assert.AreEqual("MainMenu",
        fn("Assets/Resources/UI/MainMenu.ui.xml"));
    Assert.AreEqual("subdir/X",
        fn("Assets/Samples/PromptUGUI/0.0.0/Demo/Resources/UI/subdir/X.ui.xml"));
    Assert.IsNull(fn("Assets/Other/Foo.txt"));
}
```

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Runtime/Application/ResourcesResolverHelper.cs Runtime/Application/ResourcesResolverHelper.cs.meta \
        Runtime/Application/UI.cs \
        Tests/EditMode/Application/CommonLibraryTests.cs
git commit -m "feat(ui): UseResourcesResolver helper — Resources path → src + reverse map"
```

---

## Task 24：Sample 迁移

**Files:**
- Modify: `Samples~/MainMenu/MainMenuRunner.cs`
- Move: `Samples~/MainMenu/MainMenu.ui.xml` → `Samples~/MainMenu/Resources/UI/MainMenu.ui.xml`

- [ ] **Step 1：物理移动文件**

```bash
mkdir -p Samples~/MainMenu/Resources/UI
git mv Samples~/MainMenu/MainMenu.ui.xml Samples~/MainMenu/Resources/UI/MainMenu.ui.xml
# .meta 一起迁
git mv Samples~/MainMenu/MainMenu.ui.xml.meta Samples~/MainMenu/Resources/UI/MainMenu.ui.xml.meta 2>/dev/null || true
```

如果原 .ui.xml.meta 不存在（Samples~ 目录在 package 安装前不需要 meta），跳过第二行。

- [ ] **Step 2：改 MainMenuRunner.cs**

```csharp
// Samples~/MainMenu/MainMenuRunner.cs
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.MainMenu {
    /// <summary>
    /// 加载 Resources/UI/MainMenu.ui.xml 并打开。Editor 内修改这个文件保存即自动 hot reload。
    /// </summary>
    public sealed class MainMenuRunner : MonoBehaviour {
        void Start() {
            BuiltinPrimitives.Register(UI.Registry);
            UI.UseResourcesResolver("UI");
            UI.LoadDocumentFromSrc("MainMenu");
            var screen = UI.Open("MainMenu");

            screen.Get<Btn>("playBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] play clicked")).AddTo(screen);
            screen.Get<Btn>("settingsBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] settings clicked")).AddTo(screen);
            screen.Get<Btn>("quitBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] quit clicked")).AddTo(screen);
        }
    }
}
```

去掉了 `[SerializeField] TextAsset _xml` —— 不再需要 Inspector 拖文件。

- [ ] **Step 3：手测 checklist（写进 PR 描述，并在本 plan 内 ack）**

宿主项目 `C:\xsoft\PromptUGUIDev` 内：

1. 让 Sample 通过 Package Manager 装到宿主项目（`Samples~` 内容会复制到 `Assets/Samples/...`）
2. 打开 MainMenu 场景，挂 MainMenuRunner，按 Play
3. 验：`[Sample] play clicked` 出现在按钮点击时
4. 不停 Play 状态（或停掉 Play 都可），打开 `Resources/UI/MainMenu.ui.xml` 改一个文本（如把 "开始游戏" 改成 "开始"），保存
5. 期望：Editor 控制台无报错；Game View 在重开 Screen 时显示新文本（重开方式：先 `UI.Close("MainMenu")` 再 `UI.Open("MainMenu")`，或附加一个调试按钮调 `UI.Reload("MainMenu")`）
6. 验 Player build：File → Build Settings → Build, 装出来 exe 启动后 MainMenu 正常显示（resolver 调 Resources.Load 应能命中）

- [ ] **Step 4：commit**

```bash
git add Samples~/MainMenu/MainMenuRunner.cs Samples~/MainMenu/Resources/
git commit -m "refactor(sample): migrate MainMenu to LoadDocumentFromSrc + hot reload"
```

---

# 段 M4.6 —— XsdGenerator + 主 spec 同步

## Task 25：ControlRegistry——加 `All` 枚举

**Files:**
- Modify: `Runtime/Registry/ControlRegistry.cs`
- Create: `Tests/EditMode/Registry/ControlRegistryAllTests.cs`

- [ ] **Step 1：写测试**

```csharp
// Tests/EditMode/Registry/ControlRegistryAllTests.cs
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Registry {
    public class ControlRegistryAllTests {
        [Test]
        public void All_lists_all_registered_tags() {
            var r = new ControlRegistry();
            r.Register<Frame>("Frame", null);
            r.Register<VStack>("VStack", null);

            var tags = r.All.Select(x => x.Tag).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Frame", "VStack" }, tags);
        }
    }
}
```

- [ ] **Step 2：在 ControlRegistry 加 All**

```csharp
public IEnumerable<(string Tag, Entry Entry)> All {
    get {
        foreach (var kv in _byTag)
            yield return (kv.Key, kv.Value);
    }
}
```

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Runtime/Registry/ControlRegistry.cs \
        Tests/EditMode/Registry/ControlRegistryAllTests.cs Tests/EditMode/Registry/ControlRegistryAllTests.cs.meta
git commit -m "feat(registry): expose ControlRegistry.All for XSD generator"
```

---

## Task 26：XsdGenerator——静态部分 + 反射动态部分

**Files:**
- Create: `Editor/XsdGenerator.cs`

- [ ] **Step 1：实现**

```csharp
// Editor/XsdGenerator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using PromptUGUI.Registry;

namespace PromptUGUI.Editor {
    public static class XsdGenerator {
        const string Ns = "https://prompt-ugui/v1";

        public static string Generate(ControlRegistry registry) {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
            };
            using var writer = XmlWriter.Create(sb, settings);

            writer.WriteStartElement("xs", "schema", "http://www.w3.org/2001/XMLSchema");
            writer.WriteAttributeString("targetNamespace", Ns);
            writer.WriteAttributeString("xmlns", Ns);
            writer.WriteAttributeString("elementFormDefault", "qualified");

            WriteCommonAttrGroup(writer);
            WritePromptUGUIRoot(writer);
            WriteImport(writer);
            WriteScreen(writer);
            WriteTemplate(writer);
            WriteParam(writer);
            WriteSlot(writer);
            WriteVariant(writer);
            WriteAdd(writer);

            // 7 原语 + 注册控件
            WriteControl(writer, "Frame",  Array.Empty<(string,string)>());
            WriteControl(writer, "Image",  new[] {("sprite","xs:string"),("color","xs:string"),("type","xs:string")});
            WriteControl(writer, "Text",   new[] {("font","xs:string"),("size","xs:string"),("color","xs:string"),("align","xs:string"),("wrap","xs:string"),("text","xs:string")});
            WriteControl(writer, "VStack", Array.Empty<(string,string)>());
            WriteControl(writer, "HStack", Array.Empty<(string,string)>());
            WriteControl(writer, "Grid",   new[] {("columns","xs:int")});
            WriteControl(writer, "Btn",    new[] {("sprite","xs:string"),("color","xs:string"),("text","xs:string")});

            // 已注册自定义控件——排除已写过的原语
            var primitives = new HashSet<string> {
                "Frame","Image","Text","VStack","HStack","Grid","Btn" };
            var customs = registry.All
                .Where(x => !primitives.Contains(x.Tag))
                .OrderBy(x => x.Tag, StringComparer.Ordinal)
                .ToArray();

            foreach (var (tag, entry) in customs) {
                var attrs = ReflectControlAttrs(entry.ControlType);
                WriteControl(writer, tag, attrs);
            }

            // controlGroup（含 xs:any 兜底）
            WriteControlGroup(writer, customs.Select(x => x.Tag).ToArray());

            writer.WriteEndElement();
            writer.Flush();
            return sb.ToString();
        }

        static (string Name, string XsdType)[] ReflectControlAttrs(Type controlType) {
            var props = controlType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            var list = new List<(string, string)>();
            foreach (var p in props) {
                var ui = p.GetCustomAttribute<UIAttrAttribute>();
                if (ui == null || !p.CanWrite) continue;
                var name = ui.Name ?? CamelCase(p.Name);
                var xsdType = MapXsdType(p.PropertyType);
                if (xsdType == null) continue;   // 不支持类型 → 跳过
                list.Add((name, xsdType));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list.ToArray();
        }

        static string MapXsdType(Type t) {
            if (t == typeof(string)) return "xs:string";
            if (t == typeof(int))    return "xs:int";
            if (t == typeof(float))  return "xs:float";
            if (t == typeof(bool))   return "xs:boolean";
            return null;
        }

        static string CamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // ---- 静态片段写入辅助 ----（保持 spec §8.2 schema 大致结构）

        static void WriteCommonAttrGroup(XmlWriter w) {
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("name", "commonAttrs");
            string[] commons = {
                "id","anchor","size","width","height","margin","pivot",
                "padding","spacing","hidden","interactable" };
            foreach (var a in commons) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", a);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            // 兜底放 .var 后缀属性
            w.WriteStartElement("xs", "anyAttribute", null);
            w.WriteAttributeString("processContents", "lax");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WritePromptUGUIRoot(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "PromptUGUI");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            foreach (var name in new[] {"Import","Screen","Template"}) {
                w.WriteStartElement("xs", "element", null);
                w.WriteAttributeString("ref", name);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "version");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteImport(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Import");
            w.WriteStartElement("xs", "complexType", null);
            foreach (var (n, req) in new[] {("src","required"),("as","optional")}) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteScreen(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Screen");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteEndElement();
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Variant");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "name");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteTemplate(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Template");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "sequence", null);
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Param");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            w.WriteStartElement("xs", "any", null);
            w.WriteAttributeString("namespace", "##local");
            w.WriteAttributeString("processContents", "lax");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "name");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteParam(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Param");
            w.WriteStartElement("xs", "complexType", null);
            foreach (var (n, req) in new[] {("name","required"),("default","optional")}) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteSlot(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Slot");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteVariant(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Variant");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "sequence", null);
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Add");
            w.WriteAttributeString("minOccurs", "1");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "when");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteAdd(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Add");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            foreach (var (n, req) in new[] {("into","required"),("at","optional")}) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteControl(XmlWriter w, string tag,
                                 (string Name, string XsdType)[] attrs) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", tag);
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("ref", "commonAttrs");
            w.WriteEndElement();
            foreach (var (name, type) in attrs) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", name);
                w.WriteAttributeString("type", type);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteControlGroup(XmlWriter w, string[] customTags) {
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("name", "controlGroup");
            w.WriteStartElement("xs", "choice", null);
            string[] all = new[] {
                "Frame","Image","Text","VStack","HStack","Grid","Btn"
            }.Concat(customTags).ToArray();
            foreach (var n in all) {
                w.WriteStartElement("xs", "element", null);
                w.WriteAttributeString("ref", n);
                w.WriteEndElement();
            }
            // 兜底：让未注册标签（如模板调用）不被红线
            w.WriteStartElement("xs", "any", null);
            w.WriteAttributeString("namespace", "##local");
            w.WriteAttributeString("processContents", "lax");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }
}
```

> 这是个长方法，但每个 helper 都很短、独立可读，符合 plan "smaller focused files" 心智。XsdGenerator 整体一个文件就足够。

- [ ] **Step 2：编译通过**

`mcp__UnityMCP__refresh_unity(...)` + `read_console`。

- [ ] **Step 3：commit**

```bash
git add Editor/XsdGenerator.cs Editor/XsdGenerator.cs.meta
git commit -m "feat(editor): XsdGenerator — static schema + reflected custom controls"
```

---

## Task 27：(已含在 26)

> Task 27 在最初分段中是 "动态扫 + xs:any in controlGroup"——已并入 Task 26 实现，不单独成 Task 以减少 commit 噪音。

**Skip — placeholder retained for spec §12 segment-mapping 一致性，无需操作。**

---

## Task 28：`GenerateToFile` + Editor 菜单

**Files:**
- Modify: `Editor/XsdGenerator.cs`
- Create: `Editor/XsdMenu.cs`

- [ ] **Step 1：在 XsdGenerator 加 `GenerateToFile`**

```csharp
public static void GenerateToFile(
    ControlRegistry registry,
    string assetPath = "Assets/PromptUGUI.gen.xsd") {
    var xsd = Generate(registry);
    File.WriteAllText(assetPath, xsd, new UTF8Encoding(false));
    UnityEditor.AssetDatabase.Refresh();
    UnityEngine.Debug.Log($"[PromptUGUI] XSD generated: {assetPath}");
}
```

- [ ] **Step 2：建 `XsdMenu.cs`**

```csharp
// Editor/XsdMenu.cs
using UnityEditor;
using PromptUGUI.Application;

namespace PromptUGUI.Editor {
    static class XsdMenu {
        [MenuItem("Tools/PromptUGUI/Generate XSD")]
        static void Run() {
            XsdGenerator.GenerateToFile(UI.Registry);
        }
    }
}
```

- [ ] **Step 3：commit**

```bash
git add Editor/XsdGenerator.cs Editor/XsdMenu.cs Editor/XsdMenu.cs.meta
git commit -m "feat(editor): XsdGenerator.GenerateToFile + Tools menu"
```

---

## Task 29：XSD snapshot 测试

**Files:**
- Create: `Tests/EditMode/Editor/PromptUGUI.Tests.EditorOnly.asmdef`
- Create: `Tests/EditMode/Editor/XsdGeneratorTests.cs`
- Create: `Tests/EditMode/Editor/Fixtures/expected.xsd`

- [ ] **Step 1：建 Editor-only 测试 asmdef（依赖 PromptUGUI.Editor）**

```json
{
    "name": "PromptUGUI.Tests.EditorOnly",
    "rootNamespace": "PromptUGUI.Tests.Editor",
    "references": [
        "PromptUGUI.Runtime",
        "PromptUGUI.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"]
}
```

- [ ] **Step 2：写测试**

```csharp
// Tests/EditMode/Editor/XsdGeneratorTests.cs
using System.IO;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Editor;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Tests.Editor {
    public class XsdGeneratorTests {

        [Test]
        public void Empty_registry_produces_static_skeleton() {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("<xs:schema", xsd);
            StringAssert.Contains("targetNamespace=\"https://prompt-ugui/v1\"", xsd);
            StringAssert.Contains("name=\"Frame\"", xsd);    // 7 原语都在
            StringAssert.Contains("name=\"Btn\"", xsd);
        }

        [Test]
        public void Custom_control_appears_with_UIAttr_attributes() {
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"PrimaryButton\"", xsd);
            StringAssert.Contains("name=\"label\"", xsd);   // [UIAttr] property
        }

        [Test]
        public void Generate_to_file_produces_readable_file() {
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, "test.gen.xsd");
            XsdGenerator.GenerateToFile(r, path);
            Assert.IsTrue(File.Exists(path));
            var content = File.ReadAllText(path);
            StringAssert.Contains("PrimaryButton", content);
        }
    }

    public class TestPrimaryButton : PromptUGUI.Controls.Control {
        [PromptUGUI.Registry.UIAttr] public string Label { get; set; }
    }
}
```

> 我们不做严格 byte-by-byte snapshot 对比（小改动总会触发 churn），改用 substring 断言。如未来需要严格 snapshot，可加 `expected.xsd` fixture 走文件 diff。

- [ ] **Step 3：跑 PASS + commit**

```bash
git add Tests/EditMode/Editor/PromptUGUI.Tests.EditorOnly.asmdef \
        Tests/EditMode/Editor/PromptUGUI.Tests.EditorOnly.asmdef.meta \
        Tests/EditMode/Editor/XsdGeneratorTests.cs \
        Tests/EditMode/Editor/XsdGeneratorTests.cs.meta
git commit -m "test(editor): XsdGenerator coverage — primitives + custom controls"
```

---

## Task 30：主 spec 同步

**Files:**
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1：在 §4.2 改"跨文件"为"任意来源"**

```diff
- 跨文件 `Screen` 同名 → 报错。
- 跨文件 `Template` 同名 → 报错；`as="ns"` 用于消歧（`<ns.TitledPanel/>`）。
+ 跨文件 `Screen` 同名 → 报错。
+ Template 同名（含 commons 与各 Import 的任意组合）→ 报错；`as="ns"` 是唯一显式消歧手段（`<ns.TitledPanel/>`）。
```

- [ ] **Step 2：在 §7.6 改 as= 描述**

```diff
- `as=` 仅在两个 Import 暴露同名 Template 时强制；常态不需要。
+ `as=` 是唯一显式消歧手段；commons 与 Import 多源同名时必填。常态下 Template 名唯一即可省略。
```

- [ ] **Step 3：在 §12 把 M4 行展开为 6 段**

```diff
- | **M4 可用性** | `<Import>` + 跨文件命名空间；编辑器内热重载；XSD 自动生成（IDE 补全） | 大型项目可拆文件协作 |
+ | **M4.1** | `<Import>` parser + 循环检测 + 跨文件 Template 合并 | 单 Screen + 一层 Import 跑通 |
+ | **M4.2** | `LoadCommonLibrary` + 全局 commons 池 + `LoadDocumentFromSrc` + 依赖图 | bootstrap 后 Screen 文件能用 commons 模板 |
+ | **M4.3** | `as="ns"` 命名空间（commons / Import 一致语法） | conflict + namespace 用例覆盖 |
+ | **M4.4** | `UI.Reload` + `UI.ReloadCommonLibrary` + `HotReload.NotifyAssetChanged` | EditMode 测试模拟改文件触发 reload |
+ | **M4.5** | `PromptUGUI.Editor` asmdef + AssetPostprocessor + `UseResourcesResolver` + Sample 迁移 | 真 Editor 内改 .ui.xml 自动 reload |
+ | **M4.6** | XsdGenerator + 菜单 + snapshot 测试 | IDE 内自动补全可工作 |
```

- [ ] **Step 4：commit**

```bash
git add docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "docs(spec): sync §4.2 / §7.6 / §12 with M4 implementation"
```

---

## 总验收 checklist

完成全部 30 任务（Task 27 跳过）后：

- [ ] `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全 PASS
- [ ] `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` 全 PASS
- [ ] `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])` 全 PASS（M3 测试不应受影响）
- [ ] 在宿主项目 `C:\xsoft\PromptUGUIDev` 装 Sample，跑 Task 24 手测 checklist 全部勾上
- [ ] 调 `Tools/PromptUGUI/Generate XSD` 菜单产出 `Assets/PromptUGUI.gen.xsd`，文件可被 VS Code XML 插件读
- [ ] 在 `.ui.xml` 根加 schemaLocation 后，IDE 内自动补全 `<Image sprite=...` 之类生效（手测）
- [ ] `git log --oneline` 看 30 个左右明确小 commit，便于将来 review / revert

---

_Plan 结束。_
