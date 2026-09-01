using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>*Modulate</c> fan-out onto a PROCEDURAL descendant. The reactor installed on a child
    /// <see cref="Frame"/>'s <see cref="ProceduralPanel"/> owns only the vertex multiplier
    /// (<c>Graphic.color</c>); the panel's authored fill lives in its material and belongs to the
    /// Frame. Regression: the descendant reactor wrote its fallback Peek (<c>Graphic.color</c> =
    /// white) into the panel fill on install and on every state change, so a red accent bar and a
    /// hollow border-only Frame inside a <c>&lt;Btn pressedModulate&gt;</c> both rendered as opaque
    /// white slabs.
    /// </summary>
    public class StateFanOutPanelTests
    {
        private const int Normal = 0;
        private const int Pressed = 2;

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        private static Btn Load(string btnAttrs, string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {btnAttrs}>{body}</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Btn>("b");
        }

        private static ProceduralPanel PanelOf(IControl c)
            => ((Control)c).GameObject.GetComponent<ProceduralPanel>();

        private static Color FillTop(ProceduralPanel panel)
        {
            panel.FlushParams();
            return panel.materialForRendering.GetColor("_FillTop");
        }

        [Test]
        public void DescendantFrameFill_SurvivesInstall()
        {
            var btn = Load("pressedModulate='#808080'", "<Frame id='f' color='#ff0000' radius='4'/>");
            var fill = FillTop(PanelOf(btn.Get<Frame>("f")));
            Assert.That(fill.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(fill.g, Is.EqualTo(0f).Within(0.001f),
                "the child's authored red was overwritten by the reactor's white fallback base");
            Assert.That(fill.a, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void DescendantFrameFill_SurvivesStateChange_OnlyMultiplierMoves()
        {
            var btn = Load("pressedModulate='#808080'", "<Frame id='f' color='#ff0000' radius='4'/>");
            var panel = PanelOf(btn.Get<Frame>("f"));
            var pb = btn.GameObject.GetComponent<PuiButton>();

            pb.SimulateState(Pressed);
            Assert.That(FillTop(panel).g, Is.EqualTo(0f).Within(0.001f), "pressed: fill stays red");
            Assert.That(panel.color.r, Is.EqualTo(0.5019608f).Within(0.001f),
                "the modulate lands on the vertex colour, which the shader multiplies over the fill");

            pb.SimulateState(Normal);
            Assert.That(FillTop(panel).g, Is.EqualTo(0f).Within(0.001f), "normal: fill still red");
            Assert.That(panel.color.r, Is.EqualTo(1f).Within(0.001f), "multiplier back to identity");
        }

        [Test]
        public void HollowDescendantFrame_StaysHollow()
        {
            // A border-only Frame (no color=) is a hollow stroke: fill is Color.clear, and the
            // fan-out reactor must not fill it white.
            var btn = Load("pressedModulate='#808080'",
                "<Frame id='f' radius='4' borderWidth='1' borderColor='#00ffff'/>");
            Assert.That(FillTop(PanelOf(btn.Get<Frame>("f"))).a, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void DescendantFrame_ReSolvedColor_IsNotClobberedByStaleBase()
        {
            // The Frame's color= is re-applied on a ReSolve (Variant / theme); the descendant reactor
            // must never write a base it captured earlier back over it.
            var btn = Load("pressedModulate='#808080'",
                "<Frame id='f' color='#ff0000' color.alt='#0000ff' radius='4'/>");
            var panel = PanelOf(btn.Get<Frame>("f"));
            UI.Variants.Set("alt", true);
            var fill = FillTop(panel);
            Assert.That(fill.b, Is.EqualTo(1f).Within(0.001f), "variant colour applied");
            Assert.That(fill.r, Is.EqualTo(0f).Within(0.001f), "the pre-variant red must not come back");
        }

        [Test]
        public void Peek_OnProceduralPanel_ReadsTheFill_NotGraphicColor()
        {
            // The reactor's fallback base for a graphic nobody authored a colour for is a Peek. On a
            // panel Graphic.color is the multiplier (white at rest); the base has to be the FILL, or
            // the target reactor paints white over whatever the surface painted.
            var go = new GameObject("panel");
            try
            {
                var panel = go.AddComponent<ProceduralPanel>();
                panel.SetFill(ColorSpec.Solid(Color.red));
                var peeked = ColorApplier.Peek(panel);
                Assert.That(panel.color, Is.EqualTo(Color.white), "multiplier untouched");
                Assert.That(peeked.Top.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(peeked.Top.g, Is.EqualTo(0f).Within(0.001f), "peeked the fill, not Graphic.color");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
