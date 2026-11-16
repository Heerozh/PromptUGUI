using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// Drag and drop with the real machinery (spec 2026-11-16-drag-drop §11): the hover is an EventSystem raycast
    /// through every Screen's GraphicRaycaster, the session runs from its hidden runner, the lift is a live nested
    /// canvas, and the landing is a LitMotion flight that has to end with nothing left behind.
    /// </summary>
    public class DragDropPlayTests
    {
        private readonly List<DragInfo> _ends = new();
        private readonly List<DropEvent> _drops = new();
        private readonly List<System.IDisposable> _subs = new();
        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // The hover is a real raycast: it needs an EventSystem, not an input module (the pointer is fed by hand).
            // Borrow the one a fixture before us left, or bring a bare one and remove exactly that one — never someone
            // else's: every InputSystemUIInputModule shares the Input System's static default UI actions, and
            // disabling any module destroys that asset under the others.
            if (EventSystem.current == null) _ownEs = new GameObject("EventSystem", typeof(EventSystem));
            _ends.Clear();
            _drops.Clear();
            _subs.Add(UI.Drag.OnEnd.Subscribe(_ends.Add));
            _subs.Add(UI.Drag.OnDrop.Subscribe(_drops.Add));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            UI.ResetForTests();
            if (_ownEs != null) Object.DestroyImmediate(_ownEs);
            _ownEs = null;
        }

        private static PromptUGUI.Application.Screen Open(string name, string body, int sortingOrder = 0)
        {
            UI.LoadDocument("doc-" + name,
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='{name}'>{body}</Screen></PromptUGUI>");
            var screen = UI.Open(name);
            screen.RootGameObject.GetComponent<Canvas>().sortingOrder = sortingOrder;
            return screen;
        }

        private static RectTransform Host(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        private static Vector2 ScreenOf(IControl c)
        {
            var rt = Host(c);
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        private static DragSource SourceOf(IControl c) => c.GameObject.GetComponent<DragSource>();

        private static PointerEventData Lift(IControl c)
        {
            var src = SourceOf(c);
            var p = ScreenOf(c);
            var e = new PointerEventData(EventSystem.current)
            {
                position = p,
                pressPosition = p,
                button = PointerEventData.InputButton.Left,
                pointerId = -1,
                pointerDrag = src.gameObject,
                eligibleForClick = true,
            };
            ((IInitializePotentialDragHandler)src).OnInitializePotentialDrag(e);
            ((IBeginDragHandler)src).OnBeginDrag(e);
            return e;
        }

        private static void MoveTo(IControl source, PointerEventData e, Vector2 screen)
        {
            e.delta = screen - e.position;
            e.position = screen;
            ((IDragHandler)SourceOf(source)).OnDrag(e);
        }

        private static void Release(IControl source, PointerEventData e) => ((IEndDragHandler)SourceOf(source)).OnEndDrag(e);

        [UnityTest]
        public IEnumerator A_drop_from_a_page_into_a_modal_goes_through_real_raycasts()
        {
            var page = Open("Page",
                "<Frame id='cell' anchor='top-left' size='100x100' margin='20,_,_,20'><Frame id='item' anchor='stretch' color='#888' drag='item'/></Frame>");
            var modal = Open("Modal",
                "<Frame id='slot' anchor='top-left' size='100x100' margin='20,_,_,300' drop='item'/>", sortingOrder: 1000);
            yield return null;

            var item = page.Get<Frame>("item");
            var e = Lift(item);
            yield return null;
            MoveTo(item, e, ScreenOf(modal.Get<Frame>("slot")));
            yield return null;

            Assert.IsTrue(modal.Get<Frame>("slot").GameObject.GetComponent<DropTargetMarker>().IsOver,
                "the click-through slot is hit while armed");
            Release(item, e);

            Assert.AreEqual(1, _drops.Count);
            Assert.AreSame(page, _drops[0].SourceScreen);
            Assert.AreSame(modal, _drops[0].TargetScreen);
        }

        [UnityTest]
        public IEnumerator A_backdrop_keeps_the_page_targets_out_of_reach()
        {
            var page = Open("Page", "<Frame id='slot' anchor='top-left' size='100x100' margin='20,_,_,300' drop='item'/>");
            var modal = Open("Modal",
                "<Frame anchor='stretch' raycastTarget='true'/>" +
                "<Frame id='cell' anchor='top-left' size='100x100' margin='20,_,_,20'><Frame id='item' anchor='stretch' color='#888' drag='item'/></Frame>",
                sortingOrder: 1000);
            yield return null;

            var item = modal.Get<Frame>("item");
            var e = Lift(item);
            yield return null;
            MoveTo(item, e, ScreenOf(page.Get<Frame>("slot")));
            yield return null;
            Release(item, e);

            Assert.IsEmpty(_drops, "a target under the modal backdrop cannot be dropped on");
            Assert.AreEqual(DragResult.Returned, _ends[0].Result);
        }

        [UnityTest]
        public IEnumerator Lifted_out_of_a_clipping_list_into_a_layer_of_its_own()
        {
            UI.LoadDocument("doc-L",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Template name='Row'><Frame height='60'><Frame id='card' anchor='stretch' radius='8' color='#888' drag='item'/></Frame></Template>" +
                "<Screen name='L'><ScrollList id='list' anchor='top-left' size='200x200' margin='20,_,_,20' mask='' itemTemplate='Row'/></Screen>" +
                "</PromptUGUI>");
            var screen = UI.Open("L");
            var list = screen.Get<ScrollList>("list");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<int>>(new[] { 0, 1, 2 }), (IControl r, int _) => rows.Add(r));
            yield return null;
            yield return null;

            var card = rows[0].Get<Frame>("card");
            var graphic = card.GameObject.GetComponent<Graphic>();
            Assert.IsTrue(graphic.canvasRenderer.hasRectClipping, "guard: inside the list it is clipped");

            var e = Lift(card);
            yield return null;
            yield return null;

            var layer = Host(card).GetComponent<Canvas>();
            Assert.IsTrue(layer != null && layer.enabled && layer.overrideSorting);
            Assert.AreEqual(UI.Drag.SortingOrder, layer.sortingOrder);
            Assert.AreSame(layer, graphic.canvas, "its graphics draw through the lift layer");
            Assert.IsFalse(graphic.canvasRenderer.hasRectClipping, "no longer cut by the list's viewport");
            Assert.AreNotEqual(AdditionalCanvasShaderChannels.None,
                layer.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1,
                "the procedural panel keeps its shader channel");

            Release(card, e);
        }

        [UnityTest]
        public IEnumerator The_flight_lands_with_nothing_left_behind()
        {
            var page = Open("Page",
                "<HStack id='row' anchor='top-left' height='80' spacing='10'>" +
                "  <Frame id='a' width='80' color='#888' drag='item' dragDuration='0.1s'/><Frame id='b' width='80' color='#888'/>" +
                "</HStack>" +
                "<Frame id='slot' anchor='top-left' size='100x100' margin='200,_,_,20' drop='item'/>");
            yield return null;
            var a = page.Get<Frame>("a");
            var home = Host(a).anchoredPosition;

            var e = Lift(a);
            yield return null;
            Assert.IsNotNull(Host(a).parent.Find(DragSession.PlaceholderName));
            MoveTo(a, e, ScreenOf(page.Get<Frame>("slot")));
            yield return null;
            Release(a, e);
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(1, _drops.Count);
            Assert.IsNull(Host(a).parent.Find(DragSession.PlaceholderName), "no placeholder left in the group");
            var layer = Host(a).GetComponent<Canvas>();
            Assert.IsTrue(layer == null || !layer.enabled, "the lift layer is off once landed");
            Assert.AreEqual(home, Host(a).anchoredPosition, "back in its slot");
            Assert.AreEqual(Vector3.one, Host(a).localScale, "the lift look eased back");
        }
    }
}
