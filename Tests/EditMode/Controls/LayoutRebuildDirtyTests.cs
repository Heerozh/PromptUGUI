using System.Reflection;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A ReSolve that changes nothing must dirty nothing.
    ///
    /// <para><c>Screen.ReSolve</c> replays every attribute on every node — a resize, a Variant flip
    /// and a theme switch all arrive through it, and on a bound list that is hundreds of nodes.
    /// Unity's <see cref="LayoutElement"/> setters already guard on equality (<c>SetDirty</c> fires
    /// only when the value actually changes), so replaying identical values ought to cost nothing.
    /// Writing a -1 sentinel first and the real value second defeated that guard: every property
    /// changed twice per pass, and each change walks the parent chain up to the outermost layout root
    /// calling <c>GetComponents(ILayoutGroup)</c> at every level.</para>
    ///
    /// <para>Read through <c>CanvasUpdateRegistry</c>'s layout queue: drain it, run the pass, ask
    /// whether anything landed back in it. The queue de-duplicates per layout root, so this answers
    /// "was the layout dirtied at all", not "how many times" — the former is the contract.</para>
    /// </summary>
    public class LayoutRebuildDirtyTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static readonly FieldInfo LayoutQueueField = typeof(CanvasUpdateRegistry)
            .GetField("m_LayoutRebuildQueue", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int PendingLayoutRebuilds()
        {
            Assert.IsNotNull(LayoutQueueField,
                "CanvasUpdateRegistry.m_LayoutRebuildQueue is gone — this Unity version needs a new seam");
            var queue = LayoutQueueField.GetValue(CanvasUpdateRegistry.instance);
            return (int)queue.GetType().GetProperty("Count").GetValue(queue);
        }

        private static void Drain()
        {
            Canvas.ForceUpdateCanvases();
            Assume.That(PendingLayoutRebuilds(), Is.EqualTo(0), "guard: the queue drained");
        }

        // One child per write path in ApplyLayoutElement: fixed size (pins min), stretch (weighted
        // flexible), and no size at all (native fallback / cross-axis fill).
        private const string Stack = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' anchor='stretch' spacing='4'>
    <Frame id='fixed' width='40' height='20'/>
    <Frame id='flex' height='stretch'/>
    <Frame id='bare'/>
  </VStack>
</Screen></PromptUGUI>";

        [Test]
        public void SteadyStateReSolve_InALayoutGroup_DirtiesNothing()
        {
            UI.LoadDocument("test", Stack);
            var screen = UI.Open("S");
            Drain();

            screen.ReSolve();

            Assert.AreEqual(0, PendingLayoutRebuilds(),
                "nothing about the layout changed, so nothing should have been queued for rebuild");
        }

        // The contrast that names the culprit: free positioning writes RectTransform directly, and
        // those setters have always been change-guarded, so this path was never the problem.
        [Test]
        public void SteadyStateReSolve_UnderAFreePositioningParent_DirtiesNothing()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='panel' anchor='stretch'>
    <Frame id='a' width='40' height='20'/>
    <Frame id='b' width='40' height='20'/>
  </Frame>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            Drain();

            screen.ReSolve();

            Assert.AreEqual(0, PendingLayoutRebuilds());
        }

        // …and the other half of the contract: a pass that DOES change a size must still dirty, or
        // "cheap" would just mean "broken".
        [Test]
        public void AVariantThatChangesASize_StillDirtiesTheLayout()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' anchor='stretch'>
    <Frame id='f' width='40' width.wide='120' height='20'/>
  </VStack>
</Screen></PromptUGUI>");
            UI.Variants.Set("wide", false);
            var screen = UI.Open("S");
            Drain();

            UI.Variants.Set("wide", true);

            Assert.AreEqual(120f,
                screen.Get<PromptUGUI.Controls.Frame>("f").GameObject
                    .GetComponent<LayoutElement>().preferredWidth,
                "guard: the variant did reach the LayoutElement");
            Assert.Greater(PendingLayoutRebuilds(), 0,
                "a real size change has to queue a rebuild");
        }
    }
}
