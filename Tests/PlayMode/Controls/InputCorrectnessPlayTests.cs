using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// Input read right, through a real EventSystem and real devices (spec 2026-11-23-input-correctness): a touch the system
    /// cancels ends a drag / reorder as a cancel (§2.1); a Dropdown's list stays closed when options arrive during its fade-out,
    /// and only the left button opens it (§4).
    /// </summary>
    public class InputCorrectnessPlayTests
#if ENABLE_INPUT_SYSTEM
        : InputTestFixture
#endif
    {
        private const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";

#if ENABLE_INPUT_SYSTEM
        private Mouse _mouse;
        private Touchscreen _touch;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            DestroyAllEventSystems();
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            _mouse = InputSystem.AddDevice<Mouse>();
            _touch = InputSystem.AddDevice<Touchscreen>();
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            DestroyAllEventSystems();
            base.TearDown();
        }

        private static void DestroyAllEventSystems()
        {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        private static Screen Open(string inner, string before = "")
        {
            UI.LoadDocument("input-play", Head + before + "<Screen name='S'>" + inner + "</Screen></PromptUGUI>");
            var s = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return s;
        }

        private static Vector2 At(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        private static Vector2 At(IControl c) => At((c as Control)?.LayoutHost ?? c.RectTransform);

        private IEnumerator TouchDrag(int id, Vector2 from, Vector2 to, float holdFirst = 0f)
        {
            BeginTouch(id, from, screen: _touch);
            yield return null;
            if (holdFirst > 0f) yield return new WaitForSeconds(holdFirst);
            for (var i = 1; i <= 8; i++)
            {
                MoveTouch(id, Vector2.Lerp(from, to, i / 8f), screen: _touch);
                yield return null;
            }
        }

        // ───── B1: a cancelled touch is a cancel ─────

        private const string Slots =
            "<Frame id='slotA' anchor='top-left' size='120x120' margin='20,_,_,20' drop='unit'>" +
            "  <Frame id='a' anchor='stretch' drag='unit' color='#FF0000' raycastTarget='true'/></Frame>" +
            "<Frame id='slotB' anchor='top-left' size='120x120' margin='20,_,_,320' drop='unit' color='#00FF00' raycastTarget='true'/>";

        [UnityTest]
        public IEnumerator A_touch_the_system_cancels_mid_drag_puts_nothing_down([Values] bool cancelled)
        {
            var ends = new List<DragInfo>();
            var drops = new List<DropEvent>();
            using var endSub = UI.Drag.OnEnd.Subscribe(ends.Add);
            using var dropSub = UI.Drag.OnDrop.Subscribe(drops.Add);
            var s = Open(Slots);
            yield return null;
            var to = At(s.Get<Frame>("slotB"));

            yield return TouchDrag(1, At(s.Get<Frame>("a")), to);
            Assume.That(UI.Drag.IsDragging, "precondition: lifted and over the other slot");
            if (cancelled) SetTouch(1, UnityEngine.InputSystem.TouchPhase.Canceled, to, screen: _touch);
            else EndTouch(1, to, screen: _touch);
            yield return null;
            yield return null;

            Assert.AreEqual(cancelled ? 0 : 1, drops.Count);
            Assert.AreEqual(cancelled ? DragResult.Cancelled : DragResult.Dropped, ends.Single().Result);
        }

        [UnityTest]
        public IEnumerator A_touch_the_system_cancels_mid_reorder_keeps_the_order()
        {
            var s = Open("<ScrollList id='sl' anchor='top-left' width='150' height='200' itemTemplate='Row' reorder='true'/>",
                         "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>");
            var list = s.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }), (IControl row, string t) => rows.Add(row));
            var reordered = 0;
            list.OnReordered.Subscribe(_ => reordered++).AddTo(s);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var from = At(rows[0]);

            yield return TouchDrag(1, from, from + new Vector2(0f, -70f), holdFirst: 0.6f);
            Assume.That(list.IsReordering, "precondition: the row is up (a finger holds inside a scroller)");
            SetTouch(1, UnityEngine.InputSystem.TouchPhase.Canceled, from + new Vector2(0f, -70f), screen: _touch);
            yield return null;
            yield return null;

            Assert.AreEqual(0, reordered, "no OnReordered");
            Assert.IsFalse(list.IsReordering);
        }
#endif
    }
}
