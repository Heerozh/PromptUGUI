using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Decor&gt;</c>'s node mechanics: one authored node fans out into one instance per
    /// <c>at=</c> slot, the node itself stays out of layout entirely, and instances are only ever
    /// toggled — never destroyed — so a Variant or a theme flipping <c>kind</c> round-trips.
    /// </summary>
    public class DecorTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuguiScreen LoadScreen(string body, string extraTop = "")
        {
            UI.UnloadAll();
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{extraTop}<Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static Decor Load(string decorAttrs)
            => LoadScreen($@"<Frame id='host' width='200' height='100'>
                               <Decor id='d' {decorAttrs}/>
                             </Frame>").Get<Decor>("d");

        private static Transform[] Instances(Decor d)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in d.RectTransform)
                if (child.name.StartsWith(Decor.InstancePrefix)) list.Add(child);
            return list.ToArray();
        }

        private static Transform Instance(Decor d, DecorSlot slot)
            => d.RectTransform.Find(Decor.InstancePrefix + DecorParser.SlotName(slot));

        // ---- fan-out ----

        [Test]
        public void Bracket_DefaultsToFourCornerInstances()
        {
            var d = Load("kind='bracket'");
            Assert.AreEqual(4, Instances(d).Length);
            foreach (var slot in new[] { DecorSlot.TopLeft, DecorSlot.TopRight,
                                         DecorSlot.BottomRight, DecorSlot.BottomLeft })
                Assert.IsNotNull(Instance(d, slot), $"missing instance for {slot}");
        }

        [Test]
        public void Line_DefaultsToOneBottomInstance()
        {
            var d = Load("kind='line'");
            Assert.AreEqual(1, Instances(d).Length);
            Assert.IsNotNull(Instance(d, DecorSlot.Bottom));
        }

        [Test]
        public void At_SelectsWhichInstancesExist()
        {
            var d = Load("kind='bracket' at='top-left,bottom-right'");
            Assert.AreEqual(2, Instances(d).Length);
            Assert.IsTrue(Instance(d, DecorSlot.TopLeft).gameObject.activeSelf);
            Assert.IsTrue(Instance(d, DecorSlot.BottomRight).gameObject.activeSelf);
        }

        [Test]
        public void KindNone_KeepsNothingVisible()
        {
            var d = Load("kind='none'");
            foreach (var inst in Instances(d))
                Assert.IsFalse(inst.gameObject.activeSelf);
        }

        // ---- layout neutrality (spec §5) ----

        [Test]
        public void Node_IsIgnoredByAParentLayoutGroup()
        {
            var screen = LoadScreen(@"<VStack id='v' width='200'>
                                        <Decor id='d' kind='line'/>
                                      </VStack>");
            var le = screen.Get<Decor>("d").GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "a Decor inside a LayoutGroup needs a LayoutElement to opt out");
            Assert.IsTrue(le.ignoreLayout, "decorations must never take a slot in the flow");
        }

        [Test]
        public void Node_ReportsNoNativeSize_SoShowDoesNotPassItUp()
        {
            // <Show> hands its single child's size up to a parent Stack as its own. A decoration is
            // not content, so that pass-through has to read nothing here (spec §5).
            var screen = LoadScreen(@"<VStack id='v' width='200'>
                                        <Btn id='b' width='120' height='40'>
                                          <Show id='s' on='state-normal'>
                                            <Decor id='d' kind='bracket'/>
                                          </Show>
                                        </Btn>
                                      </VStack>");
            Assert.IsNull(screen.Get<Decor>("d").GetNativeSize());
            Assert.IsNull(screen.Get<Show>("s").GetNativeSize());
        }

        [Test]
        public void Node_FillsItsHost_WithNoGraphicOfItsOwn()
        {
            var d = Load("kind='bracket'");
            Assert.IsNull(d.GameObject.GetComponent<Graphic>(),
                          "the Decor node is a holder; only its instances draw");
            Assert.AreEqual(Vector2.zero, d.RectTransform.anchorMin);
            Assert.AreEqual(Vector2.one, d.RectTransform.anchorMax);
        }

        [Test]
        public void Instances_DoNotSwallowClicks()
        {
            var d = Load("kind='bracket'");
            foreach (var inst in Instances(d))
                Assert.IsFalse(inst.GetComponent<Graphic>().raycastTarget);
        }

        // ---- reconcile: never destroy, only toggle (Strategy C) ----

        [Test]
        public void KindFlip_ReusesTheSameInstanceObjects()
        {
            var screen = LoadScreen(@"<Frame id='host' width='200' height='100'>
                                        <Decor id='d' kind='bracket'/>
                                      </Frame>");
            var d = screen.Get<Decor>("d");
            var before = Instance(d, DecorSlot.TopLeft);

            d.Kind = "none";
            d.ReconcileForTests();
            Assert.IsFalse(before.gameObject.activeSelf);

            d.Kind = "bracket";
            d.ReconcileForTests();
            Assert.AreSame(before, Instance(d, DecorSlot.TopLeft),
                           "instances are toggled, never destroyed and rebuilt");
            Assert.IsTrue(before.gameObject.activeSelf);
        }

        [Test]
        public void AtShrinking_HidesTheDroppedSlot_WithoutDestroyingIt()
        {
            var d = Load("kind='bracket'");
            var topRight = Instance(d, DecorSlot.TopRight);

            d.At = "top-left";
            d.ReconcileForTests();

            Assert.IsNotNull(Instance(d, DecorSlot.TopRight));
            Assert.IsFalse(topRight.gameObject.activeSelf);
            Assert.IsTrue(Instance(d, DecorSlot.TopLeft).gameObject.activeSelf);
        }

        // ---- cross-attribute validation reaches the author ----

        [Test]
        public void BracketOnAnEdge_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => Load("kind='bracket' at='bottom'"));
            StringAssert.Contains("bracket", ex.Message);
        }

        [Test]
        public void TickOnACorner_Throws()
        {
            Assert.Throws<ParseException>(() => Load("kind='tick' at='top-left'"));
        }

        [Test]
        public void UnknownKind_Throws()
        {
            Assert.Throws<ParseException>(() => Load("kind='sparkle'"));
        }
    }
}
