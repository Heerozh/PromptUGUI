using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.UI;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>Arming, hovering, releasing and the event contract (spec 2026-11-16 §6.2, §7.3–§7.6, §7.8).</summary>
    public class DragDropSessionTests : DragDropTestBase
    {
        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
        }

        private static bool Hittable(IControl c)
        {
            var g = c.GameObject.GetComponent<Graphic>();
            return g != null && g.enabled && g.raycastTarget;
        }

        private const string Arena =
            "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10' drop='unit'>" +
            "  <Frame id='a' anchor='stretch' drag='unit'/>" +
            "</Frame>" +
            "<Frame id='yes' anchor='top-left' size='100x100' margin='10,_,_,150' drop='unit'/>" +
            "<Frame id='no' anchor='top-left' size='100x100' margin='10,_,_,290' drop='unit'/>" +
            "<Frame id='other' anchor='top-left' size='100x100' margin='150,_,_,10' drop='item'/>" +
            "<Frame id='outer' anchor='top-left' size='200x120' margin='150,_,_,150' drop='unit'>" +
            "  <Frame id='inner' anchor='stretch' margin='10' drop='item'/>" +
            "</Frame>";

        [Test]
        public void Lifting_arms_the_targets_that_would_take_it()
        {
            var s = Open(Arena);
            var asked = 0;
            s.Get<Frame>("no").CanDrop = _ => { asked++; return false; };

            Lift(s.Get<Frame>("a"));
            Tick();
            Tick();

            Assert.IsTrue(TargetOf(s.Get<Frame>("yes")).IsArmed);
            Assert.IsFalse(TargetOf(s.Get<Frame>("no")).IsArmed, "CanDrop said no");
            Assert.IsFalse(TargetOf(s.Get<Frame>("other")).IsArmed, "another kind");
            Assert.IsFalse(TargetOf(s.Get<Frame>("home")).IsArmed, "the source's own container never is");
            Assert.AreEqual(1, asked, "CanDrop is asked once per target per drag");
        }

        [Test]
        public void Armed_and_refusing_targets_are_hittable_only_during_the_drag()
        {
            var s = Open(Arena);
            s.Get<Frame>("no").CanDrop = _ => false;
            var a = s.Get<Frame>("a");
            var e = Lift(a);

            Assert.IsTrue(Hittable(s.Get<Frame>("yes")));
            Assert.IsTrue(Hittable(s.Get<Frame>("no")), "a refusing target must be hit, or its deny never shows");
            Assert.IsFalse(Hittable(s.Get<Frame>("other")));

            End(SourceOf(a), e);

            Assert.IsFalse(Hittable(s.Get<Frame>("yes")));
            Assert.IsFalse(Hittable(s.Get<Frame>("no")));
        }

        [Test]
        public void Hovering_marks_over_or_deny_and_a_foreign_kind_is_see_through()
        {
            var s = Open(Arena);
            s.Get<Frame>("no").CanDrop = _ => false;
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var hovers = new List<string>();
            using var sub = UI.Drag.OnHover.Subscribe(i => hovers.Add(i.Target?.Id));
            var e = Lift(a);

            Move(src, e, ScreenOf(s.Get<Frame>("yes")));
            Tick();
            Assert.IsTrue(TargetOf(s.Get<Frame>("yes")).IsOver);

            Move(src, e, ScreenOf(s.Get<Frame>("no")));
            Tick();
            Assert.IsFalse(TargetOf(s.Get<Frame>("yes")).IsOver);
            Assert.IsTrue(TargetOf(s.Get<Frame>("no")).IsDenied);

            Move(src, e, ScreenOf(s.Get<Frame>("inner")));
            Tick();
            Assert.IsTrue(TargetOf(s.Get<Frame>("outer")).IsOver, "the item-only inner target lets the unit through");

            CollectionAssert.AreEqual(new[] { "yes", "no", "outer" }, hovers);
        }

        [Test]
        public void Dropping_restores_the_structure_before_anyone_hears_of_it()
        {
            var s = Open("<HStack id='row' anchor='top-left' height='60' spacing='10' drop='nothing'>" +
                         "<Frame id='a' width='50' drag='unit'/><Frame id='b' width='50'/></HStack>" +
                         "<Frame id='yes' anchor='top-left' size='100x100' margin='100,_,_,10' drop='unit'/>");
            var a = s.Get<Frame>("a");
            var yes = s.Get<Frame>("yes");
            var order = new List<string>();
            var checkedInside = false;
            using var t = yes.OnDrop.Subscribe(_ =>
            {
                order.Add("target");
                Assert.IsNull(Placeholder(a), "placeholder gone");
                Assert.IsFalse(Host(a).GetComponent<LayoutElement>().ignoreLayout, "back in the flow");
                Assert.IsFalse(UI.Drag.IsDragging);
                checkedInside = true;
            });
            using var g = UI.Drag.OnDrop.Subscribe(_ => order.Add("global"));
            using var rec = new Recorder();

            DragOnto(a, yes);

            Assert.IsTrue(checkedInside);
            CollectionAssert.AreEqual(new[] { "target", "global" }, order);
            Assert.AreEqual(1, rec.Ends.Count);
            Assert.AreEqual(DragResult.Dropped, rec.Ends[0].Result);
            Assert.AreEqual("drop:yes", rec.Log[rec.Log.Count - 2], "the drop comes before the end");
        }

        [Test]
        public void The_event_says_what_went_where()
        {
            var s = Open(Arena);
            var a = s.Get<Frame>("a");
            var yes = s.Get<Frame>("yes");
            a.DragData = "unit-7";
            yes.DragData = 3;
            using var rec = new Recorder();

            DragOnto(a, yes);

            var d = rec.Drops.Single();
            Assert.AreEqual("unit", d.Kind);
            Assert.AreSame(a, d.Source);
            Assert.AreSame(yes, d.Target);
            Assert.AreEqual("unit-7", d.Data);
            Assert.AreEqual(3, d.TargetData);
            Assert.AreSame(s, d.SourceScreen);
            Assert.AreSame(s, d.TargetScreen);
            Assert.AreEqual(-1, d.SourceIndex, "not in a list");
            Assert.AreEqual(-1, d.TargetIndex);
        }

        [Test]
        public void Releasing_over_nothing_or_a_refusal_sends_it_home()
        {
            var s = Open(Arena);
            s.Get<Frame>("no").CanDrop = _ => false;
            var a = s.Get<Frame>("a");
            var home = Host(a).anchoredPosition;
            using var rec = new Recorder();

            DragOnto(a, s.Get<Frame>("no"));
            DragOnto(a, s.Get<Frame>("home"));

            Assert.IsEmpty(rec.Drops);
            Assert.AreEqual(2, rec.Ends.Count);
            Assert.IsTrue(rec.Ends.All(i => i.Result == DragResult.Returned));
            Assert.AreEqual(home, Host(a).anchoredPosition);
            Assert.IsNull(LiftCanvasOf(a), "lift layer taken away");
        }

        [Test]
        public void A_hold_lift_released_where_it_was_goes_home()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='200x300' itemTemplate='Row'/>" +
                         "<Frame id='yes' anchor='top-left' size='100x100' margin='10,_,_,300' drop='unit'/>",
                "<Template name='Row'><Frame height='60'><Frame id='card' anchor='stretch' drag='unit'/></Frame></Template>");
            var list = s.Get<ScrollList>("list");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<int>>(new[] { 0, 1, 2 }), (IControl r, int _) => rows.Add(r));
            var card = rows[0].Get<Frame>("card");
            var src = SourceOf(card);
            using var rec = new Recorder();
            var pressed = true;
            LiftGesture.PressedProbeForTests = _ => pressed;

            Press(src, ScreenOf(card), pointerId: 0);
            src.TickForTests(0.5f);
            Assert.IsTrue(UI.Drag.IsDragging);
            pressed = false;
            src.TickForTests(0.016f);

            Assert.IsFalse(UI.Drag.IsDragging);
            Assert.IsEmpty(rec.Drops);
            Assert.AreEqual(DragResult.Returned, rec.Ends.Single().Result);
        }

        [Test]
        public void List_rows_report_their_indices_and_a_synchronous_push_is_safe()
        {
            var s = Open("<ScrollList id='grid' anchor='top-left' size='330x110' columns='3' cellSize='100x100' spacing='10' itemTemplate='Slot'/>",
                "<Template name='Slot'><Frame drop='unit'><Frame id='card' anchor='stretch' drag='unit'><Text id='name'/></Frame></Frame></Template>");
            var grid = s.Get<ScrollList>("grid");
            var data = new ReactiveProperty<IReadOnlyList<string>>(new[] { "A", "B", "C" });
            var rows = new List<IControl>();
            grid.BindItems(data, (IControl row, string name) =>
            {
                if (!rows.Contains(row)) rows.Add(row);
                row.Get<Text>("name").TextValue = name;
            });
            Canvas.ForceUpdateCanvases();
            using var sub = UI.Drag.OnDrop.Subscribe(e =>
            {
                var next = data.Value.ToArray();
                (next[e.SourceIndex], next[e.TargetIndex]) = (next[e.TargetIndex], next[e.SourceIndex]);
                data.Value = next;   // synchronous push from inside the drop
            });
            using var rec = new Recorder();

            DragOnto(rows[0].Get<Frame>("card"), rows[2]);

            var d = rec.Drops.Single();
            Assert.AreEqual(0, d.SourceIndex);
            Assert.AreEqual(2, d.TargetIndex);
            Assert.AreEqual(DragResult.Dropped, rec.Ends.Single().Result, "the push did not cancel anything");
            Assert.AreEqual("C", rows[0].Get<Text>("name").TmpComponent.text);
            Assert.AreEqual("A", rows[2].Get<Text>("name").TmpComponent.text);
        }

        [Test]
        public void A_drop_can_cross_Screens()
        {
            var bag = Open("<Frame id='bagCell' anchor='top-left' size='100x100' margin='10,_,_,10'>" +
                           "<Frame id='item' anchor='stretch' drag='item'/></Frame>", name: "Bag");
            var equip = Open("<Frame id='slot' anchor='top-left' size='100x100' margin='10,_,_,300' drop='item'/>", name: "Equip");
            using var rec = new Recorder();

            DragOnto(bag.Get<Frame>("item"), equip.Get<Frame>("slot"));

            var d = rec.Drops.Single();
            Assert.AreSame(bag, d.SourceScreen);
            Assert.AreSame(equip, d.TargetScreen);
        }
    }
}
