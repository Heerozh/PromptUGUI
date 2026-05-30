using NUnit.Framework;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TriggerSpecTests
    {
        [Test]
        public void Null_or_empty_parses_to_Open()
        {
            Assert.AreEqual(TriggerKind.Open, TriggerSpec.Parse(null).Kind);
            Assert.AreEqual(TriggerKind.Open, TriggerSpec.Parse("").Kind);
        }

        [Test] public void Open_parses() => Assert.AreEqual(TriggerKind.Open, TriggerSpec.Parse("open").Kind);
        [Test] public void Loop_parses() => Assert.AreEqual(TriggerKind.Loop, TriggerSpec.Parse("loop").Kind);
        [Test] public void Manual_parses() => Assert.AreEqual(TriggerKind.Manual, TriggerSpec.Parse("manual").Kind);

        [Test]
        public void Click_bare_parses_with_null_SourceId()
        {
            var spec = TriggerSpec.Parse("click");
            Assert.AreEqual(TriggerKind.Click, spec.Kind);
            Assert.IsNull(spec.SourceId);
        }

        [Test]
        public void Click_with_id_parses()
        {
            var spec = TriggerSpec.Parse("click@ok");
            Assert.AreEqual(TriggerKind.Click, spec.Kind);
            Assert.AreEqual("ok", spec.SourceId);
        }

        [Test]
        public void Invalid_value_throws()
        {
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover"));
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("click@"));     // empty id
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("click@a@b")); // double @
        }

        [Test]
        public void Hover_enter_parses() =>
            Assert.AreEqual(TriggerKind.HoverEnter, TriggerSpec.Parse("hover-enter").Kind);

        [Test]
        public void Hover_exit_parses() =>
            Assert.AreEqual(TriggerKind.HoverExit, TriggerSpec.Parse("hover-exit").Kind);

        [Test]
        public void Press_parses() =>
            Assert.AreEqual(TriggerKind.Press, TriggerSpec.Parse("press").Kind);

        [Test]
        public void Hover_enter_with_id_parses()
        {
            var spec = TriggerSpec.Parse("hover-enter@btn");
            Assert.AreEqual(TriggerKind.HoverEnter, spec.Kind);
            Assert.AreEqual("btn", spec.SourceId);
        }

        [Test]
        public void Hover_exit_with_id_parses()
        {
            var spec = TriggerSpec.Parse("hover-exit@img");
            Assert.AreEqual(TriggerKind.HoverExit, spec.Kind);
            Assert.AreEqual("img", spec.SourceId);
        }

        [Test]
        public void Press_with_id_parses()
        {
            var spec = TriggerSpec.Parse("press@btn");
            Assert.AreEqual(TriggerKind.Press, spec.Kind);
            Assert.AreEqual("btn", spec.SourceId);
        }

        [Test]
        public void Pointer_with_empty_id_throws()
        {
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover-enter@"));
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover-exit@"));
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("press@"));
        }

        [Test]
        public void Pointer_with_double_at_throws()
        {
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("hover-enter@a@b"));
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("press@x@y"));
        }

        // ---- Phase 3: state-* trigger kinds ----

        [Test]
        public void State_normal_parses() =>
            Assert.AreEqual(TriggerKind.StateNormal, TriggerSpec.Parse("state-normal").Kind);

        [Test]
        public void State_hover_parses() =>
            Assert.AreEqual(TriggerKind.StateHover, TriggerSpec.Parse("state-hover").Kind);

        [Test]
        public void State_pressed_parses_with_null_SourceId()
        {
            var spec = TriggerSpec.Parse("state-pressed");
            Assert.AreEqual(TriggerKind.StatePressed, spec.Kind);
            Assert.IsNull(spec.SourceId);
        }

        [Test]
        public void State_disabled_parses() =>
            Assert.AreEqual(TriggerKind.StateDisabled, TriggerSpec.Parse("state-disabled").Kind);

        [Test]
        public void State_hover_with_id_parses()
        {
            var spec = TriggerSpec.Parse("state-hover@x");
            Assert.AreEqual(TriggerKind.StateHover, spec.Kind);
            Assert.AreEqual("x", spec.SourceId);
        }

        [Test]
        public void State_pressed_with_id_parses()
        {
            var spec = TriggerSpec.Parse("state-pressed@b");
            Assert.AreEqual(TriggerKind.StatePressed, spec.Kind);
            Assert.AreEqual("b", spec.SourceId);
        }

        [Test]
        public void State_with_empty_id_throws()
        {
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("state-normal@"));
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("state-pressed@"));
        }

        [Test]
        public void State_with_double_at_throws()
        {
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("state-hover@a@b"));
            Assert.Throws<System.ArgumentException>(() => TriggerSpec.Parse("state-disabled@x@y"));
        }
    }
}
