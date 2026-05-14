using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalCancelTests : ModalTestFixture
    {
        private sealed class StuckRequest : ModalRequest<int>
        {
            public override string XmlSrc => "test/Box1";
            public override void Bind(IScreen screen, Action<int> close) { /* never close */ }
        }

        [Test]
        public void CloseAll_cancels_active_modal()
        {
            var task = UI.Modal.OpenAsync(new StuckRequest());
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            UI.Modal.CloseAll();
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void CloseAll_cancels_queued_modals()
        {
            var t1 = UI.Modal.OpenAsync(new StuckRequest());
            var t2 = UI.Modal.OpenAsync(new StuckRequest());

            UI.Modal.CloseAll();
            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => t2.GetAwaiter().GetResult());
        }

        [Test]
        public void UnloadAll_cancels_pending_modals()
        {
            var t1 = UI.Modal.OpenAsync(new StuckRequest());
            var t2 = UI.Modal.OpenAsync(new StuckRequest());

            UI.UnloadAll();

            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => t2.GetAwaiter().GetResult());
        }

        [Test]
        public void ResetForTests_cancels_pending_modals()
        {
            var t1 = UI.Modal.OpenAsync(new StuckRequest());
            UI.ResetForTests();

            Assert.Throws<OperationCanceledException>(() => t1.GetAwaiter().GetResult());
        }
    }
}
