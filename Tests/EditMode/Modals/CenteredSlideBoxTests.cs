using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine.EventSystems;
using PBtn = PromptUGUI.Controls.Btn;
using PImage = PromptUGUI.Controls.Image;
using PText = PromptUGUI.Controls.Text;

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

        // 取卡 i 的 PuiButton（AttachCardClick 挂在卡根）。
        private static PuiButton CardButton(int i)
            => UI.Modal.TopScreen.Get<Carousel>("cards").GameObject
                 .transform.Find("Viewport/Strip").GetChild(i)
                 .GetComponent<PuiButton>();

        [Test]
        public void Tap_Centered_Card_Returns_It()
        {
            var items = ThreeLevels();
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            { Items = items, BindCard = (c, l) => { } });
            // current 默认 0；点第 0 张（居中）→ 确认返回它
            CardButton(0).onClick.Invoke();
            Assert.AreSame(items[0], task.GetAwaiter().GetResult());
        }

        [Test]
        public void Tap_Side_Card_Centers_It_Without_Returning()
        {
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            { Items = ThreeLevels(), BindCard = (c, l) => { } });
            var car = Cards();
            Assert.AreEqual(0, car.Current);
            CardButton(2).onClick.Invoke();        // 点侧卡 2 → 居中它
            Assert.AreEqual(2, car.Current, "side-card tap centers it");
            Assert.IsTrue(UI.Modal.IsAnyOpen, "side-card tap must NOT confirm/close");
        }

        [Test]
        public void Null_Title_Hides_Title_Node()
        {
            UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            { Items = ThreeLevels(), BindCard = (c, l) => { }, Title = null });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
        }

        [Test]
        public void ConfirmLabel_Overrides_Button_Text()
        {
            UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            { Items = ThreeLevels(), BindCard = (c, l) => { }, ConfirmLabel = "开始" });
            Assert.AreEqual("开始",
                UI.Modal.TopScreen.Get<PBtn>("confirm").GameObject.GetComponentInChildren<TMP_Text>().text);
        }

        [Test]
        public void Empty_Items_Disables_Confirm()
        {
            UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            { Items = new List<Lv>(), BindCard = (c, l) => { } });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PBtn>("confirm").Interactable);
        }

        [Test]
        public void Bind_Fills_Card_Slots()
        {
            var items = ThreeLevels();
            UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            {
                Items = items,
                BindCard = (card, lv) => card.Get<PText>("name").TextValue = lv.Name,
            });
            var card0 = (UnityEngine.RectTransform)Cards().GameObject.transform.Find("Viewport/Strip").GetChild(0);
            Assert.AreEqual("Alpha", card0.GetComponentInChildren<TMP_Text>().text);
        }

        [Test]
        public void Configure_Runs_After_Bind()
        {
            var bindRan = false;
            var configureSawBind = false;
            Carousel cardsFromConfigure = null;
            UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
            {
                Items = ThreeLevels(),
                BindCard = (c, l) => bindRan = true,
                Configure = s => { configureSawBind = bindRan; cardsFromConfigure = s.Get<Carousel>("cards"); },
            });
            Assert.IsTrue(configureSawBind, "Configure hook runs AFTER Bind (BindCard 已执行)");
            Assert.IsNotNull(cardsFromConfigure, "Configure 能够到控件（如 carousel）去调 peek 参数");
        }

        [Test]
        public void Single_Item_Confirm_Returns_It()
        {
            var items = new List<Lv> { new Lv { Id = "solo", Name = "Solo" } };
            var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv>
                { Items = items, BindCard = (c, l) => { } });
            UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();
            Assert.AreSame(items[0], task.GetAwaiter().GetResult());
        }
    }
}
