using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.Application
{
    public class LocaleSetAsyncTests
    {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void Set_with_completed_PoResolver_loads_translations_synchronously()
        {
            // Sync-completed Awaitable: integral regression contract that the new
            // async PoResolver shape keeps sync-completion semantics for callers
            // who don't actually need to defer (Resources path, in-memory tests).
            UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(new[]
            {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.Set("en");
            Assert.AreEqual("en", UI.Locale.Current);
            Assert.AreEqual("Hello", UI.Tr("hi"),
                "Sync-completed PoResolver path must populate TranslationStore " +
                "before Set returns (preserves pre-async-shape behavior).");
        }
    }
}
