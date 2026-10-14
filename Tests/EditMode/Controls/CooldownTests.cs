using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Cooldown&gt;</c> as a drawing (spec 2026-10-13 §4.1–§5.5): three fixed layers, a colour
    /// fill that is always the SDF surface cut by a sector, the sweep folded into a sector centre and
    /// half-angle from <c>origin</c> / <c>clockwise</c> / <c>draw</c>, the bitmap path through
    /// <c>Image.Filled</c> + <c>Radial360</c>, the ring, and no glass.
    /// </summary>
    public class CooldownTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RoundSprite = "PromptUGUI/Defaults/pugui#pugui_9slice_round";
        private const float Pi = Mathf.PI;

        private static Cooldown Load(string attrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Cooldown id='c' anchor='center' size='64x64' {attrs}/>
</Screen></PromptUGUI>");
            return UI.Open("S").Get<Cooldown>("c");
        }

        private static Transform Node(Cooldown c, string path) => c.GameObject.transform.Find(path);

        private static ProceduralPanel SurfaceUnder(Cooldown c, string path)
        {
            var node = Node(c, path);
            if (node == null) return null;
            var surface = node.Find(ProceduralSurface.NodeName);
            return surface != null ? surface.GetComponent<ProceduralPanel>() : null;
        }

        private static ProceduralPanel FillPanel(Cooldown c) => SurfaceUnder(c, "Fill");

        private static UIVertex FirstVertex(ProceduralPanel panel)
        {
            using var vh = new VertexHelper();
            panel.BuildMeshForTests(vh);
            Assert.Greater(vh.currentVertCount, 0, "the panel must emit geometry");
            var v = new UIVertex();
            vh.PopulateUIVertex(ref v, 0);
            return v;
        }

        // ───── structure ─────

        [Test]
        public void ThreeFixedLayers_AndNothingCatchesAClick()
        {
            var c = Load("value='0.5'");

            Assert.IsNotNull(Node(c, "Bg"));
            Assert.IsNotNull(Node(c, "Fill"));
            Assert.IsNotNull(Node(c, "Label"));
            Assert.AreEqual(0, Node(c, "Bg").GetSiblingIndex());
            Assert.AreEqual(1, Node(c, "Fill").GetSiblingIndex());
            Assert.AreEqual(2, Node(c, "Label").GetSiblingIndex());
            Assert.IsFalse(Node(c, "Bg").gameObject.activeSelf, "no bg / bgColor → no track");
            Assert.IsFalse(Node(c, "Label").gameObject.activeSelf, "no countdown → no label");

            foreach (var g in c.GameObject.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(g.raycastTarget, $"{g.name} must be click-through — a Cooldown sits on a Btn");
        }

        [Test]
        public void NativeSize_IsAnIconSquare()
        {
            var c = Load("");
            Assert.AreEqual(new Vector2(64f, 64f), c.GetNativeSize());
        }

        // ───── the colour fill is always the SDF ─────

        [Test]
        public void AColourFill_IsTheSdfSurface_EvenWithoutRadius()
        {
            var c = Load("value='0.5'");

            var panel = FillPanel(c);
            Assert.IsNotNull(panel, "a colour fill can only be swept in the SDF");
            Assert.IsTrue(panel.isActiveAndEnabled);
            Assert.IsFalse(Node(c, "Fill").GetComponent<UnityImage>().enabled, "the Image stands down");
            Assert.IsTrue(panel.CurrentParams.Radial, "the fill panel takes the radial path");
            Assert.AreEqual(new Color(0f, 0f, 0f, 0.6f), panel.CurrentParams.Fill.Start,
                "the default fill is a dark veil — <Cooldown anchor='stretch'/> is a usable overlay as is");
        }

        [Test]
        public void FillColor_PaintsTheSurface()
        {
            var c = Load("value='0.5' fillColor='#ff0000'");
            Assert.AreEqual(Color.red, FillPanel(c).CurrentParams.Fill.Start);
        }

        [Test]
        public void TheDefaultSweep_IsTheClassicVeil()
        {
            // origin top, clockwise, draw remaining, r = 0.5: the edge has swept 180° clockwise
            // from 12 o'clock; the veil is 6 → 12 o'clock on the left, centred on 9 o'clock.
            var c = Load("value='0.5'");
            var v = FirstVertex(FillPanel(c));
            Assert.AreEqual(5.75f, v.uv1.z, 1e-4f, "centre at three quarters of a turn");
            Assert.AreEqual(Pi / 2f, v.uv1.w, 1e-4f, "half of a half turn");
        }

        // origin × clockwise × draw at r = 0.25 (swept 0.75 of a turn).
        // remaining: the drawn part is [0.75, 1] → centre 0.875, half-angle 0.25 π.
        // elapsed:   the drawn part is [0, 0.75] → centre 0.375, half-angle 0.75 π.
        [TestCase("top", true, "remaining", 0.875f, 0.25f)]
        [TestCase("top", false, "remaining", 0.125f, 0.25f)]
        [TestCase("top", true, "elapsed", 0.375f, 0.75f)]
        [TestCase("top", false, "elapsed", 0.625f, 0.75f)]
        [TestCase("right", true, "remaining", 0.125f, 0.25f)]
        [TestCase("bottom", true, "remaining", 0.375f, 0.25f)]
        [TestCase("left", true, "elapsed", 0.125f, 0.75f)]
        [TestCase("left", false, "remaining", 0.875f, 0.25f)]
        public void TheSweep_FoldsIntoACentreAndAHalfAngle(string origin, bool clockwise, string draw,
                                                           float centreTurns, float halfTurns)
        {
            var c = Load($"value='0.25' origin='{origin}' clockwise='{(clockwise ? "true" : "false")}' draw='{draw}'");
            var v = FirstVertex(FillPanel(c));
            Assert.AreEqual(5f + centreTurns, v.uv1.z, 1e-4f, "sector centre");
            Assert.AreEqual(halfTurns * Pi, v.uv1.w, 1e-4f, "sector half-angle");
        }

        [Test]
        public void Ready_DrawsNothing_AndAFreshCooldown_DrawsEverything()
        {
            var c = Load("value='0'");
            Assert.IsFalse(FillPanel(c).IsPanelVisible, "remaining 0 = ready: no veil, not even a glow");

            c.Value = 1f;
            Assert.IsTrue(FillPanel(c).IsPanelVisible);
            Assert.AreEqual(0f, FirstVertex(FillPanel(c)).uv1.z, "a full veil is the uncut shape");

            var e = Load("value='0' draw='elapsed'");
            Assert.AreEqual(0f, FirstVertex(FillPanel(e)).uv1.z, "elapsed at ready is the whole shape");
        }

        [Test]
        public void Value_Clamps_AndATweenNeverTouchesTheMaterial()
        {
            var c = Load("value='0.5'");
            var panel = FillPanel(c);
            panel.FlushParams();
            var material = panel.material;

            c.Value = 2f;
            Assert.AreEqual(1f, c.Value);
            c.Value = float.NaN;
            Assert.AreEqual(0f, c.Value);
            c.Value = 0.3f;
            panel.FlushParams();
            Assert.AreEqual(0.3f * Pi, FirstVertex(panel).uv1.w, 1e-4f);
            Assert.AreSame(material, panel.material, "the sweep is vertex data");
        }

        // ───── ring / cap ─────

        [Test]
        public void Thickness_RingsTheFill_AndAColourTrack()
        {
            var c = Load("value='0.5' radius='pill' thickness='6' bgColor='#333333'");

            Assert.AreEqual(6f, FillPanel(c).CurrentParams.RingWidth);
            var bg = SurfaceUnder(c, "Bg");
            Assert.IsNotNull(bg, "a colour track with a ring is drawn by the SDF too");
            Assert.AreEqual(6f, bg.CurrentParams.RingWidth, "the track is a ring of the same width");
            Assert.IsFalse(bg.CurrentParams.Radial, "…and is never cut");
            Assert.AreEqual(0f, FirstVertex(bg).uv1.z);
        }

        [Test]
        public void Radius_ShapesTheFillAndTheColourTrack()
        {
            var c = Load("value='0.5' radius='10' bgColor='#333333'");
            Assert.AreEqual(10f, FillPanel(c).CurrentParams.CornerWidth.x);
            Assert.AreEqual(10f, SurfaceUnder(c, "Bg").CurrentParams.CornerWidth.x);
        }

        [Test]
        public void ARoundCap_IsAskedFor_OnlyWithARing()
        {
            var ring = Load("value='0.5' radius='pill' thickness='6' cap='round'");
            Assert.AreEqual(7.75f, FirstVertex(FillPanel(ring)).uv1.z, 1e-4f, "round-capped radial code");

            var pie = Load("value='0.5' cap='round'");
            Assert.AreEqual(5.75f, FirstVertex(FillPanel(pie)).uv1.z, 1e-4f, "a solid sector has no ends to cap");
        }

        [Test]
        public void ARoundCap_OnANonCircularRing_FallsBackToFlatEnds_WithAWarning()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("cap=\"round\""));
            var c = Load("value='0.5' radius='8' thickness='6' cap='round'");
            Assert.AreEqual(5.75f, FirstVertex(FillPanel(c)).uv1.z, 1e-4f,
                "only a circle has an exact round cap; anything else is drawn with flat ends");
        }

        // ───── bitmap fill ─────

        [Test]
        public void ABitmapFill_SweepsThroughImageFilledRadial360()
        {
            var c = Load($"value='0.25' fill='{RoundSprite}'");
            var image = Node(c, "Fill").GetComponent<UnityImage>();

            Assert.IsTrue(image.enabled);
            Assert.IsNotNull(image.sprite);
            Assert.IsTrue(FillPanel(c) == null || !FillPanel(c).isActiveAndEnabled, "no SDF surface");
            Assert.AreEqual(UnityImage.Type.Filled, image.type);
            Assert.AreEqual(UnityImage.FillMethod.Radial360, image.fillMethod);
            Assert.AreEqual((int)UnityImage.Origin360.Top, image.fillOrigin);
            Assert.IsFalse(image.fillClockwise, "remaining runs back from the origin against the sweep");
            Assert.AreEqual(0.25f, image.fillAmount, 1e-5f);
        }

        [TestCase("right", true, "elapsed", UnityImage.Origin360.Right, true, 0.75f)]
        [TestCase("bottom", false, "remaining", UnityImage.Origin360.Bottom, true, 0.25f)]
        [TestCase("left", false, "elapsed", UnityImage.Origin360.Left, false, 0.75f)]
        public void TheBitmapSweep_MapsOntoTheImageFields(string origin, bool clockwise, string draw,
                                                          UnityImage.Origin360 fillOrigin, bool fillClockwise,
                                                          float amount)
        {
            var c = Load($"value='0.25' fill='{RoundSprite}' origin='{origin}' " +
                         $"clockwise='{(clockwise ? "true" : "false")}' draw='{draw}'");
            var image = Node(c, "Fill").GetComponent<UnityImage>();
            Assert.AreEqual((int)fillOrigin, image.fillOrigin);
            Assert.AreEqual(fillClockwise, image.fillClockwise);
            Assert.AreEqual(amount, image.fillAmount, 1e-5f);
        }

        [Test]
        public void AFillSwitchingBetweenBitmapAndSdf_RoundTrips()
        {
            var c = Load($"value='0.25' fill='{RoundSprite}' fill.sdf='' fillColor.sdf='#ff0000'");
            var image = Node(c, "Fill").GetComponent<UnityImage>();
            Assert.IsTrue(image.enabled, "bitmap skin");
            Assert.AreEqual(UnityImage.Type.Filled, image.type);

            UI.Variants.Set("sdf", true);
            Assert.IsFalse(image.enabled, "SDF skin: the surface takes over");
            Assert.AreEqual(5.875f, FirstVertex(FillPanel(c)).uv1.z, 1e-4f);

            UI.Variants.Set("sdf", false);
            Assert.IsTrue(image.enabled, "back to the bitmap");
            Assert.IsNotNull(image.sprite);
            Assert.AreEqual(UnityImage.Type.Filled, image.type);
            Assert.AreEqual(0.25f, image.fillAmount, 1e-5f);
            Assert.IsFalse(FillPanel(c).CurrentParams.Radial, "the parked panel forgets the sweep");
        }

        // ───── bg ─────

        [Test]
        public void TheTrack_IsShownByAColourOrASprite_AndClearedByEmpty()
        {
            var c = Load("value='0.5' bgColor='#333333' bgColor.off=''");
            Assert.IsTrue(Node(c, "Bg").gameObject.activeSelf);
            UI.Variants.Set("off", true);
            Assert.IsFalse(Node(c, "Bg").gameObject.activeSelf, "bgColor=\"\" turns the colour track back off");
            UI.Variants.Set("off", false);

            var s = Load($"value='0.5' bg='{RoundSprite}' bg.off=''");
            Assert.IsTrue(Node(s, "Bg").gameObject.activeSelf);
            UI.Variants.Set("off", true);
            Assert.IsFalse(Node(s, "Bg").gameObject.activeSelf, "bg=\"\" turns the track back off");
        }

        // ───── no glass ─────

        [Test]
        public void Glass_IsRefused_TheFillStaysOpaque()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("glass"));
            var c = Load("value='0.5' glass='true'");
            Assert.IsFalse(FillPanel(c).IsGlass,
                "a glass veil would show the scene behind the canvas, not the icon under it");
            Assert.IsFalse(FillPanel(c).CurrentParams.Glass);
        }

        // ───── bad values ─────

        [TestCase("origin='north'")]
        [TestCase("draw='all'")]
        [TestCase("cap='square'")]
        public void AnUnknownKeyword_IsAParseError(string attr)
        {
            Assert.Throws<ParseException>(() => Load(attr));
        }

        // ───── runtime state ─────

        [Test]
        public void AValueSetFromCode_SurvivesAVariantReSolve()
        {
            var c = Load("value='0' fillColor='#ff0000' fillColor.alt='#00ff00'");
            c.Value = 0.4f;
            UI.Variants.Set("alt", true);
            Assert.AreEqual(0.4f, c.Value, 1e-5f, "value is runtime state — a ReSolve does not write the XML 0 back");
        }

        [Test]
        public void TheLabel_IsATextMeshProChild()
        {
            var c = Load("");
            Assert.IsNotNull(Node(c, "Label").GetComponent<TMP_Text>());
        }
    }
}
