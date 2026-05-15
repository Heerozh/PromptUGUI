using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Modals
{
    public class ModalQueueTests : ModalTestFixture
    {
        private sealed class FakeRequest : ModalRequest<int>
        {
            public string Src = "test/Box1";
            public Action<IScreen, Action<int>> OnBind;
            public override string XmlSrc => Src;
            public override void Bind(IScreen screen, Action<int> close) => OnBind?.Invoke(screen, close);
        }

        [Test]
        public void OpenAsync_returns_awaitable_completed_after_close()
        {
            Action<int> capturedClose = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, close) => capturedClose = close });

            Assert.IsNotNull(capturedClose, "Bind must have been called synchronously by pump");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            capturedClose(42);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Second_open_queues_until_first_closes()
        {
            Action<int> close1 = null, close2 = null;
            var t1 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close1 = c });
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.IsNotNull(close1, "first Bind should run");
            Assert.IsNull(close2, "second Bind should NOT run until first closes");
            Assert.AreEqual(2, UI.Modal.QueuedCount);

            close1(1);
            Assert.AreEqual(1, t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "second Bind should now run");

            close2(2);
            Assert.AreEqual(2, t2.GetAwaiter().GetResult());
            Assert.AreEqual(0, UI.Modal.QueuedCount);
        }

        [Test]
        public void Close_double_call_is_idempotent()
        {
            Action<int> capturedClose = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => capturedClose = c });
            capturedClose(7);
            capturedClose(99);              // ignored
            Assert.AreEqual(7, task.GetAwaiter().GetResult());
        }

        [Test]
        public void SortingOrder_uses_SortingOrderBase_and_overrideSorting()
        {
            UI.Modal.SortingOrderBase = 500;
            Action<int> close1 = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close1 = c });

            var canvas = UI.Get("test/Box1").RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.AreEqual(500, canvas.sortingOrder,
                "sortingOrder must equal SortingOrderBase");

            close1(0);
        }

        [Test]
        public void Bind_exception_dequeues_and_pumps_next()
        {
            var t1 = UI.Modal.OpenAsync(new FakeRequest
            {
                OnBind = (_, __) => throw new InvalidOperationException("boom"),
            });
            Action<int> close2 = null;
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "second modal should still pump");
            close2(7);
            Assert.AreEqual(7, t2.GetAwaiter().GetResult());
        }
    }
}
