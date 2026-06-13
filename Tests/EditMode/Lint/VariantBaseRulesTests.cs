using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// PUI-VARIANT-NO-BASE: a control-specific attribute carrying a `attr.&lt;variant&gt;` override
    /// but NO base `attr=` will not revert when the variant deactivates (ControlAttributeApplier's
    /// set-only loop skips a null-resolving setter). The self-heal whitelist is verified against the
    /// runtime (ApplyCommon / ApplyScales), NOT the SKILL doc — notably `hidden` is NOT self-healing.
    /// </summary>
    public class VariantBaseRulesTests
    {
        private static ElementNode Node(string tag, string id = "n")
            => new ElementNode(tag) { Id = id };

        private static List<(string, string)> V(string variant, string value)
            => new List<(string, string)> { (variant, value) };

        // ===== control-specific attrs: flag when base-less =====

        [Test]
        public void ControlAttr_VariantOnly_NoBase_Flagged()
        {
            var n = Node("TabBar", "tb");
            n.VariantOverrides["direction"] = V("portrait", "vertical");
            var issues = VariantBaseRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(VariantBaseRules.NoBaseCode, issues[0].Code);
            Assert.AreEqual("TabBar", issues[0].Tag);
            Assert.AreEqual("tb", issues[0].Id);
            StringAssert.Contains("direction", issues[0].Message);
        }

        [Test]
        public void ControlAttr_WithBase_NotFlagged()
        {
            var n = Node("TabBar");
            n.Attributes["direction"] = "horizontal";
            n.VariantOverrides["direction"] = V("portrait", "vertical");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void ColorAttr_VariantOnly_NoBase_Flagged()
        {
            var n = Node("Image", "i");
            n.VariantOverrides["color"] = V("portrait", "red");
            var issues = VariantBaseRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("color", issues[0].Message);
        }

        [Test]
        public void MultipleVariants_SameAttr_NoBase_OneIssue()
        {
            var n = Node("TabBar");
            n.VariantOverrides["direction"] = new List<(string, string)>
            {
                ("portrait", "vertical"),
                ("landscape", "horizontal"),
            };
            Assert.AreEqual(1, VariantBaseRules.Check(n).Count(), "one issue per attribute, not per variant");
        }

        // ===== self-heal whitelist: do NOT flag (verified against ApplyCommon / ApplyScales) =====

        [Test]
        public void SelfHealing_Height_VariantOnly_NotFlagged()
        {
            var n = Node("Btn");
            n.VariantOverrides["height"] = V("portrait", "132");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void SelfHealing_Scale_VariantOnly_NotFlagged()
        {
            var n = Node("Text");
            n.VariantOverrides["scale"] = V("portrait", "2x");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void SelfHealing_Interactable_VariantOnly_NotFlagged()
        {
            var n = Node("Btn");
            n.VariantOverrides["interactable"] = V("portrait", "false");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void SelfHealing_AllCommonGeometry_VariantOnly_NotFlagged()
        {
            foreach (var attr in new[] { "anchor", "size", "width", "margin", "pivot", "flow" })
            {
                var n = Node("Frame");
                n.VariantOverrides[attr] = V("portrait", "0");
                Assert.IsEmpty(VariantBaseRules.Check(n), $"'{attr}' self-heals and must not be flagged");
            }
        }

        // ===== hidden is the correction: it does NOT self-heal, so flag it =====

        [Test]
        public void Hidden_VariantOnly_NoBase_Flagged()
        {
            // ApplyCommon applies hidden via `if (hidden.HasValue)` — a null-resolving hidden is
            // SKIPPED, not reset. So a base-less hidden.<variant> sticks. Skill is wrong to whitelist it.
            var n = Node("Image", "i");
            n.VariantOverrides["hidden"] = V("portrait", "true");
            var issues = VariantBaseRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count, "hidden does NOT self-heal (Control.cs:351)");
            StringAssert.Contains("hidden", issues[0].Message);
        }

        [Test]
        public void Hidden_WithBase_NotFlagged()
        {
            var n = Node("Image");
            n.Attributes["hidden"] = "false";
            n.VariantOverrides["hidden"] = V("portrait", "true");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        // ===== defer to dedicated *-VARIANT rules (whose advice differs from "add a base") =====

        [Test]
        public void TypeFitValue_VariantOnly_NotFlagged_OwnedByFitRule()
        {
            // type="cover"/"contain" in a variant is owned by PUI-IMAGE-FIT-VARIANT, whose guidance
            // ("not supported in v1") is correct; "add a base type=" would be WRONG advice here.
            var n = Node("Image");
            n.VariantOverrides["type"] = V("portrait", "cover");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void TypeNonFitValue_VariantOnly_NoBase_Flagged()
        {
            // sliced/tiled/simple have no AspectRatioFitter lifetime issue — FIT-VARIANT stays silent,
            // so the revert bug is real and THIS rule must catch it. A base type= cleanly fixes it.
            var n = Node("Image");
            n.VariantOverrides["type"] = V("portrait", "tiled");
            var issues = VariantBaseRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("type", issues[0].Message);
        }

        [Test]
        public void MaskFamily_VariantOnly_NotFlagged_OwnedByMaskRule()
        {
            foreach (var attr in new[] { "mask", "showMask", "maskPadding" })
            {
                var n = Node("Image");
                n.VariantOverrides[attr] = V("portrait", "rect");
                Assert.IsEmpty(VariantBaseRules.Check(n), $"'{attr}' is owned by PUI-MASK-VARIANT");
            }
        }

        // ===== text shorthand: element text content counts as the base =====

        [Test]
        public void Text_WithTextContentBase_VariantOverride_NotFlagged()
        {
            // <Btn text.portrait="Hi">Hello</Btn> — the body text is the base the loop reverts to
            // via the TextContent shorthand block, so this self-heals.
            var n = Node("Btn");
            n.TextContent = "Hello";
            n.VariantOverrides["text"] = V("portrait", "Hi");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void Text_NoBaseNoContent_VariantOverride_Flagged()
        {
            var n = Node("Text");
            n.VariantOverrides["text"] = V("portrait", "Hi");
            Assert.AreEqual(1, VariantBaseRules.Check(n).Count());
        }

        // ===== structural / non-setter attrs are out of scope =====

        [Test]
        public void StructuralAttrs_VariantOverride_NotFlagged()
        {
            foreach (var attr in new[] { "tr", "ctx", "id", "if" })
            {
                var n = Node("Btn");
                n.VariantOverrides[attr] = V("portrait", "x");
                Assert.IsEmpty(VariantBaseRules.Check(n), $"'{attr}' never reaches a control setter");
            }
        }

        // ===== non-builtin tags (template invocations / custom controls / synthetic roots) =====

        [Test]
        public void NonBuiltinTag_VariantOverride_NotFlagged()
        {
            // A pre-expansion non-builtin tag is a template invocation (param semantics, not setter
            // semantics) or a custom control the CLI can't introspect — out of scope, never flag.
            var n = Node("MyPanel");
            n.VariantOverrides["color"] = V("portrait", "red");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        [Test]
        public void ScreenRootSyntheticTag_NotFlagged()
        {
            // The parser's screen root is "__screen_root__" (holds reference= etc.) — non-builtin,
            // so reference.<variant> is never flagged here regardless of base.
            var n = new ElementNode("__screen_root__");
            n.VariantOverrides["reference"] = V("portrait", "1080x1920");
            Assert.IsEmpty(VariantBaseRules.Check(n));
        }

        // ===== template body root: invocation may supply common-attr bases =====

        [Test]
        public void TemplateRoot_InvocationMergeableCommonAttr_NotFlagged()
        {
            // padding/spacing/hidden are merged onto a template instance root from the invocation
            // (TemplateExpander.CommonAttrs), so a base-less override on the BODY ROOT is not
            // necessarily base-less after expansion. Don't flag on the template body root.
            foreach (var attr in new[] { "padding", "spacing", "hidden" })
            {
                var n = Node("VStack");
                n.VariantOverrides[attr] = V("portrait", "8");
                Assert.AreEqual(1, VariantBaseRules.Check(n, isTemplateBodyRoot: false).Count(),
                    $"'{attr}' base-less is flagged OUTSIDE a template root");
                Assert.IsEmpty(VariantBaseRules.Check(n, isTemplateBodyRoot: true),
                    $"'{attr}' may be invocation-supplied on a template body root");
            }
        }

        [Test]
        public void TemplateRoot_NonMergeableControlAttr_StillFlagged()
        {
            // direction is NOT a CommonAttr — an invocation can't supply it (would throw at expansion),
            // so a base-less direction.<variant> on a template root is a real revert bug. Still flag.
            var n = Node("TabBar");
            n.VariantOverrides["direction"] = V("portrait", "vertical");
            Assert.AreEqual(1, VariantBaseRules.Check(n, isTemplateBodyRoot: true).Count());
        }

        [Test]
        public void InvocationMergeableMirror_MatchesTemplateExpander_NoDrift()
        {
            // Guard (same idiom as BuiltinTagsTests): the lint layer can't compile Core/Template, so it
            // mirrors TemplateExpander.CommonAttrs. If that set grows a non-self-healing attr, this test
            // fails so the carve-out is kept in sync rather than silently regressing into a false positive.
            Assert.IsTrue(
                VariantBaseRules.InvocationMergeableOntoTemplateRoot.SetEquals(TemplateExpander.CommonAttrs),
                "VariantBaseRules mirror drifted from TemplateExpander.CommonAttrs");
        }

        // ===== IRWalker integration (parser → walk → issue surfaces) =====

        private static List<LintIssue> Lint(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        [Test]
        public void Walker_DirectionNoBase_Surfaced()
        {
            var issues = Lint(@"<TabBar id='tb' direction.portrait='vertical'/>");
            Assert.IsTrue(issues.Any(i => i.Code == VariantBaseRules.NoBaseCode));
        }

        [Test]
        public void Walker_HiddenNoBase_Surfaced()
        {
            var issues = Lint(@"<Image id='i' sprite='x' hidden.portrait='true'/>");
            Assert.IsTrue(issues.Any(i => i.Code == VariantBaseRules.NoBaseCode));
        }

        [Test]
        public void Walker_WithBase_NoIssue()
        {
            var issues = Lint(@"<TabBar id='tb' direction='horizontal' direction.portrait='vertical'/>");
            Assert.IsFalse(issues.Any(i => i.Code == VariantBaseRules.NoBaseCode));
        }

        private static List<LintIssue> LintDoc(string topLevel)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{topLevel}</PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        [Test]
        public void Walker_TemplateRootMergeableAttr_NotFlagged()
        {
            // <VStack hidden.portrait> as a TEMPLATE ROOT: invocation may supply hidden= → not flagged.
            var issues = LintDoc(
                @"<Template name='T'><VStack hidden.portrait='true'><Btn id='b'>x</Btn></VStack></Template>");
            Assert.IsFalse(issues.Any(i => i.Code == VariantBaseRules.NoBaseCode));
        }

        [Test]
        public void Walker_TemplateRootNonMergeableAttr_Flagged()
        {
            var issues = LintDoc(
                @"<Template name='T'><TabBar direction.portrait='vertical'><Tab id='a'>x</Tab></TabBar></Template>");
            Assert.IsTrue(issues.Any(i => i.Code == VariantBaseRules.NoBaseCode));
        }

        [Test]
        public void Walker_TemplateDescendantMergeableAttr_Flagged()
        {
            // A non-root node inside a template body does NOT receive invocation attrs, so a base-less
            // hidden.<variant> there is a real revert bug — the carve-out is root-only.
            var issues = LintDoc(
                @"<Template name='T'><Frame><Btn id='b' hidden.portrait='true'>x</Btn></Frame></Template>");
            Assert.IsTrue(issues.Any(i => i.Code == VariantBaseRules.NoBaseCode));
        }
    }
}
