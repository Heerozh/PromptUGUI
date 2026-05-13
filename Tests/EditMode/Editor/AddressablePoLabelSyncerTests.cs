using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// Pure-logic tests for <see cref="AddressablePoLabelSyncer"/>. No Addressables
    /// API touched here — the postprocessor itself is gated by PROMPTUGUI_HAS_ADDRESSABLES
    /// and exercised by integration tests in PromptUGUI.Tests.EditMode.Addressables.
    /// </summary>
    public class AddressablePoLabelSyncerTests
    {
        private static readonly string[] KnownLocales = { "zh-Hans", "zh-Hant", "en", "ja" };

        [Test]
        public void ComputeDesiredLocale_returns_locale_from_parent_folder()
        {
            var locale = AddressablePoLabelSyncer.ComputeDesiredLocale(
                "Assets/Localization/zh-Hans/main.po", KnownLocales);
            Assert.AreEqual("zh-Hans", locale);
        }

        [Test]
        public void ComputeDesiredLocale_returns_locale_from_grandparent_folder()
        {
            var locale = AddressablePoLabelSyncer.ComputeDesiredLocale(
                "Assets/Resources/PromptUGUI/i18n/en/screens/MainMenu.po", KnownLocales);
            Assert.AreEqual("en", locale);
        }

        [Test]
        public void ComputeDesiredLocale_returns_null_when_no_segment_matches()
        {
            var locale = AddressablePoLabelSyncer.ComputeDesiredLocale(
                "Assets/Misc/random/foo.po", KnownLocales);
            Assert.IsNull(locale);
        }

        [Test]
        public void ComputeDesiredLocale_leaf_wins_when_multiple_segments_match()
        {
            var locale = AddressablePoLabelSyncer.ComputeDesiredLocale(
                "Assets/Localization/zh-Hans/sub/en/leaf.po", KnownLocales);
            Assert.AreEqual("en", locale,
                "When more than one path segment is a known locale, the segment " +
                "closest to the file is more specific and should win.");
        }

        [Test]
        public void ComputeDesiredLocale_handles_zh_Hans_with_dashes_in_path()
        {
            var locale = AddressablePoLabelSyncer.ComputeDesiredLocale(
                "Assets/Localization/zh-Hant/foo.po", KnownLocales);
            Assert.AreEqual("zh-Hant", locale);
        }

        [Test]
        public void ComputeDesiredLocale_returns_null_for_empty_path()
        {
            Assert.IsNull(AddressablePoLabelSyncer.ComputeDesiredLocale("", KnownLocales));
            Assert.IsNull(AddressablePoLabelSyncer.ComputeDesiredLocale(null, KnownLocales));
        }

        [Test]
        public void ComputeDesiredLocale_returns_null_when_known_locales_empty()
        {
            var locale = AddressablePoLabelSyncer.ComputeDesiredLocale(
                "Assets/Localization/zh-Hans/main.po", System.Array.Empty<string>());
            Assert.IsNull(locale);
        }

        [Test]
        public void IsStaleLocaleLabel_returns_true_for_other_locale_prefix()
        {
            Assert.IsTrue(AddressablePoLabelSyncer.IsStaleLocaleLabel("Locale:en", "zh-Hans"));
            Assert.IsTrue(AddressablePoLabelSyncer.IsStaleLocaleLabel("Locale:ja", "zh-Hans"));
        }

        [Test]
        public void IsStaleLocaleLabel_returns_false_for_current_locale_prefix()
        {
            Assert.IsFalse(AddressablePoLabelSyncer.IsStaleLocaleLabel("Locale:zh-Hans", "zh-Hans"));
        }

        [Test]
        public void IsStaleLocaleLabel_returns_false_for_non_locale_label()
        {
            Assert.IsFalse(AddressablePoLabelSyncer.IsStaleLocaleLabel("UI", "zh-Hans"));
            Assert.IsFalse(AddressablePoLabelSyncer.IsStaleLocaleLabel("Stage:1-1", "zh-Hans"));
            Assert.IsFalse(AddressablePoLabelSyncer.IsStaleLocaleLabel("", "zh-Hans"));
        }

        [Test]
        public void IsStaleLocaleLabel_treats_malformed_Locale_colon_as_stale()
        {
            // "Locale:" (empty suffix) cannot match any current locale; safe to remove.
            Assert.IsTrue(AddressablePoLabelSyncer.IsStaleLocaleLabel("Locale:", "zh-Hans"));
        }

        [Test]
        public void LabelPrefix_constant_matches_runtime_resolver_helper()
        {
            // The Editor-side const must stay in lockstep with the Runtime resolver's
            // BuildLocaleLabel format; tests for the runtime side live in
            // LocaleAddressableResolverTests.BuildLocaleLabel_prefixes_with_Locale_colon.
            Assert.AreEqual("Locale:", AddressablePoLabelSyncer.LabelPrefix);
        }

        [Test]
        public void FindOrphanPoPaths_returns_paths_with_no_matching_locale_segment()
        {
            var orphans = AddressablePoLabelSyncer.FindOrphanPoPaths(
                new[]
                {
                    "Assets/Localization/zh-Hans/main.po",
                    "Assets/MyI18n/chinese/foo.po",
                    "Assets/Misc/random/bar.po",
                    "Assets/Resources/PromptUGUI/i18n/en/screens/MainMenu.po",
                },
                KnownLocales);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Assets/MyI18n/chinese/foo.po",
                    "Assets/Misc/random/bar.po",
                },
                orphans);
        }

        [Test]
        public void FindOrphanPoPaths_returns_empty_when_all_paths_match()
        {
            var orphans = AddressablePoLabelSyncer.FindOrphanPoPaths(
                new[]
                {
                    "Assets/Localization/zh-Hans/main.po",
                    "Assets/Localization/en/main.po",
                },
                KnownLocales);

            Assert.IsEmpty(orphans);
        }

        [Test]
        public void FindOrphanPoPaths_returns_empty_for_empty_input()
        {
            var orphans = AddressablePoLabelSyncer.FindOrphanPoPaths(
                System.Array.Empty<string>(), KnownLocales);
            Assert.IsEmpty(orphans);
        }

        [Test]
        public void FindOrphanPoPaths_treats_all_as_orphans_when_known_locales_empty()
        {
            var orphans = AddressablePoLabelSyncer.FindOrphanPoPaths(
                new[] { "Assets/Localization/zh-Hans/main.po" },
                System.Array.Empty<string>());
            CollectionAssert.AreEquivalent(
                new[] { "Assets/Localization/zh-Hans/main.po" }, orphans);
        }

        [Test]
        public void FindLocaleFolder_returns_folder_path_up_to_locale()
        {
            var folder = AddressablePoLabelSyncer.FindLocaleFolder(
                "Assets/Localization/zh-Hans/main.po", "zh-Hans");
            Assert.AreEqual("Assets/Localization/zh-Hans", folder);
        }

        [Test]
        public void FindLocaleFolder_returns_grandparent_folder_when_nested_below_locale()
        {
            var folder = AddressablePoLabelSyncer.FindLocaleFolder(
                "Assets/Localization/en/screens/MainMenu.po", "en");
            Assert.AreEqual("Assets/Localization/en", folder);
        }

        [Test]
        public void FindLocaleFolder_returns_null_when_no_segment_matches()
        {
            var folder = AddressablePoLabelSyncer.FindLocaleFolder(
                "Assets/Misc/random/foo.po", "zh-Hans");
            Assert.IsNull(folder);
        }

        [Test]
        public void FindLocaleFolder_picks_closest_when_multiple_segments_match()
        {
            var folder = AddressablePoLabelSyncer.FindLocaleFolder(
                "Assets/zh-Hans/sub/zh-Hans/leaf.po", "zh-Hans");
            Assert.AreEqual("Assets/zh-Hans/sub/zh-Hans", folder);
        }

        [Test]
        public void FindLocaleFolder_normalizes_backslashes()
        {
            var folder = AddressablePoLabelSyncer.FindLocaleFolder(
                "Assets\\Localization\\zh-Hans\\main.po", "zh-Hans");
            Assert.AreEqual("Assets/Localization/zh-Hans", folder);
        }

        [Test]
        public void FindLocaleFolder_returns_null_for_empty_path()
        {
            Assert.IsNull(AddressablePoLabelSyncer.FindLocaleFolder("", "zh-Hans"));
            Assert.IsNull(AddressablePoLabelSyncer.FindLocaleFolder(null, "zh-Hans"));
        }

        [Test]
        public void ResolveOutputDirForLocale_falls_back_when_no_labelled_paths()
        {
            var dir = AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                "zh-Hans",
                System.Array.Empty<string>(),
                "Assets/Resources/PromptUGUI/i18n",
                out var detected);
            Assert.AreEqual("Assets/Resources/PromptUGUI/i18n/zh-Hans", dir);
            Assert.IsEmpty(detected);
        }

        [Test]
        public void ResolveOutputDirForLocale_returns_locale_folder_when_single_labelled_path()
        {
            var dir = AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                "zh-Hans",
                new[] { "Assets/MyI18n/zh-Hans/main.po" },
                "Assets/Resources/PromptUGUI/i18n",
                out var detected);
            Assert.AreEqual("Assets/MyI18n/zh-Hans", dir);
            CollectionAssert.AreEqual(new[] { "Assets/MyI18n/zh-Hans" }, detected);
        }

        [Test]
        public void ResolveOutputDirForLocale_collapses_multiple_paths_in_same_folder()
        {
            var dir = AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                "zh-Hans",
                new[]
                {
                    "Assets/MyI18n/zh-Hans/screens.po",
                    "Assets/MyI18n/zh-Hans/dialogs.po",
                },
                "Assets/Resources/PromptUGUI/i18n",
                out var detected);
            Assert.AreEqual("Assets/MyI18n/zh-Hans", dir);
            Assert.AreEqual(1, detected.Count);
        }

        [Test]
        public void ResolveOutputDirForLocale_returns_first_alphabetically_when_ambiguous()
        {
            var dir = AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                "zh-Hans",
                new[]
                {
                    "Assets/OtherI18n/zh-Hans/bar.po",
                    "Assets/MyI18n/zh-Hans/foo.po",
                },
                "Assets/Resources/PromptUGUI/i18n",
                out var detected);
            Assert.AreEqual("Assets/MyI18n/zh-Hans", dir,
                "Ambiguous case must resolve deterministically — Ordinal-sorted first wins.");
            CollectionAssert.AreEquivalent(
                new[] { "Assets/MyI18n/zh-Hans", "Assets/OtherI18n/zh-Hans" }, detected);
        }

        [Test]
        public void ResolveOutputDirForLocale_ignores_paths_without_matching_locale_segment()
        {
            var dir = AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                "zh-Hans",
                new[] { "Assets/MyI18n/chinese/foo.po" },
                "Assets/Resources/PromptUGUI/i18n",
                out var detected);
            Assert.AreEqual("Assets/Resources/PromptUGUI/i18n/zh-Hans", dir);
            Assert.IsEmpty(detected);
        }
    }
}
