using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class IControlGetPathTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Get_path_walks_scoped_ids_inside_template_instance()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <HStack>
      <Text id='label'>hi</Text>
    </HStack>
  </Template>
  <Screen name='S'>
    <Row id='row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");

            var row = screen.Get("row");
            var label = row.Get<Text>("label");

            Assert.IsNotNull(label);
            Assert.AreSame(label, screen.Get<Text>("row/label"));
        }

        [Test]
        public void Get_path_throws_on_missing_segment()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <HStack><Text id='label'/></HStack>
  </Template>
  <Screen name='S'><Row id='row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var row = screen.Get("row");

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => row.Get<Text>("nope"));
        }
    }
}
