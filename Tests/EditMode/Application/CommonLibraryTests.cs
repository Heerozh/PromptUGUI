using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application
{
    public class CommonLibraryTests
    {
        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void LoadDocumentAsync_without_resolver_throws()
        {
            UI.SourceResolver = null;
            Assert.Throws<InvalidOperationException>(() =>
                UI.LoadDocumentAsync("X").GetAwaiter().GetResult());
        }

        [Test]
        public void LoadDocumentAsync_returns_screen_names()
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "main"
                ? @"<?xml version='1.0'?><PromptUGUI version='1'>
                      <Screen name='S1'><Frame/></Screen>
                      <Screen name='S2'><Frame/></Screen>
                   </PromptUGUI>"
                : null);
            var names = UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "S1", "S2" }, names);
        }

        [Test]
        public void LoadDocumentAsync_imports_resolved()
        {
            var files = new Dictionary<string, string>
            {
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Import src='shared'/>
                              <Screen name='S'><Frame/></Screen>
                            </PromptUGUI>",
                ["shared"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                <Template name='T'><Frame/></Template>
                              </PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            var names = UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            // Verify screen was loaded by checking it can be requested again and throws
            CollectionAssert.Contains(names, "S");
            Assert.Throws<InvalidOperationException>(() =>
                UI.LoadDocumentAsync("main").GetAwaiter().GetResult());
        }

        [Test]
        public void LoadCommonLibrary_makes_template_visible_to_screen()
        {
            var files = new Dictionary<string, string>
            {
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
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);

            UI.LoadCommonLibraryAsync("common/btns").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            var screen = UI.Open("M");
            Assert.IsNotNull(screen.Get<Btn>("play"));
        }

        [Test]
        public void Commons_with_screen_throws()
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='X'><Frame/></Screen>
                  </PromptUGUI>");
            Assert.Throws<PromptUGUI.Parser.ParseException>(() => UI.LoadCommonLibraryAsync("any").GetAwaiter().GetResult());
        }

        [Test]
        public void Commons_conflict_throws_on_second_register()
        {
            var xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
                          <Template name='Foo'><Frame/></Template>
                        </PromptUGUI>";
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadCommonLibraryAsync("a").GetAwaiter().GetResult();
            Assert.Throws<PromptUGUI.Template.TemplateException>(() => UI.LoadCommonLibraryAsync("b").GetAwaiter().GetResult());
        }

        [Test]
        public void Commons_conflict_with_screen_local_throws()
        {
            var files = new Dictionary<string, string>
            {
                ["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                          </PromptUGUI>",
                ["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                            <Screen name='S'><Frame/></Screen>
                          </PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();
            Assert.Throws<PromptUGUI.Template.TemplateException>(() => UI.LoadDocumentAsync("m").GetAwaiter().GetResult());
        }

        [Test]
        public void Commons_with_as_namespace_isolates()
        {
            var files = new Dictionary<string, string>
            {
                ["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                          </PromptUGUI>",
                ["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='X'><Frame/></Template>
                            <Screen name='S'><X id='a'/></Screen>
                          </PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.LoadCommonLibraryAsync("c", @as: "std").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.Pass();
        }

        [Test]
        public void Common_library_can_import_other_files()
        {
            var files = new Dictionary<string, string>
            {
                ["base"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Template name='B'><Frame/></Template>
                             </PromptUGUI>",
                ["ext"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Import src='base'/>
                               <Template name='E'><Frame/></Template>
                             </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                               <Screen name='M'><B id='b'/><E id='e'/></Screen>
                             </PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);

            UI.LoadCommonLibraryAsync("ext").GetAwaiter().GetResult();      // ext 内有 <Import src='base'/>
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            var s = UI.Open("M");
            Assert.IsNotNull(s.Get<Frame>("b"));
            Assert.IsNotNull(s.Get<Frame>("e"));
        }

        [Test]
        public void Two_commons_distinct_names_OK()
        {
            var files = new Dictionary<string, string>
            {
                ["a"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='A'><Frame/></Template>
                          </PromptUGUI>",
                ["b"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                            <Template name='B'><Frame/></Template>
                          </PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.LoadCommonLibraryAsync("a").GetAwaiter().GetResult();
            UI.LoadCommonLibraryAsync("b").GetAwaiter().GetResult();
            Assert.Pass();
        }

        [Test]
        public void UseResourcesResolver_AssetPathToSrc_strips_prefix()
        {
            UI.UseResourcesResolver("UI");
            var fn = UI.HotReload.AssetPathToSrc;
            Assert.IsNotNull(fn);
            Assert.AreEqual("MainMenu",
                fn("Assets/Resources/UI/MainMenu.ui.xml"));
            Assert.AreEqual("subdir/X",
                fn("Assets/Samples/PromptUGUI/0.0.0/Demo/Resources/UI/subdir/X.ui.xml"));
            Assert.IsNull(fn("Assets/Other/Foo.txt"));
        }

        [Test]
        public void UnloadAllCommonLibraries_clears_commons_only()
        {
            UI.SourceResolver = _ => AwaitableHelpers.Completed(@"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='T'><Frame/></Template>
              </PromptUGUI>");
            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();
            UI.UnloadAllCommonLibraries();

            // Re-loading the same commons should now succeed (no conflict)
            Assert.DoesNotThrow(() => UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult());
        }

        [Test]
        public void UnloadAll_clears_everything_but_preserves_resolver()
        {
            var savedResolver = UI.SourceResolver = _ => AwaitableHelpers.Completed(@"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='X'><Frame/></Screen>
              </PromptUGUI>");
            UI.LoadDocumentAsync("x").GetAwaiter().GetResult();
            UI.UnloadAll();

            // Resolver still set; loading the same Screen succeeds again
            Assert.AreSame(savedResolver, UI.SourceResolver);
            Assert.DoesNotThrow(() => UI.LoadDocumentAsync("x").GetAwaiter().GetResult());
        }

        [Test]
        public void OnEnteringPlayMode_clears_loaded_docs_so_replay_does_not_throw()
        {
            var savedResolver = UI.SourceResolver = _ => AwaitableHelpers.Completed(@"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='Main'><Frame/></Screen>
              </PromptUGUI>");
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            UI.OnEnteringPlayModeForTests();

            // Resolver preserved; same src can be loaded again without "already loaded"
            Assert.AreSame(savedResolver, UI.SourceResolver);
            Assert.DoesNotThrow(() => UI.LoadDocumentAsync("main").GetAwaiter().GetResult());
        }
    }
}
