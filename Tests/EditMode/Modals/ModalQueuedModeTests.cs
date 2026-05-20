using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalQueuedModeTests : ModalTestFixture
    {
        private sealed class FakeRequest : ModalRequest<int>
        {
            public Action<IScreen, Action<int>> OnBind;
            public override string XmlSrc => "test/Box1";
            public override void Bind(IScreen screen, Action<int> close) => OnBind?.Invoke(screen, close);
        }

        [Test]
        public void Queued_waits_for_stack_to_empty()
        {
            Action<int> close1 = null, close2 = null;
            var t1 = UI.Modal.OpenAsync(
                new FakeRequest { OnBind = (_, c) => close1 = c }, ModalMode.Popup);
            var t2 = UI.Modal.OpenAsync(
                new FakeRequest { OnBind = (_, c) => close2 = c }, ModalMode.Queued);

            Assert.IsNotNull(close1, "第一个立即显示");
            Assert.IsNull(close2, "Queued 的应在等待队列里,Bind 还没跑");
            Assert.AreEqual(2, UI.Modal.QueuedCount);

            close1(1);
            Assert.AreEqual(1, t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "栈空 → Queued 的现在显示");

            close2(2);
            Assert.AreEqual(2, t2.GetAwaiter().GetResult());
            Assert.AreEqual(0, UI.Modal.QueuedCount);
        }

        [Test]
        public void Multiple_queued_show_in_FIFO_order()
        {
            Action<int> c1 = null, c2 = null, c3 = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c1 = c }, ModalMode.Popup);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c2 = c }, ModalMode.Queued);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c3 = c }, ModalMode.Queued);

            Assert.IsNull(c2); Assert.IsNull(c3);
            c1(0);
            Assert.IsNotNull(c2, "第一个 Queued 先显示"); Assert.IsNull(c3);
            c2(0);
            Assert.IsNotNull(c3, "第二个 Queued 接着显示");
            c3(0);
        }

        [Test]
        public void Queued_on_empty_stack_shows_immediately()
        {
            Action<int> close = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close = c }, ModalMode.Queued);
            Assert.IsNotNull(close, "栈空时 Queued 等同 Popup,立即显示");
            close(0);
        }

        [Test]
        public void Popup_opened_during_queued_wait_stacks_on_current()
        {
            Action<int> c1 = null, cPopup = null, cQueued = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c1 = c }, ModalMode.Popup);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => cQueued = c }, ModalMode.Queued);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => cPopup = c }, ModalMode.Popup);

            Assert.IsNotNull(cPopup, "Popup 立刻叠上去");
            Assert.IsNull(cQueued, "Queued 仍在等");

            cPopup(0); c1(0);
            Assert.IsNotNull(cQueued, "栈全空后 Queued 才出来");
            cQueued(0);
        }
    }
}
