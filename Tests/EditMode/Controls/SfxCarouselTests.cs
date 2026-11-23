using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Carousel&gt;</c> flips (spec 2026-11-23-ui-sfx-cues §7.8): a swipe that really turns the page, a dot, a slide box's side
    /// card — not autoplay, code or a rebind; the dots are never button clicks; a slide box's centred card is its one button.
    /// </summary>
    public class SfxCarouselTests
    {
        private List<SfxCue> _cues;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = true;
            _cues = Record();
        }

        [TearDown]
        public void TearDown()
        {
            StateTintReactor.TestForceInstant = false;
            UI.ResetForTests();
        }

        private static Carousel Open(string attrs = "") =>
            OpenScreen($"<Carousel id='car' size='200x100' interval='0' dots='bottom-center' {attrs}><Image/><Image/><Image/></Carousel>")
                .Get<Carousel>("car");

        private static CarouselView View(Carousel car) => car.GameObject.GetComponent<CarouselView>();

        private static Vector2 ScreenAt(CarouselView v, Vector2 local)
            => RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)v.StripRect.parent).TransformPoint(local));

        private static PointerEventData Ev(Vector2 pos, Vector2 delta)
            => new PointerEventData(EventSystem.current) { position = pos, delta = delta };

        private static void Swipe(CarouselView view, float dx)
        {
            var p0 = ScreenAt(view, Vector2.zero);
            var p1 = ScreenAt(view, new Vector2(dx, 0f));
            ((IBeginDragHandler)view).OnBeginDrag(Ev(p0, Vector2.zero));
            ((IDragHandler)view).OnDrag(Ev(p1, p1 - p0));
            ((IEndDragHandler)view).OnEndDrag(Ev(p1, p1 - p0));
        }

        private static GameObject Dot(Carousel car, int i) => car.GameObject.transform.Find("Indicator").GetChild(i).gameObject;

        [Test]
        public void A_swipe_that_turns_the_page_cues_flip_before_OnCurrentChanged()
        {
            var car = Open();
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            car.OnCurrentChanged.Subscribe(i => order.Add("current:" + i));

            Swipe(View(car), -150f);

            Assert.AreEqual(1, car.Current);
            CollectionAssert.AreEqual(new[] { "carousel.flip", "current:1" }, order);
            Assert.AreSame(car, _cues.Single().Source);
        }

        [Test]
        public void A_short_swipe_that_springs_back_cues_nothing()
        {
            var car = Open();
            Swipe(View(car), -10f);
            Assert.AreEqual(0, car.Current);
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void A_dot_flips_with_a_carousel_cue_never_a_button_click()
        {
            var car = Open("sfx='flip:page'");

            Click(Dot(car, 2));
            Click(Dot(car, 2));   // already there: nothing turns

            Assert.AreEqual(2, car.Current);
            Assert.AreEqual("[carousel.flip→page]", Describe(_cues));
        }

        [Test]
        public void Code_flips_cue_nothing()
        {
            // Autoplay commits through the same GoTo as these: outside any user gesture.
            var car = Open();
            car.Next();
            car.GoTo(2);
            car.Current = 0;
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        // ───── CenteredSlideBox ─────

        private sealed class Lv { public string Name; }

        private const string SlideBoxXml = SfxTestKit.Head + @"
  <Template name='Card'><Frame size='160x200'><Text id='name' anchor='stretch'/></Frame></Template>
  <Screen name='test/SlideBox1'>
    <Image id='backdrop' anchor='stretch' color='#000000A0'/>
    <Frame id='panel' anchor='center' size='600x400'>
      <Text id='title' anchor='top-stretch' height='40'/>
      <Btn id='close' anchor='top-right' size='32x32'>x</Btn>
      <Carousel id='cards' anchor='stretch' margin='48,8,64,8' fill='false' interval='0' itemTemplate='Card'/>
      <HStack id='buttons' anchor='bottom-center' height='40' spacing='8'>
        <Btn id='button0' size='140x40' sfx='confirm'>OK</Btn>
      </HStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        [Test]
        public void A_slide_box_side_card_flips_and_the_centred_card_is_its_one_button()
        {
            var files = new Dictionary<string, string> { ["test/SlideBox1"] = SlideBoxXml };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            var skin = CenteredSlideBox.XmlSrc;
            CenteredSlideBox.XmlSrc = "test/SlideBox1";
            try
            {
                var items = new List<Lv> { new Lv { Name = "a" }, new Lv { Name = "b" }, new Lv { Name = "c" } };
                var task = UI.Modal.OpenAsync(new CenteredSlideBoxRequest<Lv> { Items = items, BindCard = (card, lv) => { } });
                var cards = UI.Modal.TopScreen.Get<Carousel>("cards");
                var strip = cards.GameObject.GetComponent<CarouselView>().StripRect;
                GameObject Card(int i) => strip.GetChild(i).gameObject;
                _cues.Clear();

                Click(Card(1));   // a side card: centre it
                Assume.That(cards.Current, Is.EqualTo(1));
                Click(Card(1));   // the centred card, one button: confirm

                Assert.AreSame(items[1], task.GetAwaiter().GetResult());
                var own = _cues.Where(c => c.Kind != "modal").ToList();
                Assert.AreEqual("[carousel.flip→carousel.flip, btn.click→confirm]", Describe(own),
                    "confirming by the card sounds like confirming by the button");
            }
            finally { CenteredSlideBox.XmlSrc = skin; }   // a process-wide skin: do not leak it into later tests
        }
    }
}
