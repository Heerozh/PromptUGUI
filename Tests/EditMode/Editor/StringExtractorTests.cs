using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

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
    }
}
