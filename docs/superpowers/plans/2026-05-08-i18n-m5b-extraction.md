# M5b — i18n Extraction + AI Translation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Author writes plain text in `.ui.xml` / `UI.Tr("...")` in C#; menu Tools → PromptUGUI → Extract Strings populates `.po` files for every Settings.locale; menu Tools → PromptUGUI → Translate Locale… calls an OpenAI-compatible LLM with Structured Outputs to fill empty msgstr; SKILL.md and main spec sync to reflect the new public surface.

**Architecture:** All Editor-only (no runtime cost beyond M5a). XML scan reuses `UIDocumentParser` on raw IR (pre-template-expansion) so msgids preserve `{{x}}` placeholders. C# scan uses `Microsoft.CodeAnalysis` (Roslyn — bundled with Unity Editor). Extraction is per-locale, mirrors source path under `Assets/Resources/PromptUGUI/i18n/<locale>/...`, leaves `i18n-custom/` untouched. Translation menu sends batches via `HttpClient` to a configurable endpoint with `response_format: { type: "json_schema" }`.

**Tech Stack:** Unity 6 Editor, C#, `Microsoft.CodeAnalysis.CSharp` (Unity-bundled), `System.Net.Http`.

**Spec:** `docs/superpowers/specs/2026-05-08-i18n-fonts-design.md`. Read it before starting.

**Prerequisite:** M5a plan must be complete — runtime PoParser, PoEntry, TranslationStore, UI.Tr, etc. all exist.

---

## File Structure

**New files (Editor):**
- `Editor/I18n/StringExtractor.cs` — top-level menu + coordinator
- `Editor/I18n/XmlStringScanner.cs` — extracts msgids from `.ui.xml`
- `Editor/I18n/CSharpStringScanner.cs` — Roslyn syntactic walker for `UI.Tr(...)` calls
- `Editor/I18n/ExtractedString.cs` — DTO carrying msgid + msgctxt + ambient ctx + comments
- `Editor/I18n/PoFileWriter.cs` — merges extracted strings into existing .po (preserve filled msgstr; drop missing)
- `Editor/I18n/TranslationProvider.cs` — ProjectSettings SO: endpoint, model, systemPrompt
- `Editor/I18n/TranslationAuth.cs` — UserSettings SO: api key only
- `Editor/I18n/TranslationProviderSettingsProvider.cs` — `Project Settings → PromptUGUI → Translation` registration
- `Editor/I18n/TranslationClient.cs` — HTTP call to OpenAI-compatible endpoint with Structured Outputs
- `Editor/I18n/TranslationMenu.cs` — menu item + dialog + progress bar
- `Editor/I18n/TmpRichTextDetector.cs` — detect TMP tags in msgid → flag for "preserve tags" comment

**Modified files:**
- `docs/superpowers/specs/2026-05-07-promptugui-description-language-design.md` — flip §10 i18n entry
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — add font / tr / ctx attrs, language-as-variant note, CDATA + TMP rich text section, i18n cheatsheet

**Tests:**
- `Tests/EditMode/Editor/XmlStringScannerTests.cs` (`PromptUGUI.Tests.EditorOnly` asmdef)
- `Tests/EditMode/Editor/CSharpStringScannerTests.cs`
- `Tests/EditMode/Editor/PoFileWriterTests.cs`
- `Tests/EditMode/Editor/TmpRichTextDetectorTests.cs`
- `Tests/EditMode/Editor/TranslationClientTests.cs` (with stubbed HTTP via `HttpMessageHandler`)

---

## Pre-flight

- [ ] **Confirm M5a is merged / present**

```bash
git -C /workspace-PromptUGUI log --oneline | grep -E "i18n|PromptUGUISettings|PoParser" | head
```

Expected: see commits from M5a. If not, do not proceed.

- [ ] **Confirm Microsoft.CodeAnalysis is available**

```
mcp__UnityMCP__execute_code(code="UnityEngine.Debug.Log(typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).FullName);")
```

Expected: prints `Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree` (Unity Editor bundles it). If it doesn't print, the agent must add a `Microsoft.CodeAnalysis.CSharp` reference via `csc.rsp` / asmdef precompiled refs before continuing — flag to the user.

---

## Task 1: ExtractedString DTO

**Files:**
- Create: `Editor/I18n/ExtractedString.cs`

- [ ] **Step 1: Write DTO**

```csharp
// Editor/I18n/ExtractedString.cs
using System.Collections.Generic;

namespace PromptUGUI.Editor.I18n {
    /// <summary>
    /// One translatable string discovered by a scanner. Multiple ExtractedStrings with the same
    /// (msgid, msgctxt) get merged at write-time — comments/refs concatenated.
    /// </summary>
    internal sealed class ExtractedString {
        public string Msgid;
        public string Msgctxt;       // null = no explicit ctx
        public List<string> Comments = new();   // # ...
        public List<string> ExtractedComments = new();   // #. ...
        public List<string> References = new();          // #: file:line
        public string LocalePartition;       // e.g. "screens/MainMenu" or "_code"
    }
}
```

- [ ] **Step 2: Refresh + commit**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

```bash
git add Editor/I18n/ExtractedString.cs Editor/I18n/ExtractedString.cs.meta
git commit -m "feat(i18n-editor): ExtractedString DTO"
```

---

## Task 2: TmpRichTextDetector

**Files:**
- Create: `Editor/I18n/TmpRichTextDetector.cs`
- Create: `Tests/EditMode/Editor/TmpRichTextDetectorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/EditMode/Editor/TmpRichTextDetectorTests.cs
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor {
    public class TmpRichTextDetectorTests {
        [TestCase("<sprite name=\"coin\"/>")]
        [TestCase("<color=#ff0>x</color>")]
        [TestCase("<b>bold</b>")]
        [TestCase("<size=20>x</size>")]
        [TestCase("<link=\"foo\">x</link>")]
        public void Detect_KnownTags_ReturnsTrue(string s) {
            Assert.IsTrue(TmpRichTextDetector.HasTmpTags(s));
        }

        [TestCase("plain")]
        [TestCase("price: {0}")]
        [TestCase("price: {{n}}")]
        [TestCase("a < b > c")]
        public void Detect_NoTmpTag_ReturnsFalse(string s) {
            Assert.IsFalse(TmpRichTextDetector.HasTmpTags(s));
        }
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="TmpRichTextDetectorTests")
```

- [ ] **Step 3: Implement**

```csharp
// Editor/I18n/TmpRichTextDetector.cs
using System.Text.RegularExpressions;

namespace PromptUGUI.Editor.I18n {
    internal static class TmpRichTextDetector {
        // Recognized TMP rich-text tags. Conservative — only tags whose attributes carry resource
        // references that translators MUST NOT touch. False positives here only add a "preserve" hint
        // comment; false negatives skip the hint.
        static readonly Regex Tag = new(
            @"<\s*/?(?:sprite|color|b|i|u|s|size|font|font-weight|align|alpha|space|indent|line-height|line-indent|link|lowercase|uppercase|smallcaps|mark|noparse|page|pos|rotate|style|sub|sup|voffset|width)(\s|=|/|>)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool HasTmpTags(string s) =>
            !string.IsNullOrEmpty(s) && Tag.IsMatch(s);
    }
}
```

- [ ] **Step 4: Run; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="TmpRichTextDetectorTests")
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/I18n/TmpRichTextDetector.cs Editor/I18n/TmpRichTextDetector.cs.meta \
        Tests/EditMode/Editor/TmpRichTextDetectorTests.cs
git commit -m "feat(i18n-editor): TmpRichTextDetector for translator-preserve hints"
```

---

## Task 3: XmlStringScanner

**Files:**
- Create: `Editor/I18n/XmlStringScanner.cs`
- Create: `Tests/EditMode/Editor/XmlStringScannerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/EditMode/Editor/XmlStringScannerTests.cs
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor {
    public class XmlStringScannerTests {
        [Test] public void Scan_SimpleTextElement_ExtractsMsgid() {
            var xml = "<PromptUGUI version='1'><Screen name='Main'>" +
                      "<Text>开始游戏</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/Main").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("开始游戏", found[0].Msgid);
            Assert.IsNull(found[0].Msgctxt);
        }

        [Test] public void Scan_BtnContent_ExtractsMsgid() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Btn id='b'>设置</Btn></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("设置", found[0].Msgid);
        }

        [Test] public void Scan_TextAttribute_AlsoExtracts() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text text='Hello' fontSize='32'/></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.IsTrue(found.Any(e => e.Msgid == "Hello"));
        }

        [Test] public void Scan_TrFalse_Skips() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text tr='false'>hardcoded</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.IsEmpty(found);
        }

        [Test] public void Scan_ExplicitCtx_PopulatesMsgctxt() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text ctx='door'>Open</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual("door", found[0].Msgctxt);
            Assert.AreEqual("Open", found[0].Msgid);
        }

        [Test] public void Scan_PureBracePlaceholder_SkippedWithWarning() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text>{{playerName}}</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.IsEmpty(found);   // pure {{x}} has no translation value
        }

        [Test] public void Scan_TextWithBraceAndStatic_Extracted() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text>金币: {{n}}</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual("金币: {{n}}", found[0].Msgid);
        }

        [Test] public void Scan_AmbientCtx_AddedAsExtractedComment() {
            var xml = "<PromptUGUI version='1'><Screen name='Main'>" +
                      "<Btn id='play'>开始</Btn>" +
                      "<Btn id='cfg'>设置</Btn>" +
                      "</Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/Main").ToList();
            var play = found.First(e => e.Msgid == "开始");
            // ambient hint should reference Main + Btn#play
            Assert.IsTrue(play.ExtractedComments.Any(c => c.Contains("Main") && c.Contains("play")));
            // sibling list should mention "设置"
            Assert.IsTrue(play.ExtractedComments.Any(c => c.Contains("设置")));
        }

        [Test] public void Scan_CDataWithRichText_ExtractsAndFlags() {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text><![CDATA[<color=#ff0>警告</color>]]></Text>" +
                      "</Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual("<color=#ff0>警告</color>", found[0].Msgid);
            Assert.IsTrue(found[0].ExtractedComments.Any(c => c.Contains("Preserve tags")));
        }
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XmlStringScannerTests")
```

- [ ] **Step 3: Implement scanner**

```csharp
// Editor/I18n/XmlStringScanner.cs
using System.Collections.Generic;
using System.Linq;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Editor.I18n {
    internal static class XmlStringScanner {
        // Tags whose textContent and "text" attr are translatable.
        static readonly System.Collections.Generic.HashSet<string> TextHostingTags =
            new() { "Text", "Btn" };

        public static IEnumerable<ExtractedString> Scan(string xmlSource, string localePartition) {
            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xmlSource); }
            catch (ParseException) { yield break; }     // unparseable file → skip

            foreach (var screen in doc.Screens) {
                foreach (var es in WalkNode(screen.Body, screen.Name, parentSiblings: null, localePartition))
                    yield return es;
            }
            foreach (var t in doc.Templates) {
                foreach (var es in WalkNode(t.Body, $"Template:{t.Name}", parentSiblings: null, localePartition))
                    yield return es;
            }
        }

        static IEnumerable<ExtractedString> WalkNode(
            ElementNode node, string screenOrTemplateName,
            List<string> parentSiblings, string localePartition) {

            // Compute siblings (raw text of Text/Btn children) for ambient ctx of children.
            var siblings = new List<string>();
            foreach (var c in node.Children) {
                var raw = c.TextContentRaw ?? c.TextContent;
                if (TextHostingTags.Contains(c.Tag) && !string.IsNullOrEmpty(raw))
                    siblings.Add(raw.Trim());
            }

            if (TextHostingTags.Contains(node.Tag) && !IsTrFalse(node)) {
                node.Attributes.TryGetValue("ctx", out var ctx);

                // textContent
                var rawText = node.TextContentRaw ?? node.TextContent;
                if (!string.IsNullOrEmpty(rawText) && !IsPureBraces(rawText)) {
                    yield return Build(rawText, ctx, screenOrTemplateName, node, parentSiblings, "text", localePartition);
                }
                // text attribute (use raw if available)
                string textAttrRaw = null;
                if (node.AttributesRaw != null && node.AttributesRaw.TryGetValue("text", out var ra))
                    textAttrRaw = ra;
                else if (node.Attributes.TryGetValue("text", out var v))
                    textAttrRaw = v;
                if (!string.IsNullOrEmpty(textAttrRaw) && !IsPureBraces(textAttrRaw)) {
                    yield return Build(textAttrRaw, ctx, screenOrTemplateName, node, parentSiblings, "text-attr", localePartition);
                }
            }

            foreach (var child in node.Children) {
                foreach (var es in WalkNode(child, screenOrTemplateName, siblings, localePartition))
                    yield return es;
            }
        }

        static bool IsTrFalse(ElementNode node) =>
            node.Attributes.TryGetValue("tr", out var v) && v == "false";

        static bool IsPureBraces(string s) {
            // exactly "{{name}}" with possibly surrounding whitespace.
            var t = s.Trim();
            return t.StartsWith("{{") && t.EndsWith("}}") && t.Count(c => c == '{') == 2 && t.Count(c => c == '}') == 2;
        }

        static ExtractedString Build(
            string msgid, string ctx, string screenOrTemplateName,
            ElementNode node, List<string> parentSiblings, string attrSlot,
            string localePartition) {

            var es = new ExtractedString {
                Msgid = msgid,
                Msgctxt = ctx,
                LocalePartition = localePartition,
            };
            var who = string.IsNullOrEmpty(node.Id) ? node.Tag : $"{node.Tag}#{node.Id}";
            es.ExtractedComments.Add($"{screenOrTemplateName} screen, {who} {attrSlot}");
            if (parentSiblings != null && parentSiblings.Count > 0) {
                var sibs = string.Join(", ", parentSiblings.Where(s => s != msgid).Take(3));
                if (!string.IsNullOrEmpty(sibs))
                    es.ExtractedComments.Add($"sibling: {sibs}");
            }
            if (TmpRichTextDetector.HasTmpTags(msgid)) {
                es.ExtractedComments.Add(
                    "Contains TMP rich text tags. Preserve tags and attribute values verbatim.");
            }
            return es;
        }
    }
}
```

- [ ] **Step 4: Run; iterate until pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="XmlStringScannerTests")
```

Expected: 9/9 PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/I18n/XmlStringScanner.cs Editor/I18n/XmlStringScanner.cs.meta \
        Tests/EditMode/Editor/XmlStringScannerTests.cs
git commit -m "feat(i18n-editor): XmlStringScanner — Text/Btn msgid extraction with ambient ctx"
```

---

## Task 4: CSharpStringScanner (Roslyn)

**Files:**
- Create: `Editor/I18n/CSharpStringScanner.cs`
- Create: `Tests/EditMode/Editor/CSharpStringScannerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/EditMode/Editor/CSharpStringScannerTests.cs
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor {
    public class CSharpStringScannerTests {
        [Test] public void Scan_PlainTrLiteral_Extracted() {
            var src = @"
                using PromptUGUI.Application;
                class X {
                    void Run() {
                        var s = UI.Tr(""hello"");
                    }
                }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("hello", found[0].Msgid);
            Assert.IsNull(found[0].Msgctxt);
        }

        [Test] public void Scan_TrWithCtx_PopulatesMsgctxt() {
            var src = @"
                class X { void Run() { var s = PromptUGUI.Application.UI.Tr(""Open"", ctx: ""door""); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("Open", found.Msgid);
            Assert.AreEqual("door", found.Msgctxt);
        }

        [Test] public void Scan_NonLiteralArgs_SkippedWithWarning() {
            var src = @"
                class X { void Run(string x) { var s = PromptUGUI.Application.UI.Tr(x); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.IsEmpty(found);
        }

        [Test] public void Scan_RefIncludesFileAndLine() {
            var src = "class X { void R() { var s = PromptUGUI.Application.UI.Tr(\"x\"); } }";
            var es = CSharpStringScanner.Scan(src, "Path/To/X.cs").Single();
            Assert.IsTrue(es.References.Any(r => r.Contains("Path/To/X.cs")));
        }

        [Test] public void Scan_LeadingComment_AddedToExtractedComments() {
            var src = @"
                class X {
                    void R() {
                        // 提示玩家当前金币
                        var s = PromptUGUI.Application.UI.Tr(""总价: {0:C}"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("提示玩家当前金币")));
        }

        [Test] public void Scan_MethodNameIncludedInExtractedComments() {
            var src = "class X { void OnGoldChanged() { var s = PromptUGUI.Application.UI.Tr(\"x\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("OnGoldChanged")));
        }
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="CSharpStringScannerTests")
```

- [ ] **Step 3: Implement scanner**

```csharp
// Editor/I18n/CSharpStringScanner.cs
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PromptUGUI.Editor.I18n {
    internal static class CSharpStringScanner {
        public static IEnumerable<ExtractedString> Scan(string source, string filePath) {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var call in calls) {
                if (!IsUiTr(call)) continue;
                var args = call.ArgumentList.Arguments;
                if (args.Count == 0) continue;

                var msgid = AsLiteral(args[0].Expression);
                if (msgid == null) continue;       // dynamic — skip

                string ctx = null;
                for (int i = 1; i < args.Count; i++) {
                    var a = args[i];
                    if (a.NameColon?.Name.Identifier.ValueText == "ctx") {
                        ctx = AsLiteral(a.Expression);
                        if (ctx == null) goto skip;
                    }
                }

                var es = new ExtractedString {
                    Msgid = msgid,
                    Msgctxt = ctx,
                    LocalePartition = "_code",
                };
                var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                es.References.Add($"{filePath}:{line}");

                var enclosing = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (enclosing != null)
                    es.ExtractedComments.Add($"in {enclosing.Identifier.ValueText}()");

                foreach (var c in CollectLeadingComments(call))
                    es.ExtractedComments.Add(c);

                if (TmpRichTextDetector.HasTmpTags(msgid))
                    es.ExtractedComments.Add(
                        "Contains TMP rich text tags. Preserve tags and attribute values verbatim.");

                yield return es;
                skip: ;
            }
        }

        static bool IsUiTr(InvocationExpressionSyntax call) {
            // Match `UI.Tr(...)` and `PromptUGUI.Application.UI.Tr(...)` and `Tr(...)` (looser).
            return call.Expression switch {
                MemberAccessExpressionSyntax m =>
                    m.Name.Identifier.ValueText == "Tr",
                IdentifierNameSyntax id =>
                    id.Identifier.ValueText == "Tr",
                _ => false,
            };
        }

        static string AsLiteral(ExpressionSyntax e) {
            if (e is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
            return null;
        }

        static IEnumerable<string> CollectLeadingComments(InvocationExpressionSyntax call) {
            var stmt = call.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
            if (stmt == null) yield break;
            foreach (var trivia in stmt.GetLeadingTrivia()) {
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) {
                    var text = trivia.ToString().TrimStart('/').Trim();
                    if (!string.IsNullOrEmpty(text)) yield return text;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="CSharpStringScannerTests")
```

Expected: 6/6 PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/I18n/CSharpStringScanner.cs Editor/I18n/CSharpStringScanner.cs.meta \
        Tests/EditMode/Editor/CSharpStringScannerTests.cs
git commit -m "feat(i18n-editor): CSharpStringScanner — Roslyn walker for UI.Tr literals"
```

---

## Task 5: PoFileWriter — merge into existing .po

**Files:**
- Create: `Editor/I18n/PoFileWriter.cs`
- Create: `Tests/EditMode/Editor/PoFileWriterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// Tests/EditMode/Editor/PoFileWriterTests.cs
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.Editor {
    public class PoFileWriterTests {
        [Test] public void Merge_NewMsgid_AddedWithEmptyMsgstr() {
            var existing = "";   // no prior file
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "hello" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("hello", entries[0].Msgid);
            Assert.AreEqual("", entries[0].Msgstr);
        }

        [Test] public void Merge_ExistingNonEmptyMsgstr_PreservedAcrossExtract() {
            var existing = PoParser.Serialize(new[] {
                new PoEntry { Msgid = "hello", Msgstr = "你好" },
            });
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "hello" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual("你好", entries[0].Msgstr);
        }

        [Test] public void Merge_ExtractedDoesNotIncludeOldMsgid_RemovesIt() {
            var existing = PoParser.Serialize(new[] {
                new PoEntry { Msgid = "old", Msgstr = "obsolete-tr" },
                new PoEntry { Msgid = "kept", Msgstr = "tr" },
            });
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "kept" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("kept", entries[0].Msgid);
        }

        [Test] public void Merge_NewExtractionRefreshesComments() {
            var existing = PoParser.Serialize(new[] {
                new PoEntry { Msgid = "x", Msgstr = "y",
                              TranslatorComments = new() { "old comment" } },
            });
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "x", ExtractedComments = { "fresh hint" } },
            });
            var entries = PoParser.Parse(result).ToList();
            // existing user msgstr preserved; comments come from current extraction
            Assert.AreEqual("y", entries[0].Msgstr);
            Assert.IsTrue(entries[0].TranslatorComments.Any(c => c.Contains("fresh hint")));
            Assert.IsFalse(entries[0].TranslatorComments.Any(c => c.Contains("old comment")));
        }

        [Test] public void Merge_SameMsgidDifferentCtx_TwoEntries() {
            var result = PoFileWriter.Merge("", new[] {
                new ExtractedString { Msgid = "Open" },
                new ExtractedString { Msgid = "Open", Msgctxt = "door" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(entries.Any(e => e.Msgctxt == null));
            Assert.IsTrue(entries.Any(e => e.Msgctxt == "door"));
        }
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="PoFileWriterTests")
```

- [ ] **Step 3: Implement writer**

```csharp
// Editor/I18n/PoFileWriter.cs
using System.Collections.Generic;
using System.Linq;
using PromptUGUI.I18n;

namespace PromptUGUI.Editor.I18n {
    internal static class PoFileWriter {
        /// <summary>
        /// Take an existing .po file's text and a fresh extraction; produce a new .po text where
        /// existing non-empty msgstr values are preserved (keyed by (msgctxt, msgid)), comments are
        /// replaced by the current extraction, and entries no longer in extraction are dropped.
        /// </summary>
        public static string Merge(string existingPoText, IEnumerable<ExtractedString> extracted) {
            var existingByKey = string.IsNullOrEmpty(existingPoText)
                ? new Dictionary<(string, string), PoEntry>()
                : PoParser.Parse(existingPoText)
                    .ToDictionary(e => (e.Msgctxt, e.Msgid));

            // Group extracted by key so multi-occurrence msgid merge their comments.
            var grouped = new Dictionary<(string ctx, string id), ExtractedString>();
            foreach (var e in extracted) {
                var k = (e.Msgctxt, e.Msgid);
                if (!grouped.TryGetValue(k, out var existing)) {
                    grouped[k] = new ExtractedString {
                        Msgid = e.Msgid,
                        Msgctxt = e.Msgctxt,
                        Comments = new List<string>(e.Comments),
                        ExtractedComments = new List<string>(e.ExtractedComments),
                        References = new List<string>(e.References),
                    };
                } else {
                    existing.ExtractedComments.AddRange(e.ExtractedComments);
                    existing.References.AddRange(e.References);
                    existing.Comments.AddRange(e.Comments);
                }
            }

            var output = new List<PoEntry>();
            foreach (var kv in grouped) {
                var es = kv.Value;
                var entry = new PoEntry {
                    Msgctxt = es.Msgctxt,
                    Msgid = es.Msgid,
                    Msgstr = existingByKey.TryGetValue(kv.Key, out var prev) ? prev.Msgstr : "",
                    TranslatorComments = new List<string>(),
                };
                // Merge comment streams: # translator (free) + #. extracted + #: refs.
                foreach (var c in es.Comments) entry.TranslatorComments.Add(c);
                foreach (var c in es.ExtractedComments) entry.TranslatorComments.Add($". {c}");
                foreach (var r in es.References) entry.TranslatorComments.Add($": {r}");
                output.Add(entry);
            }
            // Stable order: by ctx, then by msgid.
            output.Sort((a, b) => {
                int c = string.Compare(a.Msgctxt ?? "", b.Msgctxt ?? "", System.StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.Msgid ?? "", b.Msgid ?? "", System.StringComparison.Ordinal);
            });
            return PoParser.Serialize(output);
        }
    }
}
```

(Note: the `# .` and `# :` prefixes in TranslatorComments leak into the parser as plain comment lines on round-trip — the parser stores everything in TranslatorComments. That's intentional: PoParser doesn't distinguish `#`/`#.`/`#:` types in v1. The `. ` / `: ` markers preserve diagnostic intent in the serialized output. Tests cover the visible behavior.)

- [ ] **Step 4: Run; iterate to pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="PoFileWriterTests")
```

Expected: 5/5 PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/I18n/PoFileWriter.cs Editor/I18n/PoFileWriter.cs.meta \
        Tests/EditMode/Editor/PoFileWriterTests.cs
git commit -m "feat(i18n-editor): PoFileWriter — merge fresh extraction with existing msgstr"
```

---

## Task 6: StringExtractor — top-level menu coordinator

**Files:**
- Create: `Editor/I18n/StringExtractor.cs`

- [ ] **Step 1: Implement extractor + menu**

```csharp
// Editor/I18n/StringExtractor.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    internal static class StringExtractor {
        const string OutputRoot = "Assets/Resources/PromptUGUI/i18n";

        [MenuItem("Tools/PromptUGUI/Extract Strings")]
        public static void ExtractAll() {
            var settings = PromptUGUISettings.Instance;
            if (settings == null || settings.locales.Count == 0) {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No PromptUGUISettings found, or it has no locales configured. " +
                    "Add one and configure locales first.",
                    "OK");
                return;
            }

            var allExtracted = new List<ExtractedString>();
            allExtracted.AddRange(ScanAllXml());
            allExtracted.AddRange(ScanAllCSharp());

            // Group by partition.
            var byPartition = allExtracted
                .GroupBy(e => e.LocalePartition ?? "_code")
                .ToDictionary(g => g.Key, g => g.ToList());

            int filesWritten = 0;
            foreach (var lc in settings.locales) {
                if (string.IsNullOrEmpty(lc.locale)) continue;
                foreach (var kv in byPartition) {
                    var path = Path.Combine(OutputRoot, lc.locale, kv.Key + ".po")
                        .Replace('\\', '/');
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var existing = File.Exists(path) ? File.ReadAllText(path) : "";
                    var merged = PoFileWriter.Merge(existing, kv.Value);
                    File.WriteAllText(path, merged);
                    filesWritten++;
                }
            }
            AssetDatabase.Refresh();
            Debug.Log($"[PromptUGUI] Extract Strings: {allExtracted.Count} msgids → {filesWritten} .po files across {settings.locales.Count} locales.");
        }

        static IEnumerable<ExtractedString> ScanAllXml() {
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml")) continue;
                if (path.StartsWith("Packages/")) continue;
                var text = File.ReadAllText(path);
                var partition = PathToPartition(path);
                foreach (var es in XmlStringScanner.Scan(text, partition)) {
                    if (es.References.Count == 0) es.References.Add(path);
                    yield return es;
                }
            }
        }

        static IEnumerable<ExtractedString> ScanAllCSharp() {
            var guids = AssetDatabase.FindAssets("t:Script");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs")) continue;
                if (path.StartsWith("Packages/")) continue;
                if (path.Contains("/Tests/")) continue;
                var text = File.ReadAllText(path);
                foreach (var es in CSharpStringScanner.Scan(text, path))
                    yield return es;
            }
        }

        static string PathToPartition(string assetPath) {
            // "Assets/UI/screens/MainMenu.ui.xml" → "screens/MainMenu"
            // "Assets/UI/common/Buttons.ui.xml"   → "common/Buttons"
            const string prefix = "Assets/";
            var p = assetPath.StartsWith(prefix) ? assetPath.Substring(prefix.Length) : assetPath;
            // Drop top-level folder (UI/) for shorter partitions; but only if path is multi-segment.
            int firstSlash = p.IndexOf('/');
            if (firstSlash > 0) p = p.Substring(firstSlash + 1);
            if (p.EndsWith(".ui.xml")) p = p.Substring(0, p.Length - ".ui.xml".Length);
            return p;
        }
    }
}
```

- [ ] **Step 2: Refresh + manual try**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

In the host project, click `Tools → PromptUGUI → Extract Strings` and verify .po files appear under `Assets/Resources/PromptUGUI/i18n/<locale>/...`. (Skip if no host project at hand; rely on later integration.)

- [ ] **Step 3: Commit**

```bash
git add Editor/I18n/StringExtractor.cs Editor/I18n/StringExtractor.cs.meta
git commit -m "feat(i18n-editor): Extract Strings menu — scan + merge into per-partition .po"
```

---

## Task 7: TranslationProvider + TranslationAuth (settings)

**Files:**
- Create: `Editor/I18n/TranslationProvider.cs`
- Create: `Editor/I18n/TranslationAuth.cs`
- Create: `Editor/I18n/TranslationProviderSettingsProvider.cs`

- [ ] **Step 1: Write the SOs**

```csharp
// Editor/I18n/TranslationProvider.cs
using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    /// <summary>
    /// Project-level translation provider config. Lives at
    /// `ProjectSettings/PromptUGUI.asset` (in repo, team-shared).
    /// </summary>
    internal sealed class TranslationProvider : ScriptableObject {
        public string endpoint = "https://api.openai.com/v1/chat/completions";
        public string model = "gpt-4o-mini";
        [TextArea(6, 20)] public string systemPrompt =
@"你正在为一款游戏翻译 UI 字符串到 {{targetLocale}}。

规则：
1. 保留所有 {{x}} 模板占位符与 {0} {1:C} 等 C# 格式占位符不变
2. 保留 TMP 富文本标签（<sprite>、<color>、<b>、<size>、<link> 等）的字面形式与属性值不变（特别是 name=""..."", color=""..."" 等属性内的值是资源 ID，不是文本）；位置可调以符合目标语言语序
3. 参考 sibling strings 推断风格一致性
4. 源文本可能混合多种语言；按目标 locale 翻译整体含义
5. 简短直接；UI 空间有限";
    }
}
```

```csharp
// Editor/I18n/TranslationAuth.cs
using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    /// <summary>
    /// Per-user secret. Lives at `UserSettings/PromptUGUI/Auth.asset` (Unity 2020+
    /// default .gitignore excludes UserSettings/).
    /// </summary>
    internal sealed class TranslationAuth : ScriptableObject {
        public string apiKey = "";
    }
}
```

```csharp
// Editor/I18n/TranslationProviderSettingsProvider.cs
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    internal static class TranslationProviderSettingsProvider {
        const string ProviderPath = "ProjectSettings/PromptUGUI.asset";
        const string AuthPath = "UserSettings/PromptUGUI/Auth.asset";

        internal static TranslationProvider GetOrCreateProvider() {
            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(ProviderPath);
            if (loaded != null && loaded.Length > 0 && loaded[0] is TranslationProvider tp) return tp;
            var fresh = ScriptableObject.CreateInstance<TranslationProvider>();
            Save(fresh, ProviderPath);
            return fresh;
        }

        internal static TranslationAuth GetOrCreateAuth() {
            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(AuthPath);
            if (loaded != null && loaded.Length > 0 && loaded[0] is TranslationAuth a) return a;
            var fresh = ScriptableObject.CreateInstance<TranslationAuth>();
            Save(fresh, AuthPath);
            return fresh;
        }

        internal static void Save(Object obj, string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            InternalEditorUtility.SaveToSerializedFileAndForget(new[] { obj }, path, allowTextSerialization: true);
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new("Project/PromptUGUI/Translation", SettingsScope.Project) {
            label = "Translation",
            guiHandler = _ => {
                var tp = GetOrCreateProvider();
                var auth = GetOrCreateAuth();
                EditorGUI.BeginChangeCheck();
                tp.endpoint = EditorGUILayout.TextField("Endpoint", tp.endpoint);
                tp.model = EditorGUILayout.TextField("Model", tp.model);
                EditorGUILayout.LabelField("System Prompt");
                tp.systemPrompt = EditorGUILayout.TextArea(tp.systemPrompt, GUILayout.Height(140));
                EditorGUILayout.Space();
                auth.apiKey = EditorGUILayout.PasswordField("API Key (UserSettings)", auth.apiKey);
                if (EditorGUI.EndChangeCheck()) {
                    Save(tp, ProviderPath);
                    Save(auth, AuthPath);
                }
            },
            keywords = new System.Collections.Generic.HashSet<string> {
                "PromptUGUI", "Translation", "OpenAI", "i18n",
            },
        };
    }
}
```

(Use `using UnityEditorInternal;` for `InternalEditorUtility`. Add to the file if not already present.)

- [ ] **Step 2: Refresh, verify Settings panel renders**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Manual: open `Project Settings → PromptUGUI → Translation`, confirm fields visible. Edit something; verify `ProjectSettings/PromptUGUI.asset` is created.

- [ ] **Step 3: Commit**

```bash
git add Editor/I18n/TranslationProvider.cs Editor/I18n/TranslationProvider.cs.meta \
        Editor/I18n/TranslationAuth.cs Editor/I18n/TranslationAuth.cs.meta \
        Editor/I18n/TranslationProviderSettingsProvider.cs Editor/I18n/TranslationProviderSettingsProvider.cs.meta
git commit -m "feat(i18n-editor): Translation provider config (ProjectSettings + UserSettings)"
```

---

## Task 8: TranslationClient — HTTP + Structured Outputs

**Files:**
- Create: `Editor/I18n/TranslationClient.cs`
- Create: `Tests/EditMode/Editor/TranslationClientTests.cs`

- [ ] **Step 1: Write failing tests with stubbed handler**

```csharp
// Tests/EditMode/Editor/TranslationClientTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor {
    public class TranslationClientTests {
        sealed class StubHandler : HttpMessageHandler {
            public Func<HttpRequestMessage, HttpResponseMessage> Reply;
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage req, CancellationToken ct) => Task.FromResult(Reply(req));
        }

        [Test]
        public async Task TranslateBatch_ParsesStructuredJson() {
            var stub = new StubHandler {
                Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(@"
                        { ""choices"": [ { ""message"": {
                          ""content"": ""{\""translations\"":[{\""msgid\"":\""hello\"",\""msgctxt\"":null,\""msgstr\"":\""你好\""}]}""
                        } } ] }"),
                },
            };
            var client = new TranslationClient(new HttpClient(stub));
            var input = new List<TranslationItem> {
                new() { Msgid = "hello", Msgctxt = null, Comments = new() { "ctx" } },
            };
            var result = await client.TranslateBatch(
                input, "zh-Hans",
                endpoint: "https://example/v1", model: "x", apiKey: "k", systemPrompt: "p",
                CancellationToken.None);
            Assert.AreEqual("你好", result["hello"]);
        }

        [Test]
        public void TranslateBatch_NonOk_Throws() {
            var stub = new StubHandler {
                Reply = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) {
                    Content = new StringContent(""),
                },
            };
            var client = new TranslationClient(new HttpClient(stub));
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await client.TranslateBatch(
                    new List<TranslationItem>(),
                    "zh-Hans", "https://e/v1", "x", "k", "p", CancellationToken.None));
        }
    }
}
```

- [ ] **Step 2: Run; expect compile fail**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="TranslationClientTests")
```

- [ ] **Step 3: Implement client**

```csharp
// Editor/I18n/TranslationClient.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PromptUGUI.Editor.I18n {
    internal sealed class TranslationItem {
        public string Msgid { get; set; }
        public string Msgctxt { get; set; }
        public List<string> Comments { get; set; } = new();
    }

    internal sealed class TranslationClient {
        readonly HttpClient _http;
        public TranslationClient(HttpClient http = null) {
            _http = http ?? new HttpClient();
        }

        public async Task<Dictionary<string, string>> TranslateBatch(
            IList<TranslationItem> items,
            string targetLocale,
            string endpoint, string model, string apiKey, string systemPrompt,
            CancellationToken ct) {

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var prompt = systemPrompt.Replace("{{targetLocale}}", targetLocale);
            var userMessage = JsonSerializer.Serialize(new {
                target_locale = targetLocale,
                items = items.Select(i => new {
                    msgid = i.Msgid,
                    msgctxt = i.Msgctxt,
                    comments = i.Comments,
                }),
            });
            var body = new {
                model,
                messages = new object[] {
                    new { role = "system", content = prompt },
                    new { role = "user", content = userMessage },
                },
                response_format = new {
                    type = "json_schema",
                    json_schema = new {
                        name = "Translations",
                        strict = true,
                        schema = new {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "translations" },
                            properties = new {
                                translations = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        additionalProperties = false,
                                        required = new[] { "msgid", "msgctxt", "msgstr" },
                                        properties = new {
                                            msgid = new { type = "string" },
                                            msgctxt = new { type = new[] { "string", "null" } },
                                            msgstr = new { type = "string" },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            req.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var text = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(text);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content").GetString();
            using var inner = JsonDocument.Parse(content);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in inner.RootElement.GetProperty("translations").EnumerateArray()) {
                var msgid = t.GetProperty("msgid").GetString();
                var msgstr = t.GetProperty("msgstr").GetString();
                result[msgid] = msgstr;
            }
            return result;
        }
    }
}
```

(`System.Text.Json` ships in modern Unity. If not, fall back to `Newtonsoft.Json` which Unity bundles via `com.unity.nuget.newtonsoft-json`. Try `System.Text.Json` first; if compile fails, swap to Newtonsoft and re-run tests.)

- [ ] **Step 4: Run; expect pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="TranslationClientTests")
```

Expected: 2/2 PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/I18n/TranslationClient.cs Editor/I18n/TranslationClient.cs.meta \
        Tests/EditMode/Editor/TranslationClientTests.cs
git commit -m "feat(i18n-editor): TranslationClient — OpenAI-compat with Structured Outputs"
```

---

## Task 9: TranslationMenu — wiring the UX

**Files:**
- Create: `Editor/I18n/TranslationMenu.cs`

- [ ] **Step 1: Implement menu + dialog**

```csharp
// Editor/I18n/TranslationMenu.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    internal static class TranslationMenu {
        const string I18nRoot = "Assets/Resources/PromptUGUI/i18n";
        const int BatchSize = 50;

        [MenuItem("Tools/PromptUGUI/Translate Locale...")]
        public static void OpenDialog() {
            var settings = PromptUGUISettings.Instance;
            if (settings == null || settings.locales.Count == 0) {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No PromptUGUISettings or no locales configured.",
                    "OK");
                return;
            }
            TranslateLocaleWindow.Show(
                settings.locales.Select(l => l.locale).Where(s => !string.IsNullOrEmpty(s)).ToArray(),
                RunFor);
        }

        sealed class TranslateLocaleWindow : EditorWindow {
            string[] _locales;
            int _selected;
            System.Action<string> _onConfirm;

            public static void Show(string[] locales, System.Action<string> onConfirm) {
                var w = CreateInstance<TranslateLocaleWindow>();
                w.titleContent = new GUIContent("Translate Locale");
                w._locales = locales;
                w._onConfirm = onConfirm;
                w.minSize = new Vector2(320, 100);
                w.ShowUtility();
            }

            void OnGUI() {
                EditorGUILayout.LabelField("Target locale:");
                _selected = EditorGUILayout.Popup(_selected, _locales);
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Cancel")) Close();
                    if (GUILayout.Button("Translate")) {
                        var picked = _locales[_selected];
                        Close();
                        _onConfirm(picked);
                    }
                }
            }
        }

        static void RunFor(string locale) {
            var auth = TranslationProviderSettingsProvider.GetOrCreateAuth();
            var provider = TranslationProviderSettingsProvider.GetOrCreateProvider();
            if (string.IsNullOrEmpty(auth.apiKey)) {
                EditorUtility.DisplayDialog("PromptUGUI",
                    "No API key set. Edit at Project Settings → PromptUGUI → Translation.",
                    "OK");
                return;
            }

            var localeDir = Path.Combine(I18nRoot, locale);
            if (!Directory.Exists(localeDir)) {
                EditorUtility.DisplayDialog("PromptUGUI",
                    $"No .po files for locale '{locale}'. Run Extract Strings first.",
                    "OK");
                return;
            }

            // Collect empty-msgstr entries.
            var queue = new List<(string poPath, PoEntry entry)>();
            foreach (var po in Directory.GetFiles(localeDir, "*.po", SearchOption.AllDirectories)) {
                var text = File.ReadAllText(po);
                foreach (var e in PoParser.Parse(text)) {
                    if (string.IsNullOrEmpty(e.Msgstr)) queue.Add((po, e));
                }
            }
            if (queue.Count == 0) {
                EditorUtility.DisplayDialog("PromptUGUI",
                    "No empty msgstr entries to translate.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                "PromptUGUI",
                $"Translate {queue.Count} empty entries for locale '{locale}'?",
                "Translate", "Cancel")) return;

            var client = new TranslationClient();
            int done = 0;
            using var cts = new CancellationTokenSource();
            try {
                for (int i = 0; i < queue.Count; i += BatchSize) {
                    var slice = queue.Skip(i).Take(BatchSize).ToList();
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "PromptUGUI Translate",
                        $"Batch {i / BatchSize + 1} / {(queue.Count + BatchSize - 1) / BatchSize}",
                        (float)i / queue.Count)) {
                        cts.Cancel();
                        break;
                    }
                    var items = slice.Select(p => new TranslationItem {
                        Msgid = p.entry.Msgid,
                        Msgctxt = p.entry.Msgctxt,
                        Comments = p.entry.TranslatorComments,
                    }).ToList();

                    Dictionary<string, string> map = null;
                    int retry = 0;
                    Exception lastEx = null;
                    while (retry < 3) {
                        try {
                            map = client.TranslateBatch(
                                items, locale,
                                provider.endpoint, provider.model, auth.apiKey,
                                provider.systemPrompt,
                                cts.Token).GetAwaiter().GetResult();
                            break;
                        } catch (Exception e) {
                            lastEx = e;
                            retry++;
                            System.Threading.Thread.Sleep(300 * retry);
                        }
                    }
                    if (map == null) {
                        Debug.LogWarning($"[PromptUGUI] batch failed after retries: {lastEx?.Message}");
                        continue;
                    }
                    // Write back.
                    foreach (var (poPath, entry) in slice) {
                        if (!map.TryGetValue(entry.Msgid, out var translated)) continue;
                        WriteMsgstr(poPath, entry, translated);
                        done++;
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[PromptUGUI] Translate Locale '{locale}': filled {done} / {queue.Count} entries.");
        }

        static void WriteMsgstr(string poPath, PoEntry target, string newMsgstr) {
            var entries = PoParser.Parse(File.ReadAllText(poPath)).ToList();
            var idx = entries.FindIndex(e => e.Msgctxt == target.Msgctxt && e.Msgid == target.Msgid);
            if (idx < 0) return;
            entries[idx].Msgstr = newMsgstr;
            File.WriteAllText(poPath, PoParser.Serialize(entries));
        }
    }
}
```

- [ ] **Step 2: Refresh + manual smoke (host project)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Manual in host: configure key, run extract, then `Tools → PromptUGUI → Translate Locale...`, confirm batches process.

- [ ] **Step 3: Commit**

```bash
git add Editor/I18n/TranslationMenu.cs Editor/I18n/TranslationMenu.cs.meta
git commit -m "feat(i18n-editor): Translate Locale menu — batch LLM with progress + retry"
```

---

## Task 10: SKILL.md sync

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: Add `font` / `tr` / `ctx` to attribute tables**

In SKILL.md, find the table row for `<Text>` and add: `font` (string, font type from Settings; default `default`), `tr` (bool, default true), `ctx` (string, only used to disambiguate same-msgid in the .po table).

For `<Btn>` row, change the description to indicate `<Btn>开始</Btn>` shorthand now creates an internal label. Add the same `font` / `tr` / `ctx` attributes.

- [ ] **Step 2: Add an "i18n & fonts" section after "Variants: 运行时切换"**

Insert section text:

```markdown
## i18n & 字体（M5 起）

源文本直接写在 `<Text>` / `<Btn>` 中。`UI.Locale.Set("en")` 切语言；切语言走 Variant 通路，已 open 的 Screen 自动 ReSolve。

```xml
<!-- 源文本 = msgid；零 key -->
<Text>开始游戏</Text>
<Btn>设置</Btn>

<!-- 不要翻译 -->
<Text tr="false">{{playerName}}</Text>

<!-- 同 msgid 多义；ctx 进 msgctxt -->
<Btn ctx="door">Open</Btn>
<Btn ctx="file-menu">Open</Btn>

<!-- 字体 type 走 Settings；缺省 "default" -->
<Text font="title">设置</Text>
<Text font="damage" fontSize="96">9999!</Text>

<!-- 配合既有 Variant 系统 -->
<Text font="title" font.zh-Hans="title-cn">设置</Text>
```

C#:

```csharp
// 切 locale；同时切 .po 表与字体表
UI.Locale.Set("en");
UI.Locale.SetToSystemDefault();

// 代码里抽取的字符串
var text = string.Format(c, UI.Tr("总价: {0:C}"), price);
```

**保留命名空间**：`UI.Locale.Set("zh-Hans")` 内部把 `zh-Hans` 注册为活跃 Variant。author 不应使用同名 Variant 表达 locale 之外的状态。

### 图文混排 / TMP 富文本

`<Text>` 默认不允许 mix text + child elements。要 inline 写 `<sprite>` / `<color>` 等 TMP 标签，包 CDATA：

```xml
<Text><![CDATA[金币: <sprite name="coin"/>{{count}}]]></Text>
<Text><![CDATA[<color=#ff0>警告</color>: 库存不足]]></Text>
```

抽取器把 CDATA 内容作为完整 msgid 抽出；运行时翻译保留标签。
```

- [ ] **Step 3: Add cheatsheet entry**

In the speed-reference at the bottom of SKILL.md, add:

```
## i18n
<Text>...</Text>          抽取 + 翻译
<Text tr="false">...</Text>     跳过
<Text font="title">...</Text>   字体 type
<Text ctx="door">Open</Text>    msgctxt 消歧
UI.Tr("...")             C# 抽取入口
UI.Locale.Set("zh-Hans") 切 locale (= 切 .po + 切字体)
```

- [ ] **Step 4: Refresh, verify links / formatting**

Read the file, sanity-check rendering and table formatting.

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): document i18n + fonts (font/tr/ctx attrs, CDATA, UI.Locale)"
```

---

## Task 11: Main spec sync

**Files:**
- Modify: `docs/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1: Flip §10 i18n entry**

Find:

```
- ❌ **本地化**：文本由代码侧推送或经过外部 L10N 钩子；描述文件不内置 i18n
```

Replace with:

```
- ✅ **本地化** (M5 起)：见 `2026-05-08-i18n-fonts-design.md`
  - 零 key gettext 流（msgid = 源文本字面量）
  - .po 表 + Roslyn / XML 抽取 + LLM 翻译菜单
  - locale 切换走 Variant.Changed 通路
  - 字体 type → 每 locale TMP_FontAsset 表（Settings）
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "docs(spec): flip §10 i18n non-goal — now M5 deliverable"
```

---

## Task 12: Final smoke

- [ ] **Step 1: Full EditMode + EditorOnly + PlayMode**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: all PASS, no console errors.

- [ ] **Step 2: Manual extract + translate dry-run**

In host project:
1. Tools → PromptUGUI → Extract Strings
2. Verify .po files appear under `Assets/Resources/PromptUGUI/i18n/<locale>/`
3. Project Settings → PromptUGUI → Translation; set API key (from a sandbox account)
4. Tools → PromptUGUI → Translate Locale… → pick non-source locale
5. Verify msgstr fields populate

- [ ] **Step 3: Verify hot reload still works**

Edit a .po file by hand; confirm `UIAssetPostprocessor` picks it up and `UI.Locale.ReloadCurrent` updates open Screens.

---

## What M5b Ships

After this plan:
- One menu click extracts every translatable string from `.ui.xml` and `*.cs` into per-partition `.po` files for every Settings locale
- One menu click + dialog → batch LLM call fills empty msgstr entries with structured-outputs JSON
- Project Settings panel exposes endpoint / model / system prompt; UserSettings holds API key out of git
- SKILL.md and main spec reflect the new public surface — future LLM authoring sessions know about it
- Author writing in mixed-language source can ship to any locale by clicking 2 menus + reviewing translations

---

## Notes for the executing agent

- **Roslyn availability**: `Microsoft.CodeAnalysis.CSharp` is bundled in Unity Editor (Unity 2020+). If `using Microsoft.CodeAnalysis.CSharp;` won't compile, the agent must add the package reference; report to user before bypassing.
- **System.Text.Json**: Unity 6 has it natively. If older, swap to `Newtonsoft.Json` (Unity bundles via `com.unity.nuget.newtonsoft-json`).
- **Editor-only**: every file in this plan lives under `Editor/`; nothing must leak to `Runtime/`.
- **i18n-custom is sacred**: extractor MUST NOT write to `Resources/PromptUGUI/i18n-custom/`. Only `i18n/`.
- **Comment markers** in Po: PoParser stores all `#`-prefixed lines into TranslatorComments. The writer emits `# .` / `# :` etc. as plain comments — this round-trips losslessly. Don't introduce a new typed-comment API for this milestone.
- **API key handling**: never log the key; never write it to Project asset; never include it in error messages.
- **DRY / YAGNI**: don't add helper methods until a second caller exists. The settings provider is intentionally minimal.
- **Test-first**: every public function gets a test before implementation.
- **No `--no-verify` commits**.
