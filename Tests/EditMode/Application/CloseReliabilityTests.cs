using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// A Screen closed directly leaves UI's registry like one closed through UI (spec 2026-11-23-close-reliability §3): the next
    /// Open builds a new one; a dialog closed that way settles its await and frees the dialog stack; a routed page warns.
    /// </summary>
    public class CloseReliabilityTests
    {
        private const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private string _boxSrc;
        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _boxSrc = MessageBox.XmlSrc;
            _files = new Dictionary<string, string>();
            UI.SourceResolver = src => AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = _boxSrc;
        }

        private static Screen OpenS()
        {
            UI.LoadDocument("t", Head + "<Screen name='S'><Frame id='f'/></Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static void AssertUnregisteredAndReopensFresh(Screen closed)
        {
            Assert.IsNull(UI.Get("S"), "no longer the open Screen of its name");
            var again = UI.Open("S");
            Assert.AreNotSame(closed, again, "a new instance, not the dead one");
            Assert.IsNotNull(again.RootGameObject);
            Assert.IsNotNull(again.Get<Frame>("f"));
        }

        // ───── A2 ─────

        [Test]
        public void A_direct_Close_unregisters()
        {
            var s = OpenS();
            s.Close();
            AssertUnregisteredAndReopensFresh(s);
        }

        [Test]
        public void A_direct_CloseAsync_unregisters()
        {
            var s = OpenS();
            _ = s.CloseAsync();
            AssertUnregisteredAndReopensFresh(s);
        }

        [Test]
        public void Dispose_unregisters()
        {
            var s = OpenS();
            s.Dispose();
            AssertUnregisteredAndReopensFresh(s);
        }

        // ───── A3 ─────

        private void UseBoxSkin()
        {
            _files["test/Box"] = Head + @"<Screen name='test/Box'><Frame id='dialog' anchor='center' size='300x200'>
              <VStack anchor='stretch' spacing='4'><Text id='title'/><Text id='text'/>
              <Btn id='ok'>OK</Btn><Btn id='cancel'>C</Btn><Btn id='yes'>Y</Btn><Btn id='no'>N</Btn><Btn id='close'>X</Btn>
              </VStack></Frame></Screen></PromptUGUI>";
            MessageBox.XmlSrc = "test/Box";
        }

        [Test]
        public void A_dialog_closed_directly_cancels_its_await_and_frees_the_stack([Values] bool dispose)
        {
            UseBoxSkin();
            IScreen box = null;
            var first = MessageBox.Open("one", configure: s => box = s);
            var second = MessageBox.Open("two", mode: ModalMode.Queued);
            Assume.That(box, Is.Not.Null, "precondition: the first dialog is up");
            Assume.That(UI.Modal.TopScreen, Is.SameAs(box));

            if (dispose) box.Dispose();
            else _ = box.CloseAsync();

            Assert.IsTrue(first.GetAwaiter().IsCompleted, "its await is settled");
            Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
            Assert.IsTrue(UI.Modal.IsAnyOpen, "the queued dialog took its place");
            Assert.IsNotNull(UI.Modal.TopScreen);
            Assert.AreNotSame(box, UI.Modal.TopScreen);
            Assert.IsFalse(second.GetAwaiter().IsCompleted);
        }

        // ───── A4 ─────

        [Test]
        public void A_long_lived_token_holds_no_registration_once_its_dialogs_settled()
        {
            UseBoxSkin();
            using var cts = new System.Threading.CancellationTokenSource();
            for (var i = 0; i < 3; i++)
            {
                var box = MessageBox.Open("x", ct: cts.Token);
                UI.Modal.TopScreen.Get<Btn>("ok").SimulateClick();
                Assume.That(box.GetAwaiter().IsCompleted, "precondition: answered");
            }

            Assert.AreEqual(0, UI.Modal.TokenRegistrationsForTests, "each registration went with its dialog");
        }

        // ───── routed pages ─────

        private void MapHomeAndShop()
        {
            _files["home"] = Head + "<Screen name='home'><Frame id='f'/></Screen></PromptUGUI>";
            _files["shop"] = Head + "<Screen name='shop'><Frame id='f'/></Screen></PromptUGUI>";
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop");
        }

        [Test]
        public void A_routed_page_closed_directly_warns()
        {
            MapHomeAndShop();
            UI.Router.Open("home").GetAwaiter().GetResult();
            var page = UI.Get("home");

            LogAssert.Expect(LogType.Warning, new Regex("routed page"));
            page.Close();

            Assert.IsNull(UI.Get("home"));
        }

        [Test]
        public void The_router_and_teardown_close_pages_without_a_warning()
        {
            MapHomeAndShop();
            UI.Router.Open("home").GetAwaiter().GetResult();
            UI.Router.Open("shop").GetAwaiter().GetResult();
            UI.UnloadAll();

            LogAssert.NoUnexpectedReceived();
        }
    }
}
