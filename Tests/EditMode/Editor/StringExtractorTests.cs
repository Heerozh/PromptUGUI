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
    }
}
