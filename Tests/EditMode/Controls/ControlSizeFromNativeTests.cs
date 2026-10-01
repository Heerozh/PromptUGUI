using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>Control.SizeFromNative</c> and <c>Control.InApplyPass</c> — the two flags the async sprite slot reads
    /// (spec 2026-10-01-runtime-sprite-sets-design §7.3, §7.5). SizeFromNative follows the declaration and the
    /// parent type, never what <c>GetNativeSize()</c> happened to return.
    /// </summary>
    public class ControlSizeFromNativeTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var a = Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
            UI.SpriteResolver = _ => a;
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + "<Frame id='box' anchor='top-left' width='400' height='400'>" + body + "</Frame>"
                      + "</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S");
        }

        private static bool FromNative(PromptUGUI.Application.Screen s, string id) => ((Control)s.Get(id)).SizeFromNative;

        [Test]
        public void Native_keyword_sets_SizeFromNative()
        {
            var s = Open("<Icon id='i' name='ui:a' size='native'/>");
            Assert.IsTrue(FromNative(s, "i"));
        }

        [Test]
        public void Omitted_size_in_free_positioning_sets_SizeFromNative()
        {
            var s = Open("<Image id='i' sprite='ui:a'/><Icon id='j' name='ui:a' width='32'/>");
            Assert.IsTrue(FromNative(s, "i"), "neither axis written");
            Assert.IsTrue(FromNative(s, "j"), "height left to the sprite");
        }

        [Test]
        public void Explicit_size_clears_SizeFromNative_on_next_pass()
        {
            var s = Open("<Icon id='i' name='ui:a' size='native' size.alt='32x32'/>");
            Assume.That(FromNative(s, "i"), Is.True);

            UI.Variants.Set("alt", true);

            Assert.IsFalse(FromNative(s, "i"));
        }

        [Test]
        public void Stretched_axis_does_not_set_SizeFromNative()
        {
            var s = Open("<Image id='i' sprite='ui:a' anchor='stretch'/>");
            Assert.IsFalse(FromNative(s, "i"));
        }

        [Test]
        public void Grid_cell_child_does_not_set_SizeFromNative()
        {
            var s = Open("<Grid columns='2' cellSize='40x40' width='200' height='200'><Icon id='i' name='ui:a'/></Grid>");
            Assert.IsFalse(FromNative(s, "i"), "GridLayoutGroup's cellSize decides the size");
        }

        [Test]
        public void Stack_child_with_omitted_axis_sets_SizeFromNative()
        {
            var s = Open("<HStack width='300' height='100'><Icon id='i' name='ui:a' width='32'/></HStack>");
            Assert.IsTrue(FromNative(s, "i"));
        }

        [Test]
        public void Stack_cross_fill_axis_does_not_set_SizeFromNative()
        {
            // A Frame's default anchor stretches, so in a VStack its width fills the cross axis.
            var s = Open("<VStack width='300' height='300'><Frame id='f' height='40'/></VStack>");
            Assert.IsFalse(FromNative(s, "f"));
        }

        [Test]
        public void Stack_child_with_both_axes_written_does_not_set_SizeFromNative()
        {
            var s = Open("<HStack width='300' height='100'><Icon id='i' name='ui:a' size='32x32'/></HStack>");
            Assert.IsFalse(FromNative(s, "i"));
        }

        private sealed class PassProbe : Control
        {
            public bool? InPassInSetter;
            public bool? InPassInAfter;

            [UIAttr, Preserve]
            public string Mark { set => InPassInSetter = InApplyPass; }

            internal override void OnAfterApply() => InPassInAfter = InApplyPass;
        }

        [Test]
        public void InApplyPass_is_true_only_inside_own_apply()
        {
            UI.Registry.Register<PassProbe>("PassProbe", null);
            var s = Open("<PassProbe id='p' mark='x'/>");
            var probe = (PassProbe)s.Get("p");

            Assert.AreEqual(true, probe.InPassInSetter);
            Assert.AreEqual(true, probe.InPassInAfter);
            Assert.IsFalse(probe.InApplyPass, "false again once the pass is over");
        }
    }
}
