using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>Lint for drag and drop (spec 2026-11-16-drag-drop §8), over the document as written.</summary>
    public class DragDropRulesTests
    {
        private static PromptUGUI.IR.UIDocument Doc(string inner, string templates = "")
            => UIDocumentParser.Parse($@"<?xml version='1.0'?>
<PromptUGUI version='1'>{templates}<Screen name='S'>{inner}</Screen></PromptUGUI>");

        private static int Count(string code, string inner, string templates = "")
            => IRWalker.Walk(Doc(inner, templates)).Count(i => i.Code == code);

        // ───── PUI-DRAG-VALUE ─────

        [Test]
        public void Good_values_pass()
        {
            Assert.AreEqual(0, Count(DragDropRules.ValueCode,
                "<Frame drag='unit' drop='unit item' dragHold='auto' dragDuration='0.2s'/>" +
                "<Frame drag='none' drop='*' dragHold='400ms' dragDuration='0'/>" +
                "<Frame drag.compact='item' drop='a,b'/>"));
        }

        [Test]
        public void Bad_values_are_reported_base_and_variant()
        {
            Assert.AreEqual(1, Count(DragDropRules.ValueCode, "<Frame drag='true'/>"), "a switch, not a kind");
            Assert.AreEqual(1, Count(DragDropRules.ValueCode, "<Frame drag.compact='2fast'/>"));
            Assert.AreEqual(1, Count(DragDropRules.ValueCode, "<Frame drop='unit, !'/>"));
            Assert.AreEqual(1, Count(DragDropRules.ValueCode, "<Frame drag='u' dragHold='soon'/>"));
            Assert.AreEqual(1, Count(DragDropRules.ValueCode, "<Frame drag='u' dragDuration='auto'/>"), "no auto for the flight");
        }

        [Test]
        public void A_template_placeholder_is_not_judged()
        {
            Assert.AreEqual(0, Count(DragDropRules.ValueCode, "",
                "<Template name='Card'><Param name='kind' default='unit'/><Frame drag='{{kind}}'/></Template>"));
        }

        // ───── PUI-DRAG-HANDLE-ID ─────

        [Test]
        public void A_handle_inside_the_source_is_fine_and_a_missing_one_is_reported()
        {
            Assert.AreEqual(0, Count(DragDropRules.HandleCode,
                "<Frame drag='unit' dragHandle='grip'><Frame><Frame id='grip'/></Frame></Frame>"));
            Assert.AreEqual(1, Count(DragDropRules.HandleCode,
                "<Frame id='card' drag='unit' dragHandle='nope'><Frame id='grip'/></Frame>"));
        }

        [Test]
        public void A_handle_that_may_be_inside_a_template_invocation_is_left_to_the_expanded_pass()
        {
            Assert.AreEqual(0, Count(DragDropRules.HandleCode,
                "<Frame drag='unit' dragHandle='grip'><GripBar/></Frame>",
                "<Template name='GripBar'><Frame><Frame id='grip'/></Frame></Template>"));
        }

        [Test]
        public void A_handle_on_a_node_that_is_not_a_source_is_not_judged()
        {
            Assert.AreEqual(0, Count(DragDropRules.HandleCode, "<Frame dragHandle='nope'/>"));
        }

        // ───── PUI-DRAG-REORDER / -VIRTUAL / -CAROUSEL-CARD ─────

        private const string CardRow =
            "<Template name='Row'><Frame height='40'><Frame id='card' anchor='stretch' drag='unit'/></Frame></Template>";

        [Test]
        public void A_source_in_a_sortable_list_row_is_reported()
        {
            Assert.AreEqual(1, Count(DragDropRules.ReorderCode, "<ScrollList itemTemplate='Row' reorder='true'/>", CardRow));
            Assert.AreEqual(1, Count(DragDropRules.ReorderCode,
                "<ScrollList reorder='false' reorder.wide='true'><Frame height='40' drag='unit'/></ScrollList>"),
                "any variant counts");
            Assert.AreEqual(0, Count(DragDropRules.ReorderCode, "<ScrollList itemTemplate='Row'/>", CardRow));
        }

        [Test]
        public void A_source_in_a_virtual_list_row_is_reported()
        {
            Assert.AreEqual(1, Count(DragDropRules.VirtualCode, "<ScrollList itemTemplate='Row' virtualize='true'/>", CardRow));
        }

        [Test]
        public void A_carousel_card_that_is_itself_a_source_is_reported()
        {
            Assert.AreEqual(1, Count(DragDropRules.CarouselCardCode,
                "<Carousel><Frame drag='unit'/><Frame/></Carousel>"));
            Assert.AreEqual(0, Count(DragDropRules.CarouselCardCode,
                "<Carousel><Frame><Frame anchor='stretch' drag='unit'/></Frame></Carousel>"), "inside the card is fine");
        }

        // ───── PUI-DROP-NO-SOURCE / PUI-LIFT-NO-SOURCE ─────

        [Test]
        public void A_target_hook_needs_a_drop_target_on_or_around_it()
        {
            Assert.AreEqual(1, Count(DragDropRules.NoTargetCode, "<Frame><Show on='drop-over'><Frame/></Show></Frame>"));
            Assert.AreEqual(1, Count(DragDropRules.NoTargetCode,
                "<Frame><Animation on='open' reverse-on='drop-out'><Frame/></Animation></Frame>"), "reverse-on too");
            Assert.AreEqual(0, Count(DragDropRules.NoTargetCode, "<Frame drop='unit'><Show on='drop-over'><Frame/></Show></Frame>"));
            Assert.AreEqual(0, Count(DragDropRules.NoTargetCode, "<Animation on='drop-over' drop='unit'><Frame/></Animation>"),
                "the node itself counts");
            Assert.AreEqual(0, Count(DragDropRules.NoTargetCode, "<Show on='drop-over@slot'><Frame/></Show>"), "@id is left to runtime");
            Assert.AreEqual(0, Count(DragDropRules.NoTargetCode, "",
                "<Template name='Slot'><Frame><Show on='receive'><Frame/></Show></Frame></Template>"), "a template body is exempt");
        }

        [Test]
        public void A_lift_hook_is_fine_inside_a_drag_source_as_well_as_a_list_row()
        {
            Assert.AreEqual(0, Count(StateTriggerRules.NoListRowCode, "<Frame drag='unit'><Trigger on='lift'/></Frame>"));
            Assert.AreEqual(0, Count(StateTriggerRules.NoListRowCode, "<Animation on='lift' drag='unit'><Frame/></Animation>"));
            Assert.AreEqual(1, Count(StateTriggerRules.NoListRowCode, "<Frame><Trigger on='drop'/></Frame>"));
            Assert.AreEqual(1, Count(StateTriggerRules.NoListRowCode, "<Frame drag='none'><Trigger on='lift'/></Frame>"),
                "drag='none' with no variant is never a source");
        }
    }
}
