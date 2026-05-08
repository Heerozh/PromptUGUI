using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application {
    public class ImportSemanticsTests {
        [SetUp]
        public void Setup() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Imported_template_usable_in_screen_file() {
            var files = new Dictionary<string, string> {
                ["panels"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                 <Template name='Card'>
                                   <Frame><Slot/></Frame>
                                 </Template>
                               </PromptUGUI>",
                ["main"]   = @"<?xml version='1.0'?><PromptUGUI version='1'>
                                 <Import src='panels'/>
                                 <Screen name='Main'>
                                   <Card id='c'><Text>hi</Text></Card>
                                 </Screen>
                               </PromptUGUI>",
            };
            UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;
            UI.LoadDocumentFromSrc("main");
            var screen = UI.Open("Main");
            Assert.IsNotNull(screen.Get<Frame>("c"));
        }
    }
}
