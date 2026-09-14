using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// A runtime warning or error about something the author wrote has to say WHERE it was
    /// written — the src it was loaded from and the line of the opening tag — on a second line the
    /// Console shows while the entry is still collapsed:
    /// <code>
    /// Tab 't' has no &lt;TabBar&gt; ancestor; mutual exclusion disabled.
    ///   at &lt;Tab id='t'&gt; t:3
    /// </code>
    /// "t" is whatever the caller handed <c>LoadDocument</c> / the SourceResolver, so it is the name
    /// the author already knows the file by. Same string the UIXmlLint CLI prints.
    /// </summary>
    public class RuntimeSourceAttributionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Line 1 is the xml declaration, line 2 opens the Screen, line 3 is the body's first line.
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>\n" +
                                      "<PromptUGUI version='1'><Screen name='S'>\n";
        private const string Footer = "\n</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        [Test]
        public void A_control_warning_names_the_src_and_line_of_its_tag()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex(@"has no <TabBar> ancestor[\s\S]*\n  at <Tab id='t'> t:3$"));

            Open("<Tab id='t'/>");
        }

        [Test]
        public void An_instantiator_lint_warning_names_the_src_and_line_of_the_offending_child()
        {
            // PUI-LAYOUT-ANCHOR fires from ScreenInstantiator, which runs the rule itself instead of
            // going through IRWalker — so it has to stamp the place itself too.
            LogAssert.Expect(LogType.Warning,
                new Regex(@"'anchor' is ignored because the parent is a layout group[\s\S]*\n  at <Frame id='f'> t:4$"));

            Open("<VStack>\n" +
                 "  <Frame id='f' anchor='center' size='10x10'/>\n" +
                 "</VStack>");
        }

        [Test]
        public void A_node_from_a_template_body_names_the_declaration_and_the_invocation()
        {
            // The fix goes where the Tab is DECLARED (line 4, in the template body); the invocation
            // (line 7) says which instance — exactly what the CLI prints.
            LogAssert.Expect(LogType.Warning,
                new Regex(@"has no <TabBar> ancestor[\s\S]*\n  at <Tab id='t'> t:4 \(via t:7\)$"));

            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?>\n" +
                "<PromptUGUI version='1'>\n" +
                "<Template name='Solo'>\n" +
                "  <Tab id='t'/>\n" +
                "</Template>\n" +
                "<Screen name='S'>\n" +
                "  <Solo/>\n" +
                "</Screen></PromptUGUI>");
            UI.Open("S");
        }

        [Test]
        public void A_hard_error_names_the_src_and_line()
        {
            // anchor="stretch" plus width is a structural contradiction (spec §6.2): ApplyCommon
            // throws, ControlAttributeApplier wraps it — and now says where.
            var ex = Assert.Throws<ParseException>(() =>
                Open("<Frame id='f' anchor='stretch' width='100'/>"));

            StringAssert.Contains("<Frame id='f'>", ex.Message);
            StringAssert.Contains("\n  at <Frame id='f'> t:3", ex.Message);
        }

        [Test]
        public void A_lint_hard_error_names_the_src_and_line()
        {
            var ex = Assert.Throws<ParseException>(() =>
                Open("<Frame id='p' width='clamp(10, hug, 20)' scale='2'/>"));

            StringAssert.Contains("PUI-CLAMP-SCALE", ex.Message);
            StringAssert.Contains("\n  at <Frame id='p'> t:3", ex.Message);
        }

        [Test]
        public void A_sprite_that_fails_to_resolve_names_the_node_that_asked_for_it()
        {
            // UI.ResolveSprite is a static every sprite= setter calls, with no node in hand. The
            // node whose attributes are being applied is the one that asked.
            UI.SpriteResolver = _ => null;
            LogAssert.Expect(LogType.Error,
                new Regex(@"sprite 'ui:nope'[\s\S]*\n  at <Image id='m'> t:3$"));

            Open("<Image id='m' sprite='ui:nope' size='10x10'/>");
        }

        [Test]
        public void An_icon_that_fails_to_resolve_names_the_icon()
        {
            UI.SpriteResolver = _ => null;
            LogAssert.Expect(LogType.Error,
                new Regex(@"Icon 'ui:nope'[\s\S]*\n  at <Icon id='i'> t:3$"));

            Open("<Icon id='i' name='ui:nope'/>");
        }

        [Test]
        public void A_component_warning_finds_the_control_that_owns_its_game_object()
        {
            // GradientStopWarning fires from a TMP label inside the <Btn>'s tree with only that
            // label as context; walking up the transforms reaches the Btn.
            LogAssert.Expect(LogType.Warning,
                new Regex(@"gradient stop position[\s\S]*\n  at <Btn id='b'> t:3$"));

            Open("<Btn id='b' text='Go' textColor='#ff0000 70%,#0000ff'/>");
        }
    }
}
