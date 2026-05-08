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

        [Test]
        public void LoadCommonLibrary_makes_template_visible_to_screen() {
            var files = new Dictionary<string, string> {
                ["common/btns"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                      <Template name='PrimaryButton'>
                                        <Btn><Slot/></Btn>
                                      </Template>
                                    </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Screen name='M'>
                                 <PrimaryButton id='play'>开始</PrimaryButton>
                               </Screen>
                             </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;

            UI.LoadCommonLibrary("common/btns");
            UI.LoadDocumentFromSrc("main");
            var screen = UI.Open("M");
            Assert.IsNotNull(screen.Get<Btn>("play"));
        }

        [Test]
        public void Commons_with_screen_throws() {
            UI.SourceResolver = src =>
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='X'><Frame/></Screen>
                  </PromptUGUI>";
            Assert.Throws<PromptUGUI.Parser.ParseException>(() => UI.LoadCommonLibrary("any"));
        }

        [Test]
        public void Commons_conflict_throws_on_second_register() {
            var xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
                          <Template name='Foo'><Frame/></Template>
                        </PromptUGUI>";
            UI.SourceResolver = _ => xml;
            UI.LoadCommonLibrary("a");
            Assert.Throws<PromptUGUI.Template.TemplateException>(() => UI.LoadCommonLibrary("b"));
        }

        [Test]
        public void Commons_conflict_with_screen_local_throws() {
            var files = new Dictionary<string, string> {
                ["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                          </PromptUGUI>",
                ["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                            <Screen name='S'><Frame/></Screen>
                          </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;
            UI.LoadCommonLibrary("c");
            Assert.Throws<PromptUGUI.Template.TemplateException>(() => UI.LoadDocumentFromSrc("m"));
        }

        [Test]
        public void Commons_with_as_namespace_isolates() {
            var files = new Dictionary<string, string> {
                ["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                          </PromptUGUI>",
                ["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                            <Screen name='S'><X id='a'/></Screen>
                          </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;
            UI.LoadCommonLibrary("c", @as: "std");
            UI.LoadDocumentFromSrc("m");
            Assert.Pass();
        }

        [Test]
        public void Common_library_can_import_other_files() {
            var files = new Dictionary<string, string> {
                ["base"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Template name='B'><Frame/></Template>
                             </PromptUGUI>",
                ["ext"]  = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Import src='base'/>
                               <Template name='E'><Frame/></Template>
                             </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Screen name='M'><B id='b'/><E id='e'/></Screen>
                             </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;

            UI.LoadCommonLibrary("ext");      // ext 内有 <Import src='base'/>
            UI.LoadDocumentFromSrc("main");
            var s = UI.Open("M");
            Assert.IsNotNull(s.Get<Frame>("b"));
            Assert.IsNotNull(s.Get<Frame>("e"));
        }

        [Test]
        public void Two_commons_distinct_names_OK() {
            var files = new Dictionary<string, string> {
                ["a"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='A'><Frame/></Template>
                          </PromptUGUI>",
                ["b"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='B'><Frame/></Template>
                          </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;
            UI.LoadCommonLibrary("a");
            UI.LoadCommonLibrary("b");
            Assert.Pass();
        }

        [Test]
        public void UseResourcesResolver_AssetPathToSrc_strips_prefix() {
            UI.UseResourcesResolver("UI");
            var fn = UI.HotReload.AssetPathToSrc;
            Assert.IsNotNull(fn);
            Assert.AreEqual("MainMenu",
                fn("Assets/Resources/UI/MainMenu.ui.xml"));
            Assert.AreEqual("subdir/X",
                fn("Assets/Samples/PromptUGUI/0.0.0/Demo/Resources/UI/subdir/X.ui.xml"));
            Assert.IsNull(fn("Assets/Other/Foo.txt"));
        }
    }
}
