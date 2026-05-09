# PromptUGUI M1 核心实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 跑通"主菜单 + 三按钮 + 点击事件"完整闭环——XML 描述 → uGUI 实例 → R3 事件订阅。

**Architecture:** 分三层：(1) 纯 C# Parser/IR/Layout 计算；(2) Unity 控件层（Control 基类 + 6 内置原语）；(3) 反射缓存的自定义控件注册（`[UIAttr]` + `[Bind]`）。M1 不含 Template/Variant/Import，留给 M2/M3/M4。

**Tech Stack:** Unity 6 (6000.0+), TextMeshPro (含于 com.unity.ugui 2.0+), R3 (Cysharp), NUnit (Unity Test Framework)。

---

## 假设与前置

工程师执行此计划前需要：

1. **Unity 6+** 已安装（Hub 中创建 6000.0+ 项目）
2. **宿主 Unity 项目**（用于在编辑器内测试 PromptUGUI 包）已建立，路径记为 `$HOST_PROJECT`。建议位置：`~/UnityProjects/PromptUGUIDev`
3. **本仓库**已通过 UPM 本地路径依赖加入宿主项目（修改宿主项目 `Packages/manifest.json` 添加 `"com.promptugui.core": "file:../../path/to/PromptUGUI"`）
4. 工作目录始终为 PromptUGUI 仓库根（本计划中所有路径相对此根）

测试运行命令模板（替换 `$UNITY_PATH` 与 `$HOST_PROJECT`）：

```bash
"$UNITY_PATH" -batchmode -nographics -projectPath "$HOST_PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults editmode-results.xml -logFile -
```

PlayMode 测试将 `-testPlatform EditMode` 换为 `-testPlatform PlayMode`。

---

## 文件结构

```
PromptUGUI/                                    # 仓库根
├── package.json                                # Task 1
├── README.md                                   # 已存在
├── LICENSE                                     # 已存在
├── Runtime/
│   ├── PromptUGUI.Runtime.asmdef              # Task 1
│   ├── Core/
│   │   ├── IR/
│   │   │   ├── UIDocument.cs                   # Task 4
│   │   │   ├── ScreenDef.cs                    # Task 4
│   │   │   ├── ElementNode.cs                  # Task 4
│   │   │   └── AnchorPreset.cs                 # Task 10
│   │   ├── Parser/
│   │   │   ├── UIDocumentParser.cs             # Tasks 5-9
│   │   │   └── ParseException.cs               # Task 5
│   │   └── Layout/
│   │       ├── AnchorResolver.cs               # Task 11
│   │       ├── SizeSpec.cs                     # Task 12
│   │       └── MarginResolver.cs               # Task 13
│   ├── Registry/
│   │   ├── UIAttrAttribute.cs                  # Task 14
│   │   ├── BindAttribute.cs                    # Task 14
│   │   ├── ControlMeta.cs                      # Task 15
│   │   └── ControlRegistry.cs                  # Task 17
│   ├── Controls/
│   │   ├── IControl.cs                         # Task 16
│   │   ├── Control.cs                          # Task 16
│   │   ├── Frame.cs                            # Task 18
│   │   ├── Image.cs                            # Task 19
│   │   ├── Text.cs                             # Task 20
│   │   ├── VStack.cs                           # Task 21
│   │   ├── HStack.cs                           # Task 22
│   │   └── Grid.cs                             # Task 23
│   └── Application/
│       ├── UI.cs                               # Task 26
│       ├── Screen.cs                           # Task 25
│       ├── ScreenInstantiator.cs               # Task 24, 31, 32
│       ├── BuiltinPrimitives.cs                # Task 24
│       └── Disposables.cs                      # Task 27
├── Tests/
│   ├── EditMode/
│   │   ├── PromptUGUI.Tests.EditMode.asmdef    # Task 3
│   │   ├── Parser/
│   │   │   └── UIDocumentParserTests.cs        # Tasks 5-9
│   │   ├── Layout/
│   │   │   ├── AnchorResolverTests.cs          # Task 11
│   │   │   ├── SizeSpecTests.cs                # Task 12
│   │   │   └── MarginResolverTests.cs          # Task 13
│   │   └── Registry/
│   │       └── ControlMetaTests.cs             # Task 15
│   └── PlayMode/
│       ├── PromptUGUI.Tests.PlayMode.asmdef    # Task 3
│       ├── Controls/
│       │   ├── FrameTests.cs                   # Task 18
│       │   ├── ImageTests.cs                   # Task 19
│       │   ├── TextTests.cs                    # Task 20
│       │   ├── VStackTests.cs                  # Task 21
│       │   ├── HStackTests.cs                  # Task 22
│       │   └── GridTests.cs                    # Task 23
│       ├── Lifecycle/
│       │   └── ScreenLifecycleTests.cs         # Tasks 24-27
│       └── E2E/
│           └── MainMenuDemoTests.cs            # Task 30
└── Samples~/
    └── MainMenu/                                # Task 28-29
        ├── MainMenuRunner.cs
        ├── PrimaryButton.cs
        ├── DangerButton.cs
        ├── MainMenu.ui.xml
        └── Resources/
            └── UI/
                ├── PrimaryButton.prefab         # 手工创建
                └── DangerButton.prefab          # 手工创建
```

**命名空间**：根 `PromptUGUI`；子 `PromptUGUI.IR / Parser / Layout / Registry / Controls / Application`。

**Class 命名注意**：`Image`/`Text`/`Screen` 与 `UnityEngine.UI.*` 重名；用户代码中需要 `using PromptUGUI.Controls;` 并避免 `using UnityEngine.UI;`。包内代码全部用全限定 `UnityEngine.UI.Image` 防混淆。

---

## Task 1：包骨架 & asmdef

**Files:**
- Create: `package.json`
- Create: `Runtime/PromptUGUI.Runtime.asmdef`
- Create: `Runtime/.gitkeep`（确保空目录入 git）

- [ ] **Step 1: 创建 package.json**

```json
{
  "name": "com.promptugui.core",
  "version": "0.1.0",
  "displayName": "PromptUGUI",
  "description": "XML-driven uGUI for Unity 6+",
  "unity": "6000.0",
  "dependencies": {
    "com.unity.ugui": "2.0.0"
  },
  "samples": [
    {
      "displayName": "Main Menu Demo",
      "description": "Hello-world: 主菜单 + 三按钮 + R3 点击订阅",
      "path": "Samples~/MainMenu"
    }
  ]
}
```

R3 的依赖留到 Task 2 单独加，便于回滚。

- [ ] **Step 2: 创建 Runtime asmdef**

`Runtime/PromptUGUI.Runtime.asmdef`：

```json
{
  "name": "PromptUGUI.Runtime",
  "rootNamespace": "PromptUGUI",
  "references": [
    "Unity.TextMeshPro",
    "UnityEngine.UI"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 3: 在宿主 Unity 项目 manifest.json 中加本地引用**

工程师手动操作宿主项目 `Packages/manifest.json`：

```json
"dependencies": {
  "com.promptugui.core": "file:../../path/to/PromptUGUI",
  ...
}
```

- [ ] **Step 4: 在 Unity Editor 中验证包加载成功**

打开宿主项目，检查 Package Manager → In Project 中能看到 PromptUGUI。Console 不应有红字。

- [ ] **Step 5: Commit**

```bash
git add package.json Runtime/PromptUGUI.Runtime.asmdef
git commit -m "feat: package skeleton + Runtime asmdef"
```

---

## Task 2：添加 R3 依赖

**Files:**
- Modify: `package.json`
- Modify: `Runtime/PromptUGUI.Runtime.asmdef`

- [ ] **Step 1: 检查 R3 当前推荐安装方式**

访问 https://github.com/Cysharp/R3 README，找到 Unity 安装说明。当前（2026-05）推荐：先装 NuGetForUnity，再 NuGet 装 `R3`，并为 Unity 加 `R3.Unity` 桥接包。

- [ ] **Step 2: 在宿主项目按 R3 README 步骤安装 R3**

工程师按 R3 README 执行；不在本仓库 package.json 内 hard-code（R3 安装方式可能演化）。

- [ ] **Step 3: 在 PromptUGUI.Runtime.asmdef 加 R3 引用**

```json
{
  "name": "PromptUGUI.Runtime",
  "rootNamespace": "PromptUGUI",
  "references": [
    "Unity.TextMeshPro",
    "UnityEngine.UI",
    "R3",
    "R3.Unity"
  ],
  ...
}
```

- [ ] **Step 4: 在 Runtime 下加一个验证文件**

`Runtime/_R3SmokeTest.cs`：

```csharp
namespace PromptUGUI {
    internal static class _R3SmokeTest {
        public static void TouchR3() {
            var rp = new R3.ReactiveProperty<int>(0);
            rp.Dispose();
        }
    }
}
```

- [ ] **Step 5: 在 Unity 中确认编译通过**

打开宿主项目，等待编译。Console 无红字 = R3 接入成功。

- [ ] **Step 6: 删除 smoke test 文件**

```bash
rm Runtime/_R3SmokeTest.cs
```

- [ ] **Step 7: Commit**

```bash
git add Runtime/PromptUGUI.Runtime.asmdef
git commit -m "feat: wire R3 reference into Runtime asmdef"
```

---

## Task 3：测试 asmdef 与 trivial 测试

**Files:**
- Create: `Tests/EditMode/PromptUGUI.Tests.EditMode.asmdef`
- Create: `Tests/EditMode/SmokeTests.cs`
- Create: `Tests/PlayMode/PromptUGUI.Tests.PlayMode.asmdef`
- Create: `Tests/PlayMode/SmokeTests.cs`

- [ ] **Step 1: 创建 EditMode 测试 asmdef**

`Tests/EditMode/PromptUGUI.Tests.EditMode.asmdef`：

```json
{
  "name": "PromptUGUI.Tests.EditMode",
  "rootNamespace": "PromptUGUI.Tests",
  "references": [
    "PromptUGUI.Runtime",
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
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: 创建 EditMode smoke 测试**

`Tests/EditMode/SmokeTests.cs`：

```csharp
using NUnit.Framework;

namespace PromptUGUI.Tests {
    public class SmokeTests {
        [Test]
        public void EditMode_assembly_loads() {
            Assert.Pass();
        }
    }
}
```

- [ ] **Step 3: 创建 PlayMode 测试 asmdef**

`Tests/PlayMode/PromptUGUI.Tests.PlayMode.asmdef`：

```json
{
  "name": "PromptUGUI.Tests.PlayMode",
  "rootNamespace": "PromptUGUI.Tests",
  "references": [
    "PromptUGUI.Runtime",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "R3",
    "R3.Unity"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [
    "nunit.framework.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 4: 创建 PlayMode smoke 测试**

`Tests/PlayMode/SmokeTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests {
    public class PlayModeSmokeTests {
        [UnityTest]
        public IEnumerator PlayMode_assembly_loads() {
            yield return null;
            Assert.Pass();
        }
    }
}
```

- [ ] **Step 5: 在 Unity Editor 的 Test Runner 中跑一次两类测试**

预期：两个测试都 PASS。

- [ ] **Step 6: 跑 CLI 验证 EditMode（设置 $UNITY_PATH 与 $HOST_PROJECT）**

```bash
"$UNITY_PATH" -batchmode -nographics -projectPath "$HOST_PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults editmode-results.xml -logFile - 2>&1 | tail -30
```

预期：log 末尾显示 `Tests: 1 passed, 0 failed`。

- [ ] **Step 7: Commit**

```bash
git add Tests/EditMode Tests/PlayMode
git commit -m "test: scaffold EditMode + PlayMode test assemblies with smoke tests"
```

---

## Task 4：IR 数据结构

**Files:**
- Create: `Runtime/Core/IR/UIDocument.cs`
- Create: `Runtime/Core/IR/ScreenDef.cs`
- Create: `Runtime/Core/IR/ElementNode.cs`

无独立测试——这些类是数据载体，Task 5 起的 parser 测试覆盖它们。

- [ ] **Step 1: 实现 ElementNode**

`Runtime/Core/IR/ElementNode.cs`：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class ElementNode {
        public string Tag { get; }
        public string Id { get; set; }
        public Dictionary<string, string> Attributes { get; }
        public string TextContent { get; set; }
        public List<ElementNode> Children { get; }

        public ElementNode(string tag) {
            Tag = tag;
            Attributes = new Dictionary<string, string>();
            Children = new List<ElementNode>();
        }
    }
}
```

- [ ] **Step 2: 实现 ScreenDef**

`Runtime/Core/IR/ScreenDef.cs`：

```csharp
namespace PromptUGUI.IR {
    public sealed class ScreenDef {
        public string Name { get; }
        public ElementNode Root { get; }

        public ScreenDef(string name, ElementNode root) {
            Name = name;
            Root = root;
        }
    }
}
```

- [ ] **Step 3: 实现 UIDocument**

`Runtime/Core/IR/UIDocument.cs`：

```csharp
using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class UIDocument {
        public int Version { get; set; } = 1;
        public List<ScreenDef> Screens { get; } = new();
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add Runtime/Core/IR
git commit -m "feat(ir): add UIDocument / ScreenDef / ElementNode data classes"
```

---

## Task 5：Parser — 最小 `<UI><Screen/></UI>`

**Files:**
- Create: `Runtime/Core/Parser/ParseException.cs`
- Create: `Runtime/Core/Parser/UIDocumentParser.cs`
- Create: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

- [ ] **Step 1: 写最小测试**

`Tests/EditMode/Parser/UIDocumentParserTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class UIDocumentParserTests {
        [Test]
        public void Parses_minimal_document_with_one_screen() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='MainMenu' />
                </UI>";

            var doc = UIDocumentParser.Parse(xml);

            Assert.AreEqual(1, doc.Version);
            Assert.AreEqual(1, doc.Screens.Count);
            Assert.AreEqual("MainMenu", doc.Screens[0].Name);
            Assert.IsNotNull(doc.Screens[0].Root);
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认 FAIL**

预期：编译错误 `UIDocumentParser` 不存在。

- [ ] **Step 3: 实现 ParseException**

`Runtime/Core/Parser/ParseException.cs`：

```csharp
using System;

namespace PromptUGUI.Parser {
    public sealed class ParseException : Exception {
        public ParseException(string message) : base(message) { }
    }
}
```

- [ ] **Step 4: 实现 UIDocumentParser 最小版本**

`Runtime/Core/Parser/UIDocumentParser.cs`：

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

            foreach (XmlNode child in root.ChildNodes) {
                if (child is not XmlElement el) continue;
                if (el.Name == "Screen") {
                    var name = el.GetAttribute("name");
                    if (string.IsNullOrEmpty(name))
                        throw new ParseException("<Screen> requires name attribute");
                    doc.Screens.Add(new ScreenDef(name, new ElementNode("__screen_root__")));
                }
            }

            return doc;
        }
    }
}
```

- [ ] **Step 5: 运行测试，确认 PASS**

```bash
"$UNITY_PATH" -batchmode -nographics -projectPath "$HOST_PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults editmode-results.xml -logFile - 2>&1 | tail -10
```

- [ ] **Step 6: Commit**

```bash
git add Runtime/Core/Parser Tests/EditMode/Parser
git commit -m "feat(parser): minimal <UI><Screen/></UI> parsing"
```

---

## Task 6：Parser — 元素树与属性

**Files:**
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

- [ ] **Step 1: 添加测试覆盖嵌套元素与属性**

追加到 `UIDocumentParserTests.cs`：

```csharp
        [Test]
        public void Parses_nested_elements_with_attributes() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='X'>
                        <VStack anchor='center' size='480x320' spacing='12'>
                            <Image sprite='bg' anchor='stretch' />
                            <Text>Hello</Text>
                        </VStack>
                    </Screen>
                </UI>";

            var doc = UIDocumentParser.Parse(xml);
            var root = doc.Screens[0].Root;

            Assert.AreEqual(1, root.Children.Count);
            var vstack = root.Children[0];
            Assert.AreEqual("VStack", vstack.Tag);
            Assert.AreEqual("center", vstack.Attributes["anchor"]);
            Assert.AreEqual("480x320", vstack.Attributes["size"]);
            Assert.AreEqual("12", vstack.Attributes["spacing"]);

            Assert.AreEqual(2, vstack.Children.Count);
            Assert.AreEqual("Image", vstack.Children[0].Tag);
            Assert.AreEqual("bg", vstack.Children[0].Attributes["sprite"]);

            Assert.AreEqual("Text", vstack.Children[1].Tag);
            Assert.AreEqual("Hello", vstack.Children[1].TextContent);
        }
```

- [ ] **Step 2: 运行测试，确认 FAIL（children 为空）**

- [ ] **Step 3: 替换 Parse 方法以支持递归元素与属性**

`Runtime/Core/Parser/UIDocumentParser.cs` 完整替换：

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

            foreach (XmlNode child in root.ChildNodes) {
                if (child is not XmlElement el) continue;
                if (el.Name == "Screen") {
                    var name = el.GetAttribute("name");
                    if (string.IsNullOrEmpty(name))
                        throw new ParseException("<Screen> requires name attribute");
                    var rootNode = new ElementNode("__screen_root__");
                    foreach (XmlNode c in el.ChildNodes)
                        if (c is XmlElement child_el)
                            rootNode.Children.Add(ParseElement(child_el));
                    doc.Screens.Add(new ScreenDef(name, rootNode));
                }
            }

            return doc;
        }

        static ElementNode ParseElement(XmlElement el) {
            var node = new ElementNode(el.Name);

            foreach (XmlAttribute attr in el.Attributes) {
                if (attr.Name == "id") node.Id = attr.Value;
                else node.Attributes[attr.Name] = attr.Value;
            }

            // 文本内容简写：仅当唯一子节点是 text node 时算
            if (el.ChildNodes.Count == 1 && el.FirstChild is XmlText t)
                node.TextContent = t.Value;

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el));

            return node;
        }
    }
}
```

- [ ] **Step 4: 运行所有 parser 测试，确认全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser/UIDocumentParser.cs Tests/EditMode/Parser/UIDocumentParserTests.cs
git commit -m "feat(parser): recursive element tree with attributes and text content"
```

---

## Task 7：Parser — id 属性提升 + 重复检测

**Files:**
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`

- [ ] **Step 1: 加 id 测试**

追加到 `UIDocumentParserTests.cs`：

```csharp
        [Test]
        public void Lifts_id_attribute_to_dedicated_field() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='X'>
                        <Image id='bg' sprite='main' />
                    </Screen>
                </UI>";

            var doc = UIDocumentParser.Parse(xml);
            var img = doc.Screens[0].Root.Children[0];

            Assert.AreEqual("bg", img.Id);
            Assert.IsFalse(img.Attributes.ContainsKey("id"),
                "id should be lifted, not stored in Attributes dict");
        }

        [Test]
        public void Throws_on_duplicate_id_within_same_screen() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='X'>
                        <Image id='dup' />
                        <Frame>
                            <Image id='dup' />
                        </Frame>
                    </Screen>
                </UI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_screen_name() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='Same' />
                    <Screen name='Same' />
                </UI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
```

- [ ] **Step 2: 跑测试，确认前一项 PASS（已实现），后两项 FAIL**

- [ ] **Step 3: 在 Parse 方法增加重复检测**

修改 `Parse` 方法主体，screen 循环和 element 解析都加 id 集合：

```csharp
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
                if (el.Name == "Screen") {
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
            }

            return doc;
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

            if (el.ChildNodes.Count == 1 && el.FirstChild is XmlText t)
                node.TextContent = t.Value;

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el, idsInScope));

            return node;
        }
```

- [ ] **Step 4: 跑测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser Tests/EditMode/Parser
git commit -m "feat(parser): lift id to field; detect duplicate ids and screens"
```

---

## Task 8：Parser — 错误路径覆盖

**Files:**
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`

无生产代码改动——只补错误测试，验证 Parser 正确报错。

- [ ] **Step 1: 加错误覆盖测试**

```csharp
        [Test]
        public void Throws_on_missing_root_UI() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<Screen name='X' />"));
        }

        [Test]
        public void Throws_on_missing_UI_version() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<UI><Screen name='X' /></UI>"));
        }

        [Test]
        public void Throws_on_screen_without_name() {
            const string xml = "<UI version='1'><Screen /></UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_invalid_xml() {
            Assert.Throws<System.Xml.XmlException>(() =>
                UIDocumentParser.Parse("<UI version='1'><Screen></UI>"));
        }
```

- [ ] **Step 2: 跑测试，全 PASS**

- [ ] **Step 3: Commit**

```bash
git add Tests/EditMode/Parser
git commit -m "test(parser): cover invalid root / missing version / unclosed XML"
```

---

## Task 9：Parser — 文本内容简写规则收紧

**Files:**
- Modify: `Tests/EditMode/Parser/UIDocumentParserTests.cs`
- Modify: `Runtime/Core/Parser/UIDocumentParser.cs`

文本简写仅当元素**只有一个 text 子节点**时生效；混合时报错。

- [ ] **Step 1: 加测试**

```csharp
        [Test]
        public void Text_shorthand_works_when_only_text_child() {
            var doc = UIDocumentParser.Parse(
                "<UI version='1'><Screen name='X'><Text>Hello</Text></Screen></UI>");
            Assert.AreEqual("Hello", doc.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Text_shorthand_with_whitespace_is_trimmed() {
            var doc = UIDocumentParser.Parse(
                "<UI version='1'><Screen name='X'><Text>  Hello  </Text></Screen></UI>");
            Assert.AreEqual("Hello", doc.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Text_shorthand_disallowed_when_mixed_with_elements() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Btn>Hello <Image /></Btn>
                </Screen></UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
```

- [ ] **Step 2: 跑测试，第三个 FAIL（当前未拒绝混合）**

- [ ] **Step 3: 替换 ParseElement 中文本块**

```csharp
            // 文本简写：仅当所有非空白子节点都是 text 时生效
            bool hasElement = false, hasText = false;
            foreach (XmlNode c in el.ChildNodes) {
                if (c is XmlElement) hasElement = true;
                else if (c is XmlText txt && !string.IsNullOrWhiteSpace(txt.Value)) hasText = true;
            }
            if (hasText && hasElement)
                throw new ParseException(
                    $"<{el.Name}> mixes text and child elements; not allowed");
            if (hasText && !hasElement) {
                node.TextContent = el.InnerText.Trim();
            }
```

将原来的 `if (el.ChildNodes.Count == 1 && el.FirstChild is XmlText t)` 块替换为上面这段。

- [ ] **Step 4: 跑全部 parser 测试，确认 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Parser Tests/EditMode/Parser
git commit -m "feat(parser): tighten text-content shorthand rules"
```

---

## Task 10：AnchorPreset 枚举与解析

**Files:**
- Create: `Runtime/Core/IR/AnchorPreset.cs`
- Create: `Tests/EditMode/Layout/AnchorResolverTests.cs`（先建文件备用）

- [ ] **Step 1: 实现 AnchorPreset enum 与字符串解析**

`Runtime/Core/IR/AnchorPreset.cs`：

```csharp
using System;

namespace PromptUGUI.IR {
    public enum AnchorVertical   { Top, Center, Bottom, Stretch }
    public enum AnchorHorizontal { Left, Center, Right, Stretch }

    public readonly struct AnchorPreset : IEquatable<AnchorPreset> {
        public AnchorVertical V { get; }
        public AnchorHorizontal H { get; }

        public AnchorPreset(AnchorVertical v, AnchorHorizontal h) { V = v; H = h; }

        public bool StretchX => H == AnchorHorizontal.Stretch;
        public bool StretchY => V == AnchorVertical.Stretch;

        public static AnchorPreset Parse(string s) {
            if (string.IsNullOrEmpty(s))
                throw new ArgumentException("anchor cannot be empty");

            // 别名
            switch (s) {
                case "center":  return new AnchorPreset(AnchorVertical.Center, AnchorHorizontal.Center);
                case "stretch":
                case "fill":    return new AnchorPreset(AnchorVertical.Stretch, AnchorHorizontal.Stretch);
            }

            var dash = s.IndexOf('-');
            if (dash < 1 || dash == s.Length - 1)
                throw new ArgumentException($"anchor '{s}' must be '<v>-<h>'");

            var v = ParseV(s.Substring(0, dash));
            var h = ParseH(s.Substring(dash + 1));
            return new AnchorPreset(v, h);
        }

        static AnchorVertical ParseV(string s) => s switch {
            "top"     => AnchorVertical.Top,
            "center"  => AnchorVertical.Center,
            "bottom"  => AnchorVertical.Bottom,
            "stretch" => AnchorVertical.Stretch,
            _         => throw new ArgumentException($"invalid vertical '{s}'")
        };

        static AnchorHorizontal ParseH(string s) => s switch {
            "left"    => AnchorHorizontal.Left,
            "center"  => AnchorHorizontal.Center,
            "right"   => AnchorHorizontal.Right,
            "stretch" => AnchorHorizontal.Stretch,
            _         => throw new ArgumentException($"invalid horizontal '{s}'")
        };

        public bool Equals(AnchorPreset o) => V == o.V && H == o.H;
        public override bool Equals(object o) => o is AnchorPreset p && Equals(p);
        public override int GetHashCode() => ((int)V * 4) + (int)H;
    }
}
```

- [ ] **Step 2: 写解析测试（仅 enum 形态，不涉及 RectTransform）**

`Tests/EditMode/Layout/AnchorResolverTests.cs`（暂只测 Parse；resolver 在 Task 11 加）：

```csharp
using NUnit.Framework;
using PromptUGUI.IR;

namespace PromptUGUI.Tests.Layout {
    public class AnchorPresetTests {
        [TestCase("top-left",       AnchorVertical.Top,    AnchorHorizontal.Left)]
        [TestCase("bottom-right",   AnchorVertical.Bottom, AnchorHorizontal.Right)]
        [TestCase("stretch-left",   AnchorVertical.Stretch,AnchorHorizontal.Left)]
        [TestCase("top-stretch",    AnchorVertical.Top,    AnchorHorizontal.Stretch)]
        public void Parses_v_h_form(string s, AnchorVertical v, AnchorHorizontal h) {
            var a = AnchorPreset.Parse(s);
            Assert.AreEqual(v, a.V);
            Assert.AreEqual(h, a.H);
        }

        [Test]
        public void Center_alias_expands_to_center_center() {
            var a = AnchorPreset.Parse("center");
            Assert.AreEqual(AnchorVertical.Center, a.V);
            Assert.AreEqual(AnchorHorizontal.Center, a.H);
        }

        [Test]
        public void Stretch_alias_expands_to_stretch_stretch() {
            var a = AnchorPreset.Parse("stretch");
            Assert.AreEqual(AnchorVertical.Stretch, a.V);
            Assert.AreEqual(AnchorHorizontal.Stretch, a.H);
        }

        [Test]
        public void Fill_alias_equals_stretch() {
            Assert.AreEqual(AnchorPreset.Parse("stretch"), AnchorPreset.Parse("fill"));
        }

        [TestCase("")]
        [TestCase("topleft")]      // no dash
        [TestCase("up-left")]      // bad vertical
        [TestCase("top-middle")]   // bad horizontal
        public void Throws_on_invalid(string bad) {
            Assert.Throws<System.ArgumentException>(() => AnchorPreset.Parse(bad));
        }
    }
}
```

- [ ] **Step 3: 跑测试，全 PASS**

- [ ] **Step 4: Commit**

```bash
git add Runtime/Core/IR/AnchorPreset.cs Tests/EditMode/Layout
git commit -m "feat(ir): AnchorPreset enum + Parse with aliases"
```

---

## Task 11：AnchorResolver — 预设 → anchorMin/Max + pivot

**Files:**
- Create: `Runtime/Core/Layout/AnchorResolver.cs`
- Modify: `Tests/EditMode/Layout/AnchorResolverTests.cs`

- [ ] **Step 1: 在测试文件追加 resolver 测试**

```csharp
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Tests.Layout {
    public class AnchorResolverTests {
        [TestCase(AnchorVertical.Top,     AnchorHorizontal.Left,    0f, 1f, 0f, 1f, 0f, 1f)]
        [TestCase(AnchorVertical.Top,     AnchorHorizontal.Right,   1f, 1f, 1f, 1f, 1f, 1f)]
        [TestCase(AnchorVertical.Bottom,  AnchorHorizontal.Center,  0.5f, 0f, 0.5f, 0f, 0.5f, 0f)]
        [TestCase(AnchorVertical.Center,  AnchorHorizontal.Center,  0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f)]
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Stretch, 0f, 0f, 1f, 1f, 0.5f, 0.5f)]
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Left,    0f, 0f, 0f, 1f, 0f, 0.5f)]
        [TestCase(AnchorVertical.Top,     AnchorHorizontal.Stretch, 0f, 1f, 1f, 1f, 0.5f, 1f)]
        public void Resolves_anchor_min_max_and_pivot(
            AnchorVertical v, AnchorHorizontal h,
            float minX, float minY, float maxX, float maxY, float pivotX, float pivotY) {
            var preset = new AnchorPreset(v, h);
            AnchorResolver.Resolve(preset,
                out var min, out var max, out var pivot);
            Assert.AreEqual(new Vector2(minX, minY), min);
            Assert.AreEqual(new Vector2(maxX, maxY), max);
            Assert.AreEqual(new Vector2(pivotX, pivotY), pivot);
        }
    }
}
```

注意原来的 `AnchorPresetTests` 类保留；这是新增另一类 `AnchorResolverTests` 在同一文件下。

- [ ] **Step 2: 跑测试，确认 FAIL（AnchorResolver 不存在）**

- [ ] **Step 3: 实现 AnchorResolver**

`Runtime/Core/Layout/AnchorResolver.cs`：

```csharp
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout {
    public static class AnchorResolver {
        public static void Resolve(
            AnchorPreset preset,
            out Vector2 anchorMin, out Vector2 anchorMax, out Vector2 pivot) {
            float xMin, xMax, pivotX;
            switch (preset.H) {
                case AnchorHorizontal.Left:    xMin = 0f;   xMax = 0f;   pivotX = 0f;   break;
                case AnchorHorizontal.Center:  xMin = 0.5f; xMax = 0.5f; pivotX = 0.5f; break;
                case AnchorHorizontal.Right:   xMin = 1f;   xMax = 1f;   pivotX = 1f;   break;
                case AnchorHorizontal.Stretch: xMin = 0f;   xMax = 1f;   pivotX = 0.5f; break;
                default: throw new System.ArgumentOutOfRangeException();
            }

            float yMin, yMax, pivotY;
            switch (preset.V) {
                case AnchorVertical.Bottom:    yMin = 0f;   yMax = 0f;   pivotY = 0f;   break;
                case AnchorVertical.Center:    yMin = 0.5f; yMax = 0.5f; pivotY = 0.5f; break;
                case AnchorVertical.Top:       yMin = 1f;   yMax = 1f;   pivotY = 1f;   break;
                case AnchorVertical.Stretch:   yMin = 0f;   yMax = 1f;   pivotY = 0.5f; break;
                default: throw new System.ArgumentOutOfRangeException();
            }

            anchorMin = new Vector2(xMin, yMin);
            anchorMax = new Vector2(xMax, yMax);
            pivot     = new Vector2(pivotX, pivotY);
        }
    }
}
```

- [ ] **Step 4: 跑测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Layout/AnchorResolver.cs Tests/EditMode/Layout
git commit -m "feat(layout): AnchorResolver maps preset → anchorMin/Max + pivot"
```

---

## Task 12：SizeSpec — 解析与拉伸轴严格性

**Files:**
- Create: `Runtime/Core/Layout/SizeSpec.cs`
- Create: `Tests/EditMode/Layout/SizeSpecTests.cs`

- [ ] **Step 1: 写测试**

`Tests/EditMode/Layout/SizeSpecTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.Layout {
    public class SizeSpecTests {
        [Test]
        public void Parses_WxH() {
            var s = SizeSpec.Parse(size: "240x80", width: null, height: null);
            Assert.AreEqual(240f, s.Width);
            Assert.AreEqual(80f, s.Height);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
        }

        [Test]
        public void Parses_width_only() {
            var s = SizeSpec.Parse(size: null, width: "200", height: null);
            Assert.AreEqual(200f, s.Width);
            Assert.IsTrue(s.HasWidth);
            Assert.IsFalse(s.HasHeight);
        }

        [Test]
        public void Parses_height_only() {
            var s = SizeSpec.Parse(size: null, width: null, height: "64");
            Assert.AreEqual(64f, s.Height);
            Assert.IsFalse(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
        }

        [Test]
        public void Empty_when_all_null() {
            var s = SizeSpec.Parse(null, null, null);
            Assert.IsFalse(s.HasWidth);
            Assert.IsFalse(s.HasHeight);
        }

        [TestCase(AnchorVertical.Top, AnchorHorizontal.Stretch, "240x80", null, null)]      // size 含 width 而水平拉伸
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Left, "240x80", null, null)]    // size 含 height 而竖向拉伸
        [TestCase(AnchorVertical.Top, AnchorHorizontal.Stretch, null, "200", null)]        // width 与水平拉伸
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Left, null, null, "64")]        // height 与竖向拉伸
        public void Throws_when_specifying_size_on_stretched_axis(
            AnchorVertical v, AnchorHorizontal h,
            string size, string width, string height) {
            var spec = SizeSpec.Parse(size, width, height);
            var anchor = new AnchorPreset(v, h);
            Assert.Throws<System.ArgumentException>(() =>
                spec.ValidateAgainst(anchor));
        }

        [TestCase("WxH",   true)]
        [TestCase("100x",  true)]
        [TestCase("x100",  true)]
        [TestCase("100",   true)]    // size 必须含 'x'
        public void Throws_on_malformed_size(string bad, bool expectThrow) {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(bad, null, null));
        }
    }
}
```

- [ ] **Step 2: 跑测试，确认 FAIL**

- [ ] **Step 3: 实现 SizeSpec**

`Runtime/Core/Layout/SizeSpec.cs`：

```csharp
using System;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Layout {
    public readonly struct SizeSpec {
        public float Width  { get; }
        public float Height { get; }
        public bool  HasWidth  { get; }
        public bool  HasHeight { get; }

        SizeSpec(float w, float h, bool hw, bool hh) {
            Width = w; Height = h; HasWidth = hw; HasHeight = hh;
        }

        public static SizeSpec Parse(string size, string width, string height) {
            float w = 0f, h = 0f;
            bool hw = false, hh = false;

            if (!string.IsNullOrEmpty(size)) {
                var x = size.IndexOf('x');
                if (x <= 0 || x == size.Length - 1)
                    throw new ArgumentException($"size '{size}' must be 'WxH'");
                w = float.Parse(size.Substring(0, x), CultureInfo.InvariantCulture);
                h = float.Parse(size.Substring(x + 1), CultureInfo.InvariantCulture);
                hw = hh = true;
            }

            if (!string.IsNullOrEmpty(width)) {
                if (hw) throw new ArgumentException("cannot specify both size and width");
                w = float.Parse(width, CultureInfo.InvariantCulture);
                hw = true;
            }

            if (!string.IsNullOrEmpty(height)) {
                if (hh) throw new ArgumentException("cannot specify both size and height");
                h = float.Parse(height, CultureInfo.InvariantCulture);
                hh = true;
            }

            return new SizeSpec(w, h, hw, hh);
        }

        public void ValidateAgainst(AnchorPreset anchor) {
            if (anchor.StretchX && HasWidth)
                throw new ArgumentException(
                    "cannot specify width/size on a horizontally-stretched axis");
            if (anchor.StretchY && HasHeight)
                throw new ArgumentException(
                    "cannot specify height/size on a vertically-stretched axis");
        }
    }
}
```

- [ ] **Step 4: 跑测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Layout/SizeSpec.cs Tests/EditMode/Layout/SizeSpecTests.cs
git commit -m "feat(layout): SizeSpec parsing + strict validation against stretched axis"
```

---

## Task 13：MarginResolver — anchor + size + margin → RectTransform 数

**Files:**
- Create: `Runtime/Core/Layout/MarginResolver.cs`
- Create: `Tests/EditMode/Layout/MarginResolverTests.cs`

`MarginResolver` 接收 anchor、size、margin 字符串，输出 anchoredPosition 与 sizeDelta（点锚情况）或 offsetMin/offsetMax（拉伸情况）。

为简化，统一返回 `(anchoredPosition, sizeDelta)`，运行时再根据需要计算 offsetMin/Max——uGUI 内部会自动处理。

- [ ] **Step 1: 写测试**

`Tests/EditMode/Layout/MarginResolverTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Tests.Layout {
    public class MarginResolverTests {

        [Test]
        public void Top_right_with_margin_16_and_size_240x80() {
            // 锚点 (1,1)，pivot (1,1)，margin 16 → 中心位置离 (1,1) 内 16
            // anchoredPosition = (-16, -16)；sizeDelta = (240, 80)
            var anchor = AnchorPreset.Parse("top-right");
            var size   = SizeSpec.Parse("240x80", null, null);
            var r = MarginResolver.Resolve(anchor, size, "16");

            Assert.AreEqual(new Vector2(-16, -16), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(240, 80), r.SizeDelta);
        }

        [Test]
        public void Top_left_with_margin_16_8() {
            // top-left: anchor (0,1), pivot (0,1)
            // margin V=16, H=8 → 内向移动: x=+8, y=-16
            var anchor = AnchorPreset.Parse("top-left");
            var size   = SizeSpec.Parse("240x80", null, null);
            var r = MarginResolver.Resolve(anchor, size, "16,8");

            Assert.AreEqual(new Vector2(8, -16), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(240, 80), r.SizeDelta);
        }

        [Test]
        public void Center_with_no_margin() {
            var anchor = AnchorPreset.Parse("center");
            var size   = SizeSpec.Parse("400x300", null, null);
            var r = MarginResolver.Resolve(anchor, size, null);

            Assert.AreEqual(new Vector2(0, 0), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(400, 300), r.SizeDelta);
        }

        [Test]
        public void Top_stretch_with_height_64_and_horizontal_margin_8() {
            // 水平拉伸：sizeDelta.x = -(left+right) = -16
            // 竖向 anchor=top: sizeDelta.y = 64
            // anchoredPosition.x = (left-right)/2 = 0
            // anchoredPosition.y = -margin.top = 0
            var anchor = AnchorPreset.Parse("top-stretch");
            var size   = SizeSpec.Parse(null, null, "64");
            var r = MarginResolver.Resolve(anchor, size, "0,8,_,8");

            Assert.AreEqual(new Vector2(0, 0), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(-16, 64), r.SizeDelta);
        }

        [Test]
        public void Stretch_all_with_margin_0() {
            var anchor = AnchorPreset.Parse("stretch");
            var size   = SizeSpec.Parse(null, null, null);
            var r = MarginResolver.Resolve(anchor, size, null);

            Assert.AreEqual(new Vector2(0, 0), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(0, 0), r.SizeDelta);
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 MarginResolver**

`Runtime/Core/Layout/MarginResolver.cs`：

```csharp
using System;
using System.Globalization;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout {
    public readonly struct LayoutResult {
        public Vector2 AnchoredPosition { get; }
        public Vector2 SizeDelta        { get; }
        public LayoutResult(Vector2 pos, Vector2 size) {
            AnchoredPosition = pos; SizeDelta = size;
        }
    }

    public static class MarginResolver {

        public static LayoutResult Resolve(AnchorPreset anchor, SizeSpec size, string margin) {
            ParseMargin(margin, out float t, out float r, out float b, out float l);

            float anchorX, anchorY;
            float sizeX, sizeY;

            // X 轴
            if (anchor.StretchX) {
                sizeX   = -(l + r);
                anchorX = (l - r) * 0.5f;
            } else {
                sizeX = size.HasWidth ? size.Width : 0f;
                // 内向位移：左锚 = +l，右锚 = -r，中心 = 0
                anchorX = anchor.H switch {
                    AnchorHorizontal.Left   =>  l,
                    AnchorHorizontal.Right  => -r,
                    AnchorHorizontal.Center =>  0f,
                    _ => 0f,
                };
            }

            // Y 轴
            if (anchor.StretchY) {
                sizeY   = -(t + b);
                anchorY = (b - t) * 0.5f;
            } else {
                sizeY = size.HasHeight ? size.Height : 0f;
                anchorY = anchor.V switch {
                    AnchorVertical.Bottom =>  b,
                    AnchorVertical.Top    => -t,
                    AnchorVertical.Center =>  0f,
                    _ => 0f,
                };
            }

            return new LayoutResult(
                new Vector2(anchorX, anchorY),
                new Vector2(sizeX,   sizeY));
        }

        static void ParseMargin(string s, out float t, out float r, out float b, out float l) {
            t = r = b = l = 0f;
            if (string.IsNullOrEmpty(s)) return;

            var parts = s.Split(',');
            float[] vals = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) {
                var p = parts[i].Trim();
                vals[i] = (p == "_" || p == "") ? 0f
                    : float.Parse(p, CultureInfo.InvariantCulture);
            }

            switch (parts.Length) {
                case 1: t = r = b = l = vals[0]; return;
                case 2: t = b = vals[0]; r = l = vals[1]; return;
                case 4: t = vals[0]; r = vals[1]; b = vals[2]; l = vals[3]; return;
                default:
                    throw new ArgumentException(
                        $"margin '{s}' must have 1, 2, or 4 components");
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Core/Layout/MarginResolver.cs Tests/EditMode/Layout/MarginResolverTests.cs
git commit -m "feat(layout): MarginResolver computes RectTransform anchoredPosition + sizeDelta"
```

---

## Task 14：标记属性 UIAttr / Bind

**Files:**
- Create: `Runtime/Registry/UIAttrAttribute.cs`
- Create: `Runtime/Registry/BindAttribute.cs`

无独立测试——由 Task 15 ControlMeta 测试覆盖。

- [ ] **Step 1: 实现 UIAttrAttribute**

`Runtime/Registry/UIAttrAttribute.cs`：

```csharp
using System;

namespace PromptUGUI.Registry {
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UIAttrAttribute : Attribute {
        public string Name { get; }
        public UIAttrAttribute(string name = null) { Name = name; }
    }
}
```

`Name = null` 时使用属性 PascalCase → camelCase 后的同名（在 ControlMeta 处理）。

- [ ] **Step 2: 实现 BindAttribute**

`Runtime/Registry/BindAttribute.cs`：

```csharp
using System;

namespace PromptUGUI.Registry {
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class BindAttribute : Attribute {
        public string ChildName { get; }
        public BindAttribute(string childName = null) { ChildName = childName; }
    }
}
```

`ChildName = null` 时按字段名（去 `_` 前缀，PascalCase）查找 prefab 子节点。

- [ ] **Step 3: Commit**

```bash
git add Runtime/Registry
git commit -m "feat(registry): UIAttr + Bind marker attributes"
```

---

## Task 15：ControlMeta — 反射缓存

**Files:**
- Create: `Runtime/Registry/ControlMeta.cs`
- Create: `Tests/EditMode/Registry/ControlMetaTests.cs`

ControlMeta 在注册期反射一个 Control 子类，缓存：
- 所有 `[UIAttr]` 属性 → setter delegate（接受字符串值）
- 所有 `[Bind]` 字段 → 子节点路径

为隔离 Unity 依赖，让 ControlMeta 接受任意类型的反射；测试用纯 C# 假类。

- [ ] **Step 1: 写测试**

`Tests/EditMode/Registry/ControlMetaTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Registry {
    public class ControlMetaTests {

        class Sample {
            [UIAttr] public string Text { get; set; }
            [UIAttr("count")] public int Count { get; set; }
            public string IgnoredProp { get; set; }
        }

        [Test]
        public void Builds_setter_for_each_UIAttr_property() {
            var meta = ControlMeta.Build(typeof(Sample));
            Assert.IsTrue(meta.HasAttribute("text"));
            Assert.IsTrue(meta.HasAttribute("count"));
            Assert.IsFalse(meta.HasAttribute("ignoredProp"));
        }

        [Test]
        public void String_setter_assigns_value_directly() {
            var meta = ControlMeta.Build(typeof(Sample));
            var s = new Sample();
            meta.Apply(s, "text", "Hello");
            Assert.AreEqual("Hello", s.Text);
        }

        [Test]
        public void Int_setter_parses_string_to_int() {
            var meta = ControlMeta.Build(typeof(Sample));
            var s = new Sample();
            meta.Apply(s, "count", "42");
            Assert.AreEqual(42, s.Count);
        }

        [Test]
        public void Default_attribute_name_is_camelCase_of_property() {
            var meta = ControlMeta.Build(typeof(Sample));
            // "Text" → "text"
            Assert.IsTrue(meta.HasAttribute("text"));
        }

        [Test]
        public void Apply_unknown_attribute_throws() {
            var meta = ControlMeta.Build(typeof(Sample));
            var s = new Sample();
            Assert.Throws<System.ArgumentException>(() =>
                meta.Apply(s, "unknown", "x"));
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 ControlMeta**

`Runtime/Registry/ControlMeta.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace PromptUGUI.Registry {
    public sealed class ControlMeta {
        readonly Dictionary<string, Action<object, string>> _setters;

        ControlMeta(Dictionary<string, Action<object, string>> setters) {
            _setters = setters;
        }

        public bool HasAttribute(string name) => _setters.ContainsKey(name);

        public void Apply(object instance, string name, string value) {
            if (!_setters.TryGetValue(name, out var setter))
                throw new ArgumentException($"unknown attribute '{name}'");
            setter(instance, value);
        }

        public static ControlMeta Build(Type type) {
            var setters = new Dictionary<string, Action<object, string>>();

            foreach (var prop in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance)) {
                var attr = prop.GetCustomAttribute<UIAttrAttribute>();
                if (attr == null) continue;
                if (!prop.CanWrite) continue;

                var name = attr.Name ?? CamelCase(prop.Name);
                var setter = BuildSetter(prop);
                setters[name] = setter;
            }

            return new ControlMeta(setters);
        }

        static string CamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        static Action<object, string> BuildSetter(PropertyInfo prop) {
            var t = prop.PropertyType;
            if (t == typeof(string)) {
                return (obj, v) => prop.SetValue(obj, v);
            }
            if (t == typeof(int)) {
                return (obj, v) => prop.SetValue(obj,
                    int.Parse(v, CultureInfo.InvariantCulture));
            }
            if (t == typeof(float)) {
                return (obj, v) => prop.SetValue(obj,
                    float.Parse(v, CultureInfo.InvariantCulture));
            }
            if (t == typeof(bool)) {
                return (obj, v) => prop.SetValue(obj, bool.Parse(v));
            }
            // 其他类型暂不支持；M1 内置控件无需
            throw new NotSupportedException(
                $"[UIAttr] on {prop.DeclaringType.Name}.{prop.Name}: " +
                $"type {t} not supported in M1");
        }
    }
}
```

- [ ] **Step 4: 跑测试，全 PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Registry/ControlMeta.cs Tests/EditMode/Registry
git commit -m "feat(registry): ControlMeta with cached setters for [UIAttr] properties"
```

---

## Task 16：Control 基类 + IControl

**Files:**
- Create: `Runtime/Controls/IControl.cs`
- Create: `Runtime/Controls/Control.cs`

`Control` 持有 RectTransform，处理通用属性（id / anchor / size / margin / hidden / interactable / pivot），子类负责自身特定属性。

无独立 EditMode 测试——由后续控件实现的 PlayMode 测试覆盖。

- [ ] **Step 1: 实现 IControl**

`Runtime/Controls/IControl.cs`：

```csharp
using System;
using UnityEngine;

namespace PromptUGUI.Controls {
    public interface IControl : IDisposable {
        string Id { get; }
        GameObject GameObject { get; }
        RectTransform RectTransform { get; }
        bool Hidden { get; set; }
        bool Interactable { get; set; }
    }
}
```

- [ ] **Step 2: 实现 Control 基类**

`Runtime/Controls/Control.cs`：

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Controls {
    public abstract class Control : IControl {
        public string Id { get; internal set; }
        public GameObject GameObject { get; private set; }
        public RectTransform RectTransform { get; private set; }
        CanvasGroup _canvasGroup;

        readonly List<IControl> _children = new();

        public bool Hidden {
            get => !GameObject.activeSelf;
            set => GameObject.SetActive(!value);
        }

        public bool Interactable {
            get => CanvasGroup.interactable;
            set { CanvasGroup.interactable = value; CanvasGroup.blocksRaycasts = value; }
        }

        CanvasGroup CanvasGroup => _canvasGroup ??= GameObject.AddComponent<CanvasGroup>();

        internal void AttachTo(GameObject go) {
            GameObject = go;
            RectTransform = go.GetComponent<RectTransform>()
                            ?? go.AddComponent<RectTransform>();
        }

        internal void AddChild(IControl child) => _children.Add(child);

        public IReadOnlyList<IControl> Children => _children;

        // 通用属性应用（由 ScreenInstantiator 在子类自身属性应用之后调用）
        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool hidden, bool interactable) {
            var preset = string.IsNullOrEmpty(anchor)
                ? new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Left)
                : AnchorPreset.Parse(anchor);

            var sizeSpec = SizeSpec.Parse(size, width, height);
            sizeSpec.ValidateAgainst(preset);

            AnchorResolver.Resolve(preset,
                out var aMin, out var aMax, out var p);
            RectTransform.anchorMin = aMin;
            RectTransform.anchorMax = aMax;

            if (!string.IsNullOrEmpty(pivot)) {
                var parts = pivot.Split(',');
                RectTransform.pivot = new Vector2(
                    float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            } else {
                RectTransform.pivot = p;
            }

            var lr = MarginResolver.Resolve(preset, sizeSpec, margin);
            RectTransform.anchoredPosition = lr.AnchoredPosition;
            RectTransform.sizeDelta = lr.SizeDelta;

            if (hidden) Hidden = true;
            if (!interactable) Interactable = false;
        }

        public virtual void Dispose() {
            if (GameObject != null) Object.Destroy(GameObject);
        }
    }
}
```

- [ ] **Step 3: 编译通过验证**

打开 Unity 等待编译。Console 无红字。

- [ ] **Step 4: Commit**

```bash
git add Runtime/Controls
git commit -m "feat(controls): IControl interface + Control base with common attribute application"
```

---

## Task 17：ControlRegistry

**Files:**
- Create: `Runtime/Registry/ControlRegistry.cs`

`ControlRegistry` 是单例风格静态类（实际通过 `UI.Registry` 暴露，下一 task 加 facade）。

无独立测试——由 PlayMode E2E 测试覆盖（注册 + Open Screen → 看到正确类型实例化）。

- [ ] **Step 1: 实现 ControlRegistry**

`Runtime/Registry/ControlRegistry.cs`：

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Registry {
    public sealed class ControlRegistry {
        public sealed class Entry {
            public Type ControlType;
            public GameObject Prefab;       // null = 内置原语，由 ScreenInstantiator 直接 new GameObject
            public ControlMeta Meta;
            public string DefaultTextAttr;  // null = 不支持文本简写
        }

        readonly Dictionary<string, Entry> _byTag = new();

        public void Register<T>(string tag, GameObject prefab,
                                string defaultTextAttr = null)
            where T : Control, new() {
            if (_byTag.ContainsKey(tag))
                throw new InvalidOperationException($"tag '{tag}' already registered");
            _byTag[tag] = new Entry {
                ControlType = typeof(T),
                Prefab = prefab,
                Meta = ControlMeta.Build(typeof(T)),
                DefaultTextAttr = defaultTextAttr,
            };
        }

        public Entry Resolve(string tag) {
            if (!_byTag.TryGetValue(tag, out var e))
                throw new InvalidOperationException($"unregistered tag '{tag}'");
            return e;
        }

        public bool Has(string tag) => _byTag.ContainsKey(tag);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Runtime/Registry/ControlRegistry.cs
git commit -m "feat(registry): ControlRegistry tag→(Type,Prefab,Meta) mapping"
```

---

## Task 18：Frame 原语

**Files:**
- Create: `Runtime/Controls/Frame.cs`
- Create: `Tests/PlayMode/Controls/FrameTests.cs`

`Frame` 是纯定位容器，无视觉组件。也作为 stack/grid 容器属性持有者（但 Frame 自身不是 layout group）。

为支持 padding 等容器属性，Control 基类无；放在 Frame 下用作子节点容器。M1 暂不在 Frame 上实现 padding（仅 layout group 容器有意义），下面三个 stack 类才会处理。

- [ ] **Step 1: 写 PlayMode 测试**

`Tests/PlayMode/Controls/FrameTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Controls {
    public class FrameTests {
        [UnityTest]
        public IEnumerator Frame_creates_GameObject_with_RectTransform() {
            var frame = new Frame();
            var go = new GameObject("test");
            frame.AttachTo(go);

            Assert.IsNotNull(frame.RectTransform);
            Assert.AreEqual(go, frame.GameObject);

            Object.Destroy(go);
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（Frame 不存在）**

- [ ] **Step 3: 实现 Frame**

`Runtime/Controls/Frame.cs`：

```csharp
namespace PromptUGUI.Controls {
    public sealed class Frame : Control {
        // 无视觉、无特定属性；纯 RectTransform 容器
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Frame.cs Tests/PlayMode/Controls/FrameTests.cs
git commit -m "feat(controls): Frame primitive (RectTransform only)"
```

---

## Task 19：Image 原语

**Files:**
- Create: `Runtime/Controls/Image.cs`
- Create: `Tests/PlayMode/Controls/ImageTests.cs`

属性：sprite (Resources 路径)、color (#RRGGBB[AA])、type (sliced/simple/filled/tiled)。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Controls/ImageTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.Controls {
    public class ImageTests {
        [UnityTest]
        public IEnumerator Adds_UI_Image_component_on_attach() {
            var img = new Image();
            var go = new GameObject("img");
            img.AttachTo(go);
            img.OnAttached();

            Assert.IsNotNull(go.GetComponent<UnityImage>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Color_property_parses_hex_and_applies() {
            var img = new Image();
            var go = new GameObject("img");
            img.AttachTo(go);
            img.OnAttached();

            img.Color = "#FF8800";

            var ui = go.GetComponent<UnityImage>();
            Assert.That(ui.color.r, Is.EqualTo(1f).Within(0.01f));
            Assert.That(ui.color.g, Is.EqualTo(0.533f).Within(0.01f));
            Assert.That(ui.color.b, Is.EqualTo(0f).Within(0.01f));

            Object.Destroy(go);
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 Image**

`Runtime/Controls/Image.cs`：

```csharp
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls {
    public sealed class Image : Control {
        UnityImage _img;

        public void OnAttached() {
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<UnityImage>();
        }

        [UIAttr]
        public string Sprite {
            set {
                if (string.IsNullOrEmpty(value)) { _img.sprite = null; return; }
                _img.sprite = Resources.Load<Sprite>(value);
            }
        }

        [UIAttr]
        public string Color {
            set {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _img.color = c;
            }
        }

        [UIAttr]
        public string Type {
            set {
                _img.type = value switch {
                    "sliced" => UnityImage.Type.Sliced,
                    "tiled"  => UnityImage.Type.Tiled,
                    "filled" => UnityImage.Type.Filled,
                    _        => UnityImage.Type.Simple,
                };
            }
        }
    }
}
```

注意：M1 仅 ControlMeta 支持 string/int/float/bool 类型；这里所有 setter 都接 string 然后内部转换，符合 Meta 约束。

`OnAttached` 由 ScreenInstantiator 在 AttachTo 之后、ApplyAttribute 之前调用（让 setter 中能访问 `_img`）。

注意需要在 Control 基类加 `OnAttached` 钩子。回头加。

- [ ] **Step 4: 在 Control 基类加 OnAttached 虚方法**

修改 `Runtime/Controls/Control.cs`：

在 `internal void AttachTo(...)` 之后追加：

```csharp
        public virtual void OnAttached() { }
```

并修改 `AttachTo` 末尾自动调用：

```csharp
        internal void AttachTo(GameObject go) {
            GameObject = go;
            RectTransform = go.GetComponent<RectTransform>()
                            ?? go.AddComponent<RectTransform>();
            OnAttached();
        }
```

把 Image.cs 中的 `OnAttached` 标为 `override`：

```csharp
        public override void OnAttached() {
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<UnityImage>();
        }
```

同时移除 Image 测试中的显式 `img.OnAttached()` 调用（AttachTo 已自动）：

```csharp
        [UnityTest]
        public IEnumerator Adds_UI_Image_component_on_attach() {
            var img = new Image();
            var go = new GameObject("img");
            img.AttachTo(go);

            Assert.IsNotNull(go.GetComponent<UnityImage>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Color_property_parses_hex_and_applies() {
            var img = new Image();
            var go = new GameObject("img");
            img.AttachTo(go);

            img.Color = "#FF8800";
            // ... 同上
        }
```

- [ ] **Step 5: 跑测试，PASS**

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/Image.cs Runtime/Controls/Control.cs Tests/PlayMode/Controls/ImageTests.cs
git commit -m "feat(controls): Image primitive + OnAttached lifecycle hook"
```

---

## Task 20：Text 原语 (TMP)

**Files:**
- Create: `Runtime/Controls/Text.cs`
- Create: `Tests/PlayMode/Controls/TextTests.cs`

属性：text, size, color, align (left/center/right), wrap (true/false)。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Controls/TextTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using TMPro;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Controls {
    public class TextTests {
        [UnityTest]
        public IEnumerator Adds_TMP_Text_component_on_attach() {
            var t = new Text();
            var go = new GameObject("text");
            t.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<TMP_Text>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Text_property_writes_to_TMP() {
            var t = new Text();
            var go = new GameObject("text");
            t.AttachTo(go);
            t.TextValue = "你好";
            Assert.AreEqual("你好", go.GetComponent<TMP_Text>().text);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Size_property_writes_to_fontSize() {
            var t = new Text();
            var go = new GameObject("text");
            t.AttachTo(go);
            t.Size = 32;
            Assert.AreEqual(32f, go.GetComponent<TMP_Text>().fontSize);
            Object.Destroy(go);
            yield return null;
        }
    }
}
```

注意属性名 `TextValue` 是 C# 字段名（避开 `Text` 与类名冲突），但 [UIAttr("text")] 让 XML 中仍写 `text="..."`。

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 Text**

`Runtime/Controls/Text.cs`：

```csharp
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls {
    public sealed class Text : Control {
        TMP_Text _tmp;

        public override void OnAttached() {
            _tmp = GameObject.GetComponent<TMP_Text>()
                   ?? GameObject.AddComponent<TextMeshProUGUI>();
        }

        [UIAttr("text")]
        public string TextValue {
            set => _tmp.text = value ?? "";
        }

        [UIAttr]
        public int Size {
            set => _tmp.fontSize = value;
        }

        [UIAttr]
        public string Color {
            set {
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _tmp.color = c;
            }
        }

        [UIAttr]
        public string Align {
            set {
                _tmp.alignment = value switch {
                    "center" => TextAlignmentOptions.Center,
                    "right"  => TextAlignmentOptions.Right,
                    _        => TextAlignmentOptions.Left,
                };
            }
        }

        [UIAttr]
        public bool Wrap {
            set => _tmp.enableWordWrapping = value;
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Text.cs Tests/PlayMode/Controls/TextTests.cs
git commit -m "feat(controls): Text primitive backed by TMP_Text"
```

---

## Task 21：VStack 原语

**Files:**
- Create: `Runtime/Controls/VStack.cs`
- Create: `Tests/PlayMode/Controls/VStackTests.cs`

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Controls/VStackTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls {
    public class VStackTests {
        [UnityTest]
        public IEnumerator Adds_VerticalLayoutGroup() {
            var v = new VStack();
            var go = new GameObject("vstack");
            v.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<VerticalLayoutGroup>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spacing_writes_to_layout_group() {
            var v = new VStack();
            var go = new GameObject("vstack");
            v.AttachTo(go);
            v.Spacing = 12f;
            Assert.AreEqual(12f, go.GetComponent<VerticalLayoutGroup>().spacing);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Padding_uniform_writes_to_layout_group() {
            var v = new VStack();
            var go = new GameObject("vstack");
            v.AttachTo(go);
            v.Padding = "16";
            var p = go.GetComponent<VerticalLayoutGroup>().padding;
            Assert.AreEqual(16, p.top);
            Assert.AreEqual(16, p.right);
            Assert.AreEqual(16, p.bottom);
            Assert.AreEqual(16, p.left);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Padding_4_values_TRBL() {
            var v = new VStack();
            var go = new GameObject("vstack");
            v.AttachTo(go);
            v.Padding = "1,2,3,4";
            var p = go.GetComponent<VerticalLayoutGroup>().padding;
            Assert.AreEqual(1, p.top);
            Assert.AreEqual(2, p.right);
            Assert.AreEqual(3, p.bottom);
            Assert.AreEqual(4, p.left);
            Object.Destroy(go);
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 VStack**

`Runtime/Controls/VStack.cs`：

```csharp
using System;
using System.Globalization;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls {
    public sealed class VStack : Control {
        VerticalLayoutGroup _layout;

        public override void OnAttached() {
            _layout = GameObject.GetComponent<VerticalLayoutGroup>()
                      ?? GameObject.AddComponent<VerticalLayoutGroup>();
        }

        [UIAttr]
        public float Spacing {
            set => _layout.spacing = value;
        }

        [UIAttr]
        public string Padding {
            set {
                ParseTRBL(value, out int t, out int r, out int b, out int l);
                _layout.padding = new RectOffset(l, r, t, b);
                // RectOffset 顺序: left, right, top, bottom
            }
        }

        internal static void ParseTRBL(string s, out int t, out int r, out int b, out int l) {
            t = r = b = l = 0;
            if (string.IsNullOrEmpty(s)) return;
            var parts = s.Split(',');
            int[] v = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                v[i] = int.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            switch (parts.Length) {
                case 1: t = r = b = l = v[0]; return;
                case 2: t = b = v[0]; r = l = v[1]; return;
                case 4: t = v[0]; r = v[1]; b = v[2]; l = v[3]; return;
                default: throw new ArgumentException($"padding '{s}' must be 1/2/4 ints");
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/VStack.cs Tests/PlayMode/Controls/VStackTests.cs
git commit -m "feat(controls): VStack with VerticalLayoutGroup, spacing & padding"
```

---

## Task 22：HStack 原语

**Files:**
- Create: `Runtime/Controls/HStack.cs`
- Create: `Tests/PlayMode/Controls/HStackTests.cs`

结构与 VStack 一致，仅 LayoutGroup 类型不同。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Controls/HStackTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls {
    public class HStackTests {
        [UnityTest]
        public IEnumerator Adds_HorizontalLayoutGroup() {
            var h = new HStack();
            var go = new GameObject("hstack");
            h.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<HorizontalLayoutGroup>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spacing_and_padding_apply() {
            var h = new HStack();
            var go = new GameObject("hstack");
            h.AttachTo(go);
            h.Spacing = 8f;
            h.Padding = "4,8";
            var lg = go.GetComponent<HorizontalLayoutGroup>();
            Assert.AreEqual(8f, lg.spacing);
            Assert.AreEqual(4, lg.padding.top);
            Assert.AreEqual(8, lg.padding.right);
            Assert.AreEqual(4, lg.padding.bottom);
            Assert.AreEqual(8, lg.padding.left);
            Object.Destroy(go);
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 HStack**

`Runtime/Controls/HStack.cs`：

```csharp
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls {
    public sealed class HStack : Control {
        HorizontalLayoutGroup _layout;

        public override void OnAttached() {
            _layout = GameObject.GetComponent<HorizontalLayoutGroup>()
                      ?? GameObject.AddComponent<HorizontalLayoutGroup>();
        }

        [UIAttr]
        public float Spacing {
            set => _layout.spacing = value;
        }

        [UIAttr]
        public string Padding {
            set {
                VStack.ParseTRBL(value, out int t, out int r, out int b, out int l);
                _layout.padding = new RectOffset(l, r, t, b);
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/HStack.cs Tests/PlayMode/Controls/HStackTests.cs
git commit -m "feat(controls): HStack with HorizontalLayoutGroup"
```

---

## Task 23：Grid 原语

**Files:**
- Create: `Runtime/Controls/Grid.cs`
- Create: `Tests/PlayMode/Controls/GridTests.cs`

属性：columns (int)、cellSize (WxH)、spacing (V,H)、padding (TRBL)。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Controls/GridTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls {
    public class GridTests {
        [UnityTest]
        public IEnumerator Adds_GridLayoutGroup_with_fixed_column_count() {
            var g = new Grid();
            var go = new GameObject("grid");
            g.AttachTo(go);
            g.Columns = 6;
            var lg = go.GetComponent<GridLayoutGroup>();
            Assert.IsNotNull(lg);
            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, lg.constraint);
            Assert.AreEqual(6, lg.constraintCount);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CellSize_writes_WxH() {
            var g = new Grid();
            var go = new GameObject("grid");
            g.AttachTo(go);
            g.CellSize = "64x64";
            var lg = go.GetComponent<GridLayoutGroup>();
            Assert.AreEqual(new Vector2(64, 64), lg.cellSize);
            Object.Destroy(go);
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 Grid**

`Runtime/Controls/Grid.cs`：

```csharp
using System.Globalization;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls {
    public sealed class Grid : Control {
        GridLayoutGroup _layout;

        public override void OnAttached() {
            _layout = GameObject.GetComponent<GridLayoutGroup>()
                      ?? GameObject.AddComponent<GridLayoutGroup>();
            _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        }

        [UIAttr]
        public int Columns {
            set => _layout.constraintCount = value;
        }

        [UIAttr]
        public string CellSize {
            set {
                var x = value.IndexOf('x');
                var w = float.Parse(value.Substring(0, x), CultureInfo.InvariantCulture);
                var h = float.Parse(value.Substring(x + 1), CultureInfo.InvariantCulture);
                _layout.cellSize = new Vector2(w, h);
            }
        }

        [UIAttr]
        public string Spacing {
            set {
                var parts = value.Split(',');
                if (parts.Length == 1) {
                    var s = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    _layout.spacing = new Vector2(s, s);
                } else {
                    var v = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    var h = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    _layout.spacing = new Vector2(h, v);  // GridLayoutGroup.spacing is (x,y) = (horizontal, vertical)
                }
            }
        }

        [UIAttr]
        public string Padding {
            set {
                VStack.ParseTRBL(value, out int t, out int r, out int b, out int l);
                _layout.padding = new RectOffset(l, r, t, b);
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Grid.cs Tests/PlayMode/Controls/GridTests.cs
git commit -m "feat(controls): Grid with GridLayoutGroup (fixed columns)"
```

---

## Task 24：ScreenInstantiator — IR → GameObject 树

**Files:**
- Create: `Runtime/Application/ScreenInstantiator.cs`
- Create: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

`ScreenInstantiator` 接收 ScreenDef + Registry，递归实例化每个元素：
- 内置原语 (`Frame/Image/Text/VStack/HStack/Grid`)：直接 `new` 控件类，`new GameObject` 挂载
- 自定义控件：从 Prefab 实例化，附加 control 实例

文本简写：当 ElementNode.TextContent 非空时，作为 entry.DefaultTextAttr 的值传入。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Lifecycle {
    public class ScreenInstantiatorTests {

        ControlRegistry _reg;

        [SetUp] public void SetUp() {
            _reg = new ControlRegistry();
            // 注册内置原语
            BuiltinPrimitives.Register(_reg);
        }

        [UnityTest]
        public IEnumerator Instantiates_image_with_anchor_and_size() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Image id='bg' anchor='top-right' size='240x80' margin='16'/>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var rootGo = inst.Instantiate(doc.Screens[0]);

            Assert.IsNotNull(rootGo);
            var imgGo = rootGo.transform.GetChild(0).gameObject;
            Assert.AreEqual("bg", imgGo.name);
            var rt = imgGo.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(240, 80), rt.sizeDelta);
            Assert.AreEqual(new Vector2(-16, -16), rt.anchoredPosition);

            Object.Destroy(rootGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Recursive_children_are_parented_correctly() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <VStack id='root' anchor='center' size='400x300'>
                        <Image id='a'/>
                        <Image id='b'/>
                    </VStack>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var rootGo = inst.Instantiate(doc.Screens[0]);

            var vstackGo = rootGo.transform.GetChild(0).gameObject;
            Assert.AreEqual("root", vstackGo.name);
            Assert.AreEqual(2, vstackGo.transform.childCount);
            Assert.AreEqual("a", vstackGo.transform.GetChild(0).name);
            Assert.AreEqual("b", vstackGo.transform.GetChild(1).name);

            Object.Destroy(rootGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Text_content_shorthand_applies_to_default_text_attr() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Text>Hello</Text>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var rootGo = inst.Instantiate(doc.Screens[0]);

            var txt = rootGo.transform.GetChild(0).GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual("Hello", txt.text);

            Object.Destroy(rootGo);
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 BuiltinPrimitives 注册辅助**

`Runtime/Application/BuiltinPrimitives.cs`（同包目录）：

```csharp
using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class BuiltinPrimitives {
        public static void Register(ControlRegistry reg) {
            reg.Register<Frame>("Frame", null);
            reg.Register<Image>("Image", null);
            reg.Register<Text>("Text", null, defaultTextAttr: "text");
            reg.Register<VStack>("VStack", null);
            reg.Register<HStack>("HStack", null);
            reg.Register<Grid>("Grid", null);
        }
    }
}
```

- [ ] **Step 4: 实现 ScreenInstantiator**

`Runtime/Application/ScreenInstantiator.cs`：

```csharp
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application {
    public sealed class ScreenInstantiator {
        readonly ControlRegistry _registry;

        public ScreenInstantiator(ControlRegistry registry) {
            _registry = registry;
        }

        public GameObject Instantiate(ScreenDef def) {
            var rootGo = new GameObject(def.Name);
            rootGo.AddComponent<RectTransform>();
            // 注：完整 Canvas 由 Screen 类负责，本类只生成纯 GameObject 树

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, rootGo.transform);

            return rootGo;
        }

        void InstantiateRecursive(ElementNode node, Transform parent) {
            var entry = _registry.Resolve(node.Tag);

            GameObject go;
            Control control;

            if (entry.Prefab != null) {
                go = Object.Instantiate(entry.Prefab, parent);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            } else {
                go = new GameObject(node.Id ?? node.Tag);
                go.transform.SetParent(parent, worldPositionStays: false);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            }

            if (!string.IsNullOrEmpty(node.Id))
                go.name = node.Id;

            control.Id = node.Id;
            control.AttachTo(go);

            // 应用控件特定属性
            foreach (var kv in node.Attributes) {
                if (IsCommonAttribute(kv.Key)) continue;
                if (entry.Meta.HasAttribute(kv.Key))
                    entry.Meta.Apply(control, kv.Key, kv.Value);
            }

            // 文本简写
            if (!string.IsNullOrEmpty(node.TextContent) && entry.DefaultTextAttr != null)
                entry.Meta.Apply(control, entry.DefaultTextAttr, node.TextContent);

            // 应用通用属性
            node.Attributes.TryGetValue("anchor", out var anchor);
            node.Attributes.TryGetValue("size",   out var size);
            node.Attributes.TryGetValue("width",  out var width);
            node.Attributes.TryGetValue("height", out var height);
            node.Attributes.TryGetValue("margin", out var margin);
            node.Attributes.TryGetValue("pivot",  out var pivot);
            node.Attributes.TryGetValue("hidden", out var hiddenStr);
            node.Attributes.TryGetValue("interactable", out var interactableStr);
            bool hidden       = hiddenStr == "true";
            bool interactable = interactableStr != "false";

            control.ApplyCommon(anchor, size, width, height, margin, pivot, hidden, interactable);

            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform);
        }

        static bool IsCommonAttribute(string name) {
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

- [ ] **Step 5: 跑测试，PASS**

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application Tests/PlayMode/Lifecycle
git commit -m "feat(app): ScreenInstantiator + BuiltinPrimitives registration"
```

---

## Task 25：Screen 类与 IScreen API

**Files:**
- Create: `Runtime/Application/Screen.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

`Screen` 实现 `IScreen` 接口；持有 Canvas + 实例化的根 GameObject + IControl 句柄表 + 订阅生命周期。

- [ ] **Step 1: 加 Screen 测试**

追加到 `ScreenLifecycleTests.cs`：

```csharp
        [UnityTest]
        public IEnumerator Screen_creates_canvas_and_can_get_by_id() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Image id='bg' anchor='stretch'/>
                    <Text id='hello'>Hi</Text>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var screen = new Screen(doc.Screens[0], inst);

            screen.Open();

            Assert.IsNotNull(screen.RootGameObject.GetComponent<Canvas>());

            var bg = screen.Get<Image>("bg");
            Assert.IsNotNull(bg);
            var hello = screen.Get<Text>("hello");
            Assert.IsNotNull(hello);

            screen.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Screen_get_unknown_id_throws() {
            const string xml = @"<UI version='1'>
                <Screen name='X'><Image id='only'/></Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = new Screen(doc.Screens[0], new ScreenInstantiator(_reg));
            screen.Open();

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get<Image>("nope"));

            screen.Close();
            yield return null;
        }
```

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 Screen + IScreen**

`Runtime/Application/Screen.cs`：

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
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
        readonly Dictionary<string, IControl> _byId = new();
        readonly List<IDisposable> _subscriptions = new();

        public string Name => _def.Name;
        public GameObject RootGameObject { get; private set; }

        public Screen(ScreenDef def, ScreenInstantiator instantiator) {
            _def = def;
            _instantiator = instantiator;
        }

        public void Open() {
            RootGameObject = _instantiator.Instantiate(_def);
            EnsureCanvas(RootGameObject);
            CollectControls(RootGameObject.transform);
        }

        public void Close() {
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            if (RootGameObject != null) {
                UnityEngine.Object.Destroy(RootGameObject);
                RootGameObject = null;
            }
            _byId.Clear();
        }

        public T Get<T>(string id) where T : class, IControl {
            if (!_byId.TryGetValue(id, out var c))
                throw new KeyNotFoundException($"id '{id}' not found in screen '{Name}'");
            if (c is not T typed)
                throw new InvalidCastException(
                    $"id '{id}' is {c.GetType().Name}, not {typeof(T).Name}");
            return typed;
        }

        public IControl Get(string id) {
            if (!_byId.TryGetValue(id, out var c))
                throw new KeyNotFoundException($"id '{id}' not found in screen '{Name}'");
            return c;
        }

        public void Track(IDisposable d) => _subscriptions.Add(d);

        public void Dispose() => Close();

        static void EnsureCanvas(GameObject go) {
            var canvas = go.GetComponent<Canvas>() ?? go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (go.GetComponent<UnityEngine.UI.CanvasScaler>() == null)
                go.AddComponent<UnityEngine.UI.CanvasScaler>();
            if (go.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        void CollectControls(Transform t) {
            // 通过 GameObject 名（即 id）和挂载的 Control 实例查找
            // M1 尚未把 Control 挂为 component；改为遍历 ScreenInstantiator 维护的句柄表
            // 此处替代实现：用一个递归把名字与实例对齐
            // —— 见 Step 4 调整
        }
    }
}
```

注意：`CollectControls` 当前实现为空——下一步重构 ScreenInstantiator 让其返回 id→IControl 表，Screen 直接接管。

- [ ] **Step 4: 重构 ScreenInstantiator 暴露 id 表**

修改 `ScreenInstantiator.cs`：

将 `Instantiate` 方法签名改为：

```csharp
        public InstantiationResult Instantiate(ScreenDef def) {
            var result = new InstantiationResult {
                Root = new GameObject(def.Name),
                Controls = new Dictionary<string, IControl>(),
            };
            result.Root.AddComponent<RectTransform>();

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform, result.Controls);

            return result;
        }

        void InstantiateRecursive(ElementNode node, Transform parent,
                                  Dictionary<string, IControl> controls) {
            // ... 与原来基本相同 ...
            // 在 control.AttachTo(go) 之后追加：
            if (!string.IsNullOrEmpty(node.Id))
                controls[node.Id] = control;

            // ... 子节点递归改为：
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, controls);
        }
```

并在文件顶部加：

```csharp
    public sealed class InstantiationResult {
        public GameObject Root;
        public Dictionary<string, IControl> Controls;
    }
```

修改 `Screen.Open` 用新签名：

```csharp
        public void Open() {
            var result = _instantiator.Instantiate(_def);
            RootGameObject = result.Root;
            foreach (var kv in result.Controls)
                _byId[kv.Key] = kv.Value;
            EnsureCanvas(RootGameObject);
        }
```

并删除原 `Screen.CollectControls` 占位方法。

也修改之前的 `ScreenInstantiatorTests` 用新签名：把 `inst.Instantiate(...)` 调整为 `.Root`：

```csharp
            var rootGo = inst.Instantiate(doc.Screens[0]).Root;
```

更新测试文件中三处类似调用。

- [ ] **Step 5: 跑全 PlayMode Lifecycle 测试，全 PASS**

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application Tests/PlayMode/Lifecycle
git commit -m "feat(app): Screen with Canvas + Get<T> by id, instantiator yields control map"
```

---

## Task 26：UI facade

**Files:**
- Create: `Runtime/Application/UI.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

`UI` 是静态门面，持有全局 ControlRegistry、文档加载缓存、活跃 Screens。

- [ ] **Step 1: 加 facade 测试**

追加：

```csharp
        [UnityTest]
        public IEnumerator UI_open_returns_screen_and_close_destroys() {
            UI.LoadDocument("test_doc", @"<UI version='1'>
                <Screen name='UIFacade'>
                    <Image id='bg' anchor='stretch'/>
                </Screen></UI>");

            var screen = UI.Open("UIFacade");
            Assert.IsNotNull(screen);
            Assert.IsNotNull(screen.RootGameObject);

            UI.Close("UIFacade");
            // 等一帧让 Destroy 生效
            yield return null;
            Assert.IsTrue(UI.Get("UIFacade") == null);
        }
```

注意：测试需要 SetUp 重置 UI 单例状态，避免污染：

```csharp
        [SetUp] public void SetUpFacade() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }
```

合并 SetUp（保留 `_reg` 测试也用 `UI.Registry`）。

- [ ] **Step 2: 跑测试，FAIL**

- [ ] **Step 3: 实现 UI 门面**

`Runtime/Application/UI.cs`：

```csharp
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class UI {
        static ControlRegistry _registry = new();
        static readonly Dictionary<string, ScreenDef> _docs = new();
        static readonly Dictionary<string, Screen> _open = new();

        public static ControlRegistry Registry => _registry;

        public static void LoadDocument(string label, string xml) {
            var doc = UIDocumentParser.Parse(xml);
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

            var inst = new ScreenInstantiator(_registry);
            var screen = new Screen(def, inst);
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
            _registry = new ControlRegistry();
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.cs Tests/PlayMode/Lifecycle
git commit -m "feat(app): static UI facade for LoadDocument / Open / Close / Get"
```

---

## Task 27：IScreen Subscription tracking + AddTo 扩展

**Files:**
- Create: `Runtime/Application/Disposables.cs`
- Modify: `Tests/PlayMode/Lifecycle/ScreenLifecycleTests.cs`

支持 `subscription.AddTo(screen)` 模式，让 R3 流在 Screen 关闭时自动 Dispose。

- [ ] **Step 1: 加测试**

```csharp
        [UnityTest]
        public IEnumerator AddTo_screen_disposes_on_close() {
            UI.LoadDocument("addto_doc", @"<UI version='1'>
                <Screen name='AddToTest'><Image id='bg'/></Screen></UI>");
            var screen = UI.Open("AddToTest");

            bool disposed = false;
            var d = System.Reactive.Disposables.Disposable.Create(() => disposed = true);
            d.AddTo(screen);

            UI.Close("AddToTest");
            yield return null;

            Assert.IsTrue(disposed);
        }
```

注：System.Reactive 不是 R3，用 R3.Disposable 替代如果存在；为简化测试，自己构建一个 IDisposable wrapper：

```csharp
        sealed class TrackingDisposable : System.IDisposable {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        [UnityTest]
        public IEnumerator AddTo_screen_disposes_on_close() {
            UI.LoadDocument("addto_doc", @"<UI version='1'>
                <Screen name='AddToTest'><Image id='bg'/></Screen></UI>");
            var screen = UI.Open("AddToTest");

            var d = new TrackingDisposable();
            d.AddTo(screen);

            UI.Close("AddToTest");
            yield return null;

            Assert.IsTrue(d.Disposed);
        }
```

- [ ] **Step 2: 跑测试，FAIL（AddTo 不存在）**

- [ ] **Step 3: 实现 AddTo 扩展**

`Runtime/Application/Disposables.cs`：

```csharp
using System;

namespace PromptUGUI.Application {
    public static class DisposableExtensions {
        public static T AddTo<T>(this T disposable, Screen screen) where T : IDisposable {
            screen.Track(disposable);
            return disposable;
        }
    }
}
```

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Disposables.cs Tests/PlayMode/Lifecycle
git commit -m "feat(app): AddTo(screen) extension binds disposables to screen lifecycle"
```

---

## Task 28：示例 PrimaryButton / DangerButton 自定义控件

**Files:**
- Create: `Samples~/MainMenu/PrimaryButton.cs`
- Create: `Samples~/MainMenu/DangerButton.cs`
- Create: `Samples~/MainMenu/Resources/UI/PrimaryButton.prefab`（手工）
- Create: `Samples~/MainMenu/Resources/UI/DangerButton.prefab`（手工）

`Samples~/` 目录的 `~` 后缀让 Unity 默认不导入；用户从 Package Manager UI 导入。

- [ ] **Step 1: 在 Unity Editor 中手工创建 PrimaryButton.prefab**

操作步骤（写在 `Samples~/MainMenu/PREFAB_NOTES.md` 中）：

```
PrimaryButton.prefab 结构：
- Root GameObject "PrimaryButton"
  - RectTransform: 240x64
  - Image (Source Image: 任意 9-slice sprite，例 UnitySprite/UISprite，颜色蓝色 #3B82F6)
  - Button (Target Graphic: 上面的 Image)
  - 子节点 "Label"
    - RectTransform: stretch all
    - TextMeshProUGUI: text="Button", fontSize=24, alignment=Center, color 白
```

`DangerButton.prefab` 同结构，颜色改红 #DC2626。

工程师自己在 Unity Editor 里建并保存为 prefab；plan 不能自动建 .prefab 二进制文件。

- [ ] **Step 2: 实现 PrimaryButton.cs**

`Samples~/MainMenu/PrimaryButton.cs`：

```csharp
using PromptUGUI.Controls;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine.UI;

namespace PromptUGUI.Samples.MainMenu {
    public sealed class PrimaryButton : Control {
        Button _btn;
        TMP_Text _label;

        public override void OnAttached() {
            _btn = GameObject.GetComponent<Button>();
            _label = GameObject.GetComponentInChildren<TMP_Text>();
        }

        [UIAttr("text")]
        public string TextValue {
            set => _label.text = value;
        }

        public Observable<Unit> OnClick => _btn.OnClickAsObservable();
    }
}
```

- [ ] **Step 3: 实现 DangerButton.cs**

`Samples~/MainMenu/DangerButton.cs`：

```csharp
using PromptUGUI.Controls;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine.UI;

namespace PromptUGUI.Samples.MainMenu {
    public sealed class DangerButton : Control {
        Button _btn;
        TMP_Text _label;

        public override void OnAttached() {
            _btn = GameObject.GetComponent<Button>();
            _label = GameObject.GetComponentInChildren<TMP_Text>();
        }

        [UIAttr("text")]
        public string TextValue {
            set => _label.text = value;
        }

        public Observable<Unit> OnClick => _btn.OnClickAsObservable();
    }
}
```

- [ ] **Step 4: Commit 代码（prefab 由工程师导入后再 commit）**

```bash
git add Samples~/MainMenu/*.cs Samples~/MainMenu/PREFAB_NOTES.md
git commit -m "feat(samples): PrimaryButton/DangerButton sample custom controls"
```

---

## Task 29：示例 MainMenu.ui.xml + Runner 脚本

**Files:**
- Create: `Samples~/MainMenu/MainMenu.ui.xml`
- Create: `Samples~/MainMenu/MainMenuRunner.cs`

- [ ] **Step 1: 写 MainMenu.ui.xml**

`Samples~/MainMenu/MainMenu.ui.xml`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<UI version="1">
  <Screen name="MainMenu">
    <Image id="bg" anchor="stretch" color="#222244"/>
    <VStack id="menuRoot" anchor="center" size="280x240" spacing="16" padding="16">
      <PrimaryButton id="playBtn"     size="240x64">开始游戏</PrimaryButton>
      <PrimaryButton id="settingsBtn" size="240x64">设置</PrimaryButton>
      <DangerButton  id="quitBtn"     size="240x64">退出</DangerButton>
    </VStack>
  </Screen>
</UI>
```

- [ ] **Step 2: 写 MainMenuRunner.cs**

`Samples~/MainMenu/MainMenuRunner.cs`：

```csharp
using PromptUGUI.Application;
using R3;
using UnityEngine;

namespace PromptUGUI.Samples.MainMenu {
    public sealed class MainMenuRunner : MonoBehaviour {
        [SerializeField] TextAsset _xml;
        [SerializeField] GameObject _primaryButtonPrefab;
        [SerializeField] GameObject _dangerButtonPrefab;

        void Start() {
            UI.Registry.Register<PrimaryButton>("PrimaryButton", _primaryButtonPrefab);
            UI.Registry.Register<DangerButton>("DangerButton", _dangerButtonPrefab);

            // 注册内置原语
            BuiltinPrimitives.Register(UI.Registry);

            UI.LoadDocument("main", _xml.text);
            var screen = UI.Open("MainMenu");

            screen.Get<PrimaryButton>("playBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] play clicked"))
                  .AddTo(screen);
            screen.Get<PrimaryButton>("settingsBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] settings clicked"))
                  .AddTo(screen);
            screen.Get<DangerButton>("quitBtn").OnClick
                  .Subscribe(_ => Debug.Log("[Sample] quit clicked"))
                  .AddTo(screen);
        }
    }
}
```

工程师用法：在示例场景中创建空 GameObject "Runner"，加 MainMenuRunner，把 MainMenu.ui.xml 拖到 _xml 字段，把两个 prefab 拖到对应字段。Play 即可看到主菜单 + 点击日志。

- [ ] **Step 3: Commit**

```bash
git add Samples~/MainMenu/MainMenu.ui.xml Samples~/MainMenu/MainMenuRunner.cs
git commit -m "feat(samples): MainMenu.ui.xml + MainMenuRunner integration"
```

---

## Task 30：E2E PlayMode 验收测试

**Files:**
- Create: `Tests/PlayMode/E2E/MainMenuDemoTests.cs`

不依赖 Samples~ 中的真实 prefab——构造 mock prefab 验证完整链路。

- [ ] **Step 1: 写 E2E 测试**

`Tests/PlayMode/E2E/MainMenuDemoTests.cs`：

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

namespace PromptUGUI.Tests.E2E {

    public sealed class TestButton : Control {
        Button _btn;
        TMP_Text _label;
        public override void OnAttached() {
            _btn = GameObject.GetComponent<Button>();
            _label = GameObject.GetComponentInChildren<TMP_Text>();
        }
        [UIAttr("text")]
        public string TextValue { set => _label.text = value; }
        public Observable<Unit> OnClick => _btn.OnClickAsObservable();
    }

    public class MainMenuDemoTests {

        GameObject _btnPrefab;

        [SetUp]
        public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);

            // 构造一个 mock button prefab
            _btnPrefab = new GameObject("TestButtonPrefab");
            _btnPrefab.AddComponent<RectTransform>();
            _btnPrefab.AddComponent<Image>();
            _btnPrefab.AddComponent<Button>();
            var label = new GameObject("Label");
            label.transform.SetParent(_btnPrefab.transform);
            label.AddComponent<RectTransform>();
            label.AddComponent<TextMeshProUGUI>();

            UI.Registry.Register<TestButton>("TestButton", _btnPrefab);
        }

        [TearDown]
        public void TearDown() {
            if (_btnPrefab != null) Object.Destroy(_btnPrefab);
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Main_menu_with_three_buttons_clicks_propagate() {
            UI.LoadDocument("main", @"<UI version='1'>
                <Screen name='MainMenu'>
                    <VStack id='menuRoot' anchor='center' size='280x240' spacing='12'>
                        <TestButton id='play'>开始</TestButton>
                        <TestButton id='settings'>设置</TestButton>
                        <TestButton id='quit'>退出</TestButton>
                    </VStack>
                </Screen></UI>");

            var screen = UI.Open("MainMenu");

            int playClicks = 0;
            screen.Get<TestButton>("play").OnClick
                  .Subscribe(_ => playClicks++)
                  .AddTo(screen);

            // 模拟点击
            screen.Get<TestButton>("play").GameObject
                  .GetComponent<Button>().onClick.Invoke();
            yield return null;

            Assert.AreEqual(1, playClicks);

            // 验证文本写入
            var playLabel = screen.Get<TestButton>("play").GameObject
                                  .GetComponentInChildren<TMP_Text>();
            Assert.AreEqual("开始", playLabel.text);

            // 验证 VStack 实际包含三子节点
            var menuRoot = screen.Get<VStack>("menuRoot").GameObject;
            Assert.AreEqual(3, menuRoot.transform.childCount);

            UI.Close("MainMenu");
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑全部 PlayMode 测试**

```bash
"$UNITY_PATH" -batchmode -nographics -projectPath "$HOST_PROJECT" \
  -runTests -testPlatform PlayMode \
  -testResults playmode-results.xml -logFile - 2>&1 | tail -40
```

预期：所有 PlayMode 测试 PASS（包括 controls/lifecycle/E2E）。

- [ ] **Step 3: 跑全部 EditMode 测试**

```bash
"$UNITY_PATH" -batchmode -nographics -projectPath "$HOST_PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults editmode-results.xml -logFile - 2>&1 | tail -40
```

预期：所有 parser/layout/registry 测试 PASS。

- [ ] **Step 4: Commit**

```bash
git add Tests/PlayMode/E2E
git commit -m "test(e2e): main menu demo with three buttons + click propagation"
```

---

## Task 31：[Bind] 字段自动 wiring（兑现 spec §9.3 承诺）

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`
- Create: `Tests/PlayMode/Registry/BindWiringTests.cs`

`[Bind]` 标记的字段在 prefab 实例化后，按字段名（去前导 `_`，PascalCase）查找子节点上的同类型组件并赋值。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Registry/BindWiringTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Registry {

    public sealed class BindSampleControl : Control {
        [Bind] public TMP_Text Label;
        [Bind("CustomChild")] public Button Btn;
    }

    public class BindWiringTests {

        GameObject _prefab;

        [SetUp] public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);

            _prefab = new GameObject("BindSamplePrefab");
            _prefab.AddComponent<RectTransform>();

            var label = new GameObject("Label");
            label.transform.SetParent(_prefab.transform);
            label.AddComponent<RectTransform>();
            label.AddComponent<TextMeshProUGUI>();

            var custom = new GameObject("CustomChild");
            custom.transform.SetParent(_prefab.transform);
            custom.AddComponent<RectTransform>();
            custom.AddComponent<Image>();
            custom.AddComponent<Button>();

            UI.Registry.Register<BindSampleControl>("BindSample", _prefab);
        }

        [TearDown] public void TearDown() {
            if (_prefab != null) Object.Destroy(_prefab);
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Bind_field_wires_component_from_named_child() {
            UI.LoadDocument("d", @"<UI version='1'>
                <Screen name='S'><BindSample id='x'/></Screen></UI>");
            var screen = UI.Open("S");

            var ctl = screen.Get<BindSampleControl>("x");
            Assert.IsNotNull(ctl.Label, "Label should auto-wire from child 'Label'");
            Assert.IsNotNull(ctl.Btn,   "Btn should auto-wire from child 'CustomChild'");

            UI.Close("S");
            yield return null;
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（字段未被 wire）**

- [ ] **Step 3: 在 ScreenInstantiator 加 BindFields 方法并在 OnAttached 之前调用**

修改 `Runtime/Application/ScreenInstantiator.cs`：

在 `InstantiateRecursive` 中，于 `control.AttachTo(go)` **之前**插入：

```csharp
            if (entry.Prefab != null)
                BindFields(control, go);
```

在文件中添加方法：

```csharp
        static void BindFields(Control control, GameObject prefabRoot) {
            var t = control.GetType();
            foreach (var f in t.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)) {
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
```

文件顶部加 `using System.Reflection;`（如果未引入）。

注意：`BindAttribute` 已在 Task 14 创建；`Control` 基类的字段访问需要 `BindingFlags.NonPublic` 才能拿到内部字段。

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: 跑全部 PlayMode 测试确认无回归**

```bash
"$UNITY_PATH" -batchmode -nographics -projectPath "$HOST_PROJECT" \
  -runTests -testPlatform PlayMode \
  -testResults playmode-results.xml -logFile - 2>&1 | tail -20
```

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/ScreenInstantiator.cs Tests/PlayMode/Registry
git commit -m "feat(registry): wire [Bind] fields from prefab children at instantiation"
```

---

## Task 32：Stack/Grid 内子节点 anchor 警告（兑现 spec §6.5）

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`
- Create: `Tests/PlayMode/Lifecycle/StackChildWarningTests.cs`

子节点位于 VStack/HStack/Grid 之下时，写了 `anchor` 或 `margin` 属性应当 `Debug.LogWarning`（不静默丢弃）。

- [ ] **Step 1: 写测试**

`Tests/PlayMode/Lifecycle/StackChildWarningTests.cs`：

```csharp
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Lifecycle {
    public class StackChildWarningTests {

        [SetUp] public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }

        [UnityTest]
        public IEnumerator Anchor_on_VStack_child_logs_warning() {
            LogAssert.Expect(LogType.Warning,
                new Regex("anchor.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("d", @"<UI version='1'>
                <Screen name='S'>
                    <VStack id='v' anchor='center' size='200x200'>
                        <Image id='child' anchor='top-left'/>
                    </VStack>
                </Screen></UI>");
            UI.Open("S");

            yield return null;
            UI.Close("S");
        }

        [UnityTest]
        public IEnumerator Margin_on_HStack_child_logs_warning() {
            LogAssert.Expect(LogType.Warning,
                new Regex("margin.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("d2", @"<UI version='1'>
                <Screen name='S2'>
                    <HStack id='h' anchor='center' size='200x100'>
                        <Image id='child' margin='8'/>
                    </HStack>
                </Screen></UI>");
            UI.Open("S2");

            yield return null;
            UI.Close("S2");
        }

        [UnityTest]
        public IEnumerator No_warning_when_only_size_specified_inside_stack() {
            UI.LoadDocument("d3", @"<UI version='1'>
                <Screen name='S3'>
                    <VStack id='v' anchor='center' size='200x200'>
                        <Image id='child' size='100x40'/>
                    </VStack>
                </Screen></UI>");
            UI.Open("S3");
            yield return null;
            UI.Close("S3");
            // 没有 LogAssert.Expect 即代表不允许 warning；测试框架若产生 unexpected warning 会失败
        }
    }
}
```

- [ ] **Step 2: 跑测试，FAIL（无 warning 触发）**

- [ ] **Step 3: 在 ScreenInstantiator 加判断**

修改 `InstantiateRecursive` 签名加一个 parent 参数 `bool parentIsLayoutGroup`，递归时根据当前节点 tag 判断是否传 true 给子。

替换 `InstantiateRecursive` 与 `Instantiate` 入口：

```csharp
        public InstantiationResult Instantiate(ScreenDef def) {
            var result = new InstantiationResult {
                Root = new GameObject(def.Name),
                Controls = new System.Collections.Generic.Dictionary<string, IControl>(),
            };
            result.Root.AddComponent<RectTransform>();

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false, result.Controls);

            return result;
        }

        void InstantiateRecursive(ElementNode node, Transform parent,
                                  bool parentIsLayoutGroup,
                                  System.Collections.Generic.Dictionary<string, IControl> controls) {

            if (parentIsLayoutGroup) {
                if (node.Attributes.ContainsKey("anchor"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: anchor ignored inside layout group");
                if (node.Attributes.ContainsKey("margin"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: margin ignored inside layout group");
            }

            // ... 中间实例化逻辑保持不变 ...

            // 递归子节点：如果当前是 stack/grid，对子节点而言 parent 是 layout group
            bool selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, controls);
        }
```

工程师需把现有 `InstantiateRecursive` 内部"foreach 递归"那一行改成传 `selfIsLayoutGroup`，并添加方法开头的 warning 块。

- [ ] **Step 4: 跑测试，PASS**

- [ ] **Step 5: 跑全 PlayMode 测试套件**

预期：所有先前测试仍 PASS，新增 3 个 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/ScreenInstantiator.cs Tests/PlayMode/Lifecycle/StackChildWarningTests.cs
git commit -m "feat(app): warn when anchor/margin used inside layout group"
```

---

## M1 完成验收清单

跑完 32 个 task 后，确认以下为 PASS：

- [ ] `UI.LoadDocument(...)` + `UI.Open("MainMenu")` 在样例场景显示三个按钮
- [ ] 点击按钮在 Console 输出对应日志（来自 R3.Subscribe）
- [ ] EditMode 测试套件全 PASS（parser / layout / registry）
- [ ] PlayMode 测试套件全 PASS（6 控件 / lifecycle / Bind wiring / Stack warning / E2E）
- [ ] `git log --oneline | wc -l` ≥ 35（初始 + 设计 spec + README rename + 32 task commit）

接下来由 M2 处理 `<Template>` / `<Param>` / `<Slot>` / `{{}}` / `if`，参见 spec §7。

---

## 已知约束与未来工作

- **id 在 M1 是平面命名空间**：还没有 `screen.Get("a/b")` 路径访问；M2 加模板时一并加入
- **变体属性 `attr.var`**：M1 完全不识别；带 `.` 的 attr 直接被 ControlMeta 拒绝（属于 unknown 属性）。M3 实现
- **Import**：parser 不识别；M4 实现
- **ScreenView 抽象**：spec §9.6 标为可选；M1 仅提供过程式 API；M2 视需求加
- **Editor 内热重载**：M4 实现
- **prefab/UIAttr 一致性校验**：注册期还未校验 prefab 内是否真存在对应组件；M2 起补
