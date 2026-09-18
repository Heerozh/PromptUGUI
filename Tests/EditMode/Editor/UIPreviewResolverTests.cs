using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Editor.Preview;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// The UI Preview's disk-first src resolution and its hot-reload mapping (2026-09-18
    /// ui-preview-tool spec §4.4 / §4.5, §7-2 ~ 6), over fakes: a set of "files on disk", a
    /// scripted locator, no project.
    /// </summary>
    public class UIPreviewResolverTests
    {
        private const string Root = "C:/proj";
        private HashSet<string> _disk;
        private Dictionary<string, string> _locate;   // src → asset path
        private UIPreviewResolver _r;

        [SetUp]
        public void SetUp()
        {
            _disk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _locate = new Dictionary<string, string>(StringComparer.Ordinal);
            _r = new UIPreviewResolver(
                physical: p => p.StartsWith("C:/") ? p : Root + "/" + p,
                exists: p => _disk.Contains(p),
                locate: (src, anchor) => _locate.TryGetValue(src, out var a) ? a : null,
                toAssetPath: p => p.StartsWith(Root + "/") ? p.Substring(Root.Length + 1) : p);
            _r.HasHost = true;
        }

        private void OnDisk(params string[] assetPaths)
        {
            var index = new List<string>(_r.Index);
            foreach (var a in assetPaths)
            {
                _disk.Add(Root + "/" + a);
                index.Add(a);
            }
            _r.Index = index;
        }

        // ── the chain, in order (§7-3) ─────────────────────────────────────────────────────────

        [Test]
        public void Resolve_AnAbsolutePathOnDisk_IsReadFromDisk_AndKeyedByItsAssetPath()
        {
            OnDisk("Assets/UI/Home.ui.xml");

            var r = _r.Resolve(Root + "/Assets/UI/Home.ui.xml");

            Assert.AreEqual(UIPreviewResolver.Source.Disk, r.Source);
            Assert.AreEqual(Root + "/Assets/UI/Home.ui.xml", r.Physical);
            Assert.AreEqual("Assets/UI/Home.ui.xml", r.AssetPath, "served-table key is the asset path, whatever spelling came in");
        }

        [Test]
        public void Resolve_AProjectAssetPath_IsReadThroughItsPhysicalLocation()
        {
            OnDisk("Assets/UI/Home.ui.xml");

            var r = _r.Resolve("Assets/UI/Home.ui.xml");

            Assert.AreEqual(UIPreviewResolver.Source.Disk, r.Source);
            Assert.AreEqual(Root + "/Assets/UI/Home.ui.xml", r.Physical);
        }

        [Test]
        public void Resolve_ALocatedSrc_WinsOverTheSuffixGuess()
        {
            // An Addressables address that looks nothing like its path — only the locator knows.
            OnDisk("Assets/UI/Pages/Home.ui.xml", "Assets/Other/ui-home.ui.xml");
            _locate["ui-home"] = "Assets/UI/Pages/Home.ui.xml";

            var r = _r.Resolve("ui-home");

            Assert.AreEqual(UIPreviewResolver.Source.Disk, r.Source);
            Assert.AreEqual("Assets/UI/Pages/Home.ui.xml", r.AssetPath, "the suffix guess would have picked Other/ui-home.ui.xml");
        }

        [Test]
        public void Resolve_AShortSrc_FallsBackToAUniqueSuffixMatch()
        {
            OnDisk("Assets/_Project/Common/UI/Templates/DefaultTheme.ui.xml", "Assets/_Project/Lobby/Lobby.ui.xml");

            var r = _r.Resolve("UI/Templates/DefaultTheme.ui.xml");

            Assert.AreEqual(UIPreviewResolver.Source.Disk, r.Source);
            Assert.AreEqual("Assets/_Project/Common/UI/Templates/DefaultTheme.ui.xml", r.AssetPath);
        }

        [Test]
        public void Resolve_AMissingProjectPath_IsAnError_NeverTheHost()
        {
            OnDisk("Assets/UI/Other.ui.xml");

            var r = _r.Resolve("Assets/UI/Gone.ui.xml");

            Assert.AreEqual(UIPreviewResolver.Source.Error, r.Source);
            StringAssert.Contains("Assets/UI/Gone.ui.xml", r.Message);
        }

        [Test]
        public void Resolve_AnUnknownSrc_GoesToTheHost_OrErrorsWithoutOne()
        {
            Assert.AreEqual(UIPreviewResolver.Source.Host, _r.Resolve("PromptUGUI/Modals/MessageBox.ui").Source,
                "the package's built-in skins live in Resources — the host's resolver knows");

            _r.HasHost = false;
            var r = _r.Resolve("PromptUGUI/Modals/MessageBox.ui");
            Assert.AreEqual(UIPreviewResolver.Source.Error, r.Source);
            StringAssert.Contains("SourceResolver", r.Message);
        }

        // ── the suffix fallback (§7-4) ─────────────────────────────────────────────────────────

        [Test]
        public void MatchSuffix_AResourcesStyleKey_MatchesTheXmlFile()
        {
            OnDisk("Assets/Resources/UI/Skin.ui.xml");

            Assert.AreEqual("Assets/Resources/UI/Skin.ui.xml", _r.MatchSuffix("Skin.ui"), "X.ui is X.ui.xml on disk");
            Assert.AreEqual("Assets/Resources/UI/Skin.ui.xml", _r.MatchSuffix("Skin.ui.xml"));
            Assert.AreEqual("Assets/Resources/UI/Skin.ui.xml", _r.MatchSuffix("UI/Skin.ui"));
        }

        [Test]
        public void MatchSuffix_TwoCandidates_IsNoAnswer()
        {
            OnDisk("Assets/A/Skin.ui.xml", "Assets/B/Skin.ui.xml");

            Assert.IsNull(_r.MatchSuffix("Skin.ui"), "silently picking one would preview the wrong file");
            Assert.AreEqual("Assets/A/Skin.ui.xml", _r.MatchSuffix("A/Skin.ui"), "a longer suffix disambiguates");
        }

        [Test]
        public void MatchSuffix_MatchesWholePathSegmentsOnly()
        {
            OnDisk("Assets/UI/MySkin.ui.xml");

            Assert.IsNull(_r.MatchSuffix("Skin.ui"), "'MySkin.ui.xml' is not 'Skin.ui'");
        }

        // ── served table and hot reload (§7-5) ─────────────────────────────────────────────────

        [Test]
        public void AssetPathToSrc_PrefersWhatThisResolverServed_ThenTheHostMapping()
        {
            _r.RecordServed("Assets/_Project/Common/UI/Templates/DefaultTheme.ui.xml", "UI/Templates/DefaultTheme.ui.xml");
            string host(string p) => p == "Assets/Elsewhere.ui.xml" ? "elsewhere-address" : null;

            Assert.AreEqual("UI/Templates/DefaultTheme.ui.xml",
                _r.AssetPathToSrc("Assets/_Project/Common/UI/Templates/DefaultTheme.ui.xml", host),
                "the DepGraph knows the short address we loaded it under, not whatever the host would say");
            Assert.AreEqual("elsewhere-address", _r.AssetPathToSrc("Assets/Elsewhere.ui.xml", host));
            Assert.IsNull(_r.AssetPathToSrc("Assets/Unknown.ui.xml", host));
            Assert.IsNull(_r.AssetPathToSrc("Assets/Elsewhere.ui.xml", null), "no host mapping, nothing served: nothing to reload");
        }

        [Test]
        public void RecordServed_TheFirstSrcToReachAFileWins()
        {
            _r.RecordServed("Assets/UI/Foo.ui.xml", "UI/Foo.ui.xml");
            _r.RecordServed("Assets/UI/Foo.ui.xml", "Assets/UI/Foo.ui.xml");

            Assert.AreEqual("UI/Foo.ui.xml", _r.AssetPathToSrc("Assets/UI/Foo.ui.xml", null), "v1: one src per file (spec §4.5)");
        }

        // ── HasScreen / PagesKey (§7-2, §7-6) ──────────────────────────────────────────────────

        [TestCase("<Screen name='S'/>", true)]
        [TestCase("<Screen\n  name='S'>", true)]
        [TestCase("<Screen>", true)]
        [TestCase("<Screen/>", true)]
        [TestCase("<ScreenFoo name='S'/>", false)]
        [TestCase("<Template name='T'><Frame/></Template>", false)]
        [TestCase(null, true)]
        public void HasScreen(string text, bool expected)
        {
            Assert.AreEqual(expected, UIPreviewResolver.HasScreen(text));
        }

        [Test]
        public void PagesKey_IsPerFile_PerId_Numbered()
        {
            Assert.AreEqual("Assets/UI/Planet.ui.xml|pageBuild#0", UIPreviewResolver.PagesKey("Assets/UI/Planet.ui.xml", "pageBuild", 0));
            Assert.AreEqual("Assets/UI/Planet.ui.xml|pageBuild#1", UIPreviewResolver.PagesKey("Assets/UI/Planet.ui.xml", "pageBuild", 1),
                "two template instances with the same id keep separate memories");
            Assert.IsNull(UIPreviewResolver.PagesKey("Assets/UI/Planet.ui.xml", null, 0), "a <Pages> without an id has nothing to be remembered by");
            Assert.IsNull(UIPreviewResolver.PagesKey(null, "pageBuild", 0));
        }
    }
}
