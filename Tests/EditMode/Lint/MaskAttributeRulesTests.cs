using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class MaskAttributeRulesTests
    {
        // ===== Frame =====

        [Test]
        public void Frame_NoMaskAttrs_NoIssue()
        {
            var n = new ElementNode("Frame");
            Assert.IsEmpty(MaskAttributeRules.CheckFrame(n));
        }

        [Test]
        public void Frame_MaskRect_NoIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            Assert.IsEmpty(MaskAttributeRules.CheckFrame(n));
        }

        [Test]
        public void Frame_MaskSelf_FrameSelfIssue()
        {
            var n = new ElementNode("Frame") { Id = "f" };
            n.Attributes["mask"] = "self";
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.FrameSelfCode, issues[0].Code);
            StringAssert.Contains("Frame", issues[0].Message);
            StringAssert.Contains("Image", issues[0].Message);
        }

        [Test]
        public void Frame_MaskBogus_ValueIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "circle";
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ValueCode, issues[0].Code);
        }

        [Test]
        public void Frame_MaskPaddingWithoutRect_PaddingIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["maskPadding"] = "8";
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.PaddingNoRectCode, issues[0].Code);
        }

        [Test]
        public void Frame_MaskRectWithPadding_NoIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            n.Attributes["maskPadding"] = "8";
            Assert.IsEmpty(MaskAttributeRules.CheckFrame(n));
        }

        [Test]
        public void Frame_MaskInVariantOverride_VariantIssue()
        {
            var n = new ElementNode("Frame");
            n.VariantOverrides["mask"] =
                new List<(string Variant, string Value)> { ("mobile", "rect") };
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.VariantCode, issues[0].Code);
        }

        [Test]
        public void Frame_MaskPaddingInVariantOverride_VariantIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            n.VariantOverrides["maskPadding"] =
                new List<(string Variant, string Value)> { ("mobile", "8") };
            var issues = MaskAttributeRules.CheckFrame(n).ToList();
            // No PaddingNoRect (mask=rect base), but VARIANT
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.VariantCode, issues[0].Code);
        }
    }
}
