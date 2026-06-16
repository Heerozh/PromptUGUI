using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Born-frame instant rule: a state change in the SAME frame a control is built (before its first
    // rendered frame) is applied instantly, not faded — mirroring uGUI's instant-at-OnEnable for
    // serialized initial state, so a modal Configure hook disabling/selecting a control shows the
    // final visual on frame 1 instead of flashing the enabled look then fading into it.
    //
    // EditMode's Time.frameCount does not advance within a synchronous test, so every transition here
    // is "born frame"; the complementary later-frame-fades half lives in StateBornFramePlayTests.
    public class StateBornFrameTests
    {
        private const int Pressed = 2;   // UnityEngine.UI.Selectable.SelectionState.Pressed ordinal

        // #808080 as float colour (matches BtnStateTests / Color32 -> Color rounding).
        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // TestForceInstant must be FALSE: the ONLY thing that may make these transitions instant
            // is the born-frame gate under test (not the test-only force-instant escape hatch).
            StateTintReactor.TestForceInstant = false;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        [Test]
        public void ReactorStateChange_InBornFrame_SnapsToTarget_NotLeftMidFade()
        {
            // pressedModulate => a StateTintReactor (NOT uGUI ColorTint) drives the bg. The Pressed
            // target (base * #808080) is opaque<->opaque, i.e. fade-eligible: pre-fix this starts a
            // LitMotion tween that does not advance in EditMode, leaving bg at its base colour.
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' pressedModulate='#808080'><Image id='img'/></Btn>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var bg = btn.GameObject.GetComponent<UnityImage>();

            var bgBase = bg.color;                 // authored base, captured synchronously at build
            var expected = bgBase * Half;          // Pressed = base * #808080

            // Drive Pressed in the SAME frame the control was built — must SNAP, not fade.
            puiBtn.SimulateState(Pressed);

            Assert.That(bg.color.g, Is.EqualTo(expected.g).Within(0.001f),
                "born-frame Pressed modulate must snap to base*#808080 (g), not stay at the base mid-fade");
            Assert.That(bg.color.r, Is.EqualTo(expected.r).Within(0.001f),
                "born-frame Pressed modulate must snap (r)");
            Assert.That(bg.color.b, Is.EqualTo(expected.b).Within(0.001f),
                "born-frame Pressed modulate must snap (b)");
        }
    }
}
