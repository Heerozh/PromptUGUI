using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Welded glass groups: several glass Frames drawn as one continuous pane so the seam between
    /// them reads as a thickness step instead of a dividing line. The members must stay ordinary
    /// nodes — laid out, addressable, able to hold children — with only their drawing moved to the
    /// group.
    /// </summary>
    public class GlassWeldGroupTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string TwoBlocks = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' frost='0.8' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40' depth='6'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='60' height='30' depth='2'/>
  </Frame>
</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string xml)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static ProceduralPanel PanelOf(PromptUGUI.Application.Screen s, string id)
            => s.Get<Frame>(id).GameObject.GetComponent<ProceduralPanel>();

        // The group lives on its own child of the weld container — Graphic is
        // [DisallowMultipleComponent] and the container already carries a ProceduralPanel.
        private static GlassGroupPanel GroupOf(PromptUGUI.Application.Screen s, string id)
            => s.Get<Frame>(id).GameObject.GetComponentInChildren<GlassGroupPanel>(true);

        // ---- membership ----

        [Test]
        public void Weld_TakesOverDrawingFromItsGlassChildren()
        {
            var s = Open(TwoBlocks);
            var group = GroupOf(s, "g");

            Assert.IsNotNull(group);
            Assert.IsTrue(group.IsWelding);
            Assert.AreEqual(2, group.MemberCount);
            Assert.IsTrue(PanelOf(s, "a").IsSuppressed);
            Assert.IsTrue(PanelOf(s, "b").IsSuppressed);
        }

        [Test]
        public void SuppressedMembers_EmitNoGeometryAndHoldNoMaterial()
        {
            var before = ProceduralMaterialCache.LiveMaterialCount;
            var s = Open(TwoBlocks);

            var vh = new VertexHelper();
            PanelOf(s, "a").BuildMeshForTests(vh);
            Assert.AreEqual(0, vh.currentVertCount, "the group draws the fused shape, not the member");

            // Every panel here — both blocks and the container — hands drawing to the group, so none
            // of them should be holding a cached material open. (Graphic.material never reads null:
            // it falls back to uGUI's default, so the cache is what actually answers this.)
            Assert.AreEqual(before, ProceduralMaterialCache.LiveMaterialCount,
                "suppressed panels must not pin materials");
        }

        [Test]
        public void Members_StayOrdinaryNodes()
        {
            // The whole point of welding at the draw layer: layout, ids and children are untouched.
            var s = Open(TwoBlocks);
            var a = s.Get<Frame>("a");
            Assert.IsNotNull(a);
            Assert.AreEqual(100f, a.RectTransform.rect.width, 0.01f);
            Assert.AreEqual(40f, a.RectTransform.rect.height, 0.01f);
        }

        [Test]
        public void Container_DoesNotDrawAPanelOfItsOwn()
        {
            // It carries the group parameters (frost here), which would otherwise make it a visible
            // panel in its own right.
            var s = Open(TwoBlocks);
            Assert.IsTrue(PanelOf(s, "g").IsSuppressed);
        }

        [Test]
        public void OneGlassChild_DoesNotWeld()
        {
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
  </Frame>
</Screen></PromptUGUI>");
            Assert.IsFalse(GroupOf(s, "g").IsWelding);
            Assert.IsFalse(PanelOf(s, "a").IsSuppressed,
                "with nothing to fuse, the child must go on drawing itself");
        }

        [Test]
        public void NonGlassChildren_AreNotMembers()
        {
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='top-right' width='40' height='40'/>
    <Frame id='c' color='#222' anchor='bottom-left' width='20' height='20'/>
  </Frame>
</Screen></PromptUGUI>");
            Assert.AreEqual(2, GroupOf(s, "g").MemberCount);
            Assert.IsFalse(PanelOf(s, "c").IsSuppressed);
        }

        [Test]
        public void RemovingWeld_HandsDrawingBackToTheMembers()
        {
            // Variants can only overwrite a value, so weld="" is how an author turns fusing off.
            var s = Open(TwoBlocks);
            s.Get<Frame>("g").Weld = "";

            Assert.IsFalse(GroupOf(s, "g").IsWelding);
            Assert.IsFalse(PanelOf(s, "a").IsSuppressed);
            Assert.IsFalse(PanelOf(s, "b").IsSuppressed);
        }

        // ---- packed member data ----

        [Test]
        public void MemberRects_ArePackedInTheContainersLocalSpace()
        {
            var s = Open(TwoBlocks);
            var rects = GroupOf(s, "g").MaterialForTests.GetVectorArray("_WeldRects");

            // 'a' is 100x40 pinned to the container's top-left; the container is 200x100 with a
            // centre pivot, so its rect centre sits at (-50, +30).
            Assert.AreEqual(-50f, rects[0].x, 0.01f);
            Assert.AreEqual(30f, rects[0].y, 0.01f);
            Assert.AreEqual(50f, rects[0].z, 0.01f);
            Assert.AreEqual(20f, rects[0].w, 0.01f);

            // 'b' is 60x30 in the opposite corner.
            Assert.AreEqual(30f, rects[1].z, 0.01f);
            Assert.AreEqual(15f, rects[1].w, 0.01f);
            Assert.Less(rects[1].y, rects[0].y, "b sits below a");
        }

        [Test]
        public void PerBlockDepth_ReachesTheShader()
        {
            // The thickness step between blocks is what replaces a dividing line.
            var depths = GroupOf(Open(TwoBlocks), "g").MaterialForTests.GetVectorArray("_WeldDepths");
            Assert.AreEqual(6f, depths[0].x, 0.01f);
            Assert.AreEqual(2f, depths[1].x, 0.01f);
        }

        [Test]
        public void GroupParams_ComeFromTheContainer()
        {
            var mat = GroupOf(Open(TwoBlocks), "g").MaterialForTests;
            Assert.AreEqual(0.8f, mat.GetVector("_GlassA").x, 0.001f, "frost is group-level");
            Assert.AreEqual(10f, mat.GetFloat("_Weld"), 0.001f);
            Assert.AreEqual(2, mat.GetInt("_WeldCount"));
        }

        [Test]
        public void Seam_ReachesTheGroup()
        {
            var mat = GroupOf(Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' seam='6' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40' depth='6'/>
    <Frame id='b' glass='true' anchor='top-right' width='60' height='40' depth='2'/>
  </Frame>
</Screen></PromptUGUI>"), "g").MaterialForTests;

            Assert.AreEqual(6f, mat.GetVector("_GlassA").y, 0.001f,
                "seam is how wide the thickness step between two blocks is allowed to be");
        }

        [Test]
        public void Seam_DefaultsWithoutBeingWritten()
        {
            var mat = GroupOf(Open(TwoBlocks), "g").MaterialForTests;
            Assert.AreEqual(GlassAttrParser.DefaultSeam, mat.GetVector("_GlassA").y, 0.001f);
        }

        [Test]
        public void Seam_SurvivesAContainerWithNoPanelOfItsOwn()
        {
            // Nobody wrote frost/border/glow here, so the carrier has no ProceduralPanel at all.
            // seam still has to arrive: it is the one group parameter an author can write on a bare
            // weld carrier, and reading it off a panel that was never attached would silently
            // discard it.
            var mat = GroupOf(Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='8' seam='5' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40' depth='6'/>
    <Frame id='b' glass='true' anchor='top-right' width='40' height='40' depth='2'/>
  </Frame>
</Screen></PromptUGUI>"), "g").MaterialForTests;

            Assert.AreEqual(5f, mat.GetVector("_GlassA").y, 0.001f);
        }

        [Test]
        public void Seam_OnAMember_DoesNotReachTheGroup()
        {
            // One continuous pane is welded one way; a per-block seam would be two.
            // PUI-GLASS-WELD-PARAM-PLACEMENT reports it, and the runtime drops it.
            var mat = GroupOf(Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' seam='9' anchor='top-left' width='100' height='40' depth='6'/>
    <Frame id='b' glass='true' anchor='top-right' width='60' height='40' depth='2'/>
  </Frame>
</Screen></PromptUGUI>"), "g").MaterialForTests;

            Assert.AreEqual(GlassAttrParser.DefaultSeam, mat.GetVector("_GlassA").y, 0.001f);
        }

        [Test]
        public void Seam_AloneBuildsNoGroup()
        {
            // Mirrors weld: seam is a group parameter, and a Frame that fuses nothing must stay the
            // plain Frame it is (PUI-GLASS-SEAM-NO-WELD reports the document).
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' seam='6' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
  </Frame>
</Screen></PromptUGUI>");

            Assert.IsNull(GroupOf(s, "g"), "seam on its own must not build a weld group");
            Assert.IsNull(PanelOf(s, "g"),
                "nor a ProceduralPanel — seam draws nothing by itself, so it attaches no Graphic");
        }

        [Test]
        public void InnerGlow_ComesFromTheContainer()
        {
            // It follows the FUSED outline, so like the border and the outer glow it belongs to the
            // container — a per-member inner glow would trace exactly the dividing lines the weld
            // exists to remove.
            var mat = GroupOf(Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' innerGlow='12' innerGlowColor='#ff0000' anchor='top-left'
         width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='60' height='30'/>
  </Frame>
</Screen></PromptUGUI>"), "g").MaterialForTests;

            Assert.AreEqual(12f, mat.GetFloat("_InnerGlowSize"), 0.001f);
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), mat.GetColor("_InnerGlowColor"));
        }

        [Test]
        public void InnerGlow_DefaultsToNoneOnTheGroup()
        {
            var mat = GroupOf(Open(TwoBlocks), "g").MaterialForTests;
            Assert.AreEqual(0f, mat.GetFloat("_InnerGlowSize"), 0.001f,
                "nobody wrote innerGlow, so the fused pane must not sprout one");
        }

        [Test]
        public void GroupParams_FallBackToDefaultsWithoutAContainerPanel()
        {
            var mat = GroupOf(Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='8' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='top-right' width='40' height='40'/>
  </Frame>
</Screen></PromptUGUI>"), "g").MaterialForTests;
            Assert.AreEqual(GlassAttrParser.DefaultFrost, mat.GetVector("_GlassA").x, 0.001f);
        }

        [Test]
        public void MemberPill_IsResolvedOnTheCpu()
        {
            // The single-panel shader defers pill to keep materials shareable; a group material is
            // per-group, so there is nothing left to protect and the CPU can just do it.
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' radius='pill' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='top-right' width='40' height='40'/>
  </Frame>
</Screen></PromptUGUI>");
            var radii = GroupOf(s, "g").MaterialForTests.GetVectorArray("_WeldRadii");
            Assert.AreEqual(20f, radii[0].x, 0.01f, "pill on a 100x40 block is half the short side");
            Assert.AreEqual(20f, radii[0].z, 0.01f);
        }

        [Test]
        public void OversizedRadius_IsClampedToTheBlock()
        {
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' radius='500' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='top-right' width='40' height='40'/>
  </Frame>
</Screen></PromptUGUI>");
            var radii = GroupOf(s, "g").MaterialForTests.GetVectorArray("_WeldRadii");
            Assert.AreEqual(20f, radii[0].x, 0.01f);
        }

        // ---- geometry ----

        [Test]
        public void Mesh_CoversTheUnionOfTheMembers()
        {
            var s = Open(TwoBlocks);
            var group = GroupOf(s, "g");
            var rects = group.MaterialForTests.GetVectorArray("_WeldRects");

            var vh = new VertexHelper();
            group.BuildMeshForTests(vh);
            Assert.AreEqual(4, vh.currentVertCount);

            var min = default(UIVertex);
            var max = default(UIVertex);
            vh.PopulateUIVertex(ref min, 0);
            vh.PopulateUIVertex(ref max, 2);

            Assert.AreEqual(Mathf.Min(rects[0].x - rects[0].z, rects[1].x - rects[1].z),
                            min.position.x, 0.01f);
            Assert.AreEqual(Mathf.Max(rects[0].y + rects[0].w, rects[1].y + rects[1].w),
                            max.position.y, 0.01f);
            Assert.AreEqual(min.position.x, min.uv0.x, 0.01f,
                "uv0 carries the group-local position the SDF is evaluated at");
        }

        [Test]
        public void HiddenBlock_DropsOutOfTheFusedShape()
        {
            // A Variant that hides one block must not leave the group drawing glass where there is
            // no longer a block — the fused outline has to shrink with it.
            var s = Open(TwoBlocks);
            var group = GroupOf(s, "g");
            Assert.AreEqual(2, group.MaterialForTests.GetInt("_WeldCount"));

            s.Get<Frame>("b").GameObject.SetActive(false);
            group.FlushGroup();

            Assert.AreEqual(1, group.MaterialForTests.GetInt("_WeldCount"));

            var vh = new VertexHelper();
            group.BuildMeshForTests(vh);
            var v = default(UIVertex);
            vh.PopulateUIVertex(ref v, 2);
            var rects = group.MaterialForTests.GetVectorArray("_WeldRects");
            Assert.AreEqual(rects[0].y + rects[0].w, v.position.y, 0.01f,
                "the mesh must now cover only the block that is still visible");
        }

        [Test]
        public void NotWelding_EmitsNoGeometry()
        {
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
  </Frame>
</Screen></PromptUGUI>");
            var vh = new VertexHelper();
            GroupOf(s, "g").BuildMeshForTests(vh);
            Assert.AreEqual(0, vh.currentVertCount);
        }

        // ---- it actually renders (EditMode green is not evidence, procedural-style §12.2) ----

        [Test]
        public void CanvasRebuild_ReachesTheCanvasRendererWithTheGroupShader()
        {
            var s = Open(TwoBlocks);
            var group = GroupOf(s, "g");
            Canvas.ForceUpdateCanvases();

            var cr = group.canvasRenderer;
            Assert.Greater(cr.materialCount, 0);
            Assert.AreEqual("UI/GlassGroup", cr.GetMaterial(0).shader.name);
        }

        [Test]
        public void WeldedMembers_StillKeepTheBackdropRunning()
        {
            var s = Open(TwoBlocks);
            Assert.AreEqual(2, GlassRuntime.ActivePanelCount,
                "the group draws glass, so the capture must stay alive");
            s.Close();
            Assert.AreEqual(0, GlassRuntime.ActivePanelCount);
        }

        [Test]
        public void ClosingScreen_DestroysTheGroupMaterial()
        {
            var s = Open(TwoBlocks);
            var mat = GroupOf(s, "g").MaterialForTests;
            Assert.IsNotNull(mat);
            s.Close();
            Assert.IsTrue(mat == null, "a per-group material must not outlive its screen");
        }
    }
}
