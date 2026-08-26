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

        /// <summary>
        /// The premise <c>PUI-CONTAINER-VISUAL-ATTR</c>'s third bucket rests on: only
        /// <c>&lt;Frame&gt;</c> attaches a <c>ProceduralPanel</c>, so on every other built-in these
        /// attributes are silently dropped.
        ///
        /// <para>This is also the tripwire for the day that stops being true. When a control grows a
        /// procedural surface (procedural-surface spec M1/M2), this test fails — and the fix is to
        /// delete that control from the rule, which spec §14 M4 asks for and which is otherwise very
        /// easy to forget.</para>
        /// </summary>
        [Test]
        public void OnlyFrame_HasThePanelRequiringAttributes()
        {
            var offenders = new System.Collections.Generic.List<string>();
            foreach (var (tag, entry) in UI.Registry.All)
            {
                if (tag == "Frame") continue;
                foreach (var attr in ProceduralAttrNames.NeedsPanel)
                    if (entry.Meta.HasAttribute(attr))
                        offenders.Add($"{tag}.{attr}");
            }

            CollectionAssert.IsEmpty(offenders,
                "a control other than <Frame> now accepts a panel-requiring attribute, so "
                + "PureContainerVisualAttrRules reports a working attribute as ignored — drop that "
                + "control from the rule (procedural-surface spec §14 M4)");
        }

        [Test]
        public void NeedsPanel_IsAllMinusColor()
        {
            // color 是唯一在 Image 系控件上也有意义的那个，所以它不在 NeedsPanel 里 —— 第三档
            // 报的正是「除 color 以外」这一组。
            CollectionAssert.AreEqual(
                ProceduralAttrNames.All.Where(n => n != "color").ToList(),
                ProceduralAttrNames.NeedsPanel.ToList());
            CollectionAssert.DoesNotContain(ProceduralAttrNames.NeedsPanel, "color");
        }
    }
}
