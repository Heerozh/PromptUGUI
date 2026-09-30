using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;
using PromptUGUI.IR;

namespace PromptUGUI.Tests.Editor
{
    public class StringExtractorTests
    {
        [Test]
        public void FindOrphanPoFiles_FlagsFilesWhosePartitionIsNotActive()
        {
            var localeDir = "Assets/Resources/PromptUGUI/i18n/en";
            var paths = new[]
            {
                $"{localeDir}/_code.po",
                $"{localeDir}/screens/MainMenu.po",
                $"{localeDir}/screens/DeletedScreen.po",
            };
            var active = new HashSet<string> { "_code", "screens/MainMenu" };
            var orphans = StringExtractor.FindOrphanPoFiles(paths, localeDir, active).ToList();
            CollectionAssert.AreEqual(
                new[] { $"{localeDir}/screens/DeletedScreen.po" },
                orphans);
        }

        [Test]
        public void FindOrphanPoFiles_NormalizesBackslashes()
        {
            var localeDir = "Assets/Resources/PromptUGUI/i18n/en";
            var paths = new[] { $@"{localeDir}\screens\Orphan.po" };
            var active = new HashSet<string> { "screens/Other" };
            var orphans = StringExtractor.FindOrphanPoFiles(paths, localeDir, active).ToList();
            Assert.AreEqual(1, orphans.Count);
        }

        [Test]
        public void FindOrphanPoFiles_AllActive_ReturnsEmpty()
        {
            var localeDir = "Assets/Resources/PromptUGUI/i18n/zh-Hans";
            var paths = new[]
            {
                $"{localeDir}/_code.po",
                $"{localeDir}/screens/MainMenu.po",
            };
            var active = new HashSet<string> { "_code", "screens/MainMenu" };
            CollectionAssert.IsEmpty(
                StringExtractor.FindOrphanPoFiles(paths, localeDir, active).ToList());
        }

        // ---- externalPoRoots (spec 2026-09-04 EPR) ----

        [Test]
        public void FindOrphanPoFiles_SkipsPathsUnderAnExternalRoot()
        {
            // ReportOrphanPoFiles scans <localeDir> recursively, so an external root
            // that happens to sit inside it would otherwise be reported every extract.
            var localeDir = "Assets/_Project/i18n/en";
            var paths = new[]
            {
                $"{localeDir}/_code.po",
                $"{localeDir}/_server/systems.po",
                $"{localeDir}/screens/DeletedScreen.po",
            };
            var active = new HashSet<string> { "_code" };
            var roots = new[] { $"{localeDir}/_server" };

            var orphans = StringExtractor
                .FindOrphanPoFiles(paths, localeDir, active, roots).ToList();

            CollectionAssert.AreEqual(
                new[] { $"{localeDir}/screens/DeletedScreen.po" },
                orphans,
                "External .po must not be reported as orphans, but genuine orphans still are.");
        }

        [Test]
        public void FindOrphanPoFiles_SkipsExternalRootWithBackslashPaths()
        {
            var localeDir = "Assets/_Project/i18n/en";
            var paths = new[] { $@"{localeDir}\_server\systems.po" };
            var active = new HashSet<string> { "_code" };
            var roots = new[] { $"{localeDir}/_server" };
            CollectionAssert.IsEmpty(
                StringExtractor.FindOrphanPoFiles(paths, localeDir, active, roots).ToList());
        }

        [Test]
        public void FindOrphanPoFiles_EmptyExternalRoots_ReportsAsBefore()
        {
            var localeDir = "Assets/_Project/i18n/en";
            var paths = new[] { $"{localeDir}/_server/systems.po" };
            var active = new HashSet<string> { "_code" };
            var orphans = StringExtractor
                .FindOrphanPoFiles(paths, localeDir, active, new string[0]).ToList();
            Assert.AreEqual(1, orphans.Count,
                "Default (no roots configured) behaviour must be unchanged.");
        }

        // ---- ScanXml: every .ui.xml expanded the way the runtime expands it ----

        private sealed class FakeFs
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly List<(string Path, string Message)> Warnings = new List<(string, string)>();

            public FakeFs Add(string path, string body)
            {
                Files[path] = "<PromptUGUI version='1'>" + body + "</PromptUGUI>";
                return this;
            }

            public List<ExtractedString> Scan(IReadOnlyList<ImportRef> commons = null) =>
                StringExtractor.ScanXml(
                    Files.Keys.OrderBy(p => p, System.StringComparer.Ordinal).ToList(),
                    p => Files[p],
                    (src, importing) => Files.ContainsKey(src) ? src : null,
                    commons,
                    (path, message) => Warnings.Add((path, message))).ToList();
        }

        private const string FlagTabLib =
            "<Style name='flag' color='#101820'/>" +
            "<Template name='FlagTab'><Param name='label'/><Param name='caption'/>" +
            "<Tab id='tab' class='flag'><Text>{{label}}</Text><Text>{{caption}}</Text></Tab>" +
            "</Template>";

        [Test]
        public void ScanXml_ScreenImportingStyledTemplate_ExtractsInvocationArgsIntoScreenPartition()
        {
            var fs = new FakeFs()
                .Add("Assets/UI/Templates/FlagTab.ui.xml", FlagTabLib)
                .Add("Assets/UI/Round.ui.xml",
                     "<Import src='Assets/UI/Templates/FlagTab.ui.xml'/>" +
                     "<Screen name='Round'><TabBar><FlagTab id='flagDesign' label='战舰设计' caption='DESIGN'/></TabBar></Screen>");

            var found = fs.Scan();

            CollectionAssert.IsEmpty(fs.Warnings);
            var design = found.Single(e => e.Msgid == "战舰设计");
            Assert.AreEqual("Round", design.LocalePartition);
            CollectionAssert.Contains(design.References, "Assets/UI/Round.ui.xml");
        }

        [Test]
        public void ScanXml_CommonLibraryStyles_FoldIntoEveryScreen()
        {
            // The theme library is itself one of the scanned files: as an entry it has no
            // <Screen>, so it is not expanded and must not warn about anything.
            var fs = new FakeFs()
                .Add("Assets/UI/Theme.ui.xml", "<Style name='flag' color='#101820'/>")
                .Add("Assets/UI/Main.ui.xml",
                     "<Template name='FlagTab'><Param name='label'/><Tab class='flag'><Text>{{label}}</Text></Tab></Template>" +
                     "<Screen name='Main'><TabBar><FlagTab label='研究'/></TabBar></Screen>");

            var found = fs.Scan(new[] { new ImportRef("Assets/UI/Theme.ui.xml", null) });

            CollectionAssert.IsEmpty(fs.Warnings);
            Assert.IsTrue(found.Any(e => e.Msgid == "研究" && e.LocalePartition == "Main"));
        }

        [Test]
        public void ScanXml_UnresolvableImport_WarnsWithPath_AndKeepsRawText()
        {
            var fs = new FakeFs()
                .Add("Assets/UI/Main.ui.xml",
                     "<Import src='ghost.ui'/><Screen name='Main'><Text>标题</Text></Screen>");

            var found = fs.Scan();

            var warning = fs.Warnings.Single();
            Assert.AreEqual("Assets/UI/Main.ui.xml", warning.Path);
            StringAssert.Contains("ghost.ui", warning.Message);
            Assert.IsTrue(found.Any(e => e.Msgid == "标题"));
        }

        [Test]
        public void ScanXml_ExpansionFailure_WarnsWithPathAndReason()
        {
            var fs = new FakeFs()
                .Add("Assets/UI/Main.ui.xml",
                     "<Screen name='Main'><Text>标题</Text><Frame class='nope'/></Screen>");

            var found = fs.Scan();

            var warning = fs.Warnings.Single();
            Assert.AreEqual("Assets/UI/Main.ui.xml", warning.Path);
            StringAssert.Contains("unknown style 'nope'", warning.Message);
            Assert.IsTrue(found.Any(e => e.Msgid == "标题"));
        }

        [Test]
        public void ScanXml_SlotCommentInTemplateFile_AndCallSiteComment_BothReachTheScreenEntry()
        {
            // Multi-line bodies: a comment only counts when it opens its own line. The template file is
            // parsed by ImportClosure, not by the entry's own parse — its comments must survive that too.
            var fs = new FakeFs()
                .Add("Assets/UI/Templates/FlagTab.ui.xml", string.Join("\n",
                    "",
                    "<Template name='FlagTab'>",
                    "  <Param name='label'/>",
                    "  <Param name='caption'/>",
                    "  <Tab id='tab'>",
                    "    <Text>{{label}}</Text>",
                    "    <!-- 装饰小字 -->",
                    "    <Text>{{caption}}</Text>",
                    "  </Tab>",
                    "</Template>",
                    ""))
                .Add("Assets/UI/Round.ui.xml", string.Join("\n",
                    "",
                    "<Import src='Assets/UI/Templates/FlagTab.ui.xml'/>",
                    "<Screen name='Round'>",
                    "  <TabBar>",
                    "    <!-- 调用点说明 -->",
                    "    <FlagTab id='flagDesign' label='战舰设计' caption='DESIGN'/>",
                    "  </TabBar>",
                    "</Screen>",
                    ""));

            var found = fs.Scan();

            CollectionAssert.IsEmpty(fs.Warnings);
            var design = found.Single(e => e.Msgid == "DESIGN");
            Assert.AreEqual("Round", design.LocalePartition);
            CollectionAssert.IsSupersetOf(design.ExtractedComments, new[] { "装饰小字", "调用点说明" });
        }

        [Test]
        public void ScanXml_UnparseableFile_WarnsAndSkipsIt()
        {
            var fs = new FakeFs()
                .Add("Assets/UI/Main.ui.xml", "<Screen name='Main'><Text>标题</Text></Screen>");
            fs.Files["Assets/UI/Broken.ui.xml"] = "<PromptUGUI version='1'><Screen name='B'>";

            var found = fs.Scan();

            Assert.AreEqual("Assets/UI/Broken.ui.xml", fs.Warnings.Single().Path);
            Assert.IsTrue(found.Any(e => e.Msgid == "标题"));
        }
    }
}
