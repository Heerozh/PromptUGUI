using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using PromptUGUI.Lint;
using UnityEditor.PackageManager;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// <c>Tools › PromptUGUI › Lint All UI XML</c> is the UIXmlLint CLI run inside the Editor. The
    /// rules, the two passes and the spelling of a finding are <see cref="LintRun"/>'s and tested
    /// there; what the menu adds — and what is tested here — is the glue between asset paths and
    /// the filesystem: a <c>Packages/…</c> path of a local package is virtual, a finding must name
    /// the file the way the Console can ping it, and an <c>&lt;Import&gt;</c> is resolved the way
    /// the CLI guesses it (plus Addressables, which only the Editor can ask).
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

        // ── asset path ↔ file ──────────────────────────────────────────────────────────────────

        [Test]
        public void Physical_AnAssetsPath_IsUnderTheProjectRoot()
        {
            var expected = N(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "Assets", "UI", "Home.ui.xml"));
            Assert.AreEqual(expected, N(UIXmlLintMenu.Physical("Assets/UI/Home.ui.xml")));
        }

        [Test]
        public void Physical_APackagesPath_IsTheRealFile()
        {
            var physical = UIXmlLintMenu.Physical(
                ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml");

            Assert.IsTrue(File.Exists(physical), physical);
            StringAssert.StartsWith(N(ThisPackage.resolvedPath), N(physical));
        }

        [Test]
        public void Physical_AnAbsolutePath_IsLeftAlone()
        {
            var abs = Path.Combine(_dir, "x.ui.xml");
            Assert.AreEqual(abs, UIXmlLintMenu.Physical(abs));
        }

        [Test]
        public void ToAssetPath_AFileUnderAssets_IsItsAssetPath()
        {
            var physical = Path.Combine(UnityEngine.Application.dataPath, "UI", "Home.ui.xml");
            Assert.AreEqual("Assets/UI/Home.ui.xml", UIXmlLintMenu.ToAssetPath(physical));
        }

        [Test]
        public void ToAssetPath_AFileInsideAPackage_IsItsPackagesPath()
        {
            var physical = Path.Combine(ThisPackage.resolvedPath, "Runtime", "Resources", "PromptUGUI", "Modals", "MessageBox.ui.xml");
            Assert.AreEqual(ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml",
                UIXmlLintMenu.ToAssetPath(physical));
        }

        [Test]
        public void ToAssetPath_AFileOutsideTheProject_StaysAbsolute_WithForwardSlashes()
        {
            var physical = Path.Combine(_dir, "x.ui.xml");
            Assert.AreEqual(N(physical), UIXmlLintMenu.ToAssetPath(physical));
        }

        // ── which files the menu lints ─────────────────────────────────────────────────────────

        [Test]
        public void IsProjectOwned_Assets_Yes_ALocalPackage_No()
        {
            Assert.IsTrue(UIXmlLintMenu.IsProjectOwned("Assets/UI/Home.ui.xml"));
            Assume.That(ThisPackage.source, Is.Not.EqualTo(PackageSource.Embedded),
                "this package is referenced as file:, not embedded, in every host we develop in");
            Assert.IsFalse(UIXmlLintMenu.IsProjectOwned(ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml"),
                "the user cannot edit a package they only reference; its own lint runs in its own repo");
        }
    }
}
