using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerProgressTests
    {
        private static UIDocument Parse(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            return UIDocumentParser.Parse(xml);
        }

        [Test]
        public void Walker_Dispatches_Progress_Children_Rule()
        {
            var doc = Parse("<Progress id='p' fill='ui:bar'><Image/></Progress>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, ProgressAttributeRules.ChildrenCode);
        }

        [Test]
        public void Walker_Dispatches_Progress_Mode_Rule()
        {
            var doc = Parse("<Progress id='p' mode='radial' fill='ui:bar'/>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, ProgressAttributeRules.ModeCode);
        }

        [Test]
        public void Walker_Clean_Progress_No_Issues()
        {
            var doc = Parse("<Progress id='p' value='0.5' fill='ui:bar'/>");
            Assert.IsEmpty(IRWalker.Walk(doc));
        }
    }
}
