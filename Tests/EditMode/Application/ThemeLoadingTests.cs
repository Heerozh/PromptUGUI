using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

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
    }
}
