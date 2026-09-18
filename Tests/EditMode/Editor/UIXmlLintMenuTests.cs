using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using UnityEditor.PackageManager;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// <c>Tools › PromptUGUI › Lint All UI XML</c> is the UIXmlLint CLI run inside the Editor. The
    /// rules, the two passes and the spelling of a finding are <see cref="LintRun"/>'s and tested
    /// there; asset path ↔ file and src → file are <see cref="UiXmlLocator"/>'s (tested in
    /// <c>UiXmlLocatorTests</c>). What the menu adds — and what is tested here — is the run itself:
    /// a <c>Packages/…</c> path read through its real location, a finding filed against the library
    /// it lives in, and the common-library rows folded into every document's expanded pass.
    /// </summary>
    public class UIXmlLintMenuTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PromptUGUI-lint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private string Write(string name, string body)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path,
                "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>\n" + body + "\n</PromptUGUI>");
            return path;
        }

        private static string N(string p) => p.Replace('\\', '/');

        private static PackageInfo ThisPackage => PackageInfo.FindForAssembly(typeof(UIXmlLintMenu).Assembly);

        // ── the run ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Lint_ResolvesAnImportOnDisk_AndFilesTheFindingAgainstTheLibrary()
        {
            var lib = Write("lib.ui.xml", "<Template name='Card'>\n  <Frame id='card' mask='self'/>\n</Template>");
            var main = Write("main.ui.xml", "<Import src='lib.ui'/>\n<Screen name='S'>\n  <Card/>\n</Screen>");
            var findings = new List<LintRun.Finding>();

            var run = UIXmlLintMenu.Lint(new[] { main }, findings.Add);

            Assert.AreEqual(1, run.Files);
            CollectionAssert.IsEmpty(findings.Where(f => f.Kind == LintRun.Kind.Note).ToList(),
                "lib.ui next to the importer is what UseResourcesResolver would find");
            var f = findings.Single(x => x.Text.Contains(MaskAttributeRules.FrameSelfCode));
            Assert.AreEqual(N(lib), f.File, "the edit goes into the library; forward slashes are what the Console pings");
            StringAssert.StartsWith(N(lib) + ":4: [", f.Text);
            StringAssert.EndsWith("(via " + N(main) + ":5)", f.Text);
        }

        [Test]
        public void Lint_ReadsALocalPackagesPath_ThroughItsPhysicalLocation()
        {
            // Packages/<name>/… of a file: package does not exist under the project folder; reading
            // it relative to the working directory is what breaks. Modals ship in this package.
            var assetPath = ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml";
            var findings = new List<LintRun.Finding>();

            UIXmlLintMenu.Lint(new[] { assetPath }, findings.Add);

            CollectionAssert.IsEmpty(findings.Where(f => f.Kind == LintRun.Kind.Error).Select(f => f.Text).ToList());
        }

        // ── common libraries (2026-09-18 commons-settings spec §4.8, §7-25) ────────────────────

        [Test]
        public void Lint_WithACommonLibraryRow_ResolvesItFromTheEntry_AndExpandsThroughIt()
        {
            // The row's src is a Resources-style short key, no <Import> anywhere: the library is
            // found the way an <Import src='lib.ui'> written in main.ui.xml would be.
            var lib = Write("lib.ui.xml", "<Template name='Card'>\n  <Frame id='card' mask='self'/>\n</Template>");
            var main = Write("main.ui.xml", "<Screen name='S'>\n  <Card/>\n</Screen>");
            var findings = new List<LintRun.Finding>();

            UIXmlLintMenu.Lint(new[] { main }, findings.Add, new[] { new ImportRef("lib.ui", null) });

            CollectionAssert.IsEmpty(findings.Where(f => f.Kind == LintRun.Kind.Note).Select(f => f.Text).ToList());
            CollectionAssert.IsEmpty(findings.Where(f => f.Text.Contains(DocumentLinter.ExpansionCode)).Select(f => f.Text).ToList(),
                "<Card/> lives in the common library; the runtime would resolve it, so must the menu");
            var f = findings.Single(x => x.Text.Contains(MaskAttributeRules.FrameSelfCode));
            Assert.AreEqual(N(lib), f.File);
            StringAssert.EndsWith("(via " + N(main) + ":4)", f.Text);
        }

        [Test]
        public void ConfiguredCommonLibraries_ReadsTheSettingsRows_SkippingBlankOnes()
        {
            var settings = ScriptableObject.CreateInstance<PromptUGUISettings>();
            settings.commonLibraries.Add(new CommonLibraryEntry { src = " UI/Theme.ui ", @as = "" });
            settings.commonLibraries.Add(new CommonLibraryEntry { src = "", @as = "x" });
            settings.commonLibraries.Add(new CommonLibraryEntry { src = "UI/Widgets.ui", @as = "ui" });
            PromptUGUISettings.FinderForTests = () => new[] { settings };
            PromptUGUISettings.ResetInstanceCache();
            try
            {
                var rows = UIXmlLintMenu.ConfiguredCommonLibraries();

                Assert.AreEqual(2, rows.Count, "a blank src row is inert everywhere");
                Assert.AreEqual("UI/Theme.ui", rows[0].Src);
                Assert.IsNull(rows[0].Namespace, "a blank as is no namespace");
                Assert.AreEqual("UI/Widgets.ui", rows[1].Src);
                Assert.AreEqual("ui", rows[1].Namespace);
            }
            finally
            {
                PromptUGUISettings.FinderForTests = null;
                PromptUGUISettings.ResetInstanceCache();
                Object.DestroyImmediate(settings);
            }
        }
    }
}
