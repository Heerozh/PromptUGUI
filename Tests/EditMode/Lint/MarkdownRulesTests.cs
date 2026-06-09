using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class MarkdownRulesTests
    {
        private static PromptUGUI.IR.UIDocument Doc(string inner)
            => UIDocumentParser.Parse($@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>{inner}</Screen></PromptUGUI>");

        [Test]
        public void Child_elements_under_markdown_warn()
        {
            var issues = IRWalker.Walk(Doc("<Markdown id='md'><Text>x</Text></Markdown>")).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == MarkdownRules.NoChildrenCode));
        }

        [Test]
        public void Text_only_markdown_is_clean()
        {
            var issues = IRWalker.Walk(Doc("<Markdown id='md'>hello</Markdown>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == MarkdownRules.NoChildrenCode));
        }
    }
}
