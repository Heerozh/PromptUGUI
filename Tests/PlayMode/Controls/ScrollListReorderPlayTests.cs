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
    /// The animated half of <c>&lt;ScrollList reorder&gt;</c> (spec 2026-09-16 §5.4 / §5.5): in play mode
    /// the squeeze and the settle are LitMotion tweens laid over the committed structure. The
    /// displaced row and the dropped row both have to ARRIVE where the layout put them, the lift
    /// scale has to come back, and nothing of the session may be left in Content.
    /// </summary>
    public class ScrollListReorderPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
            + "<Template name='Row'><Frame height='30'><Image/></Frame></Template>"
            + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>";
        private const string Footer = "</Frame></Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static RectTransform Rt(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        private static Vector2 ScreenOf(RectTransform rt)
            => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        [UnityTest]
        public IEnumerator Squeeze_and_settle_tweens_arrive_at_the_layout_positions()
        {
            UI.LoadDocument("t", Header
                + "<ScrollList id='sl' width='150' height='200' itemTemplate='Row' reorder='true' reorderHold='0' reorderDuration='0.1s' spacing='10'/>"
                + Footer);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<int>>(new int[3]), (IControl slot, int _) => rows.Add(slot));
            yield return null;   // a layout pass

            var content = (RectTransform)list.GameObject.transform.Find("Viewport/Content");
            var d = content.GetComponent<ReorderDriver>();
            var row1 = Rt(rows[1]);
            var row2 = Rt(rows[2]);
            var slot1 = row1.anchoredPosition;
            var slot2 = row2.anchoredPosition;
            var stride = slot1.y - slot2.y;
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            var p0 = ScreenOf(row1);
            var e = new PointerEventData(EventSystem.current)
            {
                position = p0, pressPosition = p0, button = PointerEventData.InputButton.Left,
                pointerId = -1, pointerDrag = content.gameObject, eligibleForClick = true,
            };
            ((IInitializePotentialDragHandler)d).OnInitializePotentialDrag(e);
            ((IBeginDragHandler)d).OnBeginDrag(e);
            yield return null;
            Assert.Greater(row1.localScale.x, 1f, "the default lift look is tweening in");

            // Past row2's centre: row2 starts sliding up into slot 1.
            var w = content.TransformVector(new Vector3(0f, -(stride + 5f), 0f));
            e.position = p0 + new Vector2(w.x, w.y);
            ((IDragHandler)d).OnDrag(e);
            yield return null;
            Assert.AreNotEqual(slot1.y, row2.anchoredPosition.y, "mid-tween: row2 has not snapped, it is sliding");

            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(slot1.y, row2.anchoredPosition.y, 0.01f, "row2 arrived in slot 1");

            ((IEndDragHandler)d).OnEndDrag(e);
            // Structure is committed on release, before any settle frame.
            CollectionAssert.AreEqual(new[] { (1, 2) }, fired);
            Assert.AreEqual(2, row1.GetSiblingIndex());
            Assert.IsNull(content.Find(ReorderDriver.PlaceholderName));
            yield return null;
            Assert.AreNotEqual(slot2.y, row1.anchoredPosition.y, "mid-settle: the dropped row is still travelling");

            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(slot2.y, row1.anchoredPosition.y, 0.01f, "the dropped row settled into slot 2");
            Assert.AreEqual(1f, row1.localScale.x, 0.001f, "lift scale undone");
            Assert.IsFalse(row1.GetComponent<LayoutElement>().ignoreLayout);
            Assert.AreEqual(3, content.childCount, "nothing of the session left behind");
        }
    }
}
