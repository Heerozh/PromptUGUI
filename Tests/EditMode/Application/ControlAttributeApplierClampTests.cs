using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Application
{
    // PUI-CLAMP-SCALE is a HARD runtime error (spec 2026-08-30-clamp-size-design §6.5): the
    // combination cannot render correctly, so it throws at UI.Open instead of warning.
    public class ControlAttributeApplierClampTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [Test]
        public void Clamp_and_scale_on_one_node_throws_at_open()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46%, 250)' height='100' scale='2'/>" +
                "</Frame>" + Footer);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("PUI-CLAMP-SCALE", ex.Message);
            StringAssert.Contains("id='p'", ex.Message);
        }

        [Test]
        public void Clamp_with_variant_only_scale_still_throws()
        {
            // Declared, not resolved: the variant is inactive, the combination is still rejected.
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46%, 250)' height='100' scale.mobile='2x'/>" +
                "</Frame>" + Footer);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("PUI-CLAMP-SCALE", ex.Message);
        }

        [Test]
        public void Scale_on_a_child_of_the_clamped_node_is_fine()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46%, 250)' height='100'>" +
                "<Frame id='inner' anchor='stretch' scale='2'/>" +
                "</Frame></Frame>" + Footer);
            Assert.DoesNotThrow(() => UI.Open("S"));
        }
    }
}
