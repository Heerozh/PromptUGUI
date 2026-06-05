using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ImageFitRulesTests
    {
        private static ElementNode Img(string id = "i")
            => new ElementNode("Image") { Id = id };

        // ===== PUI-IMAGE-FIT-VARIANT =====

        [Test]
        public void FitInVariant_Cover_VariantIssue()
        {
            var n = Img();
            n.VariantOverrides["type"] =
                new List<(string, string)> { ("mobile", "cover") };
            var issues = ImageFitRules.CheckVariant(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFitRules.VariantCode, issues[0].Code);
            StringAssert.Contains("cover", issues[0].Message);
        }

        [Test]
        public void FitInVariant_Contain_VariantIssue()
        {
            var n = Img();
            n.VariantOverrides["type"] =
                new List<(string, string)> { ("portrait", "contain") };
            var issues = ImageFitRules.CheckVariant(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFitRules.VariantCode, issues[0].Code);
        }

        [Test]
        public void NonFitInVariant_NoIssue()
        {
            var n = Img();
            n.VariantOverrides["type"] =
                new List<(string, string)> { ("mobile", "sliced") };
            Assert.IsEmpty(ImageFitRules.CheckVariant(n));
        }

        [Test]
        public void BaseCover_NoVariant_NoVariantIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            Assert.IsEmpty(ImageFitRules.CheckVariant(n));
        }

        // ===== PUI-IMAGE-FIT-GEOMETRY =====

        [Test]
        public void CoverWithSize_GeometryIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            n.Attributes["size"] = "100x100";
            var issues = ImageFitRules.CheckGeometry(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFitRules.GeometryCode, issues[0].Code);
            StringAssert.Contains("size", issues[0].Message);
        }

        [Test]
        public void ContainWithAnchorAndMargin_GeometryIssue_ListsBoth()
        {
            var n = Img();
            n.Attributes["type"] = "contain";
            n.Attributes["anchor"] = "center";
            n.Attributes["margin"] = "8";
            var issues = ImageFitRules.CheckGeometry(n).ToList();
            Assert.AreEqual(1, issues.Count, "one combined issue per Image");
            StringAssert.Contains("anchor", issues[0].Message);
            StringAssert.Contains("margin", issues[0].Message);
        }

        [Test]
        public void CoverNoGeometry_NoIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            Assert.IsEmpty(ImageFitRules.CheckGeometry(n));
        }

        [Test]
        public void SimpleWithSize_NoIssue()
        {
            var n = Img();
            n.Attributes["type"] = "simple";
            n.Attributes["size"] = "100x100";
            Assert.IsEmpty(ImageFitRules.CheckGeometry(n));
        }

        [Test]
        public void CoverWithPivot_NoIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            n.Attributes["pivot"] = "0,0";
            Assert.IsEmpty(ImageFitRules.CheckGeometry(n));
        }

        [Test]
        public void CoverWithVariantGeometry_GeometryIssue()
        {
            var n = Img();
            n.Attributes["type"] = "cover";
            n.VariantOverrides["width"] =
                new List<(string, string)> { ("mobile", "100") };
            var issues = ImageFitRules.CheckGeometry(n).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("width", issues[0].Message);
        }
    }
}
