using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>kind="sprite"</c>: the decoration is a picture rather than an SDF, which is what gives a
    /// texture-based theme (pixel art and friends) a decoration channel of its own, and what makes
    /// arbitrary ornament — filigree, crests, vines — possible at all.
    ///
    /// <para>The increment over just placing an <c>&lt;Image&gt;</c> by hand is the automatic
    /// mirroring: the author draws one corner and the library reflects it into the other three.
    /// Nothing in the control layer could flip a graphic before this, so four corners used to mean
    /// four pre-flipped assets.</para>
    /// </summary>
    public class DecorSpriteTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Sprite StubSprite(int w = 16, int h = 12)
        {
            var tex = new Texture2D(w, h);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero);
            UI.SpriteResolver = _ => sprite;
            return sprite;
        }

        private static Decor Load(string decorAttrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='host' width='200' height='100'>
    <Decor id='d' {decorAttrs}/>
  </Frame>
</Screen></PromptUGUI>");
            return UI.Open("S").Get<Decor>("d");
        }

        private static Transform Slot(Decor d, DecorSlot slot)
            => d.RectTransform.Find(Decor.InstancePrefix + DecorParser.SlotName(slot));

        private static UnityImage ImageOf(Decor d, DecorSlot slot)
        {
            var node = Slot(d, slot);
            return node == null ? null : node.GetComponentInChildren<UnityImage>(true);
        }

        // ---- drawing ----

        [Test]
        public void Sprite_DrawsWithAnImage_NotTheSdfPanel()
        {
            StubSprite();
            var d = Load("kind='sprite' sprite='ui:corner'");

            var img = ImageOf(d, DecorSlot.TopLeft);
            Assert.IsNotNull(img, "a sprite decoration draws through a plain Image");
            Assert.IsNotNull(img.sprite);
            Assert.IsFalse(img.raycastTarget, "decorations never swallow clicks");
        }

        [Test]
        public void Sprite_TakesItsNativePixelSize_ByDefault()
        {
            StubSprite(16, 12);
            var d = Load("kind='sprite' sprite='ui:corner'");
            // The slot node carries the size (and the mirroring transform); the Image fills it.
            Assert.AreEqual(new Vector2(16f, 12f),
                            ((RectTransform)Slot(d, DecorSlot.TopLeft)).sizeDelta);
        }

        [Test]
        public void Sprite_ExplicitExtent_Scales()
        {
            StubSprite(16, 12);
            var d = Load("kind='sprite' sprite='ui:corner' extent='32x24'");
            Assert.AreEqual(new Vector2(32f, 24f),
                            ((RectTransform)Slot(d, DecorSlot.TopLeft)).sizeDelta);
        }

        [Test]
        public void Sprite_WithNoSpriteResolved_DrawsNothing()
        {
            // An Image with no sprite paints a solid rectangle, which would be a far worse outcome
            // than the missing ornament the author is already going to be told about by lint.
            UI.SpriteResolver = _ => null;
            var d = Load("kind='sprite'");
            var node = Slot(d, DecorSlot.TopLeft);
            var drawn = node != null && node.gameObject.activeSelf
                        && ImageOf(d, DecorSlot.TopLeft) != null
                        && ImageOf(d, DecorSlot.TopLeft).isActiveAndEnabled;
            Assert.IsFalse(drawn);
        }

        // ---- automatic mirroring ----

        [Test]
        public void Mirror_ReflectsOneCornerIntoTheOtherThree()
        {
            StubSprite();
            var d = Load("kind='sprite' sprite='ui:corner'");

            // The author draws the top-left; the rest are reflections of it.
            Assert.AreEqual(new Vector3(1f, 1f, 1f), Slot(d, DecorSlot.TopLeft).localScale);
            Assert.AreEqual(new Vector3(-1f, 1f, 1f), Slot(d, DecorSlot.TopRight).localScale);
            Assert.AreEqual(new Vector3(-1f, -1f, 1f), Slot(d, DecorSlot.BottomRight).localScale);
            Assert.AreEqual(new Vector3(1f, -1f, 1f), Slot(d, DecorSlot.BottomLeft).localScale);
        }

        [Test]
        public void Mirror_RotatesEdgeArtOntoTheVerticalEdges()
        {
            StubSprite();
            var d = Load("kind='sprite' sprite='ui:edge' at='bottom,top,left,right'");

            Assert.AreEqual(0f, Slot(d, DecorSlot.Bottom).localEulerAngles.z, 0.01f);
            Assert.AreEqual(new Vector3(1f, -1f, 1f), Slot(d, DecorSlot.Top).localScale);
            Assert.AreEqual(270f, Slot(d, DecorSlot.Left).localEulerAngles.z, 0.01f);
            Assert.AreEqual(90f, Slot(d, DecorSlot.Right).localEulerAngles.z, 0.01f);
        }

        [Test]
        public void MirrorFalse_LeavesEveryInstanceUpright()
        {
            // Asymmetric ornament (a signature, a crest that reads one way) opts out.
            StubSprite();
            var d = Load("kind='sprite' sprite='ui:corner' mirror='false'");

            foreach (var slot in new[] { DecorSlot.TopLeft, DecorSlot.TopRight,
                                         DecorSlot.BottomRight, DecorSlot.BottomLeft })
            {
                Assert.AreEqual(Vector3.one, Slot(d, slot).localScale, $"{slot} should not be flipped");
                Assert.AreEqual(0f, Slot(d, slot).localEulerAngles.z, 0.01f);
            }
        }

        // ---- swapping between the two drawing modes ----

        [Test]
        public void KindFlip_SpriteToSdf_SwapsWhichLayerDraws()
        {
            // The theme channel: one skin pack says kind="sprite", the next says kind="bracket".
            // Graphic is [DisallowMultipleComponent], so the two live on separate nodes and only
            // ever get toggled — never destroyed and rebuilt.
            StubSprite();
            var d = Load("kind='sprite' sprite='ui:corner'");
            var img = ImageOf(d, DecorSlot.TopLeft);
            Assert.IsTrue(img.isActiveAndEnabled);

            d.Kind = "bracket";
            d.ReconcileForTests();
            Assert.IsFalse(img.isActiveAndEnabled, "the sprite layer stands down");
            Assert.IsNotNull(Slot(d, DecorSlot.TopLeft).GetComponent<DecorPanel>());

            d.Kind = "sprite";
            d.ReconcileForTests();
            Assert.AreSame(img, ImageOf(d, DecorSlot.TopLeft), "same Image object comes back");
            Assert.IsTrue(img.isActiveAndEnabled);
        }
    }
}
