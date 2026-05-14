using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Modals
{
    public class UIUnloadDocumentTests
    {
        private const string Xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
            <Screen name='T'><Frame id='a'/></Screen>
          </PromptUGUI>";

        [SetUp]    public void SetUp()    => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void UnloadDocument_removes_screen_def()
        {
            UI.LoadDocument("T", Xml);
            UI.UnloadDocument("T");
            // re-loading should not throw "already loaded"
            UI.LoadDocument("T", Xml);
            Assert.IsNotNull(UI.Open("T"));
        }

        [Test]
        public void UnloadDocument_unknown_name_is_silent()
        {
            Assert.DoesNotThrow(() => UI.UnloadDocument("DoesNotExist"));
        }
    }
}
