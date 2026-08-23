using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Weld groups across a Screen's lifetime — Variant flips, weld being turned off, blocks being
    /// destroyed, Add blocks landing in the container. These are the paths where the group's view of
    /// its membership can drift from the live hierarchy; the visual result of drifting is a stale
    /// fused shape, which renders perfectly happily and is therefore invisible to every parameter
    /// assertion in <see cref="GlassWeldGroupTests"/>.
    /// </summary>
    public class GlassWeldLifecycleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string xml)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static ProceduralPanel PanelOf(PromptUGUI.Application.Screen s, string id)
            => s.Get<Frame>(id).GameObject.GetComponent<ProceduralPanel>();

        private static GlassGroupPanel GroupOf(PromptUGUI.Application.Screen s, string id)
            => s.Get<Frame>(id).GameObject.GetComponentInChildren<GlassGroupPanel>(true);

        // ---- Variant flips a member's glass flag ----

        // Three blocks, so that de-glassing one leaves a group that still fuses — this isolates
        // "membership tracked the flip" from "a group of one dissolves entirely".
        private const string VariantOffXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' glass.compact='false' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='60' height='30'/>
    <Frame id='c' glass='true' anchor='top-right' width='40' height='40'/>
  </Frame>
</Screen></PromptUGUI>";

        [Test]
        public void VariantTurningAMemberNonGlass_DropsItFromTheGroupSamePass()
        {
            // ReSolve walks _nodeMap in insertion order, which is parent-before-children — the
            // container's SyncMembers runs BEFORE the child's glass="false" is applied. Without the
            // child telling the group afterwards, 'a' stays welded and suppressed: it renders as
            // fused glass while its own attributes say it is an ordinary Frame.
            var s = Open(VariantOffXml);
            Assert.AreEqual(3, GroupOf(s, "g").MemberCount);

            UI.Variants.Set("compact", true);

            Assert.AreEqual(2, GroupOf(s, "g").MemberCount,
                "the de-glassed block must leave the group in the same ReSolve that de-glassed it");
            Assert.IsFalse(PanelOf(s, "a").IsSuppressed,
                "a block that is no longer glass has to go back to drawing itself");
        }

        [Test]
        public void VariantTurningAMemberGlass_AddsItToTheGroupSamePass()
        {
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='false' glass.compact='true' anchor='bottom-right' width='60' height='30'/>
  </Frame>
</Screen></PromptUGUI>");
            Assert.IsFalse(GroupOf(s, "g").IsWelding, "one glass child is not a group yet");

            UI.Variants.Set("compact", true);

            Assert.AreEqual(2, GroupOf(s, "g").MemberCount);
            Assert.IsTrue(GroupOf(s, "g").IsWelding,
                "the newly glass block must fuse immediately, not leave a visible seam for a pass");
        }

        // ---- weld turned off ----

        [Test]
        public void WeldDroppedByVariant_GivesTheContainerItsOwnVisualsBack()
        {
            // The container is suppressed only while it is actually carrying a group. Once weld is
            // gone it is an ordinary Frame again — and it must land in the same state whether the
            // author got there through a Variant (ReSolve → OnAfterApply → SyncMembers) or through
            // the setter alone.
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='0' weld.wide='10' borderWidth='2' borderColor='#fff'
         anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='60' height='30'/>
  </Frame>
</Screen></PromptUGUI>");
            Assert.IsFalse(PanelOf(s, "g").IsSuppressed, "no weld yet");

            UI.Variants.Set("wide", true);
            Assert.IsTrue(PanelOf(s, "g").IsSuppressed, "while welding, the container is a carrier");

            UI.Variants.Set("wide", false);
            Assert.IsFalse(PanelOf(s, "g").IsSuppressed,
                "with weld back at 0 the container must draw its own border again");
            Assert.IsFalse(PanelOf(s, "a").IsSuppressed);
        }

        [Test]
        public void ContainerWithTooFewGlassChildren_IsNotSuppressed()
        {
            // A weld that fuses nothing must not silently erase the container's own panel.
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' color='#333' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
  </Frame>
</Screen></PromptUGUI>");
            Assert.IsFalse(PanelOf(s, "g").IsSuppressed);
        }

        // ---- hierarchy churn ----

        [Test]
        public void DestroyedMember_DoesNotBreakTheNextReSolve()
        {
            // Unity objects are fake-null after Destroy: the group still holds the reference, and
            // touching it throws MissingReferenceException — which ControlAttributeApplier wraps and
            // rethrows, killing the whole ReSolve pass, not just this group.
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='60' height='30'/>
  </Frame>
</Screen></PromptUGUI>");
            Object.DestroyImmediate(s.Get<Frame>("a").GameObject);

            Assert.DoesNotThrow(() => s.ReSolve());
            // One block left, so the group dissolves and 'b' goes back to drawing itself — the
            // point is that it got there instead of throwing.
            Assert.IsFalse(GroupOf(s, "g").IsWelding);
            Assert.IsFalse(PanelOf(s, "b").IsSuppressed);
        }

        [Test]
        public void AddBlockIntoTheContainer_LeavesTheFusedPaneBehindTheContent()
        {
            // The group graphic is pinned at sibling index 0 so it draws behind everything the
            // blocks contain. <Add at='start'> renumbers the container's children with no idea the
            // GlassWeld child exists, pushing the fused pane on top of the added content.
            var s = Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' weld='10' anchor='top-left' width='200' height='100'>
    <Frame id='a' glass='true' anchor='top-left' width='100' height='40'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='60' height='30'/>
  </Frame>
  <Variant when='compact'>
    <Add into='#g' at='start'><Text id='cap'>hi</Text></Add>
  </Variant>
</Screen></PromptUGUI>");
            UI.Variants.Set("compact", true);

            var group = GroupOf(s, "g");
            Assert.AreEqual(0, group.transform.GetSiblingIndex(),
                "the fused pane must stay behind the container's content");
        }
    }
}
