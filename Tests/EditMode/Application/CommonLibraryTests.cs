using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application {
    public class CommonLibraryTests {
        [SetUp]   public void Setup()    => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Re-enabled in T8 once UI.LoadDocumentFromSrc exists.
        // [Test]
        // public void LoadDocumentFromSrc_without_resolver_throws() {
        //     UI.SourceResolver = null;
        //     Assert.Throws<InvalidOperationException>(() =>
        //         UI.LoadDocumentFromSrc("X"));
        // }
    }
}
