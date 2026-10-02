using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Application
{
    // A pass can throw after it has already written runtime-owned state — ApplyCommon rejects a node that
    // a Variant combination left with a size on its stretched axis. The baselines must still record those
    // writes: a stale one reads the pass's own write as a runtime takeover, and the lock never lifts, even
    // after the bad combination is gone.
    public class ControlAttributeApplierAbortTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            ThrowAfterApply.Armed = false;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            ThrowAfterApply.Armed = false;
        }

        private static PromptScreen Open(string body)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                                 + body + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        // Each variant alone is valid. Flipping between them with two UI.Variants.Set calls goes through a
        // pass with both active — narrow's horizontally stretched anchor with wide's width — which throws
        // after narrow's value has been written.
        private const string ExclusivePairSlider =
            "<Slider id='s' value='0.5' value.narrow='0.2'" +
            " anchor.wide='stretch-left' width.wide='40'" +
            " anchor.narrow='bottom-stretch' height.narrow='40'/>";

        [Test]
        public void Runtime_state_written_by_an_aborted_pass_still_follows_its_variants()
        {
            using var r3 = new R3ExceptionCapture();
            UI.Variants.Set("wide", true);
            var slider = Open(ExclusivePairSlider).Get<Slider>("s");
            Assume.That(slider.Value, Is.EqualTo(0.5f).Within(1e-4f));

            UI.Variants.Set("narrow", true);   // both active: throws after writing 0.2
            UI.Variants.Set("wide", false);
            Assume.That(r3.Caught, Has.Count.EqualTo(1), "guard: the mixed pass was rejected");
            Assert.AreEqual(0.2f, slider.Value, 1e-4f);

            UI.Variants.Set("wide", true);     // both active again
            UI.Variants.Set("narrow", false);
            Assert.AreEqual(0.5f, slider.Value, 1e-4f, "back on wide, the declared base value applies again");
        }

        [Test]
        public void Runtime_takeover_stays_locked_through_an_aborted_pass()
        {
            using var r3 = new R3ExceptionCapture();
            UI.Variants.Set("wide", true);
            var slider = Open(ExclusivePairSlider).Get<Slider>("s");
            slider.Value = 0.8f;               // the player dragged it

            UI.Variants.Set("narrow", true);
            UI.Variants.Set("wide", false);
            UI.Variants.Set("wide", true);
            UI.Variants.Set("narrow", false);

            Assume.That(r3.Caught, Has.Count.EqualTo(2), "guard: both mixed passes were rejected");
            Assert.AreEqual(0.8f, slider.Value, 1e-4f, "a value code wrote stays runtime-owned");
        }

        // hidden / interactable are written at the end of ApplyCommon; OnAfterApply runs after them.
        private sealed class ThrowAfterApply : Control
        {
            public static bool Armed;

            internal override void OnAfterApply()
            {
                if (Armed) throw new System.InvalidOperationException("armed");
            }
        }

        [Test]
        public void Hidden_written_by_an_aborted_pass_still_follows_its_variants()
        {
            UI.Registry.Register<ThrowAfterApply>("ThrowAfterApply", null);
            using var r3 = new R3ExceptionCapture();
            var probe = Open("<ThrowAfterApply id='p' hidden='false' hidden.a='true'/>").Get("p");

            ThrowAfterApply.Armed = true;
            UI.Variants.Set("a", true);        // writes Hidden, then OnAfterApply throws
            ThrowAfterApply.Armed = false;
            Assume.That(r3.Caught, Has.Count.EqualTo(1), "guard: the pass threw");
            Assume.That(probe.GameObject.activeSelf, Is.False, "guard: hidden.a landed before the throw");

            UI.Variants.Set("a", false);
            Assert.IsTrue(probe.GameObject.activeSelf, "hidden='false' applies again once the variant clears");
        }
    }
}
