using System;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEditor.PackageManager;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// <see cref="UiXmlLocator"/> is the one Editor-side answer to "which file is this src / asset
    /// path" — the lint menu and the UI Preview tool both ask it (2026-09-18 ui-preview-tool spec
    /// §4.4). Asset path ↔ physical file, project ownership, and <see cref="UiXmlLocator.Locate"/>:
    /// Addressables (exact, tested in the Addressables test assembly) then the CLI's Resources walk
    /// started from the anchoring file's real folder.
    /// </summary>
    public class UiXmlLocatorTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PromptUGUI-locator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private string Write(string relative, string body = "<Template name='T'><Frame/></Template>")
        {
            var path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>\n" + body + "\n</PromptUGUI>");
            return path;
        }

        private static string N(string p) => p.Replace('\\', '/');

        private static PackageInfo ThisPackage => PackageInfo.FindForAssembly(typeof(UiXmlLocator).Assembly);

        // ── asset path ↔ file ──────────────────────────────────────────────────────────────────

        [Test]
        public void Physical_AnAssetsPath_IsUnderTheProjectRoot()
        {
            var expected = N(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "Assets", "UI", "Home.ui.xml"));
            Assert.AreEqual(expected, N(UiXmlLocator.Physical("Assets/UI/Home.ui.xml")));
        }

        [Test]
        public void Physical_APackagesPath_IsTheRealFile()
        {
            var physical = UiXmlLocator.Physical(
                ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml");

            Assert.IsTrue(File.Exists(physical), physical);
            StringAssert.StartsWith(N(ThisPackage.resolvedPath), N(physical));
        }

        [Test]
        public void Physical_AnAbsolutePath_IsLeftAlone()
        {
            var abs = Path.Combine(_dir, "x.ui.xml");
            Assert.AreEqual(abs, UiXmlLocator.Physical(abs));
        }

        [Test]
        public void ToAssetPath_AFileUnderAssets_IsItsAssetPath()
        {
            var physical = Path.Combine(UnityEngine.Application.dataPath, "UI", "Home.ui.xml");
            Assert.AreEqual("Assets/UI/Home.ui.xml", UiXmlLocator.ToAssetPath(physical));
        }

        [Test]
        public void ToAssetPath_AFileInsideAPackage_IsItsPackagesPath()
        {
            var physical = Path.Combine(ThisPackage.resolvedPath, "Runtime", "Resources", "PromptUGUI", "Modals", "MessageBox.ui.xml");
            Assert.AreEqual(ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml",
                UiXmlLocator.ToAssetPath(physical));
        }

        [Test]
        public void ToAssetPath_AFileOutsideTheProject_StaysAbsolute_WithForwardSlashes()
        {
            var physical = Path.Combine(_dir, "x.ui.xml");
            Assert.AreEqual(N(physical), UiXmlLocator.ToAssetPath(physical));
        }

        // ── which files the project owns ───────────────────────────────────────────────────────

        [Test]
        public void IsProjectOwned_Assets_Yes_ALocalPackage_No()
        {
            Assert.IsTrue(UiXmlLocator.IsProjectOwned("Assets/UI/Home.ui.xml"));
            Assume.That(ThisPackage.source, Is.Not.EqualTo(PackageSource.Embedded),
                "this package is referenced as file:, not embedded, in every host we develop in");
            Assert.IsFalse(UiXmlLocator.IsProjectOwned(ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml"),
                "the user cannot edit a package they only reference; its own lint runs in its own repo");
        }

        // ── Locate: src → file ─────────────────────────────────────────────────────────────────

        [Test]
        public void Locate_AResourcesStyleKey_WalksUpFromTheAnchoringFile()
        {
            // UseResourcesResolver("UI") + <Import src='Skin.ui'> written in Resources/UI/Pages/Home:
            // the file is Resources/UI/Skin.ui.xml, one folder up from the importer.
            var lib = Write("Resources/UI/Skin.ui.xml");
            var main = Write("Resources/UI/Pages/Home.ui.xml", "<Screen name='S'><Frame/></Screen>");

            Assert.AreEqual(N(lib), UiXmlLocator.Locate("Skin.ui", main));
        }

        [Test]
        public void Locate_StopsAtTheResourcesFolder()
        {
            // A file above Resources/ is not what Resources.Load would find; the walk must not reach it.
            Write("Skin.ui.xml");
            var main = Write("Resources/UI/Home.ui.xml", "<Screen name='S'><Frame/></Screen>");

            Assert.IsNull(UiXmlLocator.Locate("Skin.ui", main));
        }

        [Test]
        public void Locate_AnUnknownSrc_IsNull_NotAThrow()
        {
            var main = Write("main.ui.xml", "<Screen name='S'><Frame/></Screen>");

            Assert.IsNull(UiXmlLocator.Locate("nothing/like/this.ui", main));
            Assert.IsNull(UiXmlLocator.Locate("nothing/like/this.ui", "Assets/Not/There.ui.xml"),
                "an anchor that does not exist on disk just means nowhere to walk from");
            Assert.IsNull(UiXmlLocator.Locate("nothing/like/this.ui", null),
                "no anchor at all: only the Addressables map can answer, and it does not know this src");
        }

        [Test]
        public void Locate_AnAnchorGivenAsAnAssetPath_IsReadThroughItsPhysicalFolder()
        {
            // The package's own modal skins import "PromptUGUI/Modals/ModalFrame.ui" style keys next
            // to themselves under Runtime/Resources — a Packages/ anchor must be turned into its real
            // folder before walking, or nothing is ever found.
            var anchor = ThisPackage.assetPath + "/Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml";
            var hit = UiXmlLocator.Locate("PromptUGUI/Modals/MessageBox.ui", anchor);

            Assert.AreEqual(anchor, hit, "found via Runtime/Resources/ and spelled back as its Packages/ asset path");
        }
    }
}
