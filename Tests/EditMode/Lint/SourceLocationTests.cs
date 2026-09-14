using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <see cref="SourceLocation"/> is the one place "where was this node written" is turned into
    /// text — the UIXmlLint CLI, the runtime warning channel and the hard-error path all read it,
    /// so an author can grep a Console line and a CLI line with the same string.
    /// </summary>
    public class SourceLocationTests
    {
        [Test]
        public void Format_joins_origin_and_line_the_way_editors_jump_to()
        {
            Assert.AreEqual("screens/home:12", SourceLocation.Format("screens/home", 12));
        }

        [Test]
        public void Format_without_a_line_is_the_origin_alone()
        {
            Assert.AreEqual("screens/home", SourceLocation.Format("screens/home", 0));
        }

        [Test]
        public void Format_without_an_origin_is_null()
        {
            Assert.IsNull(SourceLocation.Format(null, 0));
            Assert.IsNull(SourceLocation.Format("", 7), "a line with no file is not a place");
        }

        [Test]
        public void Via_is_only_printed_when_it_names_a_different_place()
        {
            Assert.AreEqual(" (via screens/home:3)", SourceLocation.Via("lib/cards:20", "screens/home:3"));
            Assert.AreEqual("", SourceLocation.Via("screens/home:3", "screens/home:3"));
            Assert.AreEqual("", SourceLocation.Via("screens/home:3", null));
        }

        [Test]
        public void Describe_names_the_tag_the_id_and_the_place()
        {
            var node = new ElementNode("Icon") { Id = "bell", OriginSrc = "screens/home", Line = 12 };

            Assert.AreEqual("<Icon id='bell'> screens/home:12", SourceLocation.Describe(node));
        }

        [Test]
        public void Describe_appends_the_template_invocation_when_it_is_elsewhere()
        {
            var node = new ElementNode("Icon")
            {
                OriginSrc = "lib/cards",
                Line = 20,
                InvokedAt = "screens/home:3",
            };

            Assert.AreEqual("<Icon> lib/cards:20 (via screens/home:3)", SourceLocation.Describe(node));
        }

        [Test]
        public void Describe_of_an_unlocated_node_is_just_the_tag()
        {
            // A synthesised node (Markdown output, a caller that parsed without a src): the tag is
            // still worth saying, a made-up place is not.
            Assert.AreEqual("<Text>", SourceLocation.Describe(new ElementNode("Text")));
        }

        [Test]
        public void Describe_reads_a_stamped_issue_the_same_way()
        {
            var issue = new LintIssue("PUI-X", "Frame", "f", "msg", "screens/home", 5, "screens/home:5");

            Assert.AreEqual("<Frame id='f'> screens/home:5", SourceLocation.Describe(issue),
                "the invocation IS the declaration here, so no via");
        }
    }
}
