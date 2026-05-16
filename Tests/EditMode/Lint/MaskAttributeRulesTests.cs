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

        // ===== Image =====

        [Test]
        public void Image_NoMaskAttrs_NoIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["sprite"] = "card-bg";
            Assert.IsEmpty(MaskAttributeRules.CheckImage(n));
        }

        [Test]
        public void Image_MaskRect_NoIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "rect";
            Assert.IsEmpty(MaskAttributeRules.CheckImage(n));
        }

        [Test]
        public void Image_MaskSelf_WithSprite_NoIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "self";
            n.Attributes["sprite"] = "round-card";
            Assert.IsEmpty(MaskAttributeRules.CheckImage(n));
        }

        [Test]
        public void Image_MaskSelf_NoSprite_SelfNoSpriteIssue()
        {
            var n = new ElementNode("Image") { Id = "i" };
            n.Attributes["mask"] = "self";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.SelfNoSpriteCode, issues[0].Code);
        }

        [Test]
        public void Image_MaskBogus_ValueIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "circle";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ValueCode, issues[0].Code);
        }

        [Test]
        public void Image_ShowMaskWithoutSelf_ShowMaskIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "rect";
            n.Attributes["showMask"] = "false";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ShowMaskNoSelfCode, issues[0].Code);
        }

        [Test]
        public void Image_ShowMaskWithoutMask_ShowMaskIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["showMask"] = "false";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.ShowMaskNoSelfCode, issues[0].Code);
        }

        [Test]
        public void Image_MaskPaddingWithoutRect_PaddingIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["mask"] = "self";
            n.Attributes["sprite"] = "round-card";
            n.Attributes["maskPadding"] = "8";
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.PaddingNoRectCode, issues[0].Code);
        }

        [Test]
        public void Image_MaskInVariantOverride_VariantIssue()
        {
            var n = new ElementNode("Image");
            n.Attributes["sprite"] = "round";
            n.VariantOverrides["mask"] =
                new List<(string Variant, string Value)> { ("mobile", "self") };
            var issues = MaskAttributeRules.CheckImage(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MaskAttributeRules.VariantCode, issues[0].Code);
        }
    }
}
