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

        /// <summary>
        /// A Slider is hit on its track (the Background layer: an Image in sprite mode, its
        /// surface in procedural mode) and a press there is uGUI's "jump to the pointer".
        /// </summary>
        [UnityTest]
        public IEnumerator Sprite_Slider_track_is_hit_and_a_press_moves_the_value()
            => SliderIsHit("");

        [UnityTest]
        public IEnumerator Procedural_Slider_track_is_hit_and_a_press_moves_the_value()
            => SliderIsHit("radius='4' handleRadius='pill'");

        /// <summary>
        /// The whole rect is the hit area, not just the groove: the track spans the middle 50% of
        /// the height (a 7-unit band on a 14-unit slider — ungrabbable on a phone), so the root
        /// carries a zero-geometry catcher. A press above the groove must still reach the Slider
        /// and jump the value to the pointer.
        /// </summary>
        [UnityTest]
        public IEnumerator Sprite_Slider_is_hit_above_its_track()
            => SliderIsHitOffTrack("");

        [UnityTest]
        public IEnumerator Procedural_Slider_is_hit_above_its_track()
            => SliderIsHitOffTrack("sprite='none' radius='pill' handleRadius='pill'");

        private static IEnumerator SliderIsHitOffTrack(string attrs)
        {
            var s = Open($"<Slider id='s' anchor='center' width='200' height='40' min='0' max='1' value='0' {attrs}/>");
            var sl = s.Get<Slider>("s");
            var uSlider = sl.GameObject.GetComponent<UnityEngine.UI.Slider>();
            yield return null;

            // 30% along, and 18 units above the centre line: outside the 0.25–0.75 track band (±10).
            Canvas.ForceUpdateCanvases();
            var data = new PointerEventData(EnsureES())
            {
                position = new Vector2(UnityEngine.Screen.width * 0.5f - 100f + 60f, UnityEngine.Screen.height * 0.5f + 18f),
                button = PointerEventData.InputButton.Left,
            };
            var hits = new List<RaycastResult>();
            EnsureES().RaycastAll(data, hits);
            Assert.IsTrue(hits.Count > 0, "the slider's rect must be under the pointer");
            Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(sl.GameObject.transform),
                $"top hit '{hits[0].gameObject.name}' is not part of the Slider");
            Assert.AreSame(sl.GameObject, ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject),
                "the press must route to the Slider itself");

            data.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerDownHandler);
            Assert.AreEqual(0.3f, uSlider.value, 0.05f, "a press off the groove still jumps the value there");
        }

        private static IEnumerator SliderIsHit(string attrs)
        {
            var s = Open($"<Slider id='s' anchor='center' width='200' height='40' min='0' max='1' value='0' {attrs}/>");
            var sl = s.Get<Slider>("s");
            var uSlider = sl.GameObject.GetComponent<UnityEngine.UI.Slider>();
            yield return null;

            var hits = RaycastCentre(out var data);
            Assert.IsTrue(hits.Count > 0, "the track must be under the pointer");
            Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(sl.GameObject.transform),
                $"top hit '{hits[0].gameObject.name}' is not part of the Slider");

            // Press 30% along the track (the slider is centred: x from -100 to +100 around the centre).
            data.position = new Vector2(UnityEngine.Screen.width * 0.5f - 100f + 60f, UnityEngine.Screen.height * 0.5f);
            data.button = PointerEventData.InputButton.Left;
            data.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, data, ExecuteEvents.pointerDownHandler);

            Assert.AreEqual(0.3f, uSlider.value, 0.05f, "a press on the track jumps the value there");
        }

        /// <summary>
        /// The hit role handed to a ProceduralPanel must survive a DEFERRED Awake. A node built under
        /// an inactive parent (a ScrollList row bound while its page is hidden, the way a tab page's
        /// list is filled before the tab is selected) only gets its Awake when the parent is shown —
        /// after Frame.RaycastTarget / ProceduralSurface.Retire already handed it
        /// <c>raycastTarget = true</c>. Awake's "click-through by default" used to clobber that,
        /// leaving a procedural Slider / Btn / catcher with no hit area at all (ssw_re_client 岗位页
        /// 的滑块, 2026-09-16). Instantiating under a hidden Frame is not enough to reproduce it —
        /// the children are built (and Awake'd) before the parent's own hidden= is applied.
        /// </summary>
        [UnityTest]
        public IEnumerator Procedural_Slider_bound_into_a_hidden_list_is_hit_once_shown()
            => BoundUnderHiddenPageIsHit(
                "<Template name='Row'><Frame width='stretch' height='40'>" +
                "<Slider id='s' anchor='center' width='200' height='40' min='0' max='1' value='0' " +
                "sprite='none' radius='pill' handleRadius='pill'/></Frame></Template>",
                row => row.Get<Slider>("s").GameObject);

        [UnityTest]
        public IEnumerator Catcher_frame_bound_into_a_hidden_list_is_hit_once_shown()
            => BoundUnderHiddenPageIsHit(
                "<Template name='Row'><Frame width='stretch' height='40'>" +
                "<Frame id='f' anchor='center' size='200x40' raycastTarget='true'/></Frame></Template>",
                row => row.Get<Frame>("f").GameObject);

        private static IEnumerator BoundUnderHiddenPageIsHit(string template,
            System.Func<IControl, GameObject> pick)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + template +
                "<Screen name='S'><Frame id='page' anchor='stretch'>" +
                // 40 high, one row: the row (and the node inside it) lands on the screen centre
                "<ScrollList id='list' anchor='center' size='300x40' itemTemplate='Row' sprite='none' color='#0000'/>" +
                "</Frame></Screen></PromptUGUI>");
            var s = UI.Open("S");
            var page = s.Get<Frame>("page");
            page.Hidden = true; // the page is off BEFORE the rows exist
            yield return null;

            GameObject target = null;
            s.Get<ScrollList>("list").BindItems(
                Observable.Return<IReadOnlyList<int>>(new[] { 1 }),
                (IControl row, int _) => target = pick(row));
            yield return null;
            Assert.IsNotNull(target, "the row was bound");
            Assert.IsFalse(target.activeInHierarchy, "precondition: the row was built under an inactive page");

            page.Hidden = false; // deferred Awakes run here
            yield return null;
            yield return null;

            var hits = RaycastCentre(out _);
            Assert.IsTrue(hits.Count > 0 && (hits[0].gameObject == target || hits[0].gameObject.transform.IsChildOf(target.transform)),
                $"top hit is '{(hits.Count > 0 ? hits[0].gameObject.name : "nothing")}' — the handed-over hit role must survive the deferred Awake");
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
