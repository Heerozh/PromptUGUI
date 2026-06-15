using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PBtn = PromptUGUI.Controls.Btn;

namespace PromptUGUI.Tests.PlayMode.Modals
{
    public class CenteredSlideBoxPlayTests
    {
        private sealed class Lv { public string Id; public string Name; }

        // <Template> 必须与 <Screen> 同级（文档顶层），不能嵌进 <Screen>。
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Card'>
    <Frame size='160x200'>
      <Image id='cover' anchor='stretch'/>
      <Text id='name' anchor='bottom-stretch' height='24'/>
    </Frame>
  </Template>
  <Screen name='test/SlideBoxP'>
    <Image id='backdrop' anchor='stretch' color='#000000A0'/>
    <Frame id='panel' anchor='center' size='600x400'>
      <Text id='title' anchor='top-stretch' height='40' align='center'/>
      <Btn  id='close' anchor='top-right' size='32x32'>x</Btn>
      <Carousel id='cards' anchor='stretch' margin='48,8,64,8'
                fill='false' interval='0' itemTemplate='Card'/>
      <Btn  id='confirm' anchor='bottom-center' size='140x40'>OK</Btn>
    </Frame>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["test/SlideBoxP"] = Xml };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            CenteredSlideBox.XmlSrc = "test/SlideBoxP";
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Open_GoTo_Confirm_Returns_Item_NoCrash()
        {
            var items = new List<Lv> { new Lv { Id = "a" }, new Lv { Id = "b" }, new Lv { Id = "c" } };
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            { Items = items, BindCard = (c, l) => { } });
            UI.Modal.TopScreen.Get<Carousel>("cards").GoTo(2, animated: false);
            UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();
            Assert.AreSame(items[2], task.GetAwaiter().GetResult());
        }
    }
}
