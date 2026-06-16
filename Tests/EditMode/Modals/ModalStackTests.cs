using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Modals
{
    public class ModalStackTests : ModalTestFixture
    {
        private sealed class FakeRequest : ModalRequest<int>
        {
            public string Src = "test/Box1";
            public Action<IScreen, Action<int>> OnBind;
            public override string XmlSrc => Src;
            public override void Bind(IScreen screen, Action<int> close) => OnBind?.Invoke(screen, close);
        }

        [Test]
        public void Open_then_close_resolves_awaitable()
        {
            Action<int> close = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close = c });

            Assert.IsNotNull(close, "Bind 应同步跑(fake resolver 同步完成)");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            close(42);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Popup_default_stacks_both_modals_immediately()
        {
            Action<int> close1 = null, close2 = null;
            var t1 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close1 = c });
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.IsNotNull(close1, "第一个 Bind 跑");
            Assert.IsNotNull(close2, "Popup 默认 → 第二个 Bind 也立刻跑(叠在上面)");
            Assert.AreEqual(2, UI.Modal.QueuedCount);

            close2(2);                                  // 关栈顶
            Assert.AreEqual(2, t2.GetAwaiter().GetResult());
            Assert.IsTrue(UI.Modal.IsAnyOpen, "关掉栈顶,下面那个还在");

            close1(1);
            Assert.AreEqual(1, t1.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Stacked_modals_get_incrementing_sortingOrder()
        {
            UI.Modal.SortingOrderBase = 1000;
            Action<int> c1 = null, c2 = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c1 = c });
            var bottom = UI.Modal.TopScreen;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c2 = c });
            var top = UI.Modal.TopScreen;

            Assert.AreNotSame(bottom, top);
            Assert.AreEqual(1000, bottom.RootGameObject.GetComponent<UnityEngine.Canvas>().sortingOrder);
            Assert.AreEqual(1001, top.RootGameObject.GetComponent<UnityEngine.Canvas>().sortingOrder);

            c2(0); c1(0);
        }

        [Test]
        public void Same_src_can_stack_two_instances()
        {
            Action<int> c1 = null, c2 = null;
            UI.Modal.OpenAsync(new FakeRequest { Src = "test/Box1", OnBind = (_, c) => c1 = c });
            var s1 = UI.Modal.TopScreen;
            UI.Modal.OpenAsync(new FakeRequest { Src = "test/Box1", OnBind = (_, c) => c2 = c });
            var s2 = UI.Modal.TopScreen;

            Assert.AreNotSame(s1, s2, "同一 XmlSrc 的两个 modal 必须是两份独立 Screen");

            c2(0); c1(0);
        }

        [Test]
        public void Bind_exception_cancels_that_modal_and_pumps_next()
        {
            // pump 现在 Debug.LogError 这个 open 失败（boom 来自 Bind）。
            LogAssert.Expect(LogType.Error, new Regex("failed to open"));
            var t1 = UI.Modal.OpenAsync(new FakeRequest
            {
                OnBind = (_, __) => throw new InvalidOperationException("boom"),
            });
            Action<int> close2 = null;
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "后一个 modal 仍应被实例化");
            close2(7);
            Assert.AreEqual(7, t2.GetAwaiter().GetResult());
        }

        [Test]
        public void Close_double_call_is_idempotent()
        {
            Action<int> close = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close = c });
            close(7);
            close(99);                                  // 忽略
            Assert.AreEqual(7, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Escape_listener_only_on_top_modal()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "bottom", Buttons = MsgBtn.OK | MsgBtn.Cancel });
            var bottom = UI.Modal.TopScreen;
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "top", Buttons = MsgBtn.OK | MsgBtn.Cancel });
            var top = UI.Modal.TopScreen;

            var bottomEsc = bottom.RootGameObject.GetComponent<ModalEscapeListener>();
            var topEsc = top.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsFalse(bottomEsc.enabled, "被压住的 modal 的 ESC listener 应禁用");
            Assert.IsTrue(topEsc.enabled, "栈顶 modal 的 ESC listener 应启用");

            UI.Modal.CloseAll();
        }
    }
}
