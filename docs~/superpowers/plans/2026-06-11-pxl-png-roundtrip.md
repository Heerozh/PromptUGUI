# `.pxl` ↔ PNG 双向往返 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PxlImporter` 资产的自定义 Inspector：只读信息面板 + Export PNG（节→PNG）+ Sync from PNG（PNG 像素就地回写 `.pxl` grid 块，`.pxl` 保持元数据唯一事实来源）。

**Architecture:** UI 薄壳（`PxlImporterEditor`）与可直测核心分离：`PxlPngExporter`（节→PNG 字节/命名约定/sourceFolder 防呆谓词）、`PxlPngSync`（`BuildPlan` 产出纯数据 `SyncPlan` → `Apply` 文本手术）。`PxlParser` 加 internal 源码行区间（grid 块/chars 块）与字符声明顺序，格式语义零变化。

**Tech Stack:** Unity 6 Editor（`ScriptedImporterEditor`、`ImageConversion`）、NUnit（`PromptUGUI.Tests.EditorOnly`）、Unity MCP 跑测、`.lint` dotnet format。

**Spec:** `docs~/superpowers/specs/2026-06-11-pxl-png-roundtrip-design.md`（必读）

**约定提醒（CLAUDE.md）：** 全部工作在 `feat/pxl-png-roundtrip` 分支（已建，spec 已是首 commit）；测试走 Unity MCP（refresh → console 无错 → run_tests + get_test_job 轮询，job 会经历 transient disconnect，带 wait_timeout 持续 poll）；每次 commit 前跑 `.lint` whitespace+style；新 `.cs` 的 `.meta` 必须随源码一起 commit；**禁止 `dotnet format analyzers --severity info`**；禁止 commit 到 main。

---

## File Structure

| 文件 | 职责 |
|---|---|
| Modify `Editor/Pxl/PxlParser.cs` | `PxlSection.GridStartLine/GridEndLine`、`PxlDocument.CharsHeaderLine/CharsLastEntryLine/CharOrder`（全 internal 用途字段；解析逻辑不变） |
| Modify `Editor/Pxl/PxlImporter.cs` | `BuildTexture`、`FindPalettePath` 从 private 改 internal（exporter/editor 复用，零行为变化） |
| Create `Editor/Pxl/PxlPngExporter.cs` | 命名约定 `FileNameFor`、节→PNG 字节 `EncodeSection`、`IsUnderAnySpriteSetSourceFolder` |
| Create `Editor/Pxl/PxlPngSync.cs` | `PngImage` 结构、`SyncPlan`、`BuildPlan`（配对+颜色映射+错误收集）、`Apply`（文本手术） |
| Create `Editor/Pxl/PxlImporterEditor.cs` | `[CustomEditor(typeof(PxlImporter))]`：信息面板 + 两按钮 + 面板/确认框分发（薄壳，无业务逻辑） |
| Test `Tests/EditMode/Editor/Pxl/PxlParserSpanTests.cs` | 行区间与 CharOrder 跟踪 |
| Test `Tests/EditMode/Editor/Pxl/PxlPngExporterTests.cs` | 命名/编码/防呆谓词 |
| Test `Tests/EditMode/Editor/Pxl/PxlPngSyncTests.cs` | 配对、颜色映射、文本手术、三条不变量（spec §4.4） |
| Modify `.claude/skills/authoring-promptugui-pxl/SKILL.md` | "Round-trip with art tools" 小节 |

**关键既有代码事实**（实现前自查）：
- `PxlParser.Parse`（`Editor/Pxl/PxlParser.cs:41`）：CRLF→LF + BOM strip 后按行状态机；grid 行在 `ValidateRow`（`:166`）收集；chars 条目在 inChars 分支（`:83-98`）；`chars:` 头在 `:127`。行号 `lineNo = i + 1`（1-based，基于 LF 规范化后的行数组）。
- `PxlImporter.BuildTexture(PxlSection, IReadOnlyDictionary<char,Color32>, string)`（private static）：RGBA32 / Point / Clamp / readable，grid top-down → texture bottom-up 翻转。`FindPalettePath(string, out string)`（private static）：全项目唯一 `<name>.gpl`。
- `PxlColorResolver.Resolve(doc, palette)` → `Dictionary<char, Color32>`（'.' 恒透明已含）。
- `GplPalette.Entries`（`List<(Color32 color, string name)>`，name 可 null）、`ContainsRgb`（忽略 alpha）、`TryGetByName`。
- `SpriteAtlasSyncer.FindAllSpriteSets()`（public）+ `SpriteSet.SourceFolderPath`（Editor-only，"Assets/..." 形式或 null）。
- 测试惯例：temp 目录 `Assets/__test_*__`，绝对路径写文件 + `AssetDatabase.ImportAsset(ForceUpdate|ForceSynchronousImport)`，TearDown `DeleteAsset`；纯逻辑测试无需 AssetDatabase。
- `ImageConversion.LoadImage(tex, bytes)` 解码 PNG（编辑器内可用）；`tex.GetPixels32()` 返回 **bottom-up** 行序——与 grid top-down 互转都要翻转。

**MCP 测试命令模板**（各 Task 的跑测步骤指此，`<Group>` 换测试类名）：

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])        # 无编译错误再继续
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], group_names=["<Group>"])
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=60)           # transient disconnect 属正常，继续 poll
```

**Lint 命令模板**：`cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..`

---

### Task 0: plan 入库

- [ ] **Step 1: commit plan**

```bash
git add docs~/superpowers/plans/2026-06-11-pxl-png-roundtrip.md
git commit -m "docs: .pxl <-> PNG roundtrip implementation plan

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 1: PxlParser 行区间 + 字符声明顺序

**Files:**
- Modify: `Editor/Pxl/PxlParser.cs`
- Test: `Tests/EditMode/Editor/Pxl/PxlParserSpanTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Editor;

namespace PromptUGUI.Tests.Editor
{
    public class PxlParserSpanTests
    {
        // 行号注释（1-based）：
        // 1 chars:
        // 2   K: #000000
        // 3   W: #ffffff
        // 4 [a]
        // 5 grid:
        // 6   KW
        // 7   WK
        // 8 (blank)
        // 9 [b]
        // 10 grid:
        // 11   K
        private const string TwoSections =
            "chars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  KW\n  WK\n\n[b]\ngrid:\n  K\n";

        [Test]
        public void Grid_line_spans_recorded()
        {
            var doc = PxlParser.Parse(TwoSections);
            Assert.AreEqual(6, doc.Sections[0].GridStartLine);
            Assert.AreEqual(7, doc.Sections[0].GridEndLine);
            Assert.AreEqual(11, doc.Sections[1].GridStartLine);
            Assert.AreEqual(11, doc.Sections[1].GridEndLine);
        }

        [Test]
        public void Chars_block_lines_recorded()
        {
            var doc = PxlParser.Parse(TwoSections);
            Assert.AreEqual(1, doc.CharsHeaderLine);
            Assert.AreEqual(3, doc.CharsLastEntryLine);
        }

        [Test]
        public void Chars_lines_zero_when_absent()
        {
            var doc = PxlParser.Parse("grid:\n  .\n");
            Assert.AreEqual(0, doc.CharsHeaderLine);
            Assert.AreEqual(0, doc.CharsLastEntryLine);
        }

        [Test]
        public void Char_order_preserves_declaration_order()
        {
            var doc = PxlParser.Parse(TwoSections);
            Assert.AreEqual(new[] { 'K', 'W' }, doc.CharOrder.ToArray());
        }

        [Test]
        public void Grid_span_ignores_interleaved_comment_lines()
        {
            // 注释行夹在 grid 行之间：span 覆盖整个区间（含注释行）——
            // sync 替换该区间时注释会丢失，这是已声明的取舍（spec §4.3 实施裁决）。
            var doc = PxlParser.Parse("chars:\n  K: #000000\ngrid:\n  K\n# mid\n  K\n");
            Assert.AreEqual(4, doc.Sections[0].GridStartLine);
            Assert.AreEqual(6, doc.Sections[0].GridEndLine);
        }
    }
}
```

- [ ] **Step 2: refresh 确认 CS（新字段不存在）= Red**

- [ ] **Step 3: 实现**

`PxlDocument` 增加字段（紧跟现有字段后）：

```csharp
        // —— 以下为 Sync from PNG 文本手术用的源码定位信息（internal 用途，格式语义无关）——
        public int CharsHeaderLine;      // `chars:` 行号（1-based，last-wins）；0 = 无
        public int CharsLastEntryLine;   // 最后一条 chars 条目行号；0 = 无条目
        public readonly List<char> CharOrder = new(); // 声明顺序（颜色→字符反查取先声明者）
```

`PxlSection` 增加字段：

```csharp
        // grid 行在源文本中的 1-based 行区间（含端点；含夹在中间的注释行——sync 替换时一并消失）
        public int GridStartLine, GridEndLine;
```

`Parse` 中三处插桩：

1. chars 条目成功分支（`if (key != '.' && !doc.Chars.TryAdd(...))` 之后、`continue` 之前）：

```csharp
                        if (key != '.') doc.CharOrder.Add(key);
                        doc.CharsLastEntryLine = lineNo;
```

（注意：`TryAdd` 失败已 throw，走到这里必然添加成功；`.: transparent` 显式声明也更新 `CharsLastEntryLine` 但不进 `CharOrder`。）

2. `if (line == "chars:")` 分支：

```csharp
                if (line == "chars:") { inChars = true; doc.CharsHeaderLine = lineNo; continue; }
```

3. `ValidateRow` 末尾（`section.Height = section.Rows.Count;` 之后）追加两行——但 `ValidateRow` 没有拿到行号以外的状态，直接在其中写：

```csharp
            if (section.GridStartLine == 0) section.GridStartLine = lineNo;
            section.GridEndLine = lineNo;
```

（`ValidateRow` 已有 `lineNo` 参数。）

- [ ] **Step 4: refresh + 跑 `group_names=["PxlParserSpanTests"]`（5/5）+ `["PxlParserTests"]`（15/15 回归）**

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/PxlParser.cs Tests/EditMode/Editor/Pxl/PxlParserSpanTests.cs Tests/EditMode/Editor/Pxl/PxlParserSpanTests.cs.meta
git commit -m "feat(pxl): parser records grid/chars source line spans + char declaration order

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: PxlPngExporter（+ PxlImporter 两个成员转 internal）

**Files:**
- Modify: `Editor/Pxl/PxlImporter.cs`（`BuildTexture`、`FindPalettePath`：`private static` → `internal static`，零行为变化）
- Create: `Editor/Pxl/PxlPngExporter.cs`
- Test: `Tests/EditMode/Editor/Pxl/PxlPngExporterTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlPngExporterTests
    {
        [Test]
        public void FileNameFor_explicit_and_implicit_sections()
        {
            Assert.AreEqual("ok.pressed.png",
                PxlPngExporter.FileNameFor("ok", new PxlSection { Name = "pressed" }));
            Assert.AreEqual("ok.png",
                PxlPngExporter.FileNameFor("ok", new PxlSection { Name = null }));
        }

        [Test]
        public void EncodeSection_roundtrips_pixels()
        {
            var doc = PxlParser.Parse("chars:\n  K: #102030\ngrid:\n  K.\n  .K\n");
            var colors = PxlColorResolver.Resolve(doc, null);
            var bytes = PxlPngExporter.EncodeSection(doc.Sections[0], colors);

            var tex = new Texture2D(2, 2);
            Assert.IsTrue(ImageConversion.LoadImage(tex, bytes));
            // grid 第 1 行是顶行 → texture y=1
            Assert.AreEqual(new Color32(0x10, 0x20, 0x30, 255), (Color32)tex.GetPixel(0, 1));
            Assert.AreEqual(0, ((Color32)tex.GetPixel(1, 1)).a);
            Assert.AreEqual(new Color32(0x10, 0x20, 0x30, 255), (Color32)tex.GetPixel(1, 0));
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void IsUnderAnySpriteSetSourceFolder_detects_member_folder()
        {
            const string root = "Assets/__test_pxlexport__";
            if (!AssetDatabase.IsValidFolder(root))
                AssetDatabase.CreateFolder("Assets", "__test_pxlexport__");
            try
            {
                AssetDatabase.CreateFolder(root, "Icons");
                var set = ScriptableObject.CreateInstance<PromptUGUI.Application.SpriteSet>();
                var so = new SerializedObject(set);
                so.FindProperty("setName").stringValue = "exporttest";
                so.FindProperty("sourceFolder").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<DefaultAsset>(root + "/Icons");
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(set, root + "/exporttest.asset");

                Assert.IsTrue(PxlPngExporter.IsUnderAnySpriteSetSourceFolder(root + "/Icons"));
                Assert.IsTrue(PxlPngExporter.IsUnderAnySpriteSetSourceFolder(root + "/Icons/Sub"));
                Assert.IsFalse(PxlPngExporter.IsUnderAnySpriteSetSourceFolder(root));
                Assert.IsFalse(PxlPngExporter.IsUnderAnySpriteSetSourceFolder("Assets/Nowhere"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }
    }
}
```

- [ ] **Step 2: refresh 确认 Red**

- [ ] **Step 3: 实现**

`PxlImporter.cs`：把 `private static Texture2D BuildTexture(` 与 `private static string FindPalettePath(` 改为 `internal static`（其余不动）。

`Editor/Pxl/PxlPngExporter.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>工具 1：.pxl 节 → PNG（spec 2026-06-11-pxl-png-roundtrip §3）。
    /// 文件名是往返配对契约：显式节 = "&lt;basename&gt;.&lt;section&gt;.png"，
    /// 隐式单节 = "&lt;basename&gt;.png"。编码复用 PxlImporter.BuildTexture
    /// （同一像素构建路径），不依赖导入产物。</summary>
    internal static class PxlPngExporter
    {
        public static string FileNameFor(string baseName, PxlSection s) =>
            s.Name == null ? baseName + ".png" : $"{baseName}.{s.Name}.png";

        public static byte[] EncodeSection(PxlSection s,
            IReadOnlyDictionary<char, Color32> colors)
        {
            var tex = PxlImporter.BuildTexture(s, colors, s.Name ?? "section");
            try { return tex.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>导出目录落在任一 SpriteSet sourceFolder 之下时返回 true——
        /// 导出的 PNG 会被同步工具当作新 sprite 来源，产生重复 key/重复打包，
        /// UI 层据此弹确认警告。入参为 "Assets/..." 形式的项目相对路径。</summary>
        public static bool IsUnderAnySpriteSetSourceFolder(string assetsRelativeFolder)
        {
            if (string.IsNullOrEmpty(assetsRelativeFolder)) return false;
            var probe = assetsRelativeFolder.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var set in SpriteAtlasSyncer.FindAllSpriteSets())
            {
                if (set == null) continue;
                var folder = set.SourceFolderPath;
                if (string.IsNullOrEmpty(folder)) continue;
                var prefix = folder.TrimEnd('/') + "/";
                if (probe.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 4: refresh + 跑 `group_names=["PxlPngExporterTests"]`（3/3）+ `["PxlImporterTests"]`（8/8 回归，确认 internal 化无副作用）**

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/PxlImporter.cs Editor/Pxl/PxlPngExporter.cs Editor/Pxl/PxlPngExporter.cs.meta \
        Tests/EditMode/Editor/Pxl/PxlPngExporterTests.cs Tests/EditMode/Editor/Pxl/PxlPngExporterTests.cs.meta
git commit -m "feat(pxl): PxlPngExporter — section-to-PNG encoding + naming contract + sourceFolder guard

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: PxlPngSync.BuildPlan（配对 + 颜色映射 + 错误收集）

**Files:**
- Create: `Editor/Pxl/PxlPngSync.cs`（本 task 先实现 `PngImage`/`SyncPlan`/`BuildPlan`；`Apply` 在 Task 4）
- Test: `Tests/EditMode/Editor/Pxl/PxlPngSyncTests.cs`（本 task 写 BuildPlan 部分）

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlPngSyncTests
    {
        private static readonly Color32 K = new(0x00, 0x00, 0x00, 255);
        private static readonly Color32 W = new(0xff, 0xff, 0xff, 255);
        private static readonly Color32 T = new(0, 0, 0, 0);

        private static PxlPngSync.PngImage Img(int w, int h, params Color32[] px) =>
            new(w, h, px);

        private const string TwoSections =
            "chars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  KW\n  WK\n\n[b]\ngrid:\n  K\n";

        [Test]
        public void BuildPlan_matches_missing_and_extra()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["btn.a.png"] = Img(2, 2, K, W, W, K),
                ["btn.stray.png"] = Img(1, 1, K),
            };
            var plan = PxlPngSync.BuildPlan(TwoSections, "btn", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.Updates.Count);
            Assert.AreEqual("a", plan.Updates[0].Section.Name);
            Assert.AreEqual(new[] { "b" }, plan.MissingSections.ToArray());
            Assert.AreEqual(new[] { "btn.stray.png" }, plan.ExtraPngs.ToArray());
        }

        [Test]
        public void BuildPlan_implicit_section_matches_plain_name()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["dot.png"] = Img(1, 1, K),
            };
            var plan = PxlPngSync.BuildPlan(
                "chars:\n  K: #000000\ngrid:\n  K\n", "dot", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.Updates.Count);
        }

        [Test]
        public void BuildPlan_reuses_existing_chars_first_declared_wins()
        {
            // K 与 X 同色：反查必须取先声明的 K
            var text = "chars:\n  K: #000000\n  X: #000000\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, K) };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.IsEmpty(plan.NewChars);
            Assert.AreEqual("K", plan.Updates[0].Rows.Single());
        }

        [Test]
        public void BuildPlan_new_inline_color_gets_next_free_char()
        {
            var red = new Color32(255, 0, 0, 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, red) };
            var plan = PxlPngSync.BuildPlan(
                "chars:\n  A: #000000\ngrid:\n  A\n", "d", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.NewChars.Count);
            Assert.AreEqual('B', plan.NewChars[0].ch); // A 已占用 → 字母表下一个
            Assert.AreEqual("#ff0000", plan.NewChars[0].value);
        }

        [Test]
        public void BuildPlan_palette_mode_named_color_and_alpha_variant()
        {
            var palette = GplPalette.Parse("GIMP Palette\n26 28 44\tnight\n");
            var night = new Color32(26, 28, 44, 255);
            var nightHalf = new Color32(26, 28, 44, 128);
            var text = "palette: @p\nchars:\n  K: night\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(2, 1, night, nightHalf),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, palette);
            Assert.IsEmpty(plan.Errors);
            // night 复用 K；半透明变体是新颜色 → hex+alpha 写法
            Assert.AreEqual(1, plan.NewChars.Count);
            Assert.AreEqual("#1a1c2c80", plan.NewChars[0].value);
        }

        [Test]
        public void BuildPlan_offpalette_color_errors_with_coordinate()
        {
            var palette = GplPalette.Parse("GIMP Palette\n26 28 44\tnight\n");
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(1, 1, new Color32(1, 2, 3, 255)),
            };
            var plan = PxlPngSync.BuildPlan(
                "palette: @p\nchars:\n  K: night\ngrid:\n  K\n", "d", pngs, palette);
            Assert.AreEqual(1, plan.Errors.Count);
            StringAssert.Contains("#010203", plan.Errors[0]);
            StringAssert.Contains("(0,0)", plan.Errors[0]);
        }

        [Test]
        public void BuildPlan_transparent_maps_to_dot_regardless_of_rgb()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(1, 1, new Color32(99, 99, 99, 0)),
            };
            var plan = PxlPngSync.BuildPlan(
                "chars:\n  K: #000000\ngrid:\n  K\n", "d", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(".", plan.Updates[0].Rows.Single());
            Assert.IsEmpty(plan.NewChars);
        }

        [Test]
        public void BuildPlan_resize_violating_border_errors()
        {
            var text = "chars:\n  K: #000000\n[a]\nborder: 2,0,2,0\ngrid:\n  KKKKK\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.a.png"] = Img(3, 1, K, K, K), // L+R=4 > 3
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.AreEqual(1, plan.Errors.Count);
            StringAssert.Contains("border", plan.Errors[0]);
        }

        [Test]
        public void BuildPlan_alphabet_exhaustion_errors()
        {
            // 90 个不同颜色，远超可用字符
            var px = new Color32[90];
            for (var i = 0; i < 90; i++) px[i] = new Color32((byte)i, (byte)(i * 2), (byte)(i + 7), 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(90, 1, px) };
            var plan = PxlPngSync.BuildPlan("chars:\n  K: #000000\ngrid:\n  K\n", "d", pngs, null);
            Assert.IsTrue(plan.Errors.Any(e => e.Contains("quantize")));
        }

        [Test]
        public void BuildPlan_new_chars_without_chars_block_errors()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, K) };
            var plan = PxlPngSync.BuildPlan("grid:\n  .\n", "d", pngs, null);
            Assert.IsTrue(plan.Errors.Any(e => e.Contains("chars:")));
        }
    }
}
```

- [ ] **Step 2: refresh 确认 Red**

- [ ] **Step 3: 实现**

`Editor/Pxl/PxlPngSync.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>工具 2：PNG 像素就地回写 .pxl（spec 2026-06-11-pxl-png-roundtrip §4）。
    /// BuildPlan 产出纯数据计划（不碰文件/UI），Apply 做文本手术。
    /// .pxl 是元数据唯一事实来源：sync 只更新已有节的 grid 与追加 chars 条目。</summary>
    internal static class PxlPngSync
    {
        /// <summary>解码后的 PNG：Pixels 为 top-down 行主序（调用方负责把
        /// GetPixels32 的 bottom-up 翻转过来）。</summary>
        public readonly struct PngImage
        {
            public readonly int Width, Height;
            public readonly Color32[] Pixels;
            public PngImage(int width, int height, Color32[] pixels)
            { Width = width; Height = height; Pixels = pixels; }
        }

        public sealed class SectionUpdate
        {
            public PxlSection Section;
            public int NewWidth, NewHeight;
            public readonly List<string> Rows = new(); // 已映射为字符的新 grid 行（top-down）
        }

        public sealed class SyncPlan
        {
            public readonly List<SectionUpdate> Updates = new();
            public readonly List<string> MissingSections = new(); // 没找到 PNG 的节（显示名）
            public readonly List<string> ExtraPngs = new();       // 前缀匹配但无对应节
            public readonly List<(char ch, string value)> NewChars = new();
            public readonly List<string> Errors = new();          // 非空 = 不可执行
        }

        // 新字符分配字母表：A-Z a-z 0-9，再排除保留字符的其余可打印 ASCII。
        private const string Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
            "!\"$%&'()*+,-/:;<=>?@\\^_`{|}~";

        public static SyncPlan BuildPlan(string pxlText, string baseName,
            IReadOnlyDictionary<string, PngImage> pngs, GplPalette palette)
        {
            var plan = new SyncPlan();
            PxlDocument doc;
            try { doc = PxlParser.Parse(pxlText); }
            catch (PxlParseException ex)
            {
                plan.Errors.Add($"cannot parse .pxl: {ex.Message}");
                return plan;
            }

            // 颜色→字符反查：按声明顺序，先声明者占据颜色（'.' 透明单独处理）。
            var resolved = PxlColorResolver.Resolve(doc, palette);
            var colorToChar = new Dictionary<Color32, char>();
            var usedChars = new HashSet<char>(doc.CharOrder) { '.', '#', '[', ']' };
            foreach (var ch in doc.CharOrder)
            {
                var c = resolved[ch];
                if (c.a == 0) continue; // 透明值一律走 '.'
                if (!colorToChar.ContainsKey(c)) colorToChar[c] = ch; // 先声明者占据颜色
            }

            var alphabetCursor = 0;
            var matchedFiles = new HashSet<string>(StringComparer.Ordinal);

            foreach (var section in doc.Sections)
            {
                var fileName = PxlPngExporter.FileNameFor(baseName, section);
                if (!pngs.TryGetValue(fileName, out var img))
                {
                    plan.MissingSections.Add(section.Name ?? baseName);
                    continue;
                }
                matchedFiles.Add(fileName);

                // 尺寸变化后 border 必须仍然成立（不静默改元数据）。
                if (section.Border.x + section.Border.z > img.Width ||
                    section.Border.y + section.Border.w > img.Height)
                {
                    plan.Errors.Add(
                        $"[{section.Name ?? baseName}]: border " +
                        $"({section.Border.x},{section.Border.y},{section.Border.z},{section.Border.w}) " +
                        $"exceeds new size {img.Width}x{img.Height}; fix the border: line first");
                    continue;
                }

                var update = new SectionUpdate
                { Section = section, NewWidth = img.Width, NewHeight = img.Height };
                var ok = true;
                var rowChars = new System.Text.StringBuilder(img.Width);
                for (var y = 0; y < img.Height && ok; y++)
                {
                    rowChars.Clear();
                    for (var x = 0; x < img.Width; x++)
                    {
                        var px = img.Pixels[y * img.Width + x];
                        if (px.a == 0) { rowChars.Append('.'); continue; }
                        if (colorToChar.TryGetValue(px, out var existing))
                        { rowChars.Append(existing); continue; }

                        // 新颜色
                        if (palette != null && !palette.ContainsRgb(px))
                        {
                            plan.Errors.Add(
                                $"[{section.Name ?? baseName}]({x},{y}): " +
                                $"#{px.r:x2}{px.g:x2}{px.b:x2} is not on the palette; " +
                                $"add it to the .gpl or fix it in the art tool");
                            ok = false;
                            break;
                        }
                        if (doc.CharsHeaderLine == 0)
                        {
                            plan.Errors.Add(
                                "new colors found but the file has no 'chars:' block; add one first");
                            ok = false;
                            break;
                        }
                        char newCh = default;
                        var found = false;
                        while (alphabetCursor < Alphabet.Length)
                        {
                            var cand = Alphabet[alphabetCursor++];
                            if (usedChars.Add(cand)) { newCh = cand; found = true; break; }
                        }
                        if (!found)
                        {
                            plan.Errors.Add(
                                "ran out of palette characters — this image is not " +
                                "limited-palette pixel art; quantize first");
                            ok = false;
                            break;
                        }
                        plan.NewChars.Add((newCh, ValueFor(px, palette)));
                        colorToChar[px] = newCh;
                        rowChars.Append(newCh);
                    }
                    if (ok) update.Rows.Add(rowChars.ToString());
                }
                if (ok) plan.Updates.Add(update);
            }

            foreach (var name in pngs.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (matchedFiles.Contains(name)) continue;
                if (name.StartsWith(baseName + ".", StringComparison.Ordinal) ||
                    name == baseName + ".png")
                {
                    plan.ExtraPngs.Add(name);
                }
            }
            return plan;
        }

        // 新 chars 条目的值写法：palette 模式且整 alpha 且命中有名条目 → 色名；否则 hex。
        private static string ValueFor(Color32 px, GplPalette palette)
        {
            if (palette != null && px.a == 255)
            {
                foreach (var (color, name) in palette.Entries)
                {
                    if (name != null && color.r == px.r && color.g == px.g && color.b == px.b)
                        return name;
                }
            }
            return px.a == 255
                ? $"#{px.r:x2}{px.g:x2}{px.b:x2}"
                : $"#{px.r:x2}{px.g:x2}{px.b:x2}{px.a:x2}";
        }
    }
}
```

注意 `Dictionary<Color32, char>`：`Color32` 没有自定义 GetHashCode，但它是 struct，默认值相等语义按字段比较（ValueType.Equals）——可用但慢；数据量小（≤80 色 × 节像素数 ≤ ~2304）完全够。若 lint/评审嫌弃，可换 `Dictionary<uint, char>`（RGBA 打包成 uint：`(uint)(px.r<<24|px.g<<16|px.b<<8|px.a)`），行为不变。

- [ ] **Step 4: refresh + 跑 `group_names=["PxlPngSyncTests"]`（10/10）**

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/PxlPngSync.cs Editor/Pxl/PxlPngSync.cs.meta \
        Tests/EditMode/Editor/Pxl/PxlPngSyncTests.cs Tests/EditMode/Editor/Pxl/PxlPngSyncTests.cs.meta
git commit -m "feat(pxl): PxlPngSync.BuildPlan — PNG pairing, color-to-char mapping, palette gate

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: PxlPngSync.Apply（文本手术）+ 三条不变量

**Files:**
- Modify: `Editor/Pxl/PxlPngSync.cs`（加 `Apply`）
- Modify: `Tests/EditMode/Editor/Pxl/PxlPngSyncTests.cs`（加 Apply 与不变量测试）

- [ ] **Step 1: 写失败测试（追加到 PxlPngSyncTests）**

```csharp
        // ---- Apply（文本手术）----

        private static PxlPngSync.PngImage FromSection(string pxlText, int sectionIndex)
        {
            // 用 exporter 编码再无损读回，模拟"导出后未修改"的 PNG
            var doc = PxlParser.Parse(pxlText);
            var colors = PxlColorResolver.Resolve(doc, null);
            var bytes = PxlPngExporter.EncodeSection(doc.Sections[sectionIndex], colors);
            var tex = new Texture2D(2, 2);
            ImageConversion.LoadImage(tex, bytes);
            var bottomUp = tex.GetPixels32();
            var w = tex.width; var h = tex.height;
            var topDown = new Color32[w * h];
            for (var y = 0; y < h; y++)
                System.Array.Copy(bottomUp, (h - 1 - y) * w, topDown, y * w, w);
            Object.DestroyImmediate(tex);
            return new PxlPngSync.PngImage(w, h, topDown);
        }

        [Test]
        public void Apply_roundtrip_is_byte_identical()
        {
            // spec §4.4 不变量 1：Export → 不改 PNG → Sync → 文本逐字节不变
            const string text =
                "# header comment\npalette: @p\nppu: 16\nchars:\n  K: night\n  W: #f4f4f4\n" +
                "[a]\nborder: 1,1,1,1\ngrid:\n  KKK\n  KWK\n  KKK\n\n[b]\ngrid:\n  WW\n";
            var palette = GplPalette.Parse("GIMP Palette\n26 28 44\tnight\n244 244 244\tpaper\n");
            var doc = PxlParser.Parse(text);
            var colors = PxlColorResolver.Resolve(doc, palette);
            var pngs = new Dictionary<string, PxlPngSync.PngImage>();
            foreach (var (s, i) in doc.Sections.Select((s, i) => (s, i)))
            {
                var bytes = PxlPngExporter.EncodeSection(s, colors);
                var tex = new Texture2D(2, 2);
                ImageConversion.LoadImage(tex, bytes);
                var bottomUp = tex.GetPixels32();
                var topDown = new Color32[tex.width * tex.height];
                for (var y = 0; y < tex.height; y++)
                    System.Array.Copy(bottomUp, (tex.height - 1 - y) * tex.width,
                        topDown, y * tex.width, tex.width);
                pngs[PxlPngExporter.FileNameFor("d", s)] =
                    new PxlPngSync.PngImage(tex.width, tex.height, topDown);
                Object.DestroyImmediate(tex);
            }
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, palette);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(text, PxlPngSync.Apply(text, plan));
        }

        [Test]
        public void Apply_updates_grid_preserves_everything_else()
        {
            const string text =
                "# keep me\nchars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  KW\n  WK\n\n[b]\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.a.png"] = Img(2, 2, W, W, W, W),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            var result = PxlPngSync.Apply(text, plan);
            Assert.AreEqual(
                "# keep me\nchars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  WW\n  WW\n\n[b]\ngrid:\n  K\n",
                result);
        }

        [Test]
        public void Apply_resize_changes_row_count()
        {
            const string text = "chars:\n  K: #000000\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(2, 3, K, K, K, K, K, K),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.AreEqual("chars:\n  K: #000000\ngrid:\n  KK\n  KK\n  KK\n",
                PxlPngSync.Apply(text, plan));
        }

        [Test]
        public void Apply_appends_new_chars_after_last_entry()
        {
            const string text = "chars:\n  K: #000000\ngrid:\n  K\n";
            var red = new Color32(255, 0, 0, 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, red) };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.AreEqual("chars:\n  K: #000000\n  A: #ff0000\ngrid:\n  A\n",
                PxlPngSync.Apply(text, plan));
        }

        [Test]
        public void Apply_result_reimports_with_identical_pixels()
        {
            // spec §4.4 不变量 2（像素保真）+ 3（确定性）：同一输入两次 BuildPlan/Apply 结果一致
            const string text = "chars:\n  K: #000000\ngrid:\n  K.\n  .K\n";
            var blue = new Color32(0, 0, 255, 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(2, 2, blue, T, T, K),
            };
            var r1 = PxlPngSync.Apply(text, PxlPngSync.BuildPlan(text, "d", pngs, null));
            var r2 = PxlPngSync.Apply(text, PxlPngSync.BuildPlan(text, "d", pngs, null));
            Assert.AreEqual(r1, r2);
            // 像素保真：解析回写结果再 resolve，逐像素比对
            var doc = PxlParser.Parse(r1);
            var colors = PxlColorResolver.Resolve(doc, null);
            var s = doc.Sections[0];
            Assert.AreEqual(blue, colors[s.Rows[0][0]]);
            Assert.AreEqual((byte)0, colors[s.Rows[0][1]].a);
            Assert.AreEqual(K, colors[s.Rows[1][1]]);
        }

        [Test]
        public void Apply_crlf_input_normalized_to_lf()
        {
            // spec §6 钉住：回写统一 \n
            var text = "chars:\r\n  K: #000000\r\ngrid:\r\n  K\r\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, W) };
            // W 是新颜色 → 文本必有变化，顺带验证 CRLF 路径
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            var result = PxlPngSync.Apply(text, plan);
            StringAssert.DoesNotContain("\r", result);
        }
```

- [ ] **Step 2: refresh 确认 Red（Apply 不存在）**

- [ ] **Step 3: 实现 `Apply`（追加到 PxlPngSync 类）**

```csharp
        /// <summary>文本手术：替换各更新节的 grid 行区间 + 在 chars 块末尾追加新条目。
        /// 其余内容（header/注释/未匹配节）逐字节保留。输入 CRLF 统一为 LF（spec §6）。
        /// 注意：夹在 grid 行之间的注释行位于替换区间内，会随替换消失（spec §4.3 取舍）。</summary>
        public static string Apply(string pxlText, SyncPlan plan)
        {
            if (plan.Errors.Count > 0)
                throw new InvalidOperationException("cannot apply a plan with errors");

            var lines = new List<string>(
                pxlText.TrimStart('﻿').Replace("\r\n", "\n").Split('\n'));

            // 收集编辑（1-based 行号），按行号从大到小执行，前面的索引不受影响。
            var edits = new List<(int start, int end, List<string> replacement)>();
            foreach (var u in plan.Updates)
            {
                var replacement = new List<string>(u.Rows.Count);
                foreach (var row in u.Rows) replacement.Add("  " + row);
                edits.Add((u.Section.GridStartLine, u.Section.GridEndLine, replacement));
            }
            if (plan.NewChars.Count > 0)
            {
                // 解析时已校验 CharsHeaderLine != 0（BuildPlan 出错路径拦截）。
                var doc = PxlParser.Parse(string.Join("\n", lines));
                var insertAfter = doc.CharsLastEntryLine != 0
                    ? doc.CharsLastEntryLine
                    : doc.CharsHeaderLine;
                var entries = new List<string>(plan.NewChars.Count);
                foreach (var (ch, value) in plan.NewChars) entries.Add($"  {ch}: {value}");
                // 插入建模为"替换 insertAfter 行自身 = 原行 + 新行"
                var replacement = new List<string> { lines[insertAfter - 1] };
                replacement.AddRange(entries);
                edits.Add((insertAfter, insertAfter, replacement));
            }

            edits.Sort((a, b) => b.start.CompareTo(a.start));
            foreach (var (start, end, replacement) in edits)
            {
                lines.RemoveRange(start - 1, end - start + 1);
                lines.InsertRange(start - 1, replacement);
            }
            return string.Join("\n", lines);
        }
```

实现提示：`Apply` 里重新 `PxlParser.Parse` 一次取 `CharsLastEntryLine` 看似多余——直接用 BuildPlan 时的 doc 也行，但 BuildPlan/Apply 以纯函数对（text in → text out）解耦更直测；行号针对的是同一 LF 规范化文本，BuildPlan 与 Apply 必须用**同一来源行号**。最简单稳妥：`SyncPlan` 直接携带 `CharsInsertAfterLine` 字段（BuildPlan 计算好），Apply 不再二次解析。**实现采用后者**：`SyncPlan` 加 `public int CharsInsertAfterLine;`，BuildPlan 在 doc 解析后赋值 `doc.CharsLastEntryLine != 0 ? doc.CharsLastEntryLine : doc.CharsHeaderLine`，上面 Apply 代码里删掉重解析两行改用 `plan.CharsInsertAfterLine`。

- [ ] **Step 4: refresh + 跑 `group_names=["PxlPngSyncTests"]`（16/16）**

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/PxlPngSync.cs Tests/EditMode/Editor/Pxl/PxlPngSyncTests.cs
git commit -m "feat(pxl): PxlPngSync.Apply — surgical grid/chars rewrite with roundtrip invariants

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: PxlImporterEditor（信息面板 + 两按钮）

**Files:**
- Create: `Editor/Pxl/PxlImporterEditor.cs`

UI 薄壳无自动化测试（spec §6）；验收 = 编译干净 + MCP `execute_code` 冒烟（创建 .pxl → 反射实例化 editor 跑一次 OnInspectorGUI 不抛异常即可，做不到也接受 console-clean + 用户视觉 QA）。

- [ ] **Step 1: 实现**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>.pxl 资产 Inspector（spec 2026-06-11-pxl-png-roundtrip §2）：
    /// 只读信息面板（调色板/节表/预览）+ Export PNG + Sync from PNG。
    /// importer 无可序列化设置——所有参数都在 .pxl 文本里，本面板不提供任何可改项。</summary>
    [CustomEditor(typeof(PxlImporter))]
    internal sealed class PxlImporterEditor : ScriptedImporterEditor
    {
        private const string ExportDirPrefPrefix = "PromptUGUI.Pxl.ExportDir.";

        private PxlDocument _doc;          // null = 解析失败
        private string _parseError;
        private GplPalette _palette;       // null = 内联模式或解析失败
        private string _palettePath;

        private string AssetPath => ((AssetImporter)target).assetPath;
        private string BaseName => Path.GetFileNameWithoutExtension(AssetPath);
        private string PrefKey => ExportDirPrefPrefix + AssetDatabase.AssetPathToGUID(AssetPath);

        public override void OnEnable()
        {
            base.OnEnable();
            Reload();
        }

        private void Reload()
        {
            _doc = null; _parseError = null; _palette = null; _palettePath = null;
            try
            {
                var doc = PxlParser.Parse(File.ReadAllText(AssetPath));
                if (doc.PaletteRef != null)
                {
                    _palettePath = PxlImporter.FindPalettePath(doc.PaletteRef, out var error);
                    if (_palettePath == null) { _parseError = error; return; }
                    _palette = GplPalette.Parse(File.ReadAllText(_palettePath));
                }
                _doc = doc;
            }
            catch (PxlParseException ex) { _parseError = ex.Message; }
            catch (System.FormatException ex) { _parseError = ex.Message; }
            catch (IOException ex) { _parseError = ex.Message; }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_parseError != null)
            {
                EditorGUILayout.HelpBox(_parseError, MessageType.Error);
            }
            else if (_doc != null)
            {
                DrawInfoPanel();
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_doc == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Export PNG...")) ExportPng();
                    if (GUILayout.Button("Sync from PNG...")) SyncFromPng();
                }
            }
            EditorGUILayout.HelpBox(
                "All settings (ppu / border / palette / pixels) live in the .pxl text file.",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }

        private void DrawInfoPanel()
        {
            EditorGUILayout.LabelField("Palette",
                _doc.PaletteRef == null
                    ? "inline"
                    : $"@{_doc.PaletteRef}  ({_palettePath})");
            if (_palettePath != null && GUILayout.Button("Ping .gpl", GUILayout.Width(80)))
                EditorGUIUtility.PingObject(
                    AssetDatabase.LoadAssetAtPath<Object>(_palettePath));

            EditorGUILayout.LabelField("Sections", EditorStyles.boldLabel);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(AssetPath)
                .OfType<Sprite>().ToDictionary(s => s.name, s => s);
            foreach (var s in _doc.Sections)
            {
                var name = s.Name ?? BaseName;
                var border = s.Border == Vector4.zero
                    ? "—"
                    : $"{s.Border.x},{s.Border.y},{s.Border.z},{s.Border.w}";
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                    if (sprites.TryGetValue(name, out var sp) && sp.texture != null)
                        GUI.DrawTexture(rect, sp.texture, ScaleMode.ScaleToFit);
                    EditorGUILayout.LabelField($"[{name}]  {s.Width}×{s.Height}  border: {border}");
                }
            }
        }

        private void ExportPng()
        {
            var dir = EditorUtility.SaveFolderPanel("Export .pxl sections as PNG",
                EditorPrefs.GetString(PrefKey, ""), "");
            if (string.IsNullOrEmpty(dir)) return;
            EditorPrefs.SetString(PrefKey, dir);

            var assetsRel = AbsoluteToAssetsPath(dir);
            if (assetsRel != null && PxlPngExporter.IsUnderAnySpriteSetSourceFolder(assetsRel))
            {
                if (!EditorUtility.DisplayDialog("Export into a SpriteSet source folder?",
                        "The chosen folder is inside a SpriteSet sourceFolder. Exported PNGs " +
                        "will be picked up as NEW sprite sources (duplicate keys/packing).\n\n" +
                        "Export anyway?", "Export", "Cancel"))
                {
                    return;
                }
            }

            var colors = PxlColorResolver.Resolve(_doc, _palette);
            foreach (var s in _doc.Sections)
            {
                File.WriteAllBytes(Path.Combine(dir, PxlPngExporter.FileNameFor(BaseName, s)),
                    PxlPngExporter.EncodeSection(s, colors));
            }
            if (assetsRel != null) AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(dir);
        }

        private void SyncFromPng()
        {
            var dir = EditorUtility.OpenFolderPanel("Sync .pxl from PNG",
                EditorPrefs.GetString(PrefKey, ""), "");
            if (string.IsNullOrEmpty(dir)) return;
            EditorPrefs.SetString(PrefKey, dir);

            var pngs = new Dictionary<string, PxlPngSync.PngImage>();
            foreach (var file in Directory.GetFiles(dir, BaseName + "*.png"))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(file)))
                { DestroyImmediate(tex); continue; }
                var bottomUp = tex.GetPixels32();
                var topDown = new Color32[tex.width * tex.height];
                for (var y = 0; y < tex.height; y++)
                    System.Array.Copy(bottomUp, (tex.height - 1 - y) * tex.width,
                        topDown, y * tex.width, tex.width);
                pngs[Path.GetFileName(file)] =
                    new PxlPngSync.PngImage(tex.width, tex.height, topDown);
                DestroyImmediate(tex);
            }

            var text = File.ReadAllText(AssetPath);
            var plan = PxlPngSync.BuildPlan(text, BaseName, pngs, _palette);

            if (plan.Errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Sync from PNG — errors",
                    string.Join("\n", plan.Errors), "OK");
                return;
            }
            if (plan.Updates.Count == 0)
            {
                EditorUtility.DisplayDialog("Sync from PNG",
                    "No matching PNGs found (naming: <basename>.<section>.png).", "OK");
                return;
            }

            var summary = new System.Text.StringBuilder();
            foreach (var u in plan.Updates)
                summary.AppendLine($"[{u.Section.Name ?? BaseName}] " +
                    $"{u.Section.Width}×{u.Section.Height} → {u.NewWidth}×{u.NewHeight}");
            if (plan.NewChars.Count > 0)
                summary.AppendLine("new chars: " +
                    string.Join(", ", plan.NewChars.Select(c => $"{c.ch}={c.value}")));
            foreach (var m in plan.MissingSections) summary.AppendLine($"skipped (no PNG): [{m}]");
            foreach (var e in plan.ExtraPngs) summary.AppendLine($"unmatched PNG: {e}");

            if (!EditorUtility.DisplayDialog("Sync from PNG?", summary.ToString(), "Sync", "Cancel"))
                return;

            File.WriteAllText(AssetPath, PxlPngSync.Apply(text, plan));
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            Reload();
        }

        // 绝对路径 → "Assets/..."；不在本工程内返回 null。
        private static string AbsoluteToAssetsPath(string absolute)
        {
            var dataPath = UnityEngine.Application.dataPath.Replace('\\', '/');
            var abs = absolute.Replace('\\', '/');
            if (!abs.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase)) return null;
            return "Assets" + abs.Substring(dataPath.Length);
        }
    }
}
```

- [ ] **Step 2: refresh + console 无编译错误；全量 EditorOnly 回归（基线 229 + 本计划新增 ~24）**

- [ ] **Step 3: MCP 冒烟（host 工程，尽力而为）**

`execute_code`：在 `Assets/__pxl_edsmoke__` 写一个双节 `.pxl` → import → `UnityEditor.Editor.CreateEditor(AssetImporter.GetAtPath(path))` 得到 editor 实例，断言类型名 `PxlImporterEditor`；（OnInspectorGUI 需要 GUI 上下文，不强行调用）。再直接调核心链路模拟按钮：`PxlColorResolver` + `PxlPngExporter.EncodeSection` 写 PNG 到 `Temp/`，`PxlPngSync.BuildPlan/Apply` 回写后 ImportAsset 无错。清理目录。

- [ ] **Step 4: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/PxlImporterEditor.cs Editor/Pxl/PxlImporterEditor.cs.meta
git commit -m "feat(pxl): PxlImporterEditor — info panel + Export PNG + Sync from PNG

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: SKILL 文档 + 收尾

**Files:**
- Modify: `.claude/skills/authoring-promptugui-pxl/SKILL.md`

- [ ] **Step 1: 在 SKILL.md 加 "Round-trip with art tools" 小节**（英文；内容对照最终实现核实）：选中 `.pxl` 的 Inspector 有 Export PNG / Sync from PNG；命名约定 `<basename>.<section>.png`（隐式 `<basename>.png`）；`.pxl` 是元数据唯一事实来源（border/ppu/palette/字符分配），PNG 只携带像素；sync 越板色/字符耗尽/border 越界会报错中止；节增删走文本编辑；grid 行间注释会在 sync 时丢失；导出目录别选 SpriteSet sourceFolder。

- [ ] **Step 2: 三套全量测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])   # 229 + ~24
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])     # 1595 基线
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])     # 133 基线
```

- [ ] **Step 3: lint 终验**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 4: `git status --short` 确认无未跟踪 .meta；commit SKILL**

```bash
git add .claude/skills/authoring-promptugui-pxl/SKILL.md
git commit -m "docs(skill): pxl round-trip with art tools section

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: push + PR（superpowers:finishing-a-development-branch；提醒用户视觉 QA：Inspector 面板 + 导出→Aseprite 改→回写）**

---

## Self-Review 记录

- **Spec coverage**：§2 Inspector（Task 5）、§3 Export（Task 2+5）、§4.1-4.3（Task 3+4）、§4.4 不变量（Task 4 三测）、§5 代码组织（按表）、§6 测试矩阵（CRLF/注释保留/配对三态/越板坐标/耗尽/border 均有对应测试）、§7 SKILL（Task 6）。失败态按钮禁用 = `_doc == null` DisabledScope（Task 5）。
- **实施裁决（spec 未细化处，以本计划为准）**：grid 行间注释随区间替换丢失（测试钉住）；`SyncPlan.CharsInsertAfterLine` 由 BuildPlan 计算、Apply 不二次解析；无 chars 块且需要新字符 = 报错；`Color32` 字典键可换 uint 打包（行为不变）；Export 总是全节导出。
- **Type consistency**：`PngImage(w,h,Color32[] top-down)`、`SyncPlan.Updates[].Rows`（已映射字符行）、`FileNameFor(baseName, PxlSection)`、`CharsInsertAfterLine` 在 Task 3/4/5 间已交叉核对。
- **已知风险**：Task 4 的 `Apply` 代码块按"实现提示"采用 `SyncPlan.CharsInsertAfterLine` 方案（删去二次解析两行），实现者须按提示落地。
