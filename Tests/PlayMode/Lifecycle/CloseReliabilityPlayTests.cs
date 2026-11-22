using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using LitMotion;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Screen = PromptUGUI.Application.Screen;      // disambiguates UnityEngine.Screen

namespace PromptUGUI.Tests.PlayMode.Lifecycle
{
    /// <summary>
    /// Closing is reliable on every path (spec 2026-11-23-close-reliability): subscriptions tied to the Screen live until it is
    /// destroyed (A1), a Screen closed directly leaves UI's registry (A2), a dialog cancelled from another thread closes on the
    /// main thread (A4).
    /// </summary>
    public class CloseReliabilityPlayTests
    {
        private const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>";
        private const string Tail = "</Screen></PromptUGUI>";

        private string _boxSrc;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _boxSrc = MessageBox.XmlSrc;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = _boxSrc;
        }

        // Past the frame it was built in: a Screen closed while still opening takes the destroy body straight away.
        private static IEnumerator Open(string body, System.Action<Screen> got)
        {
            UI.LoadDocument("t", Head + body + Tail);
            var screen = UI.Open("S");
            yield return null;
            yield return null;
            got(screen);
        }

        /// <summary>Stands in for an exit animation: at Begin, hand the Screen a motion of <paramref name="seconds"/>.</summary>
        private static void InjectExit(Screen screen, float seconds)
        {
            screen.OnClosing.Subscribe(_ =>
            {
                var h = LMotion.Create(0f, 1f, seconds).Bind(static v => { });
                screen.NotifyMotions(new[] { h });
            });
        }

        // ───── A1: subscriptions live until the Screen is destroyed ─────

        [UnityTest]
        public IEnumerator OnClosing_reaches_an_AddTo_screen_subscriber_without_an_exit()
        {
            Screen screen = null;
            yield return Open("<Frame id='f'/>", s => screen = s);
            var fired = 0;
            screen.OnClosing.Subscribe(_ => fired++).AddTo(screen);

            UI.Close("S");

            Assert.AreEqual(1, fired);
            Assert.IsNull(screen.RootGameObject, "no exit: destroyed on the same frame, as before");
        }

        [UnityTest]
        public IEnumerator OnClosing_reaches_an_AddTo_screen_subscriber_with_an_exit()
        {
            Screen screen = null;
            yield return Open("<Frame id='f'/>", s => screen = s);
            InjectExit(screen, 0.2f);
            var fired = 0;
            screen.OnClosing.Subscribe(_ => fired++).AddTo(screen);

            UI.Close("S");

            Assert.IsTrue(screen.IsClosing, "precondition: a ghost playing its exit");
            Assert.AreEqual(1, fired);
        }

        [UnityTest]
        public IEnumerator A_close_Trigger_reaches_an_AddTo_screen_subscriber()
        {
            Screen screen = null;
            yield return Open("<Frame id='f'><Trigger id='t' on='close'/></Frame>", s => screen = s);
            var fired = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fired++).AddTo(screen);

            UI.Close("S");

            Assert.AreEqual(1, fired);
        }

        [UnityTest]
        public IEnumerator A_push_during_the_exit_reaches_an_AddTo_screen_subscriber_and_none_after_the_destroy()
        {
            Screen screen = null;
            yield return Open("<Frame id='f'/>", s => screen = s);
            InjectExit(screen, 0.2f);
            var data = new Subject<int>();
            var seen = 0;
            data.Subscribe(_ => seen++).AddTo(screen);

            UI.Close("S");
            data.OnNext(1);
            Assert.AreEqual(1, seen, "the ghost still follows its data while it fades (C# skill: alive until destroyed)");

            yield return new WaitForSeconds(0.3f);
            yield return null;
            yield return null;
            Assume.That(screen.RootGameObject == null, "precondition: destroyed");
            data.OnNext(2);
            Assert.AreEqual(1, seen, "released with the destroy body");
        }

        // ───── A2: a direct close leaves the registry at Begin ─────

        [UnityTest]
        public IEnumerator A_direct_close_with_an_exit_leaves_the_registry_at_Begin_and_the_ghost_keeps_its_owner()
        {
            Screen screen = null;
            yield return Open("<Frame id='f'/>", s => screen = s);
            InjectExit(screen, 0.2f);
            var f = screen.Get<Frame>("f");

            screen.Close();

            Assert.IsTrue(screen.IsClosing, "precondition: a ghost playing its exit");
            Assert.IsNull(UI.Get("S"), "no longer the open Screen of its name");
            Assert.AreEqual(1, UI.ClosingCountForTests, "tracked as a ghost, like UI.Close");
            Assert.AreSame(screen, UI.OwnerScreenOf(f), "its controls still find their Screen");

            yield return new WaitForSeconds(0.3f);
            yield return null;
            yield return null;
            Assert.AreEqual(0, UI.ClosingCountForTests);
            Assert.AreNotSame(screen, UI.Open("S"));
        }

        // ───── A4: a dialog cancelled from another thread closes on the main thread ─────

        private static void UseBoxSkin()
        {
            var files = new Dictionary<string, string>
            {
                ["test/Box"] = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='test/Box'>" +
                               "<Frame id='dialog' anchor='center' size='300x200'><VStack anchor='stretch' spacing='4'>" +
                               "<Text id='title'/><Text id='text'/><Btn id='ok'>OK</Btn><Btn id='cancel'>C</Btn>" +
                               "<Btn id='yes'>Y</Btn><Btn id='no'>N</Btn><Btn id='close'>X</Btn></VStack></Frame></Screen></PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/Box";
        }

        private static IEnumerator CancelFromAnotherThread(Action<CancellationTokenSource> cancel)
        {
            UseBoxSkin();
            var main = Thread.CurrentThread.ManagedThreadId;
            var resumedOn = -1;
            var outcome = "pending";
            var cts = new CancellationTokenSource();
            Await();
            async void Await()
            {
                try
                {
                    await MessageBox.Open("x", ct: cts.Token);
                    outcome = "result";
                }
                catch (OperationCanceledException)
                {
                    outcome = "cancelled";
                }
                resumedOn = Thread.CurrentThread.ManagedThreadId;
            }
            yield return null;
            var box = UI.Modal.TopScreen;
            Assume.That(box, Is.Not.Null, "precondition: the dialog is up");

            cancel(cts);
            var deadline = Time.realtimeSinceStartup + 2f;
            while (outcome == "pending" && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual("cancelled", outcome);
            Assert.AreEqual(main, resumedOn, "the awaiting code resumes on the main thread");
            Assert.IsFalse(UI.Modal.IsAnyOpen);
            Assert.AreEqual(0, UI.ClosingCountForTests, "not left behind as a ghost");
            Assert.IsTrue(box.RootGameObject == null, "destroyed");
            cts.Dispose();
        }

        [UnityTest]
        public IEnumerator A_token_cancelled_by_CancelAfter_closes_the_dialog_on_the_main_thread()
        {
            yield return CancelFromAnotherThread(cts => cts.CancelAfter(20));
        }

        [UnityTest]
        public IEnumerator A_token_cancelled_from_a_worker_thread_closes_the_dialog_on_the_main_thread()
        {
            yield return CancelFromAnotherThread(cts => new Thread(cts.Cancel).Start());
        }
    }
}
