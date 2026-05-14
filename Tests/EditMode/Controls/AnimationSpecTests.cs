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
            s.SetCharColor("1,1,1,1:1,0,0,1");
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
    }
}
