using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Application
{
    // PUI-HUG-TAG / PUI-HUG-SCALE are HARD runtime errors (spec
    // 2026-08-31-hug-reveal-flip-checked-design §1.3): a control with no content size cannot be
    // measured at all, so opening the Screen would silently render a 0-sized node.
    public class ControlAttributeApplierHugTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [Test]
        public void Hug_on_a_frame_throws_at_open()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='p' anchor='top-left' width='150' height='hug'/>" +
                "</Frame>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("PUI-HUG-TAG", ex.Message);
            StringAssert.Contains("id='p'", ex.Message);
            StringAssert.Contains("<VStack>", ex.Message);
        }

        [Test]
        public void Hug_in_a_variant_only_still_throws()
        {
            // Declared, not resolved: the variant is inactive, the combination is still rejected.
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='p' anchor='top-left' width='150' height='200' height.mobile='hug'/>" +
                "</Frame>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("PUI-HUG-TAG", ex.Message);
        }

        [Test]
        public void Hug_and_scale_on_one_node_throws_at_open()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<VStack id='p' anchor='top-left' width='150' height='hug' scale='0.5'>" +
                "<Btn height='20'/></VStack>" +
                "</Frame>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("PUI-HUG-SCALE", ex.Message);
        }

        [Test]
        public void Hug_on_a_container_opens_cleanly()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<VStack id='p' anchor='top-left' width='150' height='hug'><Btn height='20'/></VStack>" +
                "</Frame>" + Footer);

            Assert.DoesNotThrow(() => UI.Open("S"));
        }

        [Test]
        public void Scale_on_a_child_of_the_hugged_node_is_fine()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<VStack id='p' anchor='top-left' width='150' height='hug'>" +
                "<Frame id='inner' height='40' scale='2'/></VStack>" +
                "</Frame>" + Footer);

            Assert.DoesNotThrow(() => UI.Open("S"));
        }
    }
}
