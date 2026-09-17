using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// <c>IScreen.FindAll&lt;T&gt;()</c> — every control of a type in a Screen, for tooling (the UI
    /// Preview's <c>&lt;Pages&gt;</c> picker). Spec 2026-09-17-pages-design §5.5.
    /// </summary>
    public class ScreenFindAllTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body, string prelude = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                      + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [Test]
        public void Returns_static_controls_in_document_order()
        {
            var s = Open(
                "<Frame id='f'>" +
                "  <Pages id='p1'><Frame id='a'/></Pages>" +
                "</Frame>" +
                "<Pages id='p2'><Frame id='b'/><Pages id='p3'><Frame id='c'/></Pages></Pages>");

            CollectionAssert.AreEqual(new[] { "p1", "p2", "p3" }, s.FindAll<Pages>().Select(p => p.Id).ToList(),
                "declaration order, outer before inner");
            CollectionAssert.AreEqual(new[] { "f", "a", "b", "c" }, s.FindAll<Frame>().Select(f => f.Id).ToList());
        }

        [Test]
        public void Includes_add_block_controls_once_the_block_has_been_built()
        {
            UI.Variants.Set("m", true);
            var s = Open(
                "<Frame id='host'/>" +
                "<Variant when='m'><Add into='#host'><Pages id='added'><Frame id='x'/></Pages></Add></Variant>");

            CollectionAssert.AreEqual(new[] { "added" }, s.FindAll<Pages>().Select(p => p.Id).ToList());
        }

        [Test]
        public void Includes_live_dynamic_subtrees_and_drops_disposed_ones()
        {
            var s = Open(
                "<Frame id='host'/>",
                prelude: "<Template name='Card'><Pages id='p'><Frame id='x'/></Pages></Template>");
            Assert.IsEmpty(s.FindAll<Pages>());

            var instance = s.Instantiate("Card", s.Get("host"));
            var found = s.FindAll<Pages>();
            Assert.AreEqual(1, found.Count);
            Assert.AreSame(instance, found[0], "the instance root itself is the <Pages>");

            instance.Dispose();
            Assert.IsEmpty(s.FindAll<Pages>(), "a destroyed subtree is not returned");
        }

        [Test]
        public void Is_typed_by_the_control_class()
        {
            var s = Open("<Pages id='p'><Frame id='a'/><Text id='t'>x</Text></Pages>");
            Assert.AreEqual(1, s.FindAll<Text>().Count);
            Assert.AreEqual(1, s.FindAll<Pages>().Count);
            Assert.AreEqual(3, s.FindAll<IControl>().Count, "the interface matches every control");
        }
    }
}
