using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine.EventSystems;
using PBtn = PromptUGUI.Controls.Btn;
using PImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.Modals
{
    public class CenteredSlideBoxTests
    {
        private sealed class Lv { public string Id; public string Name; }

        // 测试用模态 XML：backdrop 与 panel **同级**；卡模板写在顶层（<Template> 必须与 <Screen> 同级）。
        private const string SlideBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Card'>
    <Frame size='160x200'>
      <Image id='cover' anchor='stretch'/>
      <Text  id='name'  anchor='bottom-stretch' height='24' align='center'/>
    </Frame>
  </Template>
  <Screen name='test/SlideBox1'>
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

        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string> { ["test/SlideBox1"] = SlideBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            CenteredSlideBox.XmlSrc = "test/SlideBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static List<Lv> ThreeLevels() => new List<Lv>
        {
            new Lv { Id = "a", Name = "Alpha" },
            new Lv { Id = "b", Name = "Bravo" },
            new Lv { Id = "c", Name = "Charlie" },
        };

        private static Carousel Cards() => UI.Modal.TopScreen.Get<Carousel>("cards");

        [Test]
        public void Confirm_Returns_Centered_Item()
        {
            var items = ThreeLevels();
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            {
                Items = items,
                BindCard = (card, lv) => { },
            });
            Cards().GoTo(1, animated: false);                  // 把第 1 项居中
            UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();
            Assert.AreSame(items[1], task.GetAwaiter().GetResult(),
                "confirm returns the centered item");
        }

        [Test]
        public void Click_Close_Returns_Null()
        {
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
                { Items = ThreeLevels(), BindCard = (c, l) => { } });
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            Assert.IsNull(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Backdrop_PointerDown_Returns_Null()
        {
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
                { Items = ThreeLevels(), BindCard = (c, l) => { } });
            var backdrop = UI.Modal.TopScreen.Get<PImage>("backdrop");
            ExecuteEvents.Execute(backdrop.GameObject,
                new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler);
            Assert.IsNull(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Escape_Via_Listener_Returns_Null_And_Closes()
        {
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
                { Items = ThreeLevels(), BindCard = (c, l) => { } });
            var listener = UI.Modal.TopScreen.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener);
            listener.FireForTests();
            Assert.IsNull(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void TryEscape_Returns_Null_And_True()
        {
            var req = new CenteredSlideBoxRequest<Lv>();
            Assert.IsTrue(req.TryEscape(out var r));
            Assert.IsNull(r);
        }
    }
}
