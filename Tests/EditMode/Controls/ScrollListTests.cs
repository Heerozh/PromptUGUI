using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ScrollListTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void BindItems_template_creates_one_slot_per_data_item()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>x</Text></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }),
                (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);

            Assert.AreEqual(3, list.SlotCount);
        }

        [Test]
        public void BindItems_rebuild_replaces_slots()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'/></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            var src = new ReactiveProperty<IReadOnlyList<string>>(new[] { "a", "b" });
            list.BindItems(src, (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);
            Assert.AreEqual(2, list.SlotCount);

            src.Value = new[] { "x" };
            Assert.AreEqual(1, list.SlotCount);
        }

        [Test]
        public void Unknown_itemTemplate_throws_at_screen_open()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><ScrollList id='list' itemTemplate='Nope'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<PromptUGUI.Parser.ParseException>(() => UI.Open("S"));
        }
    }
}
