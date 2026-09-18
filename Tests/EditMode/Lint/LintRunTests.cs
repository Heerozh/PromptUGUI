using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <see cref="LintRun"/> is everything the UIXmlLint CLI's <c>Program.cs</c> used to own besides
    /// argument parsing: read, parse, prefetch the Import closure, walk with
    /// <see cref="DocumentLinter"/>, spell each finding as <c>file:line: [CODE] msg (via …)</c>, and
    /// fold duplicates ACROSS entry files. The Editor menu is a second front end over the same run,
    /// so what is tested here is what both print.
    /// </summary>
    public class LintRunTests
    {
        private static string Xml(string body) =>
            "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>\n" + body + "\n</PromptUGUI>";

        private sealed class FakeFs
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);

            public FakeFs Add(string path, string body)
            {
                Files[path] = Xml(body);
                return this;
            }

            public readonly List<string> Reads = new List<string>();

            public string Read(string path)
            {
                Reads.Add(path);
                return Files.TryGetValue(path, out var xml) ? xml : throw new FileNotFoundException("no such file", path);
            }

            public string Resolve(string src, string importing) => Files.ContainsKey(src) ? src : null;
        }

        private static List<LintRun.Finding> Lint(LintRun run, FakeFs fs, string path,
                                                  IReadOnlyList<ImportRef> commons = null) =>
            run.Lint(path, fs.Read, fs.Resolve, commons);

        private static ImportRef Commons(string src, string ns = null) => new ImportRef(src, ns);

        // ── the three kinds ────────────────────────────────────────────────────────────────────

        [Test]
        public void ReadFailure_IsAnError_FiledAgainstThePath()
        {
            var run = new LintRun();
            var findings = Lint(run, new FakeFs(), "missing.ui.xml");

            var f = findings.Single();
            Assert.AreEqual(LintRun.Kind.Error, f.Kind);
            Assert.AreEqual("missing.ui.xml", f.File);
            StringAssert.StartsWith("missing.ui.xml: read failed: ", f.Text);
            Assert.AreEqual(1, run.Issues, "a file that cannot be read is a failed lint, not a skipped one");
        }

        [Test]
        public void ParseError_IsAnError()
        {
            var fs = new FakeFs().Add("bad.ui.xml", "<Screen><Frame id='f'/></Screen>");   // <Screen> requires name
            var run = new LintRun();

            var f = Lint(run, fs, "bad.ui.xml").Single();

            Assert.AreEqual(LintRun.Kind.Error, f.Kind);
            Assert.AreEqual("bad.ui.xml", f.File);
            StringAssert.StartsWith("bad.ui.xml: parse error: ", f.Text);
            Assert.AreEqual(1, run.Issues);
        }

        [Test]
        public void MalformedXml_IsAnError_WithThePosition()
        {
            var fs = new FakeFs();
            fs.Files["broken.ui.xml"] = "<PromptUGUI version='1'>\n  <Screen name='S'>\n    <Frame id='f'>\n";
            var run = new LintRun();

            var f = Lint(run, fs, "broken.ui.xml").Single();

            Assert.AreEqual(LintRun.Kind.Error, f.Kind);
            StringAssert.StartsWith("broken.ui.xml: xml error (line ", f.Text);
            Assert.AreEqual(1, run.Issues);
        }

        [Test]
        public void RuleFinding_IsAnIssue_SpelledFileLineCodeMessage()
        {
            var fs = new FakeFs().Add("main.ui.xml",
                "<Screen name='S'>\n  <Frame id='f' mask='self'/>\n</Screen>");   // <Frame> is on line 4
            var run = new LintRun();

            var findings = Lint(run, fs, "main.ui.xml");

            var f = findings.Single(x => x.Text.Contains(MaskAttributeRules.FrameSelfCode));
            Assert.AreEqual(LintRun.Kind.Issue, f.Kind);
            Assert.AreEqual("main.ui.xml", f.File);
            StringAssert.StartsWith("main.ui.xml:4: [" + MaskAttributeRules.FrameSelfCode + "] ", f.Text);
            Assert.AreEqual(findings.Count, run.Issues);
        }

        [Test]
        public void NoFindings_CountsTheFile_AndNothingElse()
        {
            var fs = new FakeFs().Add("clean.ui.xml",
                "<Screen name='S'><Text id='t' anchor='center'>hi</Text></Screen>");
            var run = new LintRun();

            var findings = Lint(run, fs, "clean.ui.xml");

            CollectionAssert.IsEmpty(findings);
            Assert.AreEqual(1, run.Files);
            Assert.AreEqual(0, run.Issues);
            Assert.AreEqual(0, run.Skipped);
        }

        // ── the expanded pass ──────────────────────────────────────────────────────────────────

        [Test]
        public void FindingInsideAnImportedTemplate_IsFiledAgainstTheLibrary_WithTheInvocationAsVia()
        {
            var fs = new FakeFs()
                .Add("lib.ui.xml", "<Template name='Card'>\n  <Frame id='card' mask='self'/>\n</Template>")
                .Add("main.ui.xml", "<Import src='lib.ui.xml'/>\n<Screen name='S'>\n  <Card/>\n</Screen>");
            var run = new LintRun();

            var findings = Lint(run, fs, "main.ui.xml");

            var f = findings.Single(x => x.Text.Contains(MaskAttributeRules.FrameSelfCode));
            Assert.AreEqual("lib.ui.xml", f.File, "the edit goes where the markup was written");
            StringAssert.StartsWith("lib.ui.xml:4: [", f.Text);
            StringAssert.EndsWith(" (via main.ui.xml:5)", f.Text);
        }

        [Test]
        public void UnresolvableImport_IsANote_AndRawRulesStillApply()
        {
            var fs = new FakeFs().Add("main.ui.xml",
                "<Import src='ghost.ui'/>\n<Screen name='S'>\n  <Frame id='f' mask='self'/>\n  <VStack id='v' class='boxed' anchor='stretch'/>\n</Screen>");
            var run = new LintRun();

            var findings = Lint(run, fs, "main.ui.xml");

            var note = findings.Single(x => x.Kind == LintRun.Kind.Note);
            Assert.AreEqual("main.ui.xml", note.File);
            StringAssert.Contains("skipping expanded pass", note.Text);
            StringAssert.Contains("<Import src=\"ghost.ui\">", note.Text);
            Assert.IsTrue(findings.Any(x => x.Kind == LintRun.Kind.Issue && x.Text.Contains(MaskAttributeRules.FrameSelfCode)),
                "raw rules still apply without the closure");
            Assert.IsFalse(findings.Any(x => x.Text.Contains(DocumentLinter.ExpansionCode)),
                "class='boxed' may well be declared by the unseen library");
            Assert.AreEqual(1, run.Issues, "the note is information, not a failed lint");
        }

        [Test]
        public void ExpansionFailure_IsAnError()
        {
            var fs = new FakeFs().Add("main.ui.xml",
                "<Screen name='S'><Frame id='f' class='does-not-exist'/></Screen>");
            var run = new LintRun();

            var f = Lint(run, fs, "main.ui.xml").Single(x => x.Text.Contains(DocumentLinter.ExpansionCode));

            Assert.AreEqual(LintRun.Kind.Error, f.Kind, "UI.Open() would throw on this document");
            Assert.AreEqual("main.ui.xml", f.File);
            StringAssert.StartsWith("main.ui.xml: [" + DocumentLinter.ExpansionCode + "] ", f.Text);
        }

        // ── across files ───────────────────────────────────────────────────────────────────────

        [Test]
        public void SameDefectReachedFromTwoEntries_IsReportedOnce()
        {
            // Linting a directory walks both the library and the document importing it. The
            // expanded pass of the importer attributes the style's bad value to the library, exactly
            // where the library's own raw pass already reported it.
            var fs = new FakeFs()
                .Add("lib.ui.xml", "<Style name='bad' glow='abc'/>")
                .Add("main.ui.xml", "<Import src='lib.ui.xml'/><Screen name='S'><Text id='t'>x</Text></Screen>");
            var run = new LintRun();

            var fromLib = Lint(run, fs, "lib.ui.xml");
            var fromMain = Lint(run, fs, "main.ui.xml");

            Assert.AreEqual(1, fromLib.Count(x => x.Text.Contains(StyleRules.ProceduralValueCode)));
            Assert.AreEqual(0, fromMain.Count(x => x.Text.Contains(StyleRules.ProceduralValueCode)),
                "already reported against lib.ui.xml by the previous entry");
            Assert.AreEqual(2, run.Files);
            Assert.AreEqual(1, run.Issues);
        }

        [Test]
        public void Counts_AccumulateAcrossFiles()
        {
            var fs = new FakeFs()
                .Add("a.ui.xml", "<Screen name='A'><Frame id='f' mask='self'/></Screen>")
                .Add("b.ui.xml", "<Screen name='B'><Frame id='g' mask='self'/></Screen>");
            var run = new LintRun();

            var a = Lint(run, fs, "a.ui.xml");
            var b = Lint(run, fs, "b.ui.xml");
            Lint(run, fs, "missing.ui.xml");

            Assert.AreEqual(3, run.Files);
            Assert.AreEqual(a.Count + b.Count + 1, run.Issues,
                "every Error and Issue counts; the CLI's exit code and the Editor's summary read this");
        }

        // ── common libraries (2026-09-18 commons-settings spec §4.7, §7-21, §7-22) ─────────────
        //
        // The rows of PromptUGUISettings.commonLibraries are the <Import> every document implicitly
        // has. A run resolves and parses that closure ONCE and reuses it for every entry.

        [Test]
        public void StyleDeclaredOnlyInACommonLibrary_ResolvesInTheExpandedPass()
        {
            var fs = new FakeFs()
                .Add("theme.ui", "<Style name='badge' sprite='ui:pill'/>")
                .Add("main.ui.xml", "<Screen name='S'><VStack id='v' class='badge' anchor='stretch'/></Screen>");
            var run = new LintRun();

            var findings = Lint(run, fs, "main.ui.xml", new[] { Commons("theme.ui") });

            Assert.IsFalse(findings.Any(f => f.Text.Contains(DocumentLinter.ExpansionCode)),
                "the runtime merges the commons pool before expansion; so must the linter");
            Assert.IsTrue(findings.Any(f => f.Text.Contains(PureContainerVisualAttrRules.VisualAttrCode)),
                "…and the expanded pass then sees the sprite the commons class supplies");
        }

        [Test]
        public void UnresolvableCommonLibrary_IsANote_NamingIt_AndRawRulesStillApply()
        {
            var fs = new FakeFs().Add("main.ui.xml",
                "<Screen name='S'>\n  <Frame id='f' mask='self'/>\n  <VStack id='v' class='badge' anchor='stretch'/>\n</Screen>");
            var run = new LintRun();

            var findings = Lint(run, fs, "main.ui.xml", new[] { Commons("theme.ui") });

            var note = findings.Single(f => f.Kind == LintRun.Kind.Note);
            StringAssert.Contains("common library src=\"theme.ui\"", note.Text);
            Assert.IsTrue(findings.Any(f => f.Kind == LintRun.Kind.Issue && f.Text.Contains(MaskAttributeRules.FrameSelfCode)),
                "raw rules still apply without the closure");
            Assert.IsFalse(findings.Any(f => f.Text.Contains(DocumentLinter.ExpansionCode)),
                "class='badge' may well live in the library that could not be read — no false positive");
            Assert.AreEqual(1, run.Issues);
        }

        [Test]
        public void UnresolvableCommonLibrary_IsNotedOncePerRun_NotOncePerEntry()
        {
            var fs = new FakeFs()
                .Add("a.ui.xml", "<Screen name='A'><Text id='t'>x</Text></Screen>")
                .Add("b.ui.xml", "<Screen name='B'><Text id='t'>y</Text></Screen>");
            var run = new LintRun();
            var commons = new[] { Commons("theme.ui") };

            var a = Lint(run, fs, "a.ui.xml", commons);
            var b = Lint(run, fs, "b.ui.xml", commons);

            Assert.AreEqual(1, a.Count(f => f.Kind == LintRun.Kind.Note));
            Assert.AreEqual(0, b.Count(f => f.Kind == LintRun.Kind.Note),
                "forty entries would otherwise print the same note forty times");
            Assert.AreEqual(2, run.Skipped, "…but the summary must still say how many entries lost their expanded pass");
        }

        [Test]
        public void SharedDefectiveCommonLibrary_IsReportedOnce_AndReadOnce()
        {
            var fs = new FakeFs()
                .Add("theme.ui", "<Style name='bad' glow='abc'/>")
                .Add("a.ui.xml", "<Screen name='A'><Text id='t'>x</Text></Screen>")
                .Add("b.ui.xml", "<Screen name='B'><Text id='t'>y</Text></Screen>");
            var run = new LintRun();
            var commons = new[] { Commons("theme.ui") };

            var a = Lint(run, fs, "a.ui.xml", commons);
            var b = Lint(run, fs, "b.ui.xml", commons);

            var bad = a.Concat(b).Where(f => f.Text.Contains(StyleRules.ProceduralValueCode)).ToList();
            Assert.AreEqual(1, bad.Count, "one defect, however many entries reach it");
            Assert.AreEqual("theme.ui", bad[0].File, "reported against the library, where the edit goes");
            Assert.AreEqual(1, fs.Reads.Count(r => r == "theme.ui"), "the commons closure is parsed once per run");
        }

        [Test]
        public void CommonLibraryUnderANamespace_IsInvokedAsNsDotName_AndFiledAgainstTheLibrary()
        {
            var fs = new FakeFs()
                .Add("lib.ui", "<Template name='Card'>\n  <Frame id='card' mask='self'/>\n</Template>")
                .Add("main.ui.xml", "<Screen name='S'>\n  <ui.Card/>\n</Screen>");
            var run = new LintRun();

            var findings = Lint(run, fs, "main.ui.xml", new[] { Commons("lib.ui", "ui") });

            Assert.IsFalse(findings.Any(f => f.Text.Contains(DocumentLinter.ExpansionCode)));
            var f = findings.Single(x => x.Text.Contains(MaskAttributeRules.FrameSelfCode));
            Assert.AreEqual("lib.ui", f.File);
            StringAssert.StartsWith("lib.ui:4: [", f.Text);
            StringAssert.EndsWith(" (via main.ui.xml:4)", f.Text);
        }

        [Test]
        public void TheCommonLibraryItself_LintedAsAnEntry_IsNotMergedOntoItself()
        {
            // A directory run reaches the theme library as an entry; the runtime never loads a
            // library onto itself, so neither may the linter report its every style as a conflict.
            var fs = new FakeFs()
                .Add("theme.ui", "<Style name='badge' sprite='ui:pill'/>")
                .Add("main.ui.xml", "<Screen name='S'><VStack id='v' class='badge' anchor='stretch'/></Screen>");
            var run = new LintRun();
            var commons = new[] { Commons("theme.ui") };

            var lib = Lint(run, fs, "theme.ui", commons);
            var main = Lint(run, fs, "main.ui.xml", commons);

            CollectionAssert.IsEmpty(lib.Where(f => f.Text.Contains(DocumentLinter.ExpansionCode)).Select(f => f.Text).ToList());
            Assert.IsTrue(main.Any(f => f.Text.Contains(PureContainerVisualAttrRules.VisualAttrCode)),
                "…and the next entry still sees the library as its commons");
        }

        [Test]
        public void CommonLibraryRowWithADottedNamespace_IsAnError_OncePerRun()
        {
            // The mirror of PromptUGUISettings.OnValidate: <ns.Name/> cannot spell a namespace that
            // itself contains a dot, so such a row can never be invoked.
            var fs = new FakeFs()
                .Add("lib.ui", "<Template name='Card'><Frame id='card'/></Template>")
                .Add("a.ui.xml", "<Screen name='A'><Text id='t'>x</Text></Screen>")
                .Add("b.ui.xml", "<Screen name='B'><Text id='t'>y</Text></Screen>");
            var run = new LintRun();
            var commons = new[] { Commons("lib.ui", "a.b") };

            var a = Lint(run, fs, "a.ui.xml", commons);
            var b = Lint(run, fs, "b.ui.xml", commons);

            var error = a.Single(f => f.Kind == LintRun.Kind.Error);
            StringAssert.Contains("common library src=\"lib.ui\"", error.Text);
            StringAssert.Contains("as=\"a.b\"", error.Text);
            StringAssert.Contains("must not contain '.'", error.Text);
            Assert.IsNull(error.File, "a configuration error has no file to open");
            Assert.AreEqual(0, b.Count(f => f.Kind == LintRun.Kind.Error), "said once per run");
            Assert.AreEqual(1, run.Issues);
        }
    }
}
