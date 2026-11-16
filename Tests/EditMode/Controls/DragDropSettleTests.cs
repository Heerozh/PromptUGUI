using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// What flies after a drop (spec 2026-11-16-drag-drop §7.6), edge autoscroll (§7.5) and a carousel holding still
    /// (P21). EditMode lands every flight at once, so the flights are read off <see cref="SettleFlight.FlownForTests"/>.
    /// </summary>
    public class DragDropSettleTests : DragDropTestBase
    {
        private readonly List<RectTransform> _flown = new();

        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
            _flown.Clear();
            SettleFlight.FlownForTests = rt => _flown.Add(rt);
        }

        private const string Slots =
            "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10'><Frame id='a' anchor='stretch' drag='unit'/></Frame>" +
            "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'><Frame id='b' anchor='stretch' drag='unit'/></Frame>" +
            "<Frame id='twin' anchor='top-left' size='100x100' margin='10,_,_,410' drop='unit'>" +
            "  <Frame id='t1' anchor='top-left' size='40x40' drag='unit'/><Frame id='t2' anchor='bottom-right' size='40x40' drag='unit'/>" +
            "</Frame>";

        [Test]
        public void The_one_card_inside_the_target_lands()
        {
            var s = Open(Slots);

            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));

            CollectionAssert.AreEqual(new[] { Host(s.Get<Frame>("b")) }, _flown, "b — which the push would have filled — flies in");
        }

        [Test]
        public void Two_cards_inside_the_target_land_nothing()
        {
            var s = Open(Slots);

            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("twin"));

            CollectionAssert.IsEmpty(_flown);
        }

        [Test]
        public void Land_names_what_flies_in_and_null_flies_nothing()
        {
            var s = Open(Slots);
            var t2 = s.Get<Frame>("t2");
            using (UI.Drag.OnDrop.Subscribe(e => e.Land(t2)))
                DragOnto(s.Get<Frame>("a"), s.Get<Frame>("twin"));
            CollectionAssert.AreEqual(new[] { Host(t2) }, _flown);

            _flown.Clear();
            using (UI.Drag.OnDrop.Subscribe(e => e.Land(null)))
                DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            CollectionAssert.IsEmpty(_flown);
        }

        [Test]
        public void Fly_adds_the_swap_flight_before_or_after_the_landing()
        {
            var s = Open(Slots);
            var a = s.Get<Frame>("a");
            using (UI.Drag.OnDrop.Subscribe(e => e.Fly(e.Source, from: e.Target)))
                DragOnto(a, s.Get<Frame>("slotB"));
            CollectionAssert.AreEquivalent(new[] { Host(s.Get<Frame>("b")), Host(a) }, _flown);

            _flown.Clear();
            DropEvent late = null;
            using (UI.Drag.OnDrop.Subscribe(e => late = e))
                DragOnto(a, s.Get<Frame>("slotB"));
            _flown.Clear();
            late.Fly(a, from: s.Get<Frame>("slotB"));
            CollectionAssert.AreEqual(new[] { Host(a) }, _flown, "after the landing: at once");
        }

        [Test]
        public void A_rejection_sends_the_source_home_with_no_receive()
        {
            var s = Open(Slots);
            var a = s.Get<Frame>("a");
            var receives = 0;
            using var rcv = TargetOf(s.Get<Frame>("slotB")).OnReceive.Subscribe(_ => receives++);
            using (UI.Drag.OnDrop.Subscribe(e => e.Reject()))
                DragOnto(a, s.Get<Frame>("slotB"));

            CollectionAssert.AreEqual(new[] { Host(a) }, _flown);
            Assert.AreEqual(0, receives);
        }

        [Test]
        public void Releasing_over_nothing_flies_the_source_home()
        {
            var s = Open(Slots);
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            Move(src, e, e.position + new Vector2(0f, -300f));
            Tick();
            End(src, e);

            CollectionAssert.AreEqual(new[] { Host(a) }, _flown);
            Assert.IsNull(LiftCanvasOf(a));
        }

        [Test]
        public void Near_the_edge_of_a_list_holding_targets_the_list_scrolls()
        {
            var s = Open(
                "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10'><Frame id='a' anchor='stretch' drag='unit'/></Frame>" +
                "<ScrollList id='list' anchor='top-left' size='200x300' margin='10,_,_,200' itemTemplate='Row'/>",
                "<Template name='Row'><Frame height='60' drop='unit'/></Template>");
            var list = s.Get<ScrollList>("list");
            list.BindItems(Observable.Return<IReadOnlyList<int>>(Enumerable.Range(0, 12).ToArray()), (IControl _, int __) => { });
            Canvas.ForceUpdateCanvases();
            var viewport = list.ViewportRect;
            var content = list.ContentRect;
            var before = content.anchoredPosition.y;

            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            var nearBottom = RectTransformUtility.WorldToScreenPoint(null,
                viewport.TransformPoint(new Vector3(viewport.rect.center.x, viewport.rect.yMin + 5f, 0f)));
            Move(src, e, nearBottom);
            Tick(0.1f);
            Tick(0.1f);

            Assert.Greater(content.anchoredPosition.y, before, "scrolled towards the end");
        }

        [Test]
        public void A_carousel_holds_still_while_its_card_content_is_dragged()
        {
            var s = Open(
                "<Carousel id='car' anchor='top-left' size='200x120'>" +
                "  <Frame><Frame id='a' anchor='stretch' margin='10' drag='unit'/></Frame>" +
                "  <Frame/>" +
                "</Carousel>");
            var view = s.Get<Carousel>("car").GameObject.GetComponent<CarouselView>();
            var a = s.Get<Frame>("a");

            var e = Lift(a);
            Assert.IsTrue(view.HeldByDrag);

            End(SourceOf(a), e);
            Assert.IsFalse(view.HeldByDrag);
        }
    }
}
