using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialOverlaySmokeTests
    {
        private const string Src = "PromptUGUI/Tutorial/TutorialOverlay.ui";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Overlay_Loads_AndKeyIdsResolve()
        {
            ModalDocCache.EnsureLoaded(Src).GetAwaiter().GetResult();
            var (screen, key) = UI.OpenModalScreen(Src);
            try
            {
                Assert.IsNotNull(screen.Get<Frame>("mask"), "mask");
                Assert.IsNotNull(screen.Get<Frame>("bubbleRoot"), "bubbleRoot");
                Assert.IsNotNull(screen.Get<Image>("bubble"), "bubble");
                Assert.IsNotNull(screen.Get<Text>("bubbleText"), "bubbleText");
                Assert.IsNotNull(screen.Get<Image>("finger"), "finger");
            }
            finally { UI.CloseModalScreen(key); }
        }
    }
}
