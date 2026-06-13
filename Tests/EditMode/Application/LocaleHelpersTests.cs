using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application
{
    public class LocaleHelpersTests
    {
        [Test]
        public void MatchWithFallback_ExactMatch_ReturnsConfiguredEntry()
        {
            Assert.AreEqual("zh-Hans",
                LocaleHelpers.MatchWithFallback("zh-Hans", new[] { "en", "zh-Hans" }));
        }

        [Test]
        public void MatchWithFallback_TruncatesOneLevel_WhenOnlyParentConfigured()
        {
            // zh-Hans not configured → strip to zh → match.
            Assert.AreEqual("zh",
                LocaleHelpers.MatchWithFallback("zh-Hans", new[] { "en", "zh" }));
        }

        [Test]
        public void MatchWithFallback_TruncatesMultiLevel()
        {
            Assert.AreEqual("zh",
                LocaleHelpers.MatchWithFallback("zh-Hant-TW", new[] { "en", "zh" }));
        }

        [Test]
        public void MatchWithFallback_PrefersExactOverParent()
        {
            // Both present: the full-length tag wins, parent is never reached.
            Assert.AreEqual("zh-Hans",
                LocaleHelpers.MatchWithFallback("zh-Hans", new[] { "zh", "zh-Hans" }));
        }

        [Test]
        public void MatchWithFallback_DoesNotMatchSiblingSubtag()
        {
            // zh-Hans must not be answered by an unrelated zh-Hant; only the
            // truncated parent zh is a legal fallback (absent here) → null.
            Assert.IsNull(
                LocaleHelpers.MatchWithFallback("zh-Hans", new[] { "en", "zh-Hant" }));
        }

        [Test]
        public void MatchWithFallback_DoesNotExpandParentToSpecificConfigured()
        {
            // Reverse direction is intentionally unsupported: a generic request
            // "zh" does NOT match a more specific configured "zh-Hans".
            Assert.IsNull(
                LocaleHelpers.MatchWithFallback("zh", new[] { "zh-Hans" }));
        }

        [Test]
        public void MatchWithFallback_NoMatch_ReturnsNull()
        {
            Assert.IsNull(
                LocaleHelpers.MatchWithFallback("ja", new[] { "en", "zh" }));
        }

        [Test]
        public void MatchWithFallback_NullOrEmptyInputs_ReturnNull()
        {
            Assert.IsNull(LocaleHelpers.MatchWithFallback(null, new[] { "en" }));
            Assert.IsNull(LocaleHelpers.MatchWithFallback("", new[] { "en" }));
            Assert.IsNull(LocaleHelpers.MatchWithFallback("zh-Hans", null));
        }
    }
}
