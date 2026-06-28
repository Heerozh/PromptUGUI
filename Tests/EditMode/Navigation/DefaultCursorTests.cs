using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class DefaultCursorTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ScreenWithoutFocusCursor_UsesBuiltInDefault()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='a'>A</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            // 屏内没写 <FocusCursor>，但全局默认让 overlay 出现
            Assert.IsNotNull(screen.RootGameObject.transform.Find("__FocusCursor"));
        }
    }
}
