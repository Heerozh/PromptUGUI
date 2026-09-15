using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Image = PromptUGUI.Controls.Image;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// The 2026-09-15 raycastTarget contract under a real Canvas + GraphicRaycaster + EventSystem:
    /// what the raycaster actually returns for a point in the middle of each node. EditMode can only
    /// read <c>Graphic.raycastTarget</c>; the raycaster also skips a Graphic whose CanvasRenderer has
    /// no depth, which is exactly the question a zero-geometry catcher raises (spec §4.1).
    /// </summary>
    public class RaycastHitPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static EventSystem EnsureES() =>
            EventSystem.current ?? new GameObject("ES").AddComponent<EventSystem>();

        private static Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        /// <summary>A real raycast at the centre of the screen — where an anchor="center" node sits.</summary>
        private static List<RaycastResult> RaycastCentre(out PointerEventData data)
        {
            Canvas.ForceUpdateCanvases();
            data = new PointerEventData(EnsureES())
            {
                position = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f),
            };
            var hits = new List<RaycastResult>();
            EnsureES().RaycastAll(data, hits);
            return hits;
        }

        private static bool Hit(List<RaycastResult> hits, GameObject go) => hits.Any(h => h.gameObject == go);

        /// <summary>
        /// THE decisive case for spec §4.1: a catcher Frame emits no geometry at all. If an empty
        /// CanvasRenderer were left at depth -1 the raycaster would skip it, and the catcher would
        /// have to emit a degenerate triangle instead.
        /// </summary>
        [UnityTest]
        public IEnumerator Bare_catcher_frame_is_hit_by_the_raycaster()
        {
            var s = Open("<Frame id='f' anchor='center' size='200x100' raycastTarget='true'/>");
            var f = s.Get<Frame>("f");
            yield return null;

            var hits = RaycastCentre(out _);

            Assert.IsTrue(Hit(hits, f.GameObject),
                "a zero-geometry catcher must still be returned by GraphicRaycaster");
        }

        [UnityTest]
        public IEnumerator Painted_frame_without_raycastTarget_is_not_hit()
        {
            var s = Open("<Frame id='f' anchor='center' size='200x100' color='#ffffff' radius='8'/>");
            var f = s.Get<Frame>("f");
            yield return null;

            var hits = RaycastCentre(out _);

            Assert.IsFalse(Hit(hits, f.GameObject), "drawing is not declaring (spec §3)");
        }

        [UnityTest]
        public IEnumerator Painted_frame_with_raycastTarget_is_hit()
        {
            var s = Open("<Frame id='f' anchor='center' size='200x100' color='#ffffff' raycastTarget='true'/>");
            var f = s.Get<Frame>("f");
            yield return null;

            Assert.IsTrue(Hit(RaycastCentre(out _), f.GameObject));
        }

        /// <summary>
        /// The retired Image is disabled, so the only thing under the pointer is the surface — and
        /// the click still reaches the Button because the surface is a child of it.
        /// </summary>
        [UnityTest]
        public IEnumerator Procedural_Btn_is_hit_on_its_surface_and_the_click_reaches_the_Button()
        {
            var s = Open("<Btn id='b' anchor='center' size='200x100' radius='8'>OK</Btn>");
            var b = s.Get<Btn>("b");
            var clicks = 0;
            using var click = b.OnClick.Subscribe(__ => clicks++);
            yield return null;

            var hits = RaycastCentre(out var data);

            Assert.IsTrue(hits.Count > 0, "something under the pointer");
            var top = hits[0].gameObject;
            Assert.AreEqual(ProceduralSurface.NodeName, top.name,
                "the surface is the hit layer now, not an alpha-0 Image");
            Assert.IsFalse(Hit(hits, b.GameObject), "the retired Image must not be in the raycast list");

            data.button = PointerEventData.InputButton.Left;
            ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerClickHandler);
            Assert.AreEqual(1, clicks, "the click bubbles from the surface to the Button");
        }

        [UnityTest]
        public IEnumerator Image_is_not_hit_by_default_but_a_pointer_subscription_makes_it_hit()
        {
            var s = Open("<Image id='i' anchor='center' size='200x100'/>");
            var i = s.Get<Image>("i");
            yield return null;
            Assume.That(Hit(RaycastCentre(out _), i.GameObject), Is.False, "guard: click-through by default");

            using var sub = i.OnPointerDown.Subscribe(__ => { });
            yield return null;

            Assert.IsTrue(Hit(RaycastCentre(out _), i.GameObject),
                "a subscriber is the intent; the raycaster has to be able to reach it");
        }

        [UnityTest]
        public IEnumerator Text_is_not_hit()
        {
            var s = Open("<Text id='t' anchor='center' size='200x100' align='center'>hello</Text>");
            var t = s.Get<Text>("t");
            yield return null;

            Assert.IsFalse(Hit(RaycastCentre(out _), t.GameObject),
                "TMP's own default is true; the library's is false");
        }
    }
}
