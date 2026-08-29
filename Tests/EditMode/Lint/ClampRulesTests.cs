using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    // PUI-CLAMP-SCALE — spec 2026-08-30-clamp-size-design §5.5 / §6.5.
    public class ClampRulesTests
    {
        private static ElementNode Node(string width = null, string height = null, string scale = null)
        {
            var n = new ElementNode("Frame") { Id = "p" };
            if (width != null) n.Attributes["width"] = width;
            if (height != null) n.Attributes["height"] = height;
            if (scale != null) n.Attributes["scale"] = scale;
            return n;
        }

        [TestCase("clamp(167, 46%, 250)", true)]
        [TestCase("  clamp(167, 46%, 250)", true)]
        [TestCase("46%", false)]
        [TestCase("stretch", false)]
        [TestCase("250", false)]
        [TestCase("Clamp(1, 2%, 3)", false)]   // not the function form — the parser rejects it separately
        [TestCase(null, false)]
        public void IsClampValue_detects_the_function_form(string value, bool expected)
        {
            Assert.AreEqual(expected, ClampRules.IsClampValue(value));
        }

        [Test]
        public void HasClamp_sees_base_width_and_height()
        {
            Assert.IsTrue(ClampRules.HasClamp(Node(width: "clamp(167, 46%, 250)")));
            Assert.IsTrue(ClampRules.HasClamp(Node(height: "clamp(200, 55%, 400)")));
            Assert.IsFalse(ClampRules.HasClamp(Node(width: "46%", height: "200")));
        }

        [Test]
        public void HasClamp_sees_variant_overrides()
        {
            var n = Node(width: "200");
            n.VariantOverrides["width"] = new System.Collections.Generic.List<(string, string)>
            {
                ("wide", "clamp(167, 46%, 250)"),
            };
            Assert.IsTrue(ClampRules.HasClamp(n));
        }

        [Test]
        public void Clamp_with_base_scale_is_an_issue()
        {
            var issues = ClampRules.CheckClampScale(Node(width: "clamp(167, 46%, 250)", scale: "2")).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ClampRules.ClampScaleCode, issues[0].Code);
            Assert.AreEqual("Frame", issues[0].Tag);
            Assert.AreEqual("p", issues[0].Id);
            StringAssert.Contains("scale", issues[0].Message);
            StringAssert.Contains("clamp", issues[0].Message);
            StringAssert.Contains("child", issues[0].Message);
        }

        [Test]
        public void Clamp_with_variant_scale_is_an_issue()
        {
            var n = Node(width: "clamp(167, 46%, 250)");
            n.VariantOverrides["scale"] = new System.Collections.Generic.List<(string, string)> { ("mobile", "2x") };
            Assert.AreEqual(1, ClampRules.CheckClampScale(n).Count());
        }

        [Test]
        public void Variant_clamp_with_scale_is_an_issue()
        {
            var n = Node(width: "200", scale: "2");
            n.VariantOverrides["width"] = new System.Collections.Generic.List<(string, string)> { ("wide", "clamp(167, 46%, 250)") };
            Assert.AreEqual(1, ClampRules.CheckClampScale(n).Count());
        }

        [Test]
        public void Clamp_without_scale_and_scale_without_clamp_are_clean()
        {
            Assert.IsEmpty(ClampRules.CheckClampScale(Node(width: "clamp(167, 46%, 250)")));
            Assert.IsEmpty(ClampRules.CheckClampScale(Node(width: "46%", scale: "2")));
            Assert.IsEmpty(ClampRules.CheckClampScale(Node(width: "200")));
        }

        [Test]
        public void IRWalker_dispatches_the_rule()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' anchor='top-left' width='300' height='200'>
    <Frame id='p' anchor='bottom-left' width='clamp(167, 46%, 250)' height='100' scale='2'/>
  </Frame>
</Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).Where(i => i.Code == ClampRules.ClampScaleCode).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("p", issues[0].Id);
        }
    }
}
