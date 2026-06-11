# `.pxl` 像素网格文本资产 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 `.pxl` 文本格式（XPM 惯用法字符网格 + GIMP `.gpl` 项目级调色板）和 Editor ScriptedImporter，让 LLM 以文本直接产出带 9-slice/PPU 的 Unity Sprite，接入现有 SpriteSet → Sync Atlases 管线。

**Architecture:** 纯 C# 解析三件套（`GplPalette` / `PxlParser` / `PxlColorResolver`，全部 `Editor/Pxl/`，EditorOnly 直测）+ `PxlImporter`（ScriptedImporter，每节一张 point-filter Texture2D + Sprite sub-asset）+ `SpriteAtlasSyncer.EnumerateSpriteSources` 的 PxlImporter 分支（多节 key 派生）。消费端（SpriteSet entries / `<Icon>` / `<Image>` / 9-slice / InlineSpriteAssetBuilder）零改动。

**Tech Stack:** Unity 6 `UnityEditor.AssetImporters.ScriptedImporter`、NUnit（`PromptUGUI.Tests.EditorOnly`）、Unity MCP 跑测、`.lint` dotnet format。

**Spec:** `docs~/superpowers/specs/2026-06-11-pxl-pixel-sprite-importer-design.md`（必读，含格式完整定义）

**约定提醒（CLAUDE.md）：**
- 禁止 commit 到 main——全部工作在 `feat/pxl-importer` 分支。
- 测试一律走 Unity MCP（先 `refresh_unity` 等编译、`read_console` 查错，再 `run_tests` + `get_test_job` 轮询）。
- 每个源文件改动后跑 `.lint` 的 dotnet format 校验。
- 新建 `.cs` 文件后让 Unity refresh 生成 `.meta`，**`.meta` 必须随源文件一起 commit**（host 工程在 `C:\xsoft\PromptUGUIDev`，本仓库以 UPM file:// 引用）。

---

## File Structure

| 文件 | 职责 |
|---|---|
| Create `Editor/Pxl/GplPalette.cs` | GIMP `.gpl` 文本解析 + 色名/RGB 查询（纯 C#） |
| Create `Editor/Pxl/PxlParser.cs` | `.pxl` 文本 → `PxlDocument`/`PxlSection` IR + `PxlParseException`（纯 C#，含结构校验） |
| Create `Editor/Pxl/PxlColorResolver.cs` | chars 映射 → `char→Color32`（透明/hex/色名 + 越板校验，纯 C#） |
| Create `Editor/Pxl/PxlImporter.cs` | ScriptedImporter：调上面三件套 → Texture2D + Sprite sub-assets |
| Modify `Editor/SpriteAtlasSyncer.cs` | `EnumerateSpriteSources` 加 PxlImporter 分支（多节 key）；`ApplyTemplateFilterMode` pxl-only 文件夹 Point 兜底 |
| Test `Tests/EditMode/Editor/Pxl/GplPaletteTests.cs` | .gpl 解析全集 |
| Test `Tests/EditMode/Editor/Pxl/PxlParserTests.cs` | 格式全集 + 错误路径（带行号断言） |
| Test `Tests/EditMode/Editor/Pxl/PxlColorResolverTests.cs` | 颜色解析 + 越板/缺名错误 |
| Test `Tests/EditMode/Editor/Pxl/PxlImporterTests.cs` | AssetDatabase 集成：资产结构 / border / PPU / .gpl 依赖重导入 |
| Test `Tests/EditMode/Editor/Pxl/PxlSyncerTests.cs` | key 派生（单/多节）、与 PNG 混放、Reset 跳过 `.pxl`、atlas Point 兜底 |
| Create `.claude/skills/authoring-promptugui-pxl/SKILL.md` | LLM authoring 指南 |
| Modify `.claude/skills/authoring-promptugui-xml/reference/icons.md` | `.pxl` 来源指针 |

**关键既有代码事实**（实现前自查，防止接错）：
- `SpriteAtlasSyncer.EnumerateSpriteSources`（`Editor/SpriteAtlasSyncer.cs:368`）：key = 相对 sourceFolder 路径去扩展名（`Path.GetExtension` 通用剥除，`.pxl` 天然适用）；目前 `LoadAssetAtPath<Sprite>` 只取一个 Sprite——多节 `.pxl` 必须改走 `LoadAllAssetsAtPath().OfType<Sprite>()`。
- `EnsureSpriteImporter`（`:473`）：非 TextureImporter/AsepriteImporter 静默跳过 → `PxlImporter` 资产不被改设置，零改动。
- `ResetTextureImportSettings`（`:514`）/ `ApplyImportSettingsToFolder`（`:573`）/ `FindFirstTexture`（`:637`）：均有 `is (not) TextureImporter` guard → `.pxl` 自然跳过，只补测试。
- `BuildLookup`（`:440`）：裸名别名 = pathKey 最后一段，唯一时才提升——多节 key `Buttons/ok/pressed` 的裸名 `pressed` 撞名时自动不提升，零改动。
- `SpriteAtlasAutoSync.AnyUnder` 扩展名无关 → sourceFolder 下 `.pxl` 重导入（含 .gpl 依赖触发的）已会触发 repack，零改动。
- `InlineSpriteAssetBuilder.cs:72` 复用 `EnumerateSpriteSources` → 图文混排烘焙免费生效，**前提 importer 的 Texture2D 保持 readable**（`tex.Apply(false, false)`，不要 makeNoLongerReadable）。
- `PoFileImporter.cs` 是本仓库 ScriptedImporter 先例；`.pxl` 无原生 importer 抢注，用普通 `[ScriptedImporter(1, "pxl")]` 即可，不需要 override + postprocessor。
- 测试惯例照 `PoFileImporterTests.cs` / `SpriteAtlasSyncerTests.cs`：temp 文件夹 `Assets/__test_pxl__`，`File.WriteAllText` 用 `Application.dataPath` 绝对路径，`AssetDatabase.ImportAsset(path, ForceUpdate | ForceSynchronousImport)`，TearDown `DeleteAsset`。

**MCP 测试命令模板**（每个 Task 的"跑测试"步骤都指这里，`<Group>` 换成该 Task 的测试类名）：

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])        # 必须无编译错误再继续
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], group_names=["<Group>"])
mcp__UnityMCP__get_test_job(job_id=...)                            # 轮询到完成，读 pass/fail
```

**Lint 命令模板**（每个 commit 前）：

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
```

---

### Task 0: 分支 + spec/plan 入库

**Files:** 无代码。

- [ ] **Step 1: 建分支**

```bash
git checkout -b feat/pxl-importer
```

- [ ] **Step 2: commit spec + plan**

```bash
git add docs~/superpowers/specs/2026-06-11-pxl-pixel-sprite-importer-design.md \
        docs~/superpowers/plans/2026-06-11-pxl-pixel-sprite-importer.md
git commit -m "docs: .pxl pixel sprite importer spec + plan

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 1: `GplPalette`（.gpl 解析）

**Files:**
- Create: `Editor/Pxl/GplPalette.cs`
- Test: `Tests/EditMode/Editor/Pxl/GplPaletteTests.cs`

.gpl 格式：首个非空行必须是 `GIMP Palette`；随后 `Name:` / `Columns:` 头、`#` 注释行、空行均跳过；条目行 = `R G B [名字...]`（空白分隔，名字为行剩余部分，可缺省）。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class GplPaletteTests
    {
        private const string Sample = "GIMP Palette\nName: db8\nColumns: 4\n# comment\n" +
            "26 28 44\tdark-blue\n244 244 244\tWhite\n177 62 83\n";

        [Test]
        public void Parse_reads_entries_with_and_without_names()
        {
            var p = GplPalette.Parse(Sample);
            Assert.AreEqual(3, p.Entries.Count);
            Assert.AreEqual(new Color32(26, 28, 44, 255), p.Entries[0].color);
            Assert.AreEqual("dark-blue", p.Entries[0].name);
            Assert.IsNull(p.Entries[2].name); // unnamed entry
        }

        [Test]
        public void TryGetByName_normalizes_case_space_hyphen_underscore()
        {
            var p = GplPalette.Parse(Sample);
            Assert.IsTrue(p.TryGetByName("Dark Blue", out var c));
            Assert.AreEqual(new Color32(26, 28, 44, 255), c);
            Assert.IsTrue(p.TryGetByName("dark_blue", out _));
            Assert.IsTrue(p.TryGetByName("WHITE", out _));
            Assert.IsFalse(p.TryGetByName("nope", out _));
        }

        [Test]
        public void ContainsRgb_matches_ignoring_alpha()
        {
            var p = GplPalette.Parse(Sample);
            Assert.IsTrue(p.ContainsRgb(new Color32(177, 62, 83, 128)));
            Assert.IsFalse(p.ContainsRgb(new Color32(1, 2, 3, 255)));
        }

        [Test]
        public void Parse_missing_header_throws()
        {
            var ex = Assert.Throws<System.FormatException>(() => GplPalette.Parse("26 28 44\tx\n"));
            StringAssert.Contains("GIMP Palette", ex.Message);
        }

        [Test]
        public void Parse_malformed_entry_line_throws_with_line_number()
        {
            var ex = Assert.Throws<System.FormatException>(
                () => GplPalette.Parse("GIMP Palette\n26 28\tshort\n"));
            StringAssert.Contains("line 2", ex.Message);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

MCP 模板，`group_names=["GplPaletteTests"]`。Expected: 编译错误（GplPalette 不存在）——`read_console` 看到 CS0246 即算 Red 确认，先写实现再跑。

- [ ] **Step 3: 实现**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>GIMP Palette (.gpl) 文本解析。社区标准格式：Aseprite 原生读写、
    /// Lospec 全站可下载。条目 = "R G B [name]"，name 可缺省（缺省条目只能被 hex 命中）。</summary>
    internal sealed class GplPalette
    {
        public readonly List<(Color32 color, string name)> Entries = new();
        private readonly Dictionary<string, Color32> _byName = new(StringComparer.Ordinal);

        public static GplPalette Parse(string text)
        {
            var palette = new GplPalette();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var headerSeen = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (!headerSeen)
                {
                    if (line != "GIMP Palette")
                        throw new FormatException(
                            $"line {i + 1}: not a GIMP Palette file (expected 'GIMP Palette' header)");
                    headerSeen = true;
                    continue;
                }
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("Name:", StringComparison.Ordinal)) continue;
                if (line.StartsWith("Columns:", StringComparison.Ordinal)) continue;

                var parts = line.Split((char[])null, 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 ||
                    !byte.TryParse(parts[0], out var r) ||
                    !byte.TryParse(parts[1], out var g) ||
                    !byte.TryParse(parts[2], out var b))
                {
                    throw new FormatException($"line {i + 1}: expected 'R G B [name]', got '{line}'");
                }
                var name = parts.Length == 4 ? parts[3].Trim() : null;
                if (string.IsNullOrEmpty(name)) name = null;
                var color = new Color32(r, g, b, 255);
                palette.Entries.Add((color, name));
                if (name != null) palette._byName[Normalize(name)] = color;
            }
            if (!headerSeen)
                throw new FormatException("line 1: not a GIMP Palette file (expected 'GIMP Palette' header)");
            return palette;
        }

        public bool TryGetByName(string token, out Color32 color) =>
            _byName.TryGetValue(Normalize(token), out color);

        public bool ContainsRgb(Color32 c)
        {
            foreach (var (e, _) in Entries)
                if (e.r == c.r && e.g == c.g && e.b == c.b) return true;
            return false;
        }

        /// <summary>色名比较忽略大小写与空格/连字符/下划线差异（"Dark Blue" ≡ "dark-blue"）。</summary>
        public static string Normalize(string name) =>
            name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
    }
}
```

- [ ] **Step 4: refresh + 跑测试确认通过**

MCP 模板，`group_names=["GplPaletteTests"]`。Expected: 5/5 PASS。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/ Tests/EditMode/Editor/Pxl/
git commit -m "feat(pxl): GplPalette — GIMP .gpl text palette parser

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

（`Editor/Pxl/` 和 `Tests/.../Pxl/` 是新目录——确认 Unity refresh 已生成目录与文件的 `.meta` 并一并 add。）

---

### Task 2: `PxlParser`（.pxl 文本 → IR）

**Files:**
- Create: `Editor/Pxl/PxlParser.cs`
- Test: `Tests/EditMode/Editor/Pxl/PxlParserTests.cs`

格式细节（spec §2 的实现裁决，以此为准）：

- 行级语法。整行注释 = trim 后以 `#` 开头（任何位置，含 grid 块内；因此 `#` 禁作 chars key）。空行跳过（grid 块内空行 = grid 结束）。
- 文件头（首个 `[section]` 或隐式 grid 内容之前）：`palette: @<name>`（必须 `@` 前缀）、`ppu: <float>`（>0）、`chars:` 起 chars 块——后续每行 `<单字符>: <值>`（trim 后第 2 字符是 `:`），直到首个不匹配行。
- chars key 重复 = error；`.` 可显式声明但值必须 `transparent`；`#` 禁作 key。
- `[name]` 段头，名字限 `[A-Za-z0-9_-]+`；重名 = error。隐式单节 = 段头出现前就有 `border:`/`grid:`；隐式内容与显式段头混用 = error。
- 节内：`border: L,B,R,T`（4 个 ≥0 整数，须在 `grid:` 前）；`grid:` 起网格块，每行 trim 后即一行像素，直到空行/段头/EOF。grid 行字符必须 ∈ chars ∪ {`.`}（error 带行号与字符）；各行宽必须一致；节必须有非空 grid；border 须满足 L+R ≤ width 且 B+T ≤ height。
- 节外/头部出现无法识别的行 = error。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlParserTests
    {
        [Test]
        public void Parse_implicit_single_section()
        {
            var doc = PxlParser.Parse(
                "chars:\n  K: #1a1c2c\ngrid:\n  .KK.\n  K..K\n");
            Assert.IsNull(doc.PaletteRef);
            Assert.AreEqual(100f, doc.Ppu);
            Assert.AreEqual(1, doc.Sections.Count);
            Assert.IsNull(doc.Sections[0].Name);
            Assert.AreEqual(4, doc.Sections[0].Width);
            Assert.AreEqual(2, doc.Sections[0].Height);
            Assert.AreEqual(".KK.", doc.Sections[0].Rows[0]);
        }

        [Test]
        public void Parse_full_header_and_two_sections()
        {
            var doc = PxlParser.Parse(
                "palette: @main\nppu: 16\nchars:\n  K: dark-blue\n  W: #f4f4f4\n" +
                "# a comment\n" +
                "[normal]\nborder: 1,1,1,1\ngrid:\n  KKK\n  KWK\n  KKK\n\n" +
                "[pressed]\ngrid:\n  WW\n  WW\n");
            Assert.AreEqual("main", doc.PaletteRef);
            Assert.AreEqual(16f, doc.Ppu);
            Assert.AreEqual(2, doc.Chars.Count);
            Assert.AreEqual("dark-blue", doc.Chars['K']);
            Assert.AreEqual(2, doc.Sections.Count);
            Assert.AreEqual("normal", doc.Sections[0].Name);
            Assert.AreEqual(new Vector4(1, 1, 1, 1), doc.Sections[0].Border);
            Assert.AreEqual("pressed", doc.Sections[1].Name);
            Assert.AreEqual(Vector4.zero, doc.Sections[1].Border);
        }

        [Test]
        public void Parse_unknown_grid_char_reports_line()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  KK\n  KX\n"));
            Assert.AreEqual(5, ex.Line);
            StringAssert.Contains("'X'", ex.Message);
        }

        [Test]
        public void Parse_ragged_rows_reports_line()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  KKK\n  KK\n"));
            Assert.AreEqual(5, ex.Line);
        }

        [Test]
        public void Parse_duplicate_section_name_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n[a]\ngrid:\n  K\n[a]\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_duplicate_char_key_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n  K: #ffffff\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_dot_redefined_to_color_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  .: #000000\ngrid:\n  .\n"));
        }

        [Test]
        public void Parse_border_exceeding_size_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n[a]\nborder: 2,0,2,0\ngrid:\n  KKK\n"));
        }

        [Test]
        public void Parse_empty_grid_section_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n[a]\ngrid:\n[b]\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_palette_without_at_prefix_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "palette: main\nchars:\n  K: #000000\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_implicit_then_explicit_section_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  K\n[late]\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_unrecognized_line_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\nbogus directive\ngrid:\n  K\n"));
        }
    }
}
```

- [ ] **Step 2: refresh，确认 CS0246（Red）**

- [ ] **Step 3: 实现**

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PromptUGUI.Editor
{
    internal sealed class PxlParseException : Exception
    {
        public readonly int Line;
        public PxlParseException(int line, string message)
            : base($"line {line}: {message}") { Line = line; }
    }

    internal sealed class PxlDocument
    {
        public string PaletteRef;                              // "main" ← `palette: @main`; null = 纯内联
        public float Ppu = 100f;
        public readonly Dictionary<char, string> Chars = new(); // char → "transparent" | 色名 | #hex
        public readonly List<PxlSection> Sections = new();
    }

    internal sealed class PxlSection
    {
        public string Name;                  // null = 隐式单节
        public Vector4 Border;               // L,B,R,T（Unity Sprite border 序）
        public int Width, Height;
        public readonly List<string> Rows = new(); // top-down，已 trim
    }

    /// <summary>.pxl 文本 → IR。网格语法沿 XPM 惯用法：单字符=色板项、'.'=透明、
    /// 一行=一行像素。结构校验（行宽、border 越界、重名）在这里；颜色解析在
    /// <see cref="PxlColorResolver"/>。</summary>
    internal static class PxlParser
    {
        private static readonly Regex SectionHeader =
            new(@"^\[([A-Za-z0-9_-]+)\]$", RegexOptions.Compiled);
        private static readonly Regex CharsEntry =
            new(@"^(.): (.+)$", RegexOptions.Compiled);

        public static PxlDocument Parse(string text)
        {
            var doc = new PxlDocument();
            var lines = text.Replace("\r\n", "\n").Split('\n');

            PxlSection section = null;       // 当前节（含隐式）
            var sectionExplicit = false;
            var inChars = false;
            var inGrid = false;
            var sawImplicitContent = false;
            var names = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < lines.Length; i++)
            {
                var lineNo = i + 1;
                var line = lines[i].Trim();
                if (line.Length == 0) { inGrid = false; continue; }
                if (line[0] == '#') continue; // 整行注释；'#' 因此禁作 chars key

                var m = SectionHeader.Match(line);
                if (m.Success)
                {
                    if (sawImplicitContent)
                        throw new PxlParseException(lineNo,
                            "cannot mix implicit (headerless) content with [section] headers");
                    FinishSection(section, lineNo);
                    var name = m.Groups[1].Value;
                    if (!names.Add(name))
                        throw new PxlParseException(lineNo, $"duplicate section name '[{name}]'");
                    section = new PxlSection { Name = name };
                    doc.Sections.Add(section);
                    sectionExplicit = true;
                    inChars = false; inGrid = false;
                    continue;
                }

                if (inGrid)
                {
                    ValidateRow(line, doc, section, lineNo);
                    continue;
                }

                if (inChars)
                {
                    var cm = CharsEntry.Match(line);
                    if (cm.Success)
                    {
                        var key = cm.Groups[1].Value[0];
                        var value = cm.Groups[2].Value.Trim();
                        if (key == '#')
                            throw new PxlParseException(lineNo, "'#' is reserved for comments");
                        if (key == '.' && value != "transparent")
                            throw new PxlParseException(lineNo,
                                "'.' is reserved for transparent and cannot be redefined");
                        if (key != '.' && !doc.Chars.TryAdd(key, value))
                            throw new PxlParseException(lineNo, $"duplicate chars key '{key}'");
                        continue;
                    }
                    inChars = false; // 掉出 chars 块，按普通行继续解析
                }

                if (line.StartsWith("palette:", StringComparison.Ordinal))
                {
                    var v = line.Substring("palette:".Length).Trim();
                    if (!v.StartsWith("@", StringComparison.Ordinal) || v.Length < 2)
                        throw new PxlParseException(lineNo,
                            $"palette must be '@<name>' (a project .gpl reference), got '{v}'");
                    doc.PaletteRef = v.Substring(1);
                    continue;
                }
                if (line.StartsWith("ppu:", StringComparison.Ordinal))
                {
                    var v = line.Substring("ppu:".Length).Trim();
                    if (!float.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var ppu) || ppu <= 0)
                        throw new PxlParseException(lineNo, $"ppu must be a positive number, got '{v}'");
                    doc.Ppu = ppu;
                    continue;
                }
                if (line == "chars:") { inChars = true; continue; }

                if (line.StartsWith("border:", StringComparison.Ordinal))
                {
                    section = EnsureSection(doc, section, ref sectionExplicit,
                        ref sawImplicitContent, names, lineNo);
                    if (section.Rows.Count > 0)
                        throw new PxlParseException(lineNo, "border must come before grid");
                    section.Border = ParseBorder(line.Substring("border:".Length).Trim(), lineNo);
                    continue;
                }
                if (line == "grid:")
                {
                    section = EnsureSection(doc, section, ref sectionExplicit,
                        ref sawImplicitContent, names, lineNo);
                    if (section.Rows.Count > 0)
                        throw new PxlParseException(lineNo, "section already has a grid");
                    inGrid = true;
                    continue;
                }

                throw new PxlParseException(lineNo, $"unrecognized line '{line}'");
            }

            FinishSection(section, lines.Length);
            if (doc.Sections.Count == 0)
                throw new PxlParseException(lines.Length, "file declares no grid");
            return doc;
        }

        private static PxlSection EnsureSection(PxlDocument doc, PxlSection current,
            ref bool isExplicit, ref bool sawImplicit, HashSet<string> names, int lineNo)
        {
            if (current != null) return current;
            // 段头出现前的 border:/grid: → 隐式单节
            var s = new PxlSection { Name = null };
            doc.Sections.Add(s);
            isExplicit = false;
            sawImplicit = true;
            return s;
        }

        private static void ValidateRow(string row, PxlDocument doc, PxlSection section, int lineNo)
        {
            if (section.Rows.Count > 0 && row.Length != section.Width)
                throw new PxlParseException(lineNo,
                    $"row width {row.Length} != first row width {section.Width}");
            foreach (var c in row)
            {
                if (c == '.') continue;
                if (!doc.Chars.ContainsKey(c))
                    throw new PxlParseException(lineNo, $"unknown grid char '{c}' (not in chars:)");
            }
            if (section.Rows.Count == 0) section.Width = row.Length;
            section.Rows.Add(row);
            section.Height = section.Rows.Count;
        }

        private static Vector4 ParseBorder(string v, int lineNo)
        {
            var parts = v.Split(',');
            if (parts.Length != 4)
                throw new PxlParseException(lineNo, $"border must be 'L,B,R,T' (4 ints), got '{v}'");
            var n = new int[4];
            for (var i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out n[i]) || n[i] < 0)
                    throw new PxlParseException(lineNo, $"border component '{parts[i].Trim()}' must be a non-negative int");
            }
            return new Vector4(n[0], n[1], n[2], n[3]);
        }

        private static void FinishSection(PxlSection s, int lineNo)
        {
            if (s == null) return;
            if (s.Rows.Count == 0)
                throw new PxlParseException(lineNo, $"section '[{s.Name}]' has no grid");
            if (s.Border.x + s.Border.z > s.Width || s.Border.y + s.Border.w > s.Height)
                throw new PxlParseException(lineNo,
                    $"border ({s.Border.x},{s.Border.y},{s.Border.z},{s.Border.w}) exceeds " +
                    $"grid size {s.Width}x{s.Height}");
        }
    }
}
```

注意：`EnsureSection` 里 `isExplicit` 参数当前实现未读——如 lint 报 IDE0060，去掉该参数并同步调用点（两处）。

- [ ] **Step 4: refresh + 跑测试确认通过**

MCP 模板，`group_names=["PxlParserTests"]`。Expected: 12/12 PASS。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/ Tests/EditMode/Editor/Pxl/
git commit -m "feat(pxl): PxlParser — .pxl text to IR with line-numbered errors

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `PxlColorResolver`（chars → Color32 + 越板校验）

**Files:**
- Create: `Editor/Pxl/PxlColorResolver.cs`
- Test: `Tests/EditMode/Editor/Pxl/PxlColorResolverTests.cs`

规则（spec §2/§3）：`transparent` → `(0,0,0,0)`；`#RRGGBB`/`#RRGGBBAA` → 直解，**palette 模式下 RGB 必须命中色板**（忽略 alpha），否则 error（越板色）；色名 → 仅 palette 模式可用（内联模式给色名 = error），normalized 查找，查不到 = error。`.` 恒透明。错误用 `PxlParseException`（line=0，消息带 char 与值上下文——resolve 阶段无行号，char 足以定位）。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlColorResolverTests
    {
        private static GplPalette Palette() => GplPalette.Parse(
            "GIMP Palette\n26 28 44\tdark-blue\n244 244 244\twhite\n100 100 100\n");

        private static PxlDocument Doc(params (char, string)[] chars)
        {
            var d = new PxlDocument();
            foreach (var (k, v) in chars) d.Chars[k] = v;
            return d;
        }

        [Test]
        public void Resolve_inline_hex_and_transparent()
        {
            var map = PxlColorResolver.Resolve(Doc(('K', "#1a1c2c"), ('T', "transparent")), null);
            Assert.AreEqual(new Color32(0x1a, 0x1c, 0x2c, 255), map['K']);
            Assert.AreEqual(new Color32(0, 0, 0, 0), map['T']);
            Assert.AreEqual(new Color32(0, 0, 0, 0), map['.']);
        }

        [Test]
        public void Resolve_hex_with_alpha()
        {
            var map = PxlColorResolver.Resolve(Doc(('S', "#1a1c2c80")), null);
            Assert.AreEqual(new Color32(0x1a, 0x1c, 0x2c, 0x80), map['S']);
        }

        [Test]
        public void Resolve_palette_name()
        {
            var doc = Doc(('K', "dark-blue"));
            doc.PaletteRef = "main";
            var map = PxlColorResolver.Resolve(doc, Palette());
            Assert.AreEqual(new Color32(26, 28, 44, 255), map['K']);
        }

        [Test]
        public void Resolve_palette_mode_hex_must_be_on_palette()
        {
            var doc = Doc(('K', "#1a1c2c"), ('X', "#010203"));
            doc.PaletteRef = "main";
            var ex = Assert.Throws<PxlParseException>(() => PxlColorResolver.Resolve(doc, Palette()));
            StringAssert.Contains("'X'", ex.Message);
            StringAssert.Contains("#010203", ex.Message);
        }

        [Test]
        public void Resolve_palette_hex_alpha_variant_allowed()
        {
            var doc = Doc(('S', "#1a1c2c80")); // RGB 在板上，alpha 自由
            doc.PaletteRef = "main";
            var map = PxlColorResolver.Resolve(doc, Palette());
            Assert.AreEqual((byte)0x80, map['S'].a);
        }

        [Test]
        public void Resolve_name_without_palette_throws()
        {
            var ex = Assert.Throws<PxlParseException>(
                () => PxlColorResolver.Resolve(Doc(('K', "dark-blue")), null));
            StringAssert.Contains("palette:", ex.Message);
        }

        [Test]
        public void Resolve_unknown_name_throws()
        {
            var doc = Doc(('K', "magenta"));
            doc.PaletteRef = "main";
            var ex = Assert.Throws<PxlParseException>(() => PxlColorResolver.Resolve(doc, Palette()));
            StringAssert.Contains("magenta", ex.Message);
        }

        [Test]
        public void Resolve_bad_hex_throws()
        {
            Assert.Throws<PxlParseException>(
                () => PxlColorResolver.Resolve(Doc(('K', "#12345")), null));
        }
    }
}
```

- [ ] **Step 2: refresh，确认 CS0246（Red）**

- [ ] **Step 3: 实现**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>chars 映射 → 具体颜色。palette 模式（doc.PaletteRef != null）下
    /// hex 的 RGB 必须命中色板（忽略 alpha）——把全项目调色板一致性从 LLM 自觉
    /// 变成管线强制（spec §3）。</summary>
    internal static class PxlColorResolver
    {
        public static Dictionary<char, Color32> Resolve(PxlDocument doc, GplPalette palette)
        {
            var map = new Dictionary<char, Color32> { ['.'] = new Color32(0, 0, 0, 0) };
            foreach (var kv in doc.Chars)
            {
                var (key, value) = (kv.Key, kv.Value);
                if (value == "transparent") { map[key] = new Color32(0, 0, 0, 0); continue; }
                if (value.StartsWith("#", StringComparison.Ordinal))
                {
                    var c = ParseHex(key, value);
                    if (palette != null && !palette.ContainsRgb(c))
                        throw new PxlParseException(0,
                            $"chars '{key}': {value} is not on palette '@{doc.PaletteRef}' " +
                            $"(off-palette color; pick a palette color or add it to the .gpl)");
                    map[key] = c;
                    continue;
                }
                // 色名
                if (palette == null)
                    throw new PxlParseException(0,
                        $"chars '{key}': color name '{value}' requires a 'palette: @<name>' declaration");
                if (!palette.TryGetByName(value, out var named))
                    throw new PxlParseException(0,
                        $"chars '{key}': color name '{value}' not found in palette '@{doc.PaletteRef}'");
                map[key] = named;
            }
            return map;
        }

        private static Color32 ParseHex(char key, string value)
        {
            var hex = value.Substring(1);
            if ((hex.Length != 6 && hex.Length != 8) ||
                !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                throw new PxlParseException(0,
                    $"chars '{key}': '{value}' is not #RRGGBB / #RRGGBBAA");
            }
            byte P(int i) => byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            return new Color32(P(0), P(2), P(4), hex.Length == 8 ? P(6) : (byte)255);
        }
    }
}
```

- [ ] **Step 4: refresh + 跑测试确认通过**

MCP 模板，`group_names=["PxlColorResolverTests"]`。Expected: 8/8 PASS。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/ Tests/EditMode/Editor/Pxl/
git commit -m "feat(pxl): PxlColorResolver — palette enforcement + hex/name resolution

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `PxlImporter`（ScriptedImporter）

**Files:**
- Create: `Editor/Pxl/PxlImporter.cs`
- Test: `Tests/EditMode/Editor/Pxl/PxlImporterTests.cs`

要点：每节一张 RGBA32 Texture2D（Point、Clamp、`alphaIsTransparency`、**保持 readable**——InlineSpriteAssetBuilder 要读像素）+ `Sprite.Create(..., FullRect, border)` sub-asset；节名 = 段名，隐式单节 = 文件 basename；main asset = 首节 Texture2D（保证 `FindAssets("t:Texture2D")` 可发现）。grid 行序 top-down，texture 像素行序 bottom-up——写像素时翻转。`palette: @name` 经 `AssetDatabase.FindAssets` 找 `<name>.gpl`（0 个/多个都报错），并 `ctx.DependsOnSourceAsset` 注册依赖。所有错误 `ctx.LogImportError` 后 return（资产保持失败态）。

- [ ] **Step 1: 写失败测试**

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Editor
{
    public class PxlImporterTests
    {
        private const string TmpDir = "Assets/__test_pxl__";

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(TmpDir))
                AssetDatabase.CreateFolder("Assets", "__test_pxl__");
        }

        [TearDown]
        public void Teardown()
        {
            AssetDatabase.DeleteAsset(TmpDir);
        }

        private static string Write(string fileName, string content)
        {
            var abs = Path.Combine(UnityEngine.Application.dataPath, "__test_pxl__", fileName);
            File.WriteAllText(abs, content);
            var assetPath = $"{TmpDir}/{fileName}";
            AssetDatabase.ImportAsset(assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return assetPath;
        }

        [Test]
        public void Import_single_implicit_section_pixels_and_filter()
        {
            var path = Write("dot.pxl", "chars:\n  K: #102030\ngrid:\n  K.\n  ..\n");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(tex);
            Assert.AreEqual("dot", tex.name);
            Assert.AreEqual(FilterMode.Point, tex.filterMode);
            // grid 第 1 行是顶行 → texture y=1（bottom-up 翻转）
            Assert.AreEqual(new Color32(0x10, 0x20, 0x30, 255), (Color32)tex.GetPixel(0, 1));
            Assert.AreEqual(0, ((Color32)tex.GetPixel(1, 1)).a);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsNotNull(sprite);
            Assert.AreEqual("dot", sprite.name);
        }

        [Test]
        public void Import_border_and_ppu_land_on_sprite()
        {
            var path = Write("framed.pxl",
                "ppu: 16\nchars:\n  K: #000000\n[bg]\nborder: 1,1,1,1\ngrid:\n  KKK\n  KKK\n  KKK\n");
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            Assert.AreEqual("bg", sprite.name);
            Assert.AreEqual(new Vector4(1, 1, 1, 1), sprite.border);
            Assert.AreEqual(16f, sprite.pixelsPerUnit);
        }

        [Test]
        public void Import_multi_section_produces_sub_sprites_main_is_first_texture()
        {
            var path = Write("btn.pxl",
                "chars:\n  K: #000000\n  W: #ffffff\n" +
                "[normal]\ngrid:\n  KW\n[pressed]\ngrid:\n  WK\n");
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .Select(s => s.name).OrderBy(n => n).ToArray();
            Assert.AreEqual(new[] { "normal", "pressed" }, sprites);
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.IsInstanceOf<Texture2D>(main);
            Assert.AreEqual("normal", main.name);
        }

        [Test]
        public void Import_palette_ref_resolves_and_offpalette_fails()
        {
            Write("main.gpl", "GIMP Palette\n26 28 44\tdark-blue\n");
            var ok = Write("ok.pxl", "palette: @main\nchars:\n  K: dark-blue\ngrid:\n  K\n");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(ok));

            LogAssert.ignoreFailingMessages = true;
            var bad = Write("bad.pxl", "palette: @main\nchars:\n  K: #010203\ngrid:\n  K\n");
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Sprite>(bad), "off-palette must fail import");
        }

        [Test]
        public void Import_gpl_edit_triggers_dependent_reimport()
        {
            Write("pal.gpl", "GIMP Palette\n255 0 0\tred\n");
            var path = Write("dep.pxl", "palette: @pal\nchars:\n  R: red\ngrid:\n  R\n");
            Assert.AreEqual(new Color32(255, 0, 0, 255),
                (Color32)AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixel(0, 0));

            // 改色板 → 依赖的 .pxl 自动重导入，颜色跟着变
            Write("pal.gpl", "GIMP Palette\n0 255 0\tred\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.AreEqual(new Color32(0, 255, 0, 255),
                (Color32)AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetPixel(0, 0));
        }

        [Test]
        public void Import_parse_error_fails_import_with_logged_error()
        {
            LogAssert.ignoreFailingMessages = true;
            var path = Write("broken.pxl", "chars:\n  K: #000000\ngrid:\n  KK\n  K\n");
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        [Test]
        public void Import_missing_palette_fails_import()
        {
            LogAssert.ignoreFailingMessages = true;
            var path = Write("orphan.pxl", "palette: @nosuch\nchars:\n  K: #000000\ngrid:\n  K\n");
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }
    }
}
```

- [ ] **Step 2: refresh，跑 `group_names=["PxlImporterTests"]` 确认全 FAIL（.pxl 无 importer → Load 返回 null/非 Sprite）**

注意第一个测试 `Import_single_implicit_section_pixels_and_filter` 此时 `LoadAssetAtPath<Texture2D>` 返回 null → FAIL，符合 Red。

- [ ] **Step 3: 实现**

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>.pxl（像素网格文本，spec 2026-06-11-pxl-pixel-sprite-importer）→
    /// 每节一张 point-filter Texture2D + Sprite sub-asset。main asset = 首节
    /// Texture2D，保证 SpriteAtlasSyncer 的 FindAssets("t:Texture2D") 能发现。
    /// Texture 保持 readable：InlineSpriteAssetBuilder 烘焙图文混排时要读像素。</summary>
    [ScriptedImporter(1, "pxl")]
    internal sealed class PxlImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text;
            try { text = File.ReadAllText(ctx.assetPath); }
            catch (IOException ex)
            {
                ctx.LogImportError($"{ctx.assetPath}: cannot read: {ex.Message}");
                return;
            }

            PxlDocument doc;
            try { doc = PxlParser.Parse(text); }
            catch (PxlParseException ex)
            {
                ctx.LogImportError($"{ctx.assetPath}: {ex.Message}");
                return;
            }

            GplPalette palette = null;
            if (doc.PaletteRef != null)
            {
                var gplPath = FindPalettePath(doc.PaletteRef, out var error);
                if (gplPath == null)
                {
                    ctx.LogImportError($"{ctx.assetPath}: {error}");
                    return;
                }
                // 色板改动 → 所有引用它的 .pxl 自动重导入（全项目换色一次完成）。
                ctx.DependsOnSourceAsset(gplPath);
                try { palette = GplPalette.Parse(File.ReadAllText(gplPath)); }
                catch (System.FormatException ex)
                {
                    ctx.LogImportError($"{gplPath}: {ex.Message}");
                    return;
                }
            }

            System.Collections.Generic.Dictionary<char, Color32> colors;
            try { colors = PxlColorResolver.Resolve(doc, palette); }
            catch (PxlParseException ex)
            {
                ctx.LogImportError($"{ctx.assetPath}: {ex.Message}");
                return;
            }

            var basename = Path.GetFileNameWithoutExtension(ctx.assetPath);
            Texture2D main = null;
            foreach (var section in doc.Sections)
            {
                var name = section.Name ?? basename;
                var tex = BuildTexture(section, colors, name);
                var sprite = Sprite.Create(tex,
                    new Rect(0, 0, section.Width, section.Height),
                    new Vector2(0.5f, 0.5f), doc.Ppu, 0,
                    SpriteMeshType.FullRect, section.Border);
                sprite.name = name;
                ctx.AddObjectToAsset($"tex:{name}", tex);
                ctx.AddObjectToAsset($"sprite:{name}", sprite);
                if (main == null) main = tex;
            }
            ctx.SetMainObject(main);
        }

        private static Texture2D BuildTexture(PxlSection section,
            System.Collections.Generic.IReadOnlyDictionary<char, Color32> colors, string name)
        {
            var w = section.Width;
            var h = section.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                alphaIsTransparency = true,
            };
            var px = new Color32[w * h];
            for (var row = 0; row < h; row++)        // grid top-down → texture bottom-up
                for (var col = 0; col < w; col++)
                    px[(h - 1 - row) * w + col] = colors[section.Rows[row][col]];
            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>按文件名（去扩展名）全项目找 &lt;name&gt;.gpl。0 个或多个都报错
        /// （error out 参数带候选列表）。</summary>
        private static string FindPalettePath(string paletteRef, out string error)
        {
            var matches = AssetDatabase.FindAssets(paletteRef)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileName(p), paletteRef + ".gpl",
                    System.StringComparison.Ordinal))
                .Distinct()
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 1) { error = null; return matches[0]; }
            error = matches.Count == 0
                ? $"palette '@{paletteRef}' not found (no '{paletteRef}.gpl' in project)"
                : $"palette '@{paletteRef}' is ambiguous: {string.Join(", ", matches)}";
            return null;
        }
    }
}
```

- [ ] **Step 4: refresh + 跑测试确认通过**

MCP 模板，`group_names=["PxlImporterTests"]`。Expected: 7/7 PASS。
若 `Import_gpl_edit_triggers_dependent_reimport` 失败：检查 `DependsOnSourceAsset` 的路径是否与 `.gpl` 实际 assetPath 一致（大小写/分隔符）。

- [ ] **Step 5: 顺跑全量 EditorOnly，确认无连带回归**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`。Expected: 全绿（基线 184 + 本计划新增）。

- [ ] **Step 6: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/Pxl/ Tests/EditMode/Editor/Pxl/
git commit -m "feat(pxl): PxlImporter — ScriptedImporter, per-section Texture2D+Sprite, .gpl dependency

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: `SpriteAtlasSyncer` 对接（key 派生 + Point 兜底）

**Files:**
- Modify: `Editor/SpriteAtlasSyncer.cs:383-415`（`EnumerateSpriteSources` 循环体）、`:895-903`（`ApplyTemplateFilterMode`）
- Test: `Tests/EditMode/Editor/Pxl/PxlSyncerTests.cs`

key 规则（spec §2 的实现裁决）：`PxlImporter` 资产按 Sprite sub-asset 枚举；`sprite.name == 文件 basename` → key = pathKey（隐式单节，与 PNG 规则一致；顺带让 `ok.pxl` 里的 `[ok]` 也折叠为 `Buttons/ok`），否则 key = `pathKey/sprite.name`。裸名别名交给既有 `BuildLookup`（`pressed` 撞名时自动不提升）。

- [ ] **Step 1: 写失败测试**

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlSyncerTests
    {
        private const string TestRoot = "Assets/__test_pxlsync__";

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.CreateFolder("Assets", "__test_pxlsync__");
        }

        [TearDown]
        public void Teardown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
        }

        private static void WritePxl(string relPath, string content)
        {
            var abs = Path.Combine(UnityEngine.Application.dataPath, "__test_pxlsync__",
                relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, content);
        }

        private static void ImportAll() =>
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        private const string SinglePxl = "chars:\n  K: #000000\ngrid:\n  K\n";
        private const string MultiPxl =
            "chars:\n  K: #000000\n  W: #ffffff\n[normal]\ngrid:\n  K\n[pressed]\ngrid:\n  W\n";

        [Test]
        public void Enumerate_single_section_key_is_pathkey()
        {
            WritePxl("icon.pxl", SinglePxl);
            ImportAll();
            var entries = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("icon", entries[0].pathKey);
        }

        [Test]
        public void Enumerate_multi_section_keys_append_section_name()
        {
            WritePxl("Buttons/ok.pxl", MultiPxl);
            ImportAll();
            var keys = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot)
                .Select(e => e.pathKey).OrderBy(k => k).ToArray();
            Assert.AreEqual(new[] { "Buttons/ok/normal", "Buttons/ok/pressed" }, keys);
        }

        [Test]
        public void Enumerate_mixed_png_and_pxl()
        {
            WritePxl("a.pxl", SinglePxl);
            var png = new Texture2D(1, 1);
            png.SetPixel(0, 0, Color.red);
            File.WriteAllBytes(
                Path.Combine(UnityEngine.Application.dataPath, "__test_pxlsync__", "b.png"),
                png.EncodeToPNG());
            Object.DestroyImmediate(png);
            ImportAll();
            var keys = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot)
                .Select(e => e.pathKey).OrderBy(k => k).ToArray();
            Assert.AreEqual(new[] { "a", "b" }, keys);
        }

        [Test]
        public void BuildLookup_promotes_unique_bare_alias_for_section_key()
        {
            WritePxl("Buttons/ok.pxl", MultiPxl);
            ImportAll();
            var entries = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot);
            var lookup = SpriteAtlasSyncer.BuildLookup(entries, out _);
            Assert.IsTrue(lookup.ContainsKey("Buttons/ok/pressed"));
            Assert.IsTrue(lookup.ContainsKey("pressed"), "unique bare alias promoted");
        }

        [Test]
        public void ResetTextureImportSettings_skips_pxl()
        {
            WritePxl("icon.pxl", SinglePxl);
            ImportAll();
            Assert.AreEqual(0, SpriteAtlasSyncer.ResetTextureImportSettings(TestRoot),
                ".pxl has no TextureImporter; reset must skip it");
        }
    }
}
```

- [ ] **Step 2: refresh + 跑 `group_names=["PxlSyncerTests"]`，确认 key 相关测试 FAIL**

预期失败形态：`Enumerate_multi_section_keys` 只得到 1 个 entry（`LoadAssetAtPath<Sprite>` 任取其一、key 无节名段）。`ResetTextureImportSettings_skips_pxl` 应已 PASS（guard 既有）——它是钉死回归的。

- [ ] **Step 3: 修改 `EnumerateSpriteSources`**

在 `Editor/SpriteAtlasSyncer.cs` 的 `EnumerateSpriteSources` 循环里，`EnsureSpriteImporter(assetPath);` 之后、`#if PROMPTUGUI_HAS_ASEPRITE` 之前插入：

```csharp
                // .pxl（PxlImporter）：每节一个 Sprite sub-asset。隐式单节的 sprite.name
                // == 文件 basename → key 与 PNG 规则一致（pathKey）；显式多节 → pathKey/节名。
                if (AssetImporter.GetAtPath(assetPath) is PxlImporter)
                {
                    var relPxl = assetPath.Substring(folderPrefix.Length);
                    var pxlKey = relPxl.Substring(0, relPxl.Length - Path.GetExtension(relPxl).Length);
                    var pxlBase = Path.GetFileNameWithoutExtension(assetPath);
                    foreach (var s in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>())
                    {
                        result.Add((s.name == pxlBase ? pxlKey : $"{pxlKey}/{s.name}", s));
                    }
                    continue;
                }
```

（文件已 `using System.Linq;`，无需新增。）

- [ ] **Step 4: 修改 `ApplyTemplateFilterMode`（pxl-only 文件夹 atlas Point 兜底）**

`FindFirstTexture` 排除非 TextureImporter → 纯 `.pxl` 的 sourceFolder 拿不到模板，atlas 会落默认 Bilinear，把像素画糊掉。替换 `ApplyTemplateFilterMode`：

```csharp
        private static void ApplyTemplateFilterMode(SpriteAtlas atlas, string folderAssetPath)
        {
            var firstTexture = FindFirstTexture(folderAssetPath);
            if (firstTexture == null)
            {
                // 纯 .pxl 文件夹：PxlImporter 贴图恒为 point-filter 像素画 → atlas 跟随。
                if (HasPxlSource(folderAssetPath))
                {
                    var pxlTs = atlas.GetTextureSettings();
                    pxlTs.filterMode = FilterMode.Point;
                    atlas.SetTextureSettings(pxlTs);
                }
                return;
            }
            if (AssetImporter.GetAtPath(firstTexture) is not TextureImporter ti) return;
            var ts = atlas.GetTextureSettings();
            ts.filterMode = ti.filterMode;
            atlas.SetTextureSettings(ts);
        }

        private static bool HasPxlSource(string folderAssetPath)
        {
            if (string.IsNullOrEmpty(folderAssetPath)) return false;
            if (!AssetDatabase.IsValidFolder(folderAssetPath)) return false;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath }))
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) is PxlImporter)
                    return true;
            }
            return false;
        }
```

并在 `PxlSyncerTests` 补一条（写在 Step 1 文件里也可，此处列全）：

```csharp
        [Test]
        public void EnsureAtlas_pxl_only_folder_gets_point_filter()
        {
            WritePxl("icon.pxl", SinglePxl);
            ImportAll();
            var set = ScriptableObject.CreateInstance<PromptUGUI.Application.SpriteSet>();
            var so = new SerializedObject(set);
            so.FindProperty("setName").stringValue = "pxltest";
            so.FindProperty("sourceFolder").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(TestRoot);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(set, $"{TestRoot}/pxltest.asset");

            var atlas = SpriteAtlasSyncer.EnsureAtlasAsset(set);
            Assert.IsNotNull(atlas);
            Assert.AreEqual(FilterMode.Point, atlas.GetTextureSettings().filterMode);
        }
```

（`SerializedObject` 需 `using UnityEditor;` 已有；`SpriteSet` 字段名 `setName`/`sourceFolder` 见 `Runtime/Application/SpriteSet.cs:18,40`。atlas 资产会建在 `TestRoot` 下，TearDown 的 `DeleteAsset(TestRoot)` 一并清掉。）

- [ ] **Step 5: refresh + 跑 `group_names=["PxlSyncerTests"]` 确认全 PASS（6/6）**

- [ ] **Step 6: 全量回归**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` 与 `assembly_names=["PromptUGUI.Tests.EditMode"]` 都跑。Expected: 全绿（EditMode 基线 1570）。

- [ ] **Step 7: lint + commit**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format style PromptUGUI.Lint.slnx && cd ..
git add Editor/SpriteAtlasSyncer.cs Tests/EditMode/Editor/Pxl/
git commit -m "feat(pxl): syncer integration — per-section keys + point-filter atlas fallback

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: SKILL 文档

**Files:**
- Create: `.claude/skills/authoring-promptugui-pxl/SKILL.md`
- Modify: `.claude/skills/authoring-promptugui-xml/reference/icons.md`（来源指针）

- [ ] **Step 1: 写 `SKILL.md`**（英文，结构如下，写作时对照 spec §2/§3 与最终实现核对每条语法）

```markdown
---
name: authoring-promptugui-pxl
description: Use when creating or editing PromptUGUI .pxl pixel-grid sprite files — LLM-authored pixel art (9-slice borders, button skins, icons) that imports directly as Unity Sprites. For referencing the resulting sprites from XML see authoring-promptugui-xml.
---

# Authoring PromptUGUI `.pxl` pixel sprites

(sections:)
## When to use / pipeline overview
- .pxl in a SpriteSet sourceFolder → auto-imports as Sprite(s) → reference as `set:key` from <Icon>/<Image sprite=>; Sync Atlases packs them like PNGs.
- Sweet spot ≤48×48 UI chrome; NOT for large illustrations.
## File format (full grammar + one complete multi-section example)
- header: palette/ppu/chars; sections [name]; border L,B,R,T before grid; grid rows; '.'=transparent; '#'-comments; error behaviors (line-numbered import errors).
- key derivation: file path key + /section for multi-section; bare-name aliases.
## Palette workflow (.gpl)
- GIMP Palette format, Lospec download, @name resolution, off-palette = import error, palette edit → auto reimport.
- caveat: renaming/moving the .gpl does not auto-reimport dependents; reimport manually.
## Pixel-art craft rules for LLMs
- 1px outline, limited ramp per material, 9-slice corner design (corners hold the detail, edges must tile), button state ladder (normal/hover lighter/pressed darker+shifted), odd sizes for centered icons, etc.
## Verifying your output
- import errors land in the Unity console (and fail the asset); re-read your grid — text IS the image.
```

- [ ] **Step 2: 在 `icons.md` 文末加指针小节**

```markdown
## `.pxl` text sprites

A SpriteSet `sourceFolder` may also contain `.pxl` files — LLM-authored pixel-grid
text that imports directly as point-filtered Sprites (with 9-slice border / PPU
declared in-file). Multi-section files contribute `path/section` keys. Full format
and drawing guidance: the **authoring-promptugui-pxl** skill.
```

- [ ] **Step 3: commit**

```bash
git add .claude/skills/authoring-promptugui-pxl/ .claude/skills/authoring-promptugui-xml/reference/icons.md
git commit -m "docs(skill): authoring-promptugui-pxl + icons.md pointer

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: 端到端验证 + 收尾

- [ ] **Step 1: 端到端冒烟（host 工程）**

在 host 工程任一 SpriteSet sourceFolder 写一个真实 `.pxl`（带 `[normal]`/`[pressed]` 的 9-slice 按钮皮肤 + `main.gpl`），在某个 `.ui.xml` 里 `<Image sprite="<set>:<key>"/>` 引用，跑 `Tools → PromptUGUI → Sprite → Sync Atlases (All Sets)`（或 MCP `execute_menu_item`，注意不是 Reimport All），用 `read_console` 确认无错误、SpriteSet entries 出现新 key。

- [ ] **Step 2: 三套全量测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])     # 基线 1570
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])   # 基线 184 + 新增 ~32
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])     # 基线 133
```

Expected: 全绿。

- [ ] **Step 3: lint 终验**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx && \
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: exit 0。

- [ ] **Step 4: 确认 `.meta` 完整**

```bash
git status --short   # 不应有未跟踪的 .cs/.md 缺 .meta 伴生
```

- [ ] **Step 5: push + PR（走 superpowers:finishing-a-development-branch）**

PR 描述引用 spec 路径；提醒用户做视觉 QA（Unity 里看导入的 .pxl 按钮皮肤 9-slice 拉伸效果）。

---

## Self-Review 记录

- **Spec coverage**：§2 格式（Task 2/3）、§3 .gpl（Task 1/4）、§4 importer（Task 4）、§5 改动面五行（Task 5 前两行 + guard 测试；AutoSync 行经代码核实零改动，Task 7 冒烟覆盖；消费端零改动）、§6 测试（Task 1–5）、§7 SKILL（Task 6）。无缺口。
- **Spec 偏差（已回写 spec）**：默认 PPU = 100（项目无"项目默认 PPU"设置）。
- **实现裁决（spec 未细化处，以本计划为准）**：单显式节 `[x]` 在 `x.pxl` 中折叠为 pathKey；`#` 整行注释、禁作 chars key；空行终止 grid 块；rows 取 trim 后内容（空格不是合法网格字符）。
- **类型一致性**：`PxlDocument.Chars`/`Sections`/`PaletteRef`/`Ppu`、`PxlSection.Name/Border/Width/Height/Rows`、`GplPalette.Entries/TryGetByName/ContainsRgb` 在 Task 2/3/4/5 间已交叉核对一致。
