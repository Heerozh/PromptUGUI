using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Animation reveal&gt;</c>'s structure and resting state (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §2.4.1–§2.4.4 / §2.4.7). The motion itself needs a
    /// running player loop and lives in the PlayMode suite.
    /// </summary>
    public class AnimationRevealTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            var s = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return s;
        }

        // Three 44-high rows inside the revealed child → a content height of 140. The child hangs off
        // the Animation's proxy, which is free-positioning, so it carries a plain numeric width.
        private const string Rows =
            "<VStack width='200' spacing='4'><Btn height='44'/><Btn height='44'/><Btn height='44'/></VStack>";

        private static string InStack(string animAttrs) =>
            "<VStack id='outer' anchor='top-left' width='200' height='400' spacing='0'>" +
            $"<Animation id='a' {animAttrs}>{Rows}</Animation>" +
            "<Btn id='below' height='20'/></VStack>";

        private static LayoutElement Le(PromptUGUI.Application.Screen s)
            => s.Get<PromptUGUI.Controls.Animation>("a").LayoutHost.GetComponent<LayoutElement>();

        [Test]
        public void Rest_state_is_reveal_from_not_identity()
        {
            var s = Open(InStack("on='manual' reveal='y'"));

            Assert.AreEqual(0f, Le(s).preferredHeight, 0.01f, "reveal starts closed — that is what it means");
            Assert.AreEqual(0f, Le(s).minHeight, 0.01f, "rigid: the group hands over exactly the box");
            Assert.AreEqual(0f, Le(s).flexibleHeight, 0.01f);
        }

        [Test]
        public void Rest_state_honours_an_explicit_from()
        {
            var s = Open(InStack("on='manual' reveal='y' reveal-from='24'"));

            Assert.AreEqual(24f, Le(s).preferredHeight, 0.01f);
        }

        [Test]
        public void A_closed_reveal_clips()
        {
            var s = Open(InStack("on='manual' reveal='y'"));
            var mask = s.Get<PromptUGUI.Controls.Animation>("a").LayoutHost.GetComponent<RectMask2D>();

            Assert.IsNotNull(mask);
            Assert.IsTrue(mask.enabled, "the content overflows the closed box and must not spill");
        }

        [Test]
        public void A_reveal_that_starts_open_does_not_clip()
        {
            // from=hug, to=0 — a fold that starts open and closes on the event.
            var s = Open(InStack("on='manual' reveal='y' reveal-from='hug' reveal-to='0'"));
            var host = s.Get<PromptUGUI.Controls.Animation>("a").LayoutHost;
            var mask = host.GetComponent<RectMask2D>();

            Assert.AreEqual(140f, Le(s).preferredHeight, 0.01f, "hug measured the three rows");
            Assert.IsTrue(mask == null || !mask.enabled, "nothing is hidden at the open end — keep batching");
        }

        [Test]
        public void A_closed_reveal_leaves_no_room_for_its_siblings_to_move_into()
        {
            var s = Open(InStack("on='manual' reveal='y'"));
            var below = s.Get<Btn>("below").RectTransform;

            Assert.AreEqual(0f, Le(s).preferredHeight, 0.01f);
            var topEdge = below.anchoredPosition.y + below.rect.height * below.pivot.y;
            Assert.AreEqual(0f, topEdge, 0.01f,
                "the sibling sits where the closed fold leaves it — flush against the top of the stack");
        }

        [Test]
        public void Reveal_on_the_x_axis_owns_the_width()
        {
            var s = Open(
                "<HStack id='outer' anchor='top-left' width='400' height='60' spacing='0'>" +
                $"<Animation id='a' on='manual' reveal='x' reveal-from='hug' reveal-to='0'>{Rows}</Animation>" +
                "</HStack>");

            Assert.Greater(Le(s).preferredWidth, 0f);
            Assert.AreEqual(-1f, Le(s).preferredHeight, 0.01f, "the cross axis keeps its sentinel");
        }

        [Test]
        public void Free_positioning_reveal_writes_the_rect()
        {
            var s = Open(
                "<Frame id='box' anchor='top-left' width='400' height='300'>" +
                $"<Animation id='a' anchor='top-left' width='200' on='manual' reveal='y'>{Rows}</Animation>" +
                "</Frame>");
            var rt = s.Get<PromptUGUI.Controls.Animation>("a").RectTransform;

            Assert.AreEqual(0f, rt.rect.height, 0.01f);
            Assert.AreEqual(200f, rt.rect.width, 0.01f, "the other axis is the author's");
        }

        [Test]
        public void ReSolve_does_not_reopen_a_closed_reveal()
        {
            var s = Open(InStack("on='manual' reveal='y'"));
            Assume.That(Le(s).preferredHeight, Is.EqualTo(0f).Within(0.01f));

            UI.Variants.Set("unrelated", true);
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(0f, Le(s).preferredHeight, 0.01f,
                "ApplyCommon resets the geometry every pass; the reveal has to re-assert its box");
        }

        [Test]
        public void The_box_is_reported_as_the_native_size()
        {
            // The child carries an explicit size, so Trigger.GetNativeSize has something to report —
            // the reveal then replaces the animating axis with the box actually being shown.
            var s = Open(
                "<VStack id='outer' anchor='top-left' width='200' height='400' spacing='0'>" +
                "<Animation id='a' on='manual' reveal='y' reveal-from='24'>" +
                "<VStack width='200' height='140' spacing='4'><Btn height='44'/></VStack>" +
                "</Animation></VStack>");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");

            var native = anim.GetNativeSize();

            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(200f, native.Value.x, 0.01f, "the cross axis is the child's");
            Assert.AreEqual(24f, native.Value.y, 0.01f, "a parent must reserve what is shown, not the full content");
        }

        [Test]
        public void Measuring_hug_works_through_an_inactive_child()
        {
            // A TMP added under an inactive parent never runs Awake and would report 0 forever.
            var s = Open(InStack("on='manual' reveal='y' reveal-from='hug' reveal-to='0'"));
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");
            var child = (RectTransform)anim.ChildHostTransform.GetChild(0);
            child.gameObject.SetActive(false);

            var measured = anim.ResolveReveal(PromptUGUI.Controls.Internal.RevealValue.Hug);

            Assert.AreEqual(140f, measured, 0.01f);
            Assert.IsFalse(child.gameObject.activeSelf, "and it is switched back off again");
        }
    }
}
