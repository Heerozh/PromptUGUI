using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Spec 2026-09-18: the fill is <c>&lt;Progress&gt;</c>'s primary surface. Every procedural
    /// attribute lands on it; <c>radius</c> is the bar's shape and is shared three ways (the fill
    /// when procedural, the colour bg, the clip mask when the fill is a bitmap); a procedural fill
    /// keeps a full-size rect and drives the SDF cut with <c>value</c>; and no mask is built for it,
    /// so the glow escapes the track.
    /// </summary>
    public class ProgressFillSurfaceTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RoundSprite = "PromptUGUI/Defaults/pugui#pugui_9slice_round";

        private static Progress Load(string attrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Progress id='p' anchor='center' width='200' height='40' {attrs}/>
</Screen></PromptUGUI>");
            return UI.Open("S").Get<Progress>("p");
        }

        private static Transform Node(Progress p, string path) => p.GameObject.transform.Find(path);

        private static ProceduralPanel SurfaceUnder(Progress p, string path)
        {
            var node = Node(p, path);
            if (node == null) return null;
            var surface = node.Find(ProceduralSurface.NodeName);
            return surface != null ? surface.GetComponent<ProceduralPanel>() : null;
        }

        private static UIVertex FirstVertex(ProceduralPanel panel)
        {
            using var vh = new VertexHelper();
            panel.BuildMeshForTests(vh);
            Assert.Greater(vh.currentVertCount, 0, "the panel must emit geometry");
            var v = new UIVertex();
            vh.PopulateUIVertex(ref v, 0);
            return v;
        }

        private static bool MaskIsOn(Progress p)
        {
            var mask = Node(p, "MaskWrapper").GetComponent<Mask>();
            return mask != null && mask.enabled && mask.MaskEnabled();
        }

        // ───── the fill is the surface ─────

        [Test]
        public void AnyProceduralAttribute_LandsOnTheFill()
        {
            var p = Load("glow='6' fillColor='#ffcc33' value='0.3'");

            var fill = SurfaceUnder(p, "MaskWrapper/Fill");
            Assert.IsNotNull(fill, "the surface lives under Fill now");
            Assert.IsNull(SurfaceUnder(p, "MaskWrapper/Bg"), "…and not under Bg");
            Assert.AreEqual(6f, fill.CurrentParams.GlowSize);
            Assert.IsTrue(fill.IsPanelVisible);

            var image = Node(p, "MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.IsFalse(image.enabled, "the fill Image stands down while the SDF draws");
        }

        [Test]
        public void AProceduralFill_KeepsAFullSizeRect_AndCutsInTheShader()
        {
            var p = Load("glow='6' fillColor='#ffcc33' value='0.3'");
            var rt = (RectTransform)Node(p, "MaskWrapper/Fill");

            Assert.AreEqual(Vector2.zero, rt.anchorMin, "no rect shrinking — the cut is in the SDF");
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(Vector2.zero, rt.offsetMin);
            Assert.AreEqual(Vector2.zero, rt.offsetMax);

            var v = FirstVertex(SurfaceUnder(p, "MaskWrapper/Fill"));
            Assert.AreEqual(3f, v.uv1.z, "horizontal, default mode=scale → the round cut along +x");
            Assert.AreEqual((2f * 0.3f - 1f) * 100f, v.uv1.w, 1e-3f, "e = (2·value − 1) · half width");
        }

        [Test]
        public void Value_AtRuntime_MovesTheCut_WithoutTouchingTheMaterial()
        {
            var p = Load("glow='6' fillColor='#ffcc33' value='0.3'");
            var panel = SurfaceUnder(p, "MaskWrapper/Fill");
            panel.FlushParams();
            var material = panel.material;

            p.Value = 0.6f;

            Assert.AreEqual((2f * 0.6f - 1f) * 100f, FirstVertex(panel).uv1.w, 1e-3f);
            panel.FlushParams();
            Assert.AreSame(material, panel.material, "a value change is vertex data only");
            Assert.AreEqual(Vector2.one, ((RectTransform)Node(p, "MaskWrapper/Fill")).anchorMax, "still full-size");
        }

        [Test]
        public void Direction_DrivesTheCutAxis()
        {
            var p = Load("glow='6' fillColor='#ffcc33' value='0.3' direction='reverse-vertical'");
            Assert.AreEqual(-4f, FirstVertex(SurfaceUnder(p, "MaskWrapper/Fill")).uv1.z);

            p.Direction = "vertical";
            Assert.AreEqual(4f, FirstVertex(SurfaceUnder(p, "MaskWrapper/Fill")).uv1.z);
        }

        [Test]
        public void ValueZero_OnAProceduralFill_DrawsNothing()
        {
            var p = Load("glow='6' fillColor='#ffcc33' value='0'");
            Assert.IsFalse(SurfaceUnder(p, "MaskWrapper/Fill").IsPanelVisible);

            p.Value = 0.5f;
            Assert.IsTrue(SurfaceUnder(p, "MaskWrapper/Fill").IsPanelVisible);
        }

        [Test]
        public void Mode_PicksTheRoundOrTheFlatCut()
        {
            // The same word means the same thing as for a bitmap fill: scale = the shape itself
            // shrinks to the value (a 9-slice keeps its rounded ends; so does the SDF), fill =
            // cropped at the value (Image.fillAmount; the half-plane). Neither touches the rect.
            var p = Load("mode='fill' glow='6' fillColor='#ffcc33' value='0.3'");
            var rt = (RectTransform)Node(p, "MaskWrapper/Fill");
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(1f, FirstVertex(SurfaceUnder(p, "MaskWrapper/Fill")).uv1.z, "fill → flat cut");

            p.Mode = "scale";
            Assert.AreEqual(Vector2.one, rt.anchorMax, "still full-size");
            Assert.AreEqual(3f, FirstVertex(SurfaceUnder(p, "MaskWrapper/Fill")).uv1.z, "scale → round cut");
        }

        // ───── radius: the bar's shape, shared three ways ─────

        [Test]
        public void Radius_OnAColourBar_RoundsFillAndBg_AndBuildsNoMask()
        {
            var p = Load("radius='8' bgColor='#22345a' fillColor='#ffcc33' value='0.6'");

            var fill = SurfaceUnder(p, "MaskWrapper/Fill");
            var bg = SurfaceUnder(p, "MaskWrapper/Bg");
            Assert.IsNotNull(fill, "a plain fill goes procedural for the radius");
            Assert.IsNotNull(bg, "the colour track takes the same corner");
            Assert.AreEqual(8f, fill.CurrentParams.CornerWidth.x);
            Assert.AreEqual(8f, bg.CurrentParams.CornerWidth.x);
            Assert.IsTrue(Node(p, "MaskWrapper/Bg").gameObject.activeSelf);

            Assert.IsFalse(MaskIsOn(p), "nothing to clip — both layers round themselves, and a mask would eat the glow");
            Assert.AreEqual(3f, FirstVertex(fill).uv1.z, "the fill is still cut at value");
        }

        [Test]
        public void Radius_WithABitmapFill_KeepsTheBitmap_AndTracksTheMask()
        {
            var p = Load($"radius='8' fill='{RoundSprite}' bgColor='#22345a' value='0.6'");

            Assert.IsNull(SurfaceUnder(p, "MaskWrapper/Fill"), "radius alone never retires a bitmap fill");
            var image = Node(p, "MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.IsTrue(image.enabled);
            Assert.IsNotNull(image.sprite);
            Assert.AreEqual(new Vector2(0.6f, 1f), image.rectTransform.anchorMax, "scale mode: the rect follows value");

            var mask = Node(p, "MaskWrapper").GetComponent<Mask>();
            Assert.IsNotNull(mask, "the bitmap path still rounds through the auto-tracked mask");
            Assert.IsTrue(mask.MaskEnabled());
            Assert.IsInstanceOf<ProceduralPanel>(mask.graphic);
            Assert.AreEqual(8f, ((ProceduralPanel)mask.graphic).CurrentParams.CornerWidth.x);
            Assert.AreEqual(8f, SurfaceUnder(p, "MaskWrapper/Bg").CurrentParams.CornerWidth.x, "…and the colour bg rounds itself");
        }

        [Test]
        public void Radius_WithABitmapBg_LeavesTheBitmapAlone()
        {
            var p = Load($"radius='8' bg='{RoundSprite}' fillColor='#ffcc33' value='0.6'");

            var bg = Node(p, "MaskWrapper/Bg").GetComponent<UnityImage>();
            Assert.IsTrue(bg.gameObject.activeSelf);
            Assert.IsTrue(bg.enabled, "a bitmap has its corners baked in; the SDF does not touch it");
            Assert.IsNotNull(bg.sprite);
            Assert.IsNull(SurfaceUnder(p, "MaskWrapper/Bg"));
            Assert.AreEqual(8f, SurfaceUnder(p, "MaskWrapper/Fill").CurrentParams.CornerWidth.x);
        }

        [Test]
        public void Radius_WithoutBgColour_LeavesTheBgLayerOff()
        {
            var p = Load("radius='pill' fillColor='#ffcc33' value='0.6'");

            Assert.IsFalse(Node(p, "MaskWrapper/Bg").gameObject.activeSelf,
                "radius no longer conjures a white track nobody asked for");
            Assert.IsTrue(SurfaceUnder(p, "MaskWrapper/Fill").CurrentParams.Pill);
        }

        [Test]
        public void FillRadius_IsGone()
        {
            Assert.IsFalse(UI.Registry.Resolve("Progress").Meta.HasAttribute("fillRadius"),
                "fillRadius retired — radius is the fill's corner now (Slider keeps its own)");
        }

        // ───── mask ─────

        [Test]
        public void AnExplicitMaskRadius_StillClips_AProceduralFill()
        {
            // The author's call — lint warns when a glow is involved.
            var p = Load("maskRadius='12' glow='4' fillColor='#ffcc33' value='0.6'");
            Assert.IsTrue(MaskIsOn(p));
            Assert.AreEqual(12f, ((ProceduralPanel)Node(p, "MaskWrapper").GetComponent<Mask>().graphic).CurrentParams.CornerWidth.x);
        }

        // ───── theme round trip ─────

        [Test]
        public void AFillSwitchingBetweenBitmapAndSdf_RoundTripsRectMaskAndImage()
        {
            var p = Load($"radius='8' bgColor='#22345a' value='0.6' " +
                         $"fill='{RoundSprite}' fill.glass='' glow.glass='6' fillColor.glass='#ffcc33'");
            var rt = (RectTransform)Node(p, "MaskWrapper/Fill");
            var image = rt.GetComponent<UnityImage>();

            // bitmap skin
            Assert.IsTrue(image.enabled);
            Assert.AreEqual(new Vector2(0.6f, 1f), rt.anchorMax);
            Assert.IsTrue(MaskIsOn(p));

            UI.Variants.Set("glass", true);
            Assert.IsFalse(image.enabled, "glass skin: the SDF fill takes over");
            Assert.AreEqual(Vector2.one, rt.anchorMax, "…with a full-size rect");
            Assert.IsFalse(MaskIsOn(p), "…and the mask stands down so the glow can escape");
            Assert.AreEqual(3f, FirstVertex(SurfaceUnder(p, "MaskWrapper/Fill")).uv1.z);

            UI.Variants.Set("glass", false);
            Assert.IsTrue(image.enabled, "back to the bitmap");
            Assert.IsNotNull(image.sprite);
            Assert.AreEqual(new Vector2(0.6f, 1f), rt.anchorMax, "…anchored to value again");
            Assert.IsTrue(MaskIsOn(p), "…clipped by the mask again");

            Assert.AreEqual(1, Node(p, "MaskWrapper").GetComponents<Mask>().Length, "toggled, never rebuilt");
        }
    }
}
