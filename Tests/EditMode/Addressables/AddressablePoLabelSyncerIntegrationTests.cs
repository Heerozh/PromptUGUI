using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace PromptUGUI.Tests.Addressables
{
    /// <summary>
    /// Verifies <see cref="AddressablePoLabelSyncer.MakeLocalePoFilesAddressable"/>
    /// fully wires .po assets for Addressables shipping: adds the asset to the default
    /// AA group when it isn't yet Addressable, applies <c>Locale:&lt;locale&gt;</c>,
    /// scrubs stale <c>Locale:*</c> labels, and leaves non-Locale labels alone.
    ///
    /// Uses .txt fixture files rather than real .po — Unity 6's bundled localization
    /// package claims <c>.po</c> as a native importer and logs a benign-but-noisy
    /// "File couldn't be read" during the initial discover-import. The syncer logic
    /// is extension-agnostic (only inspects path segments and AA entries), so .txt
    /// fixtures exercise the same code paths without that noise. The menu wrapper
    /// that filters for <c>.po</c> in <c>AddressablePoMenu</c> is trivial enough to
    /// verify by inspection.
    /// </summary>
    public class AddressablePoLabelSyncerIntegrationTests
    {
        private const string FixturesRoot = "Assets/PromptUGUI_TestFixtures_PoLabel";
        private static readonly string[] Locales = { "zh-Hans", "zh-Hant", "en" };
        private AddressableAssetGroup _testGroup;

        [SetUp]
        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(FixturesRoot))
                AssetDatabase.CreateFolder("Assets", "PromptUGUI_TestFixtures_PoLabel");
            var settings = AddressableAssetSettingsDefaultObject.Settings
                          ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            _testGroup = settings.CreateGroup(
                "PromptUGUI_PoLabel_Test", false, false, false, null,
                typeof(BundledAssetGroupSchema));
        }

        [TearDown]
        public void TearDown()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && _testGroup != null)
                settings.RemoveGroup(_testGroup);
            if (AssetDatabase.IsValidFolder(FixturesRoot))
                AssetDatabase.DeleteAsset(FixturesRoot);
        }

        [Test]
        public void Make_sets_Locale_label_on_AA_entry_in_locale_folder()
        {
            var poPath = WritePo("zh-Hans", "main.txt");
            var entry = AddToTestGroup(poPath);
            Assert.IsFalse(entry.labels.Contains("Locale:zh-Hans"),
                "Pre-condition: fresh entry has no locale label yet.");

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);

            Assert.IsTrue(entry.labels.Contains("Locale:zh-Hans"),
                "Make should add the Locale:zh-Hans label to the entry.");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.IsTrue(settings.GetLabels().Contains("Locale:zh-Hans"),
                "Make should register the label string on the settings asset so " +
                "it shows up in the AA labels dropdown.");
        }

        [Test]
        public void Make_scrubs_stale_Locale_prefixed_labels_after_move()
        {
            var poPath = WritePo("zh-Hans", "main.txt");
            var entry = AddToTestGroup(poPath);
            // Simulate a prior sync that labelled it differently (e.g., file was
            // moved between locale folders).
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            settings.AddLabel("Locale:en");
            entry.SetLabel("Locale:en", true, force: true);
            Assert.IsTrue(entry.labels.Contains("Locale:en"));

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);

            Assert.IsFalse(entry.labels.Contains("Locale:en"),
                "Stale Locale:en should be removed since the file is now in zh-Hans/.");
            Assert.IsTrue(entry.labels.Contains("Locale:zh-Hans"),
                "Correct Locale:zh-Hans should be added.");
        }

        [Test]
        public void Make_preserves_non_Locale_labels()
        {
            var poPath = WritePo("zh-Hans", "main.txt");
            var entry = AddToTestGroup(poPath);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            settings.AddLabel("UI");
            entry.SetLabel("UI", true, force: true);

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);

            Assert.IsTrue(entry.labels.Contains("UI"),
                "Non-Locale labels (author's own grouping) must be untouched.");
            Assert.IsTrue(entry.labels.Contains("Locale:zh-Hans"));
        }

        [Test]
        public void Make_skips_paths_outside_known_locale_folders()
        {
            var poPath = WritePo("misc", "random.txt");
            var entry = AddToTestGroup(poPath);

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);

            Assert.IsFalse(entry.labels.Any(l => l.StartsWith("Locale:")),
                "Path has no locale segment; no Locale label should be added.");
        }

        [Test]
        public void Make_adds_to_default_group_when_not_yet_addressable()
        {
            var poPath = WritePo("zh-Hans", "standalone.txt");
            // Intentionally do NOT pre-add to any AA group — this is what the menu
            // operation does for the user.
            var guid = AssetDatabase.AssetPathToGUID(poPath);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            Assert.IsNull(settings.FindAssetEntry(guid),
                "Pre-condition: asset is not in any AA group.");

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);

            var entry = settings.FindAssetEntry(guid);
            Assert.IsNotNull(entry,
                "Make must add the asset to the AA default group.");
            Assert.AreSame(settings.DefaultGroup, entry.parentGroup,
                "New entry should be placed in the AA default group.");
            Assert.IsTrue(entry.labels.Contains("Locale:zh-Hans"));
        }

        [Test]
        public void Make_is_idempotent_across_repeated_calls()
        {
            var poPath = WritePo("zh-Hans", "main.txt");
            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);
            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(
                new[] { poPath }, Locales);

            var guid = AssetDatabase.AssetPathToGUID(poPath);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var entry = settings.FindAssetEntry(guid);
            Assert.IsNotNull(entry);
            var localeCount = entry.labels.Count(l => l.StartsWith("Locale:"));
            Assert.AreEqual(1, localeCount,
                "Repeated calls must converge to a single Locale: label.");
        }

        private static string WritePo(string subfolder, string fileName)
        {
            // Use AssetDatabase.CreateFolder so Unity discovers the subdir without a
            // bulk Refresh — bulk Refresh kicks the bundled localization importer
            // which logs spurious "File couldn't be read" errors that the test
            // framework treats as unhandled.
            var folder = $"{FixturesRoot}/{subfolder}";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(FixturesRoot, subfolder);

            var assetPath = $"{folder}/{fileName}";
            File.WriteAllText(
                Path.Combine(UnityEngine.Application.dataPath, "..", assetPath), "fixture\n");
            AssetDatabase.ImportAsset(assetPath);
            return assetPath;
        }

        private AddressableAssetEntry AddToTestGroup(string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings.CreateOrMoveEntry(guid, _testGroup);
        }
    }
}
