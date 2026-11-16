using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Image = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>The five attributes and what they install (spec 2026-11-16-drag-drop §5.1, §7.3).</summary>
    public class DragDropAttributeTests : DragDropTestBase
    {
        [Test]
        public void Drag_installs_a_source_and_drop_installs_a_target()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var slotA = s.Get<Frame>("slotA");

            Assert.IsNotNull(SourceOf(a), "drag= puts a DragSource on the control");
            Assert.AreEqual("unit", a.DragKind);
            Assert.IsNotNull(TargetOf(slotA), "drop= puts a DropTargetMarker on the control");
            Assert.IsTrue(TargetOf(slotA).Accepts("unit"));
            Assert.IsFalse(TargetOf(slotA).Accepts("item"));
            Assert.IsNull(TargetOf(a), "a source is not a target unless it says so");
        }

        [Test]
        public void Drop_takes_a_list_separated_by_spaces_or_commas_and_a_wildcard()
        {
            var s = Open(
                "<Frame id='x' size='10x10' drop='unit item'/>" +
                "<Frame id='y' size='10x10' drop='unit,item'/>" +
                "<Frame id='z' size='10x10' drop='*'/>");

            foreach (var id in new[] { "x", "y" })
            {
                var m = TargetOf(s.Get<Frame>(id));
                Assert.IsTrue(m.Accepts("unit"), id);
                Assert.IsTrue(m.Accepts("item"), id);
                Assert.IsFalse(m.Accepts("ship"), id);
            }
            Assert.IsTrue(TargetOf(s.Get<Frame>("z")).Accepts("anything"));
        }

        [Test]
        public void None_turns_them_off()
        {
            var s = Open("<Frame id='x' size='10x10' drag='none' drop='none'/>");
            var x = s.Get<Frame>("x");

            Assert.IsNull(x.DragKind);
            var m = TargetOf(x);
            Assert.IsTrue(m == null || !m.Accepts("unit"));
        }

        [Test]
        public void A_variant_only_declaration_installs_the_source_up_front()
        {
            // Hooks inside the source resolve while the tree is still being applied (DFS post-order), so
            // the component must already be there whatever variant is active.
            var s = Open("<Frame id='x' size='10x10' drag='none' drag.compact='unit'/>");
            var x = s.Get<Frame>("x");

            Assert.IsNotNull(SourceOf(x));
            Assert.IsNull(x.DragKind, "the variant is not active: off");
        }

        [Test]
        public void A_template_invocation_carries_drag_and_drop_onto_the_instance_root()
        {
            var s = Open(
                "<Card id='c' drag='unit' dragHold='0.2s'/><Card id='t' drop='unit'/>",
                "<Template name='Card'><Frame size='50x50'/></Template>");

            var c = s.Get<Frame>("c");
            Assert.AreEqual("unit", c.DragKind);
            Assert.AreEqual(0.2f, c.DragHold, 1e-4f);
            Assert.IsTrue(TargetOf(s.Get<Frame>("t")).Accepts("unit"));
        }

        [Test]
        public void Bad_values_warn_and_keep_the_previous_one()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"PUI-DRAG-VALUE.*drag='true'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"PUI-DRAG-VALUE.*dragHold='soon'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"PUI-DRAG-VALUE.*dragDuration='slow'"));
            var s = Open("<Frame id='x' size='10x10' drag='true' dragHold='soon' dragDuration='slow'/>");
            var x = s.Get<Frame>("x");

            Assert.IsNull(x.DragKind);
            Assert.Less(x.DragHold, 0f, "auto kept");
            Assert.AreEqual(0.18f, x.DragDuration, 1e-4f, "default kept");
        }

        [Test]
        public void Hold_auto_duration_and_handle_parse()
        {
            var s = Open("<Frame id='x' size='10x10' drag='unit' dragHold='400ms' dragHandle='grip' dragDuration='0'>" +
                         "<Frame id='grip' size='5x5'/></Frame>");
            var x = s.Get<Frame>("x");

            Assert.AreEqual(0.4f, x.DragHold, 1e-4f);
            Assert.AreEqual("grip", x.DragHandle);
            Assert.AreEqual(0f, x.DragDuration);
        }

        [Test]
        public void DragKind_written_from_code_installs_the_source()
        {
            var s = Open("<Frame id='x' size='10x10'/>");
            var x = s.Get<Frame>("x");

            x.DragKind = "unit";

            Assert.IsNotNull(SourceOf(x));
            Assert.AreEqual("unit", x.DragKind);
        }
    }

    /// <summary>Hit testing: a source catches its press; a target is hit only while armed (spec §7.3).</summary>
    public class DragDropHitTests : DragDropTestBase
    {
        private static bool Hittable(IControl c)
        {
            var g = c.GameObject.GetComponent<Graphic>();
            return g != null && g.enabled && g.raycastTarget;
        }

        [Test]
        public void A_source_is_hittable_whatever_it_is_made_of()
        {
            var s = Open(
                "<Frame id='f' size='10x10' drag='unit'/>" +
                "<Image id='i' size='10x10' drag='unit'/>" +
                "<VStack id='v' size='10x10' drag='unit'/>");

            Assert.IsTrue(Hittable(s.Get<Frame>("f")), "a colourless Frame grows a transparent catcher panel");
            Assert.IsTrue(Hittable(s.Get<Image>("i")), "an Image turns its own raycastTarget on");
            Assert.IsTrue(Hittable(s.Get<VStack>("v")), "a container gets a HitCatcher");
        }

        [Test]
        public void A_source_stays_hittable_across_a_ReSolve()
        {
            var s = Open("<Frame id='f' size='10x10' drag='unit'/><Image id='i' size='10x10' drag='unit'/>");

            s.ReSolve();

            Assert.IsTrue(Hittable(s.Get<Frame>("f")));
            Assert.IsTrue(Hittable(s.Get<Image>("i")));
        }

        [Test]
        public void An_explicit_raycastTarget_false_wins_on_a_source_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("drag="));
            var s = Open("<Image id='i' size='10x10' raycastTarget='false' drag='unit'/>");

            Assert.IsFalse(Hittable(s.Get<Image>("i")));
        }

        [Test]
        public void A_target_is_not_hittable_outside_a_drag()
        {
            var s = Open(TwoSlots);

            Assert.IsFalse(Hittable(s.Get<Frame>("slotB")), "drop= alone changes nothing about clicks");
        }

        [Test]
        public void Turning_drag_off_takes_the_catcher_away()
        {
            var s = Open("<VStack id='v' size='10x10' drag='unit'/>");
            var v = s.Get<VStack>("v");

            v.DragKind = null;

            Assert.IsFalse(Hittable(v));
        }
    }
}
