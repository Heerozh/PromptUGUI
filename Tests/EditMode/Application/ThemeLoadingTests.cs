using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Application
{
    public class ThemeLoadingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Resolver(Dictionary<string, string> files)
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(files[src]);
        }

        [Test]
        public void LoadCommonLibrary_Registers_Themes_And_AutoSets_When_Single()
        {
            Resolver(new()
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                </PromptUGUI>"
            });
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "light" }, UI.Theme.Available);
            Assert.AreEqual("light", UI.Theme.Current);  // auto-set single
        }

        [Test]
        public void LoadCommonLibrary_Two_Themes_Does_Not_AutoSet()
        {
            Resolver(new()
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                    <Theme name='dark' base='light'><Color name='primary' value='#cc6600'/></Theme>
                </PromptUGUI>"
            });
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(2, UI.Theme.Available.Count);
            Assert.IsNull(UI.Theme.Current);
        }

        [Test]
        public void CrossDoc_Duplicate_Theme_Throws()
        {
            Resolver(new()
            {
                ["themes/a"] = "<?xml version='1.0'?><PromptUGUI version='1'><Theme name='light'/></PromptUGUI>",
                ["themes/b"] = "<?xml version='1.0'?><PromptUGUI version='1'><Theme name='light'/></PromptUGUI>",
            });
            UI.LoadCommonLibraryAsync("themes/a").GetAwaiter().GetResult();
            var ex = Assert.Throws<ParseException>(() =>
                UI.LoadCommonLibraryAsync("themes/b").GetAwaiter().GetResult());
            StringAssert.Contains("themes/a", ex.Message);
            StringAssert.Contains("themes/b", ex.Message);
        }

        [Test]
        public void Missing_Base_Throws_At_Load()
        {
            Resolver(new()
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='dark' base='ghost'/>
                </PromptUGUI>"
            });
            var ex = Assert.Throws<ParseException>(() =>
                UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult());
            StringAssert.Contains("ghost", ex.Message);
        }

        [Test]
        public void LoadDocument_Registers_Themes_From_Screen_Doc()
        {
            Resolver(new()
            {
                ["s/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                    <Screen name='S'/>
                </PromptUGUI>"
            });
            UI.LoadDocumentAsync("s/main").GetAwaiter().GetResult();
            CollectionAssert.Contains(UI.Theme.Available, "light");
            Assert.AreEqual("light", UI.Theme.Current);
        }

        [Test]
        public void LoadDocument_Registers_Themes_From_Import()
        {
            Resolver(new()
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                </PromptUGUI>",
                ["screen/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Import src='themes/main'/>
                    <Screen name='S'/>
                </PromptUGUI>"
            });
            UI.LoadDocumentAsync("screen/main").GetAwaiter().GetResult();
            CollectionAssert.Contains(UI.Theme.Available, "light");
        }

        [Test]
        public void PreSet_Theme_Then_Load_Fires_Changed_And_Resolves_To_Real_Color()
        {
            // The boot-time race scenario the API now guarantees: user fires
            // Theme.Set("dark") before LoadCommonLibraryAsync resolves. When the
            // load lands and "dark" registers, Theme.Changed must fire AGAIN so
            // any open Screens repaint with the now-resolvable token values.
            int firedCount = 0;
            string lastFired = null;
            UI.Theme.Changed += n => { firedCount++; lastFired = n; };
            UI.Theme.Set("dark");
            Assert.AreEqual(1, firedCount, "Set itself fires Changed once");
            Assert.AreEqual("dark", lastFired);
            // While pending, token Resolve soft-fails to white; literal still resolves.
            Assert.AreEqual(Color.white, UI.Theme.Resolve("primary"));

            Resolver(new()
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                    <Theme name='dark' base='light'><Color name='primary' value='#cc6600'/></Theme>
                </PromptUGUI>"
            });
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();

            Assert.AreEqual(2, firedCount,
                "Post-load registration must re-fire Changed so open Screens ReSolve");
            Assert.AreEqual("dark", lastFired);
            Assert.AreEqual(new Color32(0xcc, 0x66, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void ReLoad_Same_Src_With_New_Color_Values_Replaces_Old()
        {
            // Editor "edit XML → re-Play with Domain Reload off" scenario via the
            // production path: ThemeStore persists across the simulated re-Play,
            // so the second LoadCommonLibraryAsync re-registers the same
            // (name, src) with NEW color values. Register must replace, or the
            // author's edit is silently dropped.
            var files = new Dictionary<string, string>
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                </PromptUGUI>"
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files[src]);
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));

            // Simulate the "Domain Reload off" path: keep ThemeStore + Theme.Current
            // alive but feed the resolver new XML content (mirrors what an edited
            // .ui.xml would deliver on the next Resources.Load call).
            files["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Theme name='light'><Color name='primary' value='#00ff00'/></Theme>
            </PromptUGUI>";
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void PreSet_Theme_That_Never_Loads_Warns_After_LoadCommonLibrary()
        {
            // Typo case: user Set a name that no <Theme> matches. The load
            // completes, Resolve keeps soft-failing, and we log a warning so
            // the author isn't left staring at a white UI without a hint.
            UI.Theme.Set("drak");
            Resolver(new()
            {
                ["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                    <Theme name='dark' base='light'><Color name='primary' value='#cc6600'/></Theme>
                </PromptUGUI>"
            });
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Theme.Current is 'drak'.*not registered"));
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            // Token still soft-fails to white.
            Assert.AreEqual(Color.white, UI.Theme.Resolve("primary"));
        }
    }
}
