using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// The on-disk half of linting the EXPANDED tree: guessing which file an <c>&lt;Import src&gt;</c>
    /// means, and pulling every reachable document in before <see cref="DocumentLinter"/> assembles
    /// them. Both the UIXmlLint CLI and the Editor menu (<c>Tools › PromptUGUI › Lint All UI XML</c>)
    /// go through this one implementation; they differ only in the delegates they hand it.
    /// </summary>
    public class ImportClosureTests
    {
        // Path.Combine spells the separator per OS; a fake filesystem should not care.
        private static string N(string p) => p?.Replace('\\', '/');

        private static Func<string, bool> Fs(params string[] existing)
        {
            var set = new HashSet<string>(existing, StringComparer.Ordinal);
            return p => set.Contains(N(p));
        }

        private static string Xml(string body) =>
            "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>";

        // ── ResolveInResources: the UseResourcesResolver(root) approximation ──────────────────

        [Test]
        public void ResolveInResources_TriesSrcPlusXml_InTheImportingDirectoryFirst()
        {
            var exists = Fs("Assets/Resources/UI/Skin.ui.xml");
            Assert.AreEqual("Assets/Resources/UI/Skin.ui.xml",
                N(ImportClosure.ResolveInResources("Skin.ui", "Assets/Resources/UI", exists)));
        }

        [Test]
        public void ResolveInResources_FallsBackToTheSrcVerbatim()
        {
            var exists = Fs("Assets/UI/Skin.ui.xml");
            Assert.AreEqual("Assets/UI/Skin.ui.xml",
                N(ImportClosure.ResolveInResources("Skin.ui.xml", "Assets/UI", exists)),
                "an Addressables-style address already carries its extension");
        }

        [Test]
        public void ResolveInResources_WalksUpThroughAncestors_UpToAndIncludingResources()
        {
            var exists = Fs("Assets/Resources/Skin.ui.xml");
            Assert.AreEqual("Assets/Resources/Skin.ui.xml",
                N(ImportClosure.ResolveInResources("Skin.ui", "Assets/Resources/UI/Modals", exists)),
                "UseResourcesResolver(root) maps src to root/src, and root is some ancestor");
        }

        [Test]
        public void ResolveInResources_DoesNotLookAboveResources()
        {
            var exists = Fs("Assets/Skin.ui.xml");
            Assert.IsNull(ImportClosure.ResolveInResources("Skin.ui", "Assets/Resources/UI", exists),
                "Resources.Load cannot see outside Resources/, so neither may the guess");
        }

        [Test]
        public void ResolveInResources_WithoutAResourcesAncestor_WalksToTheTop()
        {
            var exists = Fs("Assets/Skin.ui.xml");
            Assert.AreEqual("Assets/Skin.ui.xml",
                N(ImportClosure.ResolveInResources("Skin.ui", "Assets/UI/Modals", exists)));
        }

        [Test]
        public void ResolveInResources_EmptySrc_IsNull()
        {
            Assert.IsNull(ImportClosure.ResolveInResources("", "Assets", Fs("Assets/.xml")));
            Assert.IsNull(ImportClosure.ResolveInResources(null, "Assets", Fs()));
        }

        // ── TryLoad: all-or-nothing prefetch of the Import closure ─────────────────────────────

        private static Dictionary<string, string> Files(params (string path, string body)[] files)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (path, body) in files) d[path] = Xml(body);
            return d;
        }

        private static Dictionary<string, UIDocument> Load(
            Dictionary<string, string> files, string entry, out string unresolved)
        {
            var doc = UIDocumentParser.Parse(files[entry], entry);
            // The resolver here is a bare table lookup: which file a src means is ResolveInResources'
            // business above, and the closure must work with ANY answer (an Addressables map, say).
            return ImportClosure.TryLoad(
                entry, doc,
                (src, importing) => files.ContainsKey(src) ? src : null,
                path => files[path],
                out unresolved);
        }

        [Test]
        public void TryLoad_FollowsNestedImports_KeyedBySrc_StampedWithTheResolvedPath()
        {
            var files = Files(
                ("main.ui.xml", "<Import src='lib.ui.xml'/><Screen name='S'><Frame id='f'/></Screen>"),
                ("lib.ui.xml", "<Import src='deep.ui.xml'/><Template name='T'><Frame id='t'/></Template>"),
                ("deep.ui.xml", "<Style name='s' color='#fff'/>"));

            var closure = Load(files, "main.ui.xml", out var unresolved);

            Assert.IsNotNull(closure, unresolved);
            CollectionAssert.AreEquivalent(new[] { "lib.ui.xml", "deep.ui.xml" }, closure.Keys,
                "keyed by the src the assembler will ask for; the entry is the caller's to supply");
            Assert.AreEqual("lib.ui.xml", closure["lib.ui.xml"].Templates["T"].Body.OriginSrc,
                "a finding inside the template must name the file it was written in");
        }

        [Test]
        public void TryLoad_CyclicImport_Terminates_AndLeavesTheDiagnosticToTheAssembler()
        {
            var files = Files(
                ("a.ui.xml", "<Import src='b.ui.xml'/><Screen name='S'><Frame id='f'/></Screen>"),
                ("b.ui.xml", "<Import src='a.ui.xml'/><Style name='s' color='#fff'/>"));

            var closure = Load(files, "a.ui.xml", out var unresolved);

            Assert.IsNotNull(closure, unresolved);
            CollectionAssert.AreEquivalent(new[] { "b.ui.xml", "a.ui.xml" }, closure.Keys,
                "the loader only fetches; DocumentAssembler words the cycle error, so CLI and runtime agree");
        }

        [Test]
        public void TryLoad_UnresolvableSrc_ReturnsNull_AndNamesIt()
        {
            var files = Files(
                ("main.ui.xml", "<Import src='lib.ui.xml'/><Import src='ghost.ui'/><Screen name='S'><Frame id='f'/></Screen>"),
                ("lib.ui.xml", "<Style name='s' color='#fff'/>"));

            var closure = Load(files, "main.ui.xml", out var unresolved);

            Assert.IsNull(closure, "a partial closure would report a phantom 'unknown template'");
            Assert.AreEqual("ghost.ui", unresolved);
        }

        [Test]
        public void TryLoad_UnparseableImport_ReturnsNull_AndNamesTheSrc()
        {
            var files = Files(
                ("main.ui.xml", "<Import src='broken.ui.xml'/><Screen name='S'><Frame id='f'/></Screen>"));
            files["broken.ui.xml"] = "<PromptUGUI version='1'><Style name='s'";   // truncated

            var closure = Load(files, "main.ui.xml", out var unresolved);

            Assert.IsNull(closure);
            Assert.AreEqual("broken.ui.xml", unresolved,
                "the broken file is reported when IT is linted; here it just blocks the expanded pass");
        }

        [Test]
        public void TryLoad_NoImports_IsAnEmptyClosure()
        {
            var files = Files(("main.ui.xml", "<Screen name='S'><Frame id='f'/></Screen>"));

            var closure = Load(files, "main.ui.xml", out var unresolved);

            Assert.IsNotNull(closure);
            Assert.IsNull(unresolved);
            CollectionAssert.IsEmpty(closure);
        }

        [Test]
        public void TryLoad_ResolvesRelativeToTheImportingFile_NotTheEntry()
        {
            // lib imports "deep" — that src must be resolved from lib's location, which is what the
            // Resources walk keys on. The resolver receives the importing path to make that possible.
            var seen = new List<(string src, string importing)>();
            var files = Files(
                ("main.ui.xml", "<Import src='lib.ui.xml'/><Screen name='S'><Frame id='f'/></Screen>"),
                ("lib.ui.xml", "<Import src='deep.ui.xml'/>"),
                ("deep.ui.xml", "<Style name='s' color='#fff'/>"));
            var doc = UIDocumentParser.Parse(files["main.ui.xml"], "main.ui.xml");

            ImportClosure.TryLoad(
                "main.ui.xml", doc,
                (src, importing) => { seen.Add((src, importing)); return src; },
                path => files[path],
                out _);

            CollectionAssert.AreEqual(
                new[] { ("lib.ui.xml", "main.ui.xml"), ("deep.ui.xml", "lib.ui.xml") }, seen);
        }

        // ── common libraries (2026-09-18 commons-settings spec §4.7) ───────────────────────────
        //
        // A common library is the <Import> every document implicitly has, so the closure has to
        // reach it the same way: resolved from the ENTRY file, then its own imports followed.

        private static Dictionary<string, UIDocument> LoadWithCommons(
            Dictionary<string, string> files, string entry, IReadOnlyList<ImportRef> commons,
            ImportClosure.Cache cache, out string unresolved,
            List<(string src, string importing)> seen = null, List<string> reads = null)
            => LoadWithCommons(files, entry, commons, cache, out _, out unresolved, seen, reads);

        private static Dictionary<string, UIDocument> LoadWithCommons(
            Dictionary<string, string> files, string entry, IReadOnlyList<ImportRef> commons,
            ImportClosure.Cache cache, out IReadOnlyList<ImportRef> applied, out string unresolved,
            List<(string src, string importing)> seen = null, List<string> reads = null)
        {
            var doc = UIDocumentParser.Parse(files[entry], entry);
            return ImportClosure.TryLoad(
                entry, doc,
                (src, importing) => { seen?.Add((src, importing)); return files.ContainsKey(src) ? src : null; },
                path => { reads?.Add(path); return files[path]; },
                commons, cache,
                out applied, out unresolved);
        }

        [Test]
        public void TryLoad_ACommonsRowThatIsTheEntryItself_IsDropped_TheOthersApply()
        {
            // Linting a directory reaches the theme library as an entry in its own right. Merging it
            // onto itself would make every one of its styles "conflict with commons pool".
            var files = Files(
                ("theme.ui", "<Style name='badge' color='#f00'/>"),
                ("widgets.ui", "<Template name='Card'><Frame id='card'/></Template>"));
            var commons = new[] { new ImportRef("theme.ui", null), new ImportRef("widgets.ui", "ui") };

            var closure = LoadWithCommons(files, "theme.ui", commons, null, out var applied, out var unresolved);

            Assert.IsNotNull(closure, unresolved);
            CollectionAssert.AreEquivalent(new[] { "widgets.ui" }, closure.Keys, "a library is not its own commons");
            Assert.AreEqual(1, applied.Count);
            Assert.AreEqual("widgets.ui", applied[0].Src);
            Assert.AreEqual("ui", applied[0].Namespace);
        }

        [Test]
        public void TryLoad_ResolvesEachCommonsRowFromTheEntry_AndFollowsItsOwnImports()
        {
            var seen = new List<(string src, string importing)>();
            var files = Files(
                ("main.ui.xml", "<Screen name='S'><Frame id='f'/></Screen>"),
                ("theme.ui", "<Import src='deep.ui.xml'/><Style name='badge' color='#f00'/>"),
                ("deep.ui.xml", "<Style name='s' color='#fff'/>"));

            var closure = LoadWithCommons(
                files, "main.ui.xml", new[] { new ImportRef("theme.ui", null) }, null, out var unresolved, seen);

            Assert.IsNotNull(closure, unresolved);
            CollectionAssert.AreEquivalent(new[] { "theme.ui", "deep.ui.xml" }, closure.Keys);
            CollectionAssert.AreEqual(
                new[] { ("theme.ui", "main.ui.xml"), ("deep.ui.xml", "theme.ui") }, seen,
                "a Resources-style short key is guessed from the entry's folder, like an <Import> written there");
        }

        [Test]
        public void TryLoad_EntryImportsComeFirst_ACommonsRowAlreadyInTheClosureIsNotFetchedAgain()
        {
            var seen = new List<(string src, string importing)>();
            var files = Files(
                ("main.ui.xml", "<Import src='theme.ui'/><Screen name='S'><Frame id='f'/></Screen>"),
                ("theme.ui", "<Style name='badge' color='#f00'/>"));

            var closure = LoadWithCommons(
                files, "main.ui.xml", new[] { new ImportRef("theme.ui", null) }, null, out var unresolved, seen);

            Assert.IsNotNull(closure, unresolved);
            Assert.AreEqual(1, seen.Count, "the explicit <Import> already brought it in");
        }

        [Test]
        public void TryLoad_UnresolvableCommonsRow_ReturnsNull_AndNamesIt()
        {
            var files = Files(("main.ui.xml", "<Screen name='S'><Frame id='f'/></Screen>"));

            var closure = LoadWithCommons(
                files, "main.ui.xml", new[] { new ImportRef("ghost.ui", null) }, null, out var unresolved);

            Assert.IsNull(closure, "a document that cannot see its commons must not be expanded against half of them");
            Assert.AreEqual("ghost.ui", unresolved);
        }

        [Test]
        public void TryLoad_CommonsClosure_IsParsedOncePerCache_AndReusedByTheNextEntry()
        {
            var reads = new List<string>();
            var files = Files(
                ("a.ui.xml", "<Screen name='A'><Frame id='f'/></Screen>"),
                ("b.ui.xml", "<Screen name='B'><Frame id='g'/></Screen>"),
                ("theme.ui", "<Import src='deep.ui.xml'/><Style name='badge' color='#f00'/>"),
                ("deep.ui.xml", "<Style name='s' color='#fff'/>"));
            var commons = new[] { new ImportRef("theme.ui", null) };
            var cache = new ImportClosure.Cache();

            var first = LoadWithCommons(files, "a.ui.xml", commons, cache, out _, reads: reads);
            var second = LoadWithCommons(files, "b.ui.xml", commons, cache, out _, reads: reads);

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            CollectionAssert.AreEquivalent(new[] { "theme.ui", "deep.ui.xml" }, reads,
                "forty entries re-parsing the same DefaultTheme would be forty times the work for nothing");
            Assert.AreSame(first["theme.ui"], second["theme.ui"]);
            Assert.AreSame(first["deep.ui.xml"], second["deep.ui.xml"], "the library's own imports are cached with it");
        }

        [Test]
        public void TryLoad_WithoutCommons_ReadsNothingExtra()
        {
            var seen = new List<(string src, string importing)>();
            var files = Files(("main.ui.xml", "<Screen name='S'><Frame id='f'/></Screen>"));

            var closure = LoadWithCommons(files, "main.ui.xml", null, null, out var unresolved, seen);

            Assert.IsNotNull(closure);
            CollectionAssert.IsEmpty(closure);
            CollectionAssert.IsEmpty(seen);
        }
    }
}
