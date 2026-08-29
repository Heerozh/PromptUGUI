using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // The callback path of ClampFitter: a parent resize that goes through no ReSolve at all
    // (direct sizeDelta write) must still re-clamp the child on the next canvas update(s).
    // EditMode can't reach this — it needs OnRectTransformDimensionsChange → dirty → rebuild.
    public class ClampFitterPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static float Left(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMin.x + child.anchorMin.x * parent.rect.width;
        }

        [UnityTest]
        public IEnumerator Parent_resize_without_ReSolve_reclamps_via_callback()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100'/>" +
                "</Frame>" + Footer);
            var screen = UI.Open("S");
            yield return null;
            var box = screen.Get<Frame>("box").RectTransform;
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(167f, rt.rect.width, 0.01f, "open: 46.4% of 300 floors at 167");

            box.sizeDelta = new Vector2(800f, 200f);
            yield return null;
            yield return null;   // delayed-dirty path may need the extra frame
            Assert.AreEqual(250f, rt.rect.width, 0.01f, "grown: capped at 250");
            Assert.AreEqual(0f, Left(rt), 0.01f, "still hugging the left edge");

            box.sizeDelta = new Vector2(500f, 200f);
            yield return null;
            yield return null;
            Assert.AreEqual(232f, rt.rect.width, 0.01f, "in range: 46.4% of 500");
        }

        [UnityTest]
        public IEnumerator Nested_clamps_follow_a_parent_resize()
        {
            // The inner node's callback fires while the outer node is being rebuilt (inside the
            // layout loop) — that is the delayed-dirty branch; it must converge within a frame or two.
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='outer' anchor='top-left' width='clamp(100, 50%, 400)' height='100'>" +
                "<Frame id='inner' anchor='top-left' width='clamp(50, 50%, 120)' height='50'/>" +
                "</Frame></Frame>" + Footer);
            var screen = UI.Open("S");
            yield return null;
            var box = screen.Get<Frame>("box").RectTransform;
            var outer = screen.Get<Frame>("outer").RectTransform;
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(150f, outer.rect.width, 0.01f);
            Assert.AreEqual(75f, inner.rect.width, 0.01f);

            box.sizeDelta = new Vector2(1000f, 200f);
            yield return null;
            yield return null;
            yield return null;
            Assert.AreEqual(400f, outer.rect.width, 0.01f);
            Assert.AreEqual(120f, inner.rect.width, 0.01f);
        }
    }
}
