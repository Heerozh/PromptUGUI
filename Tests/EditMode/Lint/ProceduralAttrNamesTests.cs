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
        public void All_IsPanelAttachingPlusTheTwoGroupAttrs()
        {
            CollectionAssert.AreEqual(
                ProceduralAttrNames.PanelAttaching.Concat(new[] { "weld", "seam" }).ToList(),
                ProceduralAttrNames.All.ToList(),
                "weld and seam are the two procedural attributes that do NOT give the Frame a "
                + "Graphic of its own (GlassGroupPanel.Attach puts the fused pane on a child, and "
                + "seam is a value that pane reads), which is exactly why the two lists differ");
        }

        [TestCase("weld")]
        [TestCase("seam")]
        public void GroupAttrs_AreFrameAttributes_ButNotPanelAttaching(string attr)
        {
            Assert.IsTrue(UI.Registry.Resolve("Frame").Meta.HasAttribute(attr));
            CollectionAssert.DoesNotContain(ProceduralAttrNames.PanelAttaching, attr);
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
                if (ProceduralSurfaceRules.SurfaceTags.Contains(tag)) continue;
                foreach (var attr in ProceduralAttrNames.NeedsPanel)
                {
                    // <Decor> draws procedurally without being a surface: it accepts the glow pair
                    // and nothing else, and PureContainerVisualAttrRules exempts exactly that pair.
                    if (tag == DecorRules.Tag
                        && DecorRules.SupportedProceduralAttrs.Contains(attr)) continue;
                    if (entry.Meta.HasAttribute(attr))
                        offenders.Add($"{tag}.{attr}");
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "this control accepts a panel-requiring attribute but is not in "
                + "ProceduralSurfaceRules.SurfaceTags, so PureContainerVisualAttrRules still reports "
                + "a working attribute as silently ignored. Add it there (spec §14 M2/M4)");
        }

        /// <summary>The other direction: a tag claimed as procedural that in fact is not.</summary>
        [Test]
        public void EverySurfaceTag_ReallyHasTheAttributes()
        {
            foreach (var tag in ProceduralSurfaceRules.SurfaceTags)
            {
                var entry = UI.Registry.Resolve(tag);
                Assert.IsNotNull(entry, $"'{tag}' is not a registered control at all");
                Assert.IsTrue(entry.Meta.HasAttribute("radius"),
                    $"<{tag}> is listed as having a procedural surface but does not accept 'radius' — "
                    + "the linter would stay silent about attributes that really are dropped");
            }
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
