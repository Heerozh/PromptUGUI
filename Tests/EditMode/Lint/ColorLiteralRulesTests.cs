using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ColorLiteralRulesTests
    {
        [Test]
        public void Valid_Hex_No_Issue()
        {
            var n = new IR.ElementNode("Image") { Id = "test" };
            n.Attributes["color"] = "#ff8800";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void Valid_Hex_8Digit_No_Issue()
        {
            var n = new IR.ElementNode("Text") { Id = "test" };
            n.Attributes["color"] = "#ff880055";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void Token_Word_No_Issue()
        {
            // Bare word: could be a token defined elsewhere. Lint does not flag.
            var n = new IR.ElementNode("Image") { Id = "test" };
            n.Attributes["color"] = "primary";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void Token_Word_White_No_Issue()
        {
            var n = new IR.ElementNode("Text") { Id = "test" };
            n.Attributes["color"] = "white";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void Malformed_Hex_5Digits_Flagged()
        {
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = "#ff800";  // 5 digits, invalid
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ColorLiteralRules.ColorLiteralCode, issues[0].Code);
            StringAssert.Contains("color", issues[0].Message);
            StringAssert.Contains("#ff800", issues[0].Message);
        }

        [Test]
        public void Malformed_Hex_7Digits_Flagged()
        {
            var n = new IR.ElementNode("Text") { Id = "bad" };
            n.Attributes["color"] = "#ff80001";  // 7 digits, invalid
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ColorLiteralRules.ColorLiteralCode, issues[0].Code);
        }

        [Test]
        public void Malformed_Hex_Invalid_Chars_Flagged()
        {
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = "#gggggg";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ColorLiteralRules.ColorLiteralCode, issues[0].Code);
        }

        [Test]
        public void Empty_Value_Not_Crashing()
        {
            // Empty color attr at lint level: not flagged by this rule.
            var n = new IR.ElementNode("Image") { Id = "test" };
            n.Attributes["color"] = "";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void No_ColorAttr_No_Issue()
        {
            var n = new IR.ElementNode("Image") { Id = "test" };
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void IRWalker_Integration_DispatchesColorLiteralRule()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text id='bad' color='#ff800'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i =>
                i.Code == ColorLiteralRules.ColorLiteralCode && i.Id == "bad"));
        }

        [Test]
        public void IRWalker_Integration_TokenWord_NoDispatch()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text id='good' color='primary'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc)
                .Where(i => i.Code == ColorLiteralRules.ColorLiteralCode).ToList();
            Assert.IsEmpty(issues);
        }
    }
}
