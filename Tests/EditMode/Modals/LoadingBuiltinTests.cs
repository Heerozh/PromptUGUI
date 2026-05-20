using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class LoadingBuiltinTests : ModalTestFixture
    {
        [Test]
        public void Default_xml_src_loads_builtin_template()
        {
            Loading.XmlSrc = "PromptUGUI/Modals/Loading.ui";   // 内置,走 Resources
            var handle = Loading.Open("from real template");

            Assert.IsNotNull(handle);
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);

            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            var text = screen.Get<PromptUGUI.Controls.Text>("text");
            Assert.AreEqual("from real template", text.TmpComponent.text);

            handle.Close();
        }
    }
}
