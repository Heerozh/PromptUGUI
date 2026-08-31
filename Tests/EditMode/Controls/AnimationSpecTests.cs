using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class AnimationSpecTests
    {
        [Test]
        public void Empty_spec_validates()
        {
            var s = new AnimationSpec();
            Assert.DoesNotThrow(s.Validate);
            Assert.AreEqual(AnimationFamily.None, s.Family);
        }

        [Test]
        public void Preset_family_recognized()
        {
            var s = new AnimationSpec(); s.SetType("fadein");
            s.Validate();
            Assert.AreEqual(AnimationFamily.Preset, s.Family);
        }

        [Test]
        public void LowLevel_family_recognized()
        {
            var s = new AnimationSpec(); s.SetTranslate("0,-50:0,0");
            s.Validate();
            Assert.AreEqual(AnimationFamily.LowLevel, s.Family);
            Assert.AreEqual(new Vector2(0, -50), s.TranslateFrom);
            Assert.AreEqual(Vector2.zero, s.TranslateTo);
        }

        [Test]
        public void Text_family_recognized()
        {
            var s = new AnimationSpec(); s.SetCount("0:1000");
            s.Validate();
            Assert.AreEqual(AnimationFamily.Text, s.Family);
            Assert.AreEqual(0f, s.CountFrom);
            Assert.AreEqual(1000f, s.CountTo);
        }

        [Test]
        public void Preset_and_LowLevel_throws()
        {
            var s = new AnimationSpec();
            s.SetType("fadein");
            s.SetTranslate("0,0:0,0");
            Assert.Throws<System.ArgumentException>(s.Validate);
        }

        [Test]
        public void LowLevel_and_Text_throws()
        {
            var s = new AnimationSpec();
            s.SetTranslate("0,0:0,0");
            s.SetCount("0:100");
            Assert.Throws<System.ArgumentException>(s.Validate);
        }

        [Test]
        public void Count_and_CharColor_throws()
        {
            var s = new AnimationSpec();
            s.SetCount("0:100");
            s.SetCharColor("#ffffff:#ff0000");
            Assert.Throws<System.ArgumentException>(s.Validate);
        }

        [Test]
        public void Invalid_preset_name_throws()
        {
            var s = new AnimationSpec();
            s.SetType("explodeIn");
            Assert.Throws<System.ArgumentException>(s.Validate);
        }

        [Test]
        public void Translate_single_to_uses_zero_from()
        {
            var s = new AnimationSpec();
            s.SetTranslate(":50,0");
            s.Validate();
            Assert.AreEqual(Vector2.zero, s.TranslateFrom);
            Assert.AreEqual(new Vector2(50, 0), s.TranslateTo);
        }

        [Test]
        public void Scale_single_value_expands_to_vector()
        {
            var s = new AnimationSpec();
            s.SetScale("0.5:1");
            s.Validate();
            Assert.AreEqual(new Vector2(0.5f, 0.5f), s.ScaleFrom);
            Assert.AreEqual(Vector2.one, s.ScaleTo);
        }

        [Test]
        public void Loop_count_parses()
        {
            var s = new AnimationSpec();
            s.SetLoop("count:3");
            s.Validate();
            Assert.AreEqual(LoopMode.Count, s.LoopMode);
            Assert.AreEqual(3, s.LoopCount);
        }

        [Test]
        public void Snapshot_equality_for_control_props()
        {
            var s1 = new AnimationSpec(); s1.SetType("fadein"); s1.SetDuration("0.3s");
            var s2 = new AnimationSpec(); s2.SetType("fadein"); s2.SetDuration("0.3s");
            s1.Validate(); s2.Validate();
            Assert.AreEqual(s1.Snapshot(), s2.Snapshot());
        }

        [Test]
        public void Snapshot_differs_when_duration_changes()
        {
            var s1 = new AnimationSpec(); s1.SetType("fadein"); s1.SetDuration("0.3s");
            var s2 = new AnimationSpec(); s2.SetType("fadein"); s2.SetDuration("0.5s");
            s1.Validate(); s2.Validate();
            Assert.AreNotEqual(s1.Snapshot(), s2.Snapshot());
        }

        [Test]
        public void Target_with_at_sign_strips_prefix()
        {
            var s = new AnimationSpec();
            s.SetCount("0:1");
            s.SetTarget("@score");
            s.Validate();
            Assert.AreEqual("score", s.TargetId);
        }

        [Test]
        public void Preset_fadein_expands_to_fade_0_to_1()
        {
            var s = new AnimationSpec(); s.SetType("fadein"); s.Validate();
            s.ExpandPreset();
            Assert.IsTrue(s.HasFade);
            Assert.AreEqual(0f, s.FadeFrom);
            Assert.AreEqual(1f, s.FadeTo);
        }

        [Test]
        public void Preset_slidein_left_expands_to_translate_and_fade()
        {
            var s = new AnimationSpec(); s.SetType("slidein-left"); s.Validate();
            s.ExpandPreset();
            Assert.IsTrue(s.HasTranslate);
            Assert.AreEqual(new UnityEngine.Vector2(-100, 0), s.TranslateFrom);
            Assert.AreEqual(UnityEngine.Vector2.zero, s.TranslateTo);
            Assert.IsTrue(s.HasFade);
        }

        [Test]
        public void Preset_pulse_sets_yoyo_loop_implicitly()
        {
            var s = new AnimationSpec(); s.SetType("pulse"); s.Validate();
            s.ExpandPreset();
            Assert.AreEqual(LoopMode.Yoyo, s.LoopMode);
        }

        [Test]
        public void Preset_bounce_sets_outback_easing_implicitly()
        {
            var s = new AnimationSpec(); s.SetType("bounce"); s.Validate();
            // explicitly check the easing was NOT overwritten if user set it
            Assert.AreEqual(EasingKind.OutCubic, s.Easing); // user didn't set, default
            s.ExpandPreset();
            Assert.AreEqual(EasingKind.OutBack, s.Easing);
        }

        // ── Family D: reveal (spec 2026-08-31-hug-reveal-flip-checked-design §2.3) ────────

        [Test]
        public void Reveal_y_defaults_to_zero_through_hug()
        {
            var s = new AnimationSpec(); s.SetReveal("y"); s.Validate();

            Assert.IsTrue(s.HasReveal);
            Assert.AreEqual(1, s.RevealAxis);
            Assert.IsFalse(s.RevealFrom.IsHug);
            Assert.AreEqual(0f, s.RevealFrom.Px);
            Assert.IsTrue(s.RevealTo.IsHug, "the natural end point is 'as big as the content'");
            Assert.AreEqual(AnimationFamily.LowLevel, s.Family, "a bare reveal plays through the low-level path");
        }

        [Test]
        public void Reveal_x_is_the_width_axis()
        {
            var s = new AnimationSpec(); s.SetReveal("x"); s.Validate();
            Assert.AreEqual(0, s.RevealAxis);
        }

        [Test]
        public void Reveal_rejects_any_other_axis()
        {
            var s = new AnimationSpec();
            var ex = Assert.Throws<System.ArgumentException>(() => s.SetReveal("z"));
            StringAssert.Contains("'y'", ex.Message);
        }

        [Test]
        public void Reveal_endpoints_take_pixels_or_hug()
        {
            var s = new AnimationSpec();
            s.SetReveal("y");
            s.SetRevealFrom("24");
            s.SetRevealTo("hug");
            s.Validate();

            Assert.AreEqual(24f, s.RevealFrom.Px);
            Assert.IsFalse(s.RevealFrom.IsHug);
            Assert.IsTrue(s.RevealTo.IsHug);
        }

        [TestCase("")]
        [TestCase("tall")]
        [TestCase("-4")]
        public void Reveal_endpoints_reject_nonsense(string value)
        {
            var s = new AnimationSpec();
            Assert.Throws<System.ArgumentException>(() => s.SetRevealFrom(value));
        }

        [Test]
        public void Reveal_rejects_identical_endpoints()
        {
            var s = new AnimationSpec();
            s.SetReveal("y");
            s.SetRevealFrom("hug");
            s.SetRevealTo("hug");

            var ex = Assert.Throws<System.ArgumentException>(() => s.Validate());
            StringAssert.Contains("nothing would move", ex.Message);
        }

        [Test]
        public void Reveal_composes_with_a_low_level_channel()
        {
            var s = new AnimationSpec();
            s.SetReveal("y");
            s.SetFade("0:1");
            s.Validate();

            Assert.AreEqual(AnimationFamily.LowLevel, s.Family);
            Assert.IsTrue(s.HasReveal);
            Assert.IsTrue(s.HasFade);
        }

        [Test]
        public void Reveal_and_preset_are_mutually_exclusive()
        {
            var s = new AnimationSpec();
            s.SetReveal("y");
            s.SetType("fadein");

            var ex = Assert.Throws<System.ArgumentException>(() => s.Validate());
            StringAssert.Contains("mutually exclusive", ex.Message);
        }

        [Test]
        public void Reveal_and_text_family_are_mutually_exclusive()
        {
            var s = new AnimationSpec();
            s.SetReveal("y");
            s.SetCount("0:10");

            Assert.Throws<System.ArgumentException>(() => s.Validate());
        }

        // ── reverse-on (§2.3 / §2.4.5) ───────────────────────────────────────────────────

        [Test]
        public void ReverseOn_parses_an_event_with_a_source_id()
        {
            var s = new AnimationSpec();
            s.SetFade("0:1");
            s.SetReverseOn("collapse@tasks");
            s.Validate();

            Assert.IsNotNull(s.ReverseOn);
            Assert.AreEqual(TriggerKind.Collapse, s.ReverseOn.Kind);
            Assert.AreEqual("tasks", s.ReverseOn.SourceId);
        }

        [TestCase("open")]
        [TestCase("loop")]
        public void ReverseOn_rejects_the_two_beginnings(string value)
        {
            var s = new AnimationSpec();
            var ex = Assert.Throws<System.ArgumentException>(() => s.SetReverseOn(value));
            StringAssert.Contains("reverse-on", ex.Message);
        }

        [Test]
        public void ReverseOn_and_loop_are_mutually_exclusive()
        {
            var s = new AnimationSpec();
            s.SetFade("0:1");
            s.SetReverseOn("click");
            s.SetLoop("yoyo");

            var ex = Assert.Throws<System.ArgumentException>(() => s.Validate());
            StringAssert.Contains("loop", ex.Message);
        }

        [Test]
        public void ReverseOn_and_the_text_family_are_mutually_exclusive()
        {
            var s = new AnimationSpec();
            s.SetCount("0:10");
            s.SetReverseOn("click");

            Assert.Throws<System.ArgumentException>(() => s.Validate());
        }

        // ── snapshot / clone carry the new fields ────────────────────────────────────────

        [Test]
        public void Snapshot_notices_a_changed_reveal_endpoint()
        {
            var a = new AnimationSpec(); a.SetReveal("y"); a.Validate();
            var before = a.Snapshot();

            a.SetRevealTo("120");
            var after = a.Snapshot();

            Assert.IsFalse(before.Equals(after), "ReSolve must see reveal changes, or it would keep a stale box");
        }

        [Test]
        public void Snapshot_notices_a_changed_reverse_on()
        {
            var a = new AnimationSpec(); a.SetFade("0:1"); a.SetReverseOn("click"); a.Validate();
            var before = a.Snapshot();

            a.SetReverseOn("collapse");
            Assert.IsFalse(before.Equals(a.Snapshot()));
        }

        [Test]
        public void Clone_carries_the_reveal_fields()
        {
            var a = new AnimationSpec(); a.SetReveal("x"); a.SetRevealTo("80"); a.Validate();

            var copy = a.Clone();

            Assert.IsTrue(copy.HasReveal);
            Assert.AreEqual(0, copy.RevealAxis);
            Assert.AreEqual(80f, copy.RevealTo.Px);
        }
    }
}
