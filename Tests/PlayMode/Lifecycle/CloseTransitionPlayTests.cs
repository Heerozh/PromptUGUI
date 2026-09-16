using System.Collections;
using LitMotion;
using NUnit.Framework;
using PromptUGUI.Application;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Screen = PromptUGUI.Application.Screen;      // disambiguates UnityEngine.Screen

namespace PromptUGUI.Tests.PlayMode.Lifecycle
{
    /// <summary>
    /// The two-phase Close (spec 2026-09-16-close-transition-design §5): Begin unregisters the
    /// Screen, fires <c>OnClosing</c> and collects the exit motions handed back through
    /// <c>NotifyMotions</c>; Finish awaits them and only then runs the destroy body. A Screen that
    /// hands back nothing is destroyed on the same frame, exactly as before.
    ///
    /// <para>M0 has no trigger that produces exit motions yet, so these tests do what
    /// <c>&lt;Animation reverse-on="close"&gt;</c> will do in M1: subscribe <c>OnClosing</c> and hand
    /// a running LitMotion handle to the Screen.</para>
    /// </summary>
    public class CloseTransitionPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";
        private const string Xml = Header + "<Frame id='f'/>" + Footer;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen OpenPlain()
        {
            UI.LoadDocument("t", Xml);
            return UI.Open("S");
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

        [UnityTest]
        public IEnumerator Close_without_exit_motions_destroys_on_the_same_frame()
        {
            var screen = OpenPlain();
            UI.Close("S");
            Assert.IsNull(screen.RootGameObject, "no exit motions → the destroy body runs synchronously, as before");
            Assert.IsFalse(screen.IsClosing);
            Assert.IsNull(UI.Get("S"));
            Assert.AreEqual(0, UI.ClosingCountForTests);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Close_with_exit_motions_keeps_the_root_until_they_finish()
        {
            var screen = OpenPlain();
            InjectExit(screen, 0.2f);
            var root = screen.RootGameObject;

            UI.Close("S");
            Assert.IsTrue(screen.IsClosing, "Begin ran");
            Assert.IsNotNull(screen.RootGameObject, "the ghost stays alive while the exit plays");
            Assert.IsNull(UI.Get("S"), "but it is no longer the open Screen of that name");
            Assert.AreEqual(1, UI.ClosingCountForTests);

            yield return null;
            Assert.IsTrue(root != null && screen.IsClosing, "still exiting one frame later");

            yield return new WaitForSeconds(0.3f);
            yield return null;
            yield return null;
            Assert.IsTrue(root == null, "destroyed once the motion finished");
            Assert.IsNull(screen.RootGameObject);
            Assert.IsFalse(screen.IsClosing);
            Assert.AreEqual(0, UI.ClosingCountForTests);
        }

        [UnityTest]
        public IEnumerator OnClosing_fires_exactly_once_before_Close_returns()
        {
            var screen = OpenPlain();
            var fired = 0;
            screen.OnClosing.Subscribe(_ => fired++);
            UI.Close("S");
            Assert.AreEqual(1, fired);
            screen.Close();        // idempotent
            screen.Dispose();
            Assert.AreEqual(1, fired);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CloseAsync_completes_after_the_root_is_destroyed()
        {
            var screen = OpenPlain();
            InjectExit(screen, 0.2f);
            var root = screen.RootGameObject;

            var aw = UI.CloseAsync("S");
            Assert.IsFalse(aw.GetAwaiter().IsCompleted, "the exit is still playing");
            Assert.IsTrue(root != null);

            for (int i = 0; i < 60 && !aw.GetAwaiter().IsCompleted; i++) yield return null;
            Assert.IsTrue(aw.GetAwaiter().IsCompleted, "completes once the exit has finished");
            Assert.IsNull(screen.RootGameObject, "and only after the destroy body ran");
            Assert.IsFalse(screen.IsClosing);
            aw.GetAwaiter().GetResult();
            yield return null;                         // Object.Destroy lands at end of frame
            Assert.IsTrue(root == null);
        }

        [UnityTest]
        public IEnumerator CloseAsync_without_exit_motions_is_already_completed()
        {
            var screen = OpenPlain();
            var aw = UI.CloseAsync("S");
            Assert.IsTrue(aw.GetAwaiter().IsCompleted);
            Assert.IsNull(screen.RootGameObject);
            // Unknown / already-closed names complete too — nothing to wait for.
            Assert.IsTrue(UI.CloseAsync("S").GetAwaiter().IsCompleted);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Dispose_destroys_immediately_even_with_exit_motions()
        {
            var screen = OpenPlain();
            InjectExit(screen, 0.5f);
            screen.Dispose();
            Assert.IsNull(screen.RootGameObject, "IDisposable means now");
            Assert.IsFalse(screen.IsClosing);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResetForTests_during_an_exit_destroys_now_and_releases_waiters()
        {
            var screen = OpenPlain();
            InjectExit(screen, 0.5f);
            var root = screen.RootGameObject;
            var aw = UI.CloseAsync("S");

            UI.ResetForTests();    // teardown: the immediate path, re-entered while Finish is pending
            Assert.IsNull(screen.RootGameObject);
            Assert.IsTrue(aw.GetAwaiter().IsCompleted, "a teardown must not leave CloseAsync hanging");
            Assert.AreEqual(0, UI.ClosingCountForTests);

            // The pending Finish continuation wakes up in the next frames and must be a no-op:
            // the test runner fails on any error logged here.
            yield return null;
            yield return null;
            Assert.IsTrue(root == null);
        }

        [UnityTest]
        public IEnumerator UnloadAll_during_an_exit_destroys_now()
        {
            var screen = OpenPlain();
            InjectExit(screen, 0.5f);
            UI.Close("S");
            Assert.IsTrue(screen.IsClosing);

            UI.UnloadAll();
            Assert.IsNull(screen.RootGameObject);
            Assert.AreEqual(0, UI.ClosingCountForTests);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator External_destroy_during_an_exit_is_tolerated()
        {
            var screen = OpenPlain();
            InjectExit(screen, 0.5f);
            var aw = UI.CloseAsync("S");
            Assert.IsTrue(screen.IsClosing);

            Object.Destroy(screen.RootGameObject);   // scene unload, not our Close
            yield return null;                        // the relay's OnDestroy ran
            yield return null;
            yield return null;
            Assert.IsNull(screen.RootGameObject);
            Assert.IsTrue(aw.GetAwaiter().IsCompleted, "the waiter is released by the external-destroy path");
            Assert.AreEqual(0, UI.ClosingCountForTests, "and the ghost left the closing set");
        }

        [UnityTest]
        public IEnumerator Reopening_the_same_name_during_an_exit_creates_an_independent_instance()
        {
            var a = OpenPlain();
            InjectExit(a, 0.2f);
            UI.Close("S");

            var b = UI.Open("S");
            Assert.AreNotSame(a, b);
            Assert.AreSame(b, UI.Get("S"));
            Assert.IsNotNull(a.RootGameObject, "the ghost is still playing its exit");
            Assert.IsNotNull(b.RootGameObject);

            yield return new WaitForSeconds(0.3f);
            yield return null;
            yield return null;
            Assert.IsNull(a.RootGameObject, "the ghost finished on its own");
            Assert.IsNotNull(b.RootGameObject, "the new instance is untouched");
            Assert.IsFalse(b.IsClosing);
        }

        [UnityTest]
        public IEnumerator Hot_reload_closes_the_open_instance_immediately_even_with_exit_motions()
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "t" ? Xml : null);
            var load = UI.LoadDocumentAsync("t");
            for (int i = 0; i < 30 && !load.GetAwaiter().IsCompleted; i++) yield return null;
            load.GetAwaiter().GetResult();

            var old = UI.Open("S");
            InjectExit(old, 0.5f);

            var reload = UI.ReloadAsync("S");
            for (int i = 0; i < 30 && !reload.GetAwaiter().IsCompleted; i++) yield return null;
            reload.GetAwaiter().GetResult();

            Assert.IsNull(old.RootGameObject, "a reload never plays an exit — two same-named roots would overlap");
            Assert.IsFalse(old.IsClosing);
            var fresh = UI.Get("S");
            Assert.IsNotNull(fresh);
            Assert.AreNotSame(old, fresh);
            Assert.AreEqual(0, UI.ClosingCountForTests);
        }
    }
}
