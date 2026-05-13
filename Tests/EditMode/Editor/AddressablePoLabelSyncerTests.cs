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
    }
}
