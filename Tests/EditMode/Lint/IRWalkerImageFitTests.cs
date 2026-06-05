using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerImageFitTests
    {
        private static List<LintIssue> Lint(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            return IRWalker.Walk(doc).ToList();
        }

        [Test]
        public void FitInVariant_SurfacedByWalker()
        {
            var issues = Lint(@"<Image id='i' sprite='x' type='simple' type.mobile='cover'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFitRules.VariantCode));
        }

        [Test]
        public void FitGeometry_SurfacedByWalker()
        {
            var issues = Lint(@"<Image id='i' sprite='x' type='cover' size='100x100'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFitRules.GeometryCode));
        }

        [Test]
        public void CleanFitImage_NoFitIssues()
        {
            var issues = Lint(@"<Frame id='box' size='320x180'><Image id='i' sprite='x' type='cover'/></Frame>");
            Assert.IsFalse(issues.Any(i =>
                i.Code == ImageFitRules.GeometryCode || i.Code == ImageFitRules.VariantCode));
        }
    }
}
