using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application {
    public class CommonLibraryTests {
        [SetUp]
        public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void LoadDocumentFromSrc_without_resolver_throws() {
            UI.SourceResolver = null;
            Assert.Throws<InvalidOperationException>(() =>
                UI.LoadDocumentFromSrc("X"));
        }

        [Test]
        public void LoadDocumentFromSrc_returns_screen_names() {
            UI.SourceResolver = src => src == "main"
                ? @"<?xml version='1.0'?><PromptUGUI version='1'>
                      <Screen name='S1'><Frame/></Screen>
                      <Screen name='S2'><Frame/></Screen>
                   </PromptUGUI>"
                : null;
            var names = UI.LoadDocumentFromSrc("main");
            CollectionAssert.AreEquivalent(new[] { "S1", "S2" }, names);
        }

        [Test]
        public void LoadDocumentFromSrc_imports_resolved() {
            var files = new Dictionary<string, string> {
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Import src='shared'/>
                              <Screen name='S'><Frame/></Screen>
                            </PromptUGUI>",
                ["shared"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                <Template name='T'><Frame/></Template>
                              </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;
            var names = UI.LoadDocumentFromSrc("main");
            // Verify screen was loaded by checking it can be requested again and throws
            CollectionAssert.Contains(names, "S");
            Assert.Throws<InvalidOperationException>(() =>
                UI.LoadDocumentFromSrc("main"));
        }
    }
}
