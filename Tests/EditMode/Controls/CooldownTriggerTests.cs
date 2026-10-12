using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using R3;
using UnityEngine;
using Animation = PromptUGUI.Controls.Animation;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>on="ready"</c> / <c>on="cooling"</c> (spec 2026-10-13 §4.3, §5.8): a persistent pair like
    /// <c>checked</c> / <c>unchecked</c>, resolved to the nearest <c>&lt;Cooldown&gt;</c> — except that
    /// a plain <c>&lt;Trigger&gt;</c> is never fired for the state found at open.
    /// </summary>
    public class CooldownTriggerTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            CooldownClock.SetTimeForTests(100f);
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        private static void At(float seconds)
        {
            CooldownClock.SetTimeForTests(seconds);
            CooldownClock.TickForTests();
        }

        /// <summary>Counts every fire, the one dispatched inside <c>UI.Open</c> included.</summary>
        private sealed class CountingTrigger : Trigger
        {
            public static int Fired;
            protected override void OnTriggerFired() => Fired++;
        }

        private static PromptUGUI.Application.Screen OpenCounting(string on, string cooldownAttrs = "")
        {
            CountingTrigger.Fired = 0;
            UI.Registry.Register<CountingTrigger>("CountingTrigger", null);
            return Open($"<CountingTrigger on='{on}'><Btn id='b' size='64x64'>" +
                        $"<Cooldown id='cd' anchor='stretch' {cooldownAttrs}/></Btn></CountingTrigger>");
        }

        // ───── <Trigger> ─────

        [Test]
        public void Ready_IsNotFiredAtOpen_OnlyWhenACooldownRunsOut()
        {
            var s = OpenCounting("ready");
            Assert.AreEqual(0, CountingTrigger.Fired,
                "already ready at open: no 'skill ready' sound every time the bar opens");

            var cd = s.Get<Cooldown>("cd");
            var onReady = 0;
            cd.OnReady.Subscribe(_ => onReady++);
            cd.Start(5f);
            At(105f);

            Assert.AreEqual(1, CountingTrigger.Fired);
            Assert.AreEqual(onReady, CountingTrigger.Fired, "OnFire and OnReady report the same flips");
        }

        [Test]
        public void Cooling_IsNotFiredAtOpen_EvenWhenAValueStartsIt()
        {
            var s = OpenCounting("cooling", "value='0.5'");
            Assert.AreEqual(0, CountingTrigger.Fired,
                "a value= applied while the Screen opens is how it starts, not an event");

            var cd = s.Get<Cooldown>("cd");
            cd.Stop();
            cd.Start(5f);
            Assert.AreEqual(1, CountingTrigger.Fired);
            cd.Start(5f, 2f);
            Assert.AreEqual(1, CountingTrigger.Fired, "a re-time is not a new cooling");
        }

        // ───── <Animation> ─────

        [Test]
        public void AnAnimation_LandsOnItsEndStateAtOpen_WithoutFiring()
        {
            var s = Open("<Animation id='a' on='ready' reverse-on='cooling' fade='0.3:1' duration='0.1s'>" +
                         "<Btn size='64x64'><Cooldown id='cd' anchor='stretch'/></Btn></Animation>");
            var a = s.Get<Animation>("a");
            Assert.AreEqual(1f, a.GameObject.GetComponent<CanvasGroup>().alpha, 1e-5f,
                "ready at open: the ready side of the pair is the resting look");

            var fired = 0;
            var reversed = 0;
            a.OnFire.Subscribe(_ => fired++);
            a.OnReverse.Subscribe(_ => reversed++);

            var cd = s.Get<Cooldown>("cd");
            cd.Start(5f);
            Assert.AreEqual(1, reversed, "cooling plays it backwards");
            At(105f);
            Assert.AreEqual(1, fired, "ready plays it forwards");
        }

        [Test]
        public void AnAnimation_OpeningOnACoolingCooldown_RestsOnTheOtherSide()
        {
            var s = Open("<Animation id='a' on='ready' reverse-on='cooling' fade='0.3:1' duration='0.1s'>" +
                         "<Btn size='64x64'><Cooldown id='cd' anchor='stretch' value='0.5'/></Btn></Animation>");
            Assert.AreEqual(0.3f, s.Get<Animation>("a").GameObject.GetComponent<CanvasGroup>().alpha, 1e-5f);
        }

        // ───── <Show> ─────

        [Test]
        public void AShow_BesideTheCooldown_FollowsItsState()
        {
            var s = Open("<Btn size='64x64'><Cooldown id='cd' anchor='stretch'/>" +
                         "<Show on='ready'><Frame id='lit' anchor='stretch'/></Show>" +
                         "<Show on='cooling'><Frame id='dim' anchor='stretch'/></Show></Btn>");
            var lit = s.Get<Frame>("lit").GameObject;
            var dim = s.Get<Frame>("dim").GameObject;
            Assert.IsTrue(lit.activeInHierarchy, "a bare ready binds to the sibling Cooldown");
            Assert.IsFalse(dim.activeInHierarchy);

            s.Get<Cooldown>("cd").Start(5f);
            Assert.IsFalse(lit.activeInHierarchy);
            Assert.IsTrue(dim.activeInHierarchy);

            At(105f);
            Assert.IsTrue(lit.activeInHierarchy);
            Assert.IsFalse(dim.activeInHierarchy);
        }

        [Test]
        public void AShow_WrittenBeforeTheCooldown_StillFindsIt()
        {
            var s = Open("<Btn size='64x64'><Show on='cooling'><Frame id='dim' anchor='stretch'/></Show>" +
                         "<Cooldown id='cd' anchor='stretch' value='0.5'/></Btn>");
            Assert.IsTrue(s.Get<Frame>("dim").GameObject.activeInHierarchy,
                "the whole tree exists before anything subscribes, and value= lands while opening");
        }

        [Test]
        public void AShow_ByIdWorksAcrossTheScreen()
        {
            var s = Open("<Frame><Cooldown id='cd' size='64x64'/></Frame>" +
                         "<Frame><Show on='cooling@cd'><Frame id='dim'/></Show></Frame>");
            Assert.IsFalse(s.Get<Frame>("dim").GameObject.activeInHierarchy);
            s.Get<Cooldown>("cd").Value = 0.3f;
            Assert.IsTrue(s.Get<Frame>("dim").GameObject.activeInHierarchy);
        }

        // ───── nearest resolution ─────

        [Test]
        public void TheNearestLevel_Wins()
        {
            var s = Open("<Frame>" +
                         "<Cooldown id='far' size='64x64'/>" +
                         "<Frame><Cooldown id='near' size='64x64'/>" +
                         "<Show on='cooling'><Frame id='dim'/></Show></Frame>" +
                         "</Frame>");
            var dim = s.Get<Frame>("dim").GameObject;

            s.Get<Cooldown>("far").Value = 0.5f;
            Assert.IsFalse(dim.activeInHierarchy, "the outer Cooldown is one level further away");
            s.Get<Cooldown>("near").Value = 0.5f;
            Assert.IsTrue(dim.activeInHierarchy);
        }

        [Test]
        public void TwoCooldownsAtTheNearestLevel_AreAmbiguous()
        {
            UI.LoadDocument("t", Header +
                "<Btn size='64x64'><Cooldown id='a'/><Cooldown id='b'/>" +
                "<Show on='ready'><Frame/></Show></Btn>" + Footer);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("ambiguous", ex.Message);
            StringAssert.Contains("ready@", ex.Message);
        }

        [Test]
        public void NoCooldownAnywhere_IsAnError()
        {
            UI.LoadDocument("t", Header + "<Show on='ready'><Frame/></Show>" + Footer);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("no <Cooldown>", ex.Message);
        }

        [Test]
        public void AnIdThatIsNotACooldown_IsAnError()
        {
            UI.LoadDocument("t", Header + "<Frame id='f'/><Show on='ready@f'><Frame/></Show>" + Footer);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("not a <Cooldown>", ex.Message);
        }

        // ───── grammar ─────

        // The kind is passed by name: TriggerKind is internal, and a public test method cannot take it.
        [TestCase("ready", "Ready", null)]
        [TestCase("cooling", "Cooling", null)]
        [TestCase("ready@cd", "Ready", "cd")]
        [TestCase("cooling@cd", "Cooling", "cd")]
        public void TheEventsParse(string on, string kindName, string id)
        {
            var kind = (TriggerKind)System.Enum.Parse(typeof(TriggerKind), kindName);
            var spec = TriggerSpec.Parse(on);
            Assert.AreEqual(kind, spec.Kind);
            Assert.AreEqual(id, spec.SourceId);
            Assert.AreEqual(kind, TriggerSpec.ParseReverseOn(on).Kind, "reverse-on takes them too");
        }
    }
}
