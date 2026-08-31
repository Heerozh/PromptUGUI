using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Parser
{
    /// <summary>
    /// <c>&lt;Header&gt;</c> is a structural element of <c>&lt;Collapsible&gt;</c>: it stays an
    /// ordinary child node all the way through parsing and template expansion (so
    /// <c>{{param}}</c>, <c>if=</c> and <c>&lt;Slot/&gt;</c> work inside it for free) and is only
    /// routed to the header host at instantiation time. Spec 2026-08-31-collapsible-design §4.2.
    /// </summary>
    public class CollapsibleHeaderParseTests
    {
        private static UIDocument Parse(string body) => UIDocumentParser.Parse(
            "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
            + body + "</Screen></PromptUGUI>");

        [Test]
        public void Header_survives_parsing_as_an_ordinary_child()
        {
            var doc = Parse("<Collapsible id='c'><Header><Text id='t'>任务</Text></Header><Btn id='b'/></Collapsible>");
            var col = doc.Screens[0].Root.Children[0];

            Assert.AreEqual("Collapsible", col.Tag);
            Assert.AreEqual(2, col.Children.Count, "header and body children both stay in Children");
            Assert.AreEqual("Header", col.Children[0].Tag);
            Assert.AreEqual("Text", col.Children[0].Children[0].Tag);
            Assert.AreEqual("Btn", col.Children[1].Tag);
        }

        [Test]
        public void Header_takes_no_attributes_in_v1()
        {
            var ex = Assert.Throws<ParseException>(
                () => Parse("<Collapsible id='c'><Header height='24'><Text>x</Text></Header></Collapsible>"));
            StringAssert.Contains("<Header>", ex.Message);
            StringAssert.Contains("no attributes", ex.Message);
            StringAssert.Contains("headerHeight", ex.Message, "…and names the attribute that does this");
        }

        [Test]
        public void Ids_inside_a_header_share_the_screen_scope()
        {
            // Duplicate ids across header and body must still be caught by the Screen-wide check.
            var ex = Assert.Throws<ParseException>(
                () => Parse("<Collapsible id='c'><Header><Text id='dup'>x</Text></Header><Btn id='dup'/></Collapsible>"));
            StringAssert.Contains("dup", ex.Message);
        }

        // ── Through template expansion ──────────────────────────────────────────────────────

        private static UIDocument Expand(string xml) => TemplateExpander.Expand(UIDocumentParser.Parse(xml));

        [Test]
        public void Params_and_slot_work_inside_and_around_a_header()
        {
            var expanded = Expand(@"<PromptUGUI version='1'>
                <Template name='Panel'>
                    <Param name='title'/>
                    <Param name='badge' default=''/>
                    <Collapsible id='c'>
                        <Header>
                            <Text id='t'>{{title}}</Text>
                            <Text id='badge' if='{{badge}}'>{{badge}}</Text>
                        </Header>
                        <Slot/>
                    </Collapsible>
                </Template>
                <Screen name='S'>
                    <Panel id='p' title='任务'><Btn id='row'/></Panel>
                </Screen></PromptUGUI>");

            var col = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("Collapsible", col.Tag);

            var header = col.Children[0];
            Assert.AreEqual("Header", header.Tag);
            Assert.AreEqual(1, header.Children.Count, "if='' dropped the badge, like anywhere else");
            Assert.AreEqual("任务", header.Children[0].TextContent, "{{title}} substituted inside the header");

            Assert.AreEqual("Btn", col.Children[1].Tag, "<Slot/> injected the body children after it");
            Assert.AreEqual("row", col.Children[1].Id);
        }

        [Test]
        public void Two_instances_of_a_header_template_stay_independent()
        {
            var expanded = Expand(@"<PromptUGUI version='1'>
                <Template name='Panel'>
                    <Param name='title'/>
                    <Collapsible id='c'><Header><Text id='t'>{{title}}</Text></Header></Collapsible>
                </Template>
                <Screen name='S'>
                    <Panel id='a' title='画面'/>
                    <Panel id='b' title='音频'/>
                </Screen></PromptUGUI>");

            var root = expanded.Screens[0].Root;
            Assert.AreEqual("画面", root.Children[0].Children[0].Children[0].TextContent);
            Assert.AreEqual("音频", root.Children[1].Children[0].Children[0].TextContent);
        }

        // ── expanded= is runtime-owned ──────────────────────────────────────────────────────

        [Test]
        public void A_theme_style_cannot_set_expanded()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Theme name='t'><Style name='panel' expanded='false'/></Theme>
                <Screen name='S'/></PromptUGUI>"));
            StringAssert.Contains("expanded", ex.Message);
            StringAssert.Contains("runtime-owned", ex.Message);
        }
    }
}
