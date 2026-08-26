using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <see cref="ProceduralAttrNames"/> is a hand-kept mirror of Frame's procedural
    /// <c>[UIAttr]</c> setters, because <c>Core/Lint</c> is the pure-C# subset the CLI compiles
    /// outside Unity and cannot reflect over the control registry. Same shape as
    /// <c>BuiltinTagsTests</c>: the mirror is fine, drifting silently is not.
    ///
    /// <para>The failure it guards against is the one this repo has already been bitten by — a
    /// misspelled or renamed attribute name is silently dropped by <c>ControlAttributeApplier</c>,
    /// so nothing anywhere reports it. Here it would quietly turn two lint rules into no-ops.</para>
    /// </summary>
    public class ProceduralAttrNamesTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void EveryPanelAttachingName_IsARealFrameAttribute()
        {
            var meta = UI.Registry.Resolve("Frame").Meta;
            var missing = ProceduralAttrNames.PanelAttaching
                .Where(name => !meta.HasAttribute(name))
                .ToList();

            CollectionAssert.IsEmpty(missing,
                "these names are not attributes of <Frame>, so the rules reading them can never "
                + "fire — the exact silent failure this list exists to catch");
        }

        [Test]
        public void All_IsPanelAttachingPlusWeld()
        {
            CollectionAssert.AreEqual(
                ProceduralAttrNames.PanelAttaching.Concat(new[] { "weld" }).ToList(),
                ProceduralAttrNames.All.ToList(),
                "weld is the one procedural attribute that does NOT give the Frame a Graphic of its "
                + "own (GlassGroupPanel.Attach puts the fused pane on a child), which is exactly why "
                + "the two lists differ");
        }

        [Test]
        public void Weld_IsAFrameAttribute_ButNotPanelAttaching()
        {
            Assert.IsTrue(UI.Registry.Resolve("Frame").Meta.HasAttribute("weld"));
            CollectionAssert.DoesNotContain(ProceduralAttrNames.PanelAttaching, "weld");
        }
    }
}
