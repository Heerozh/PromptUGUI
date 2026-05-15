using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.EditorOnly
{
    public class ModalHotReloadTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void InvalidateCacheForEditor_is_silent_for_unknown_src()
        {
            Assert.DoesNotThrow(() => UI.Modal.InvalidateCacheForEditor("not/cached"));
        }
    }
}
