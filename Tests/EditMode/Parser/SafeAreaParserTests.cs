using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    public class SafeAreaParserTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [Test]
        public void SafeArea_no_attrs_parses()
        {
            var xml = Header + "<SafeArea id='sa'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void SafeArea_with_anchor_throws()
        {
            var xml = Header + "<SafeArea anchor='stretch'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("anchor", ex.Message);
            StringAssert.Contains("SafeArea", ex.Message);
        }

        [Test]
        public void SafeArea_with_size_throws()
        {
            var xml = Header + "<SafeArea size='100x100'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("size", ex.Message);
        }

        [Test]
        public void SafeArea_with_width_throws()
        {
            var xml = Header + "<SafeArea width='100'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("width", ex.Message);
        }

        [Test]
        public void SafeArea_with_height_throws()
        {
            var xml = Header + "<SafeArea height='100'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("height", ex.Message);
        }

        [Test]
        public void SafeArea_with_margin_throws()
        {
            var xml = Header + "<SafeArea margin='10'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("margin", ex.Message);
        }

        [Test]
        public void SafeArea_with_pivot_throws()
        {
            var xml = Header + "<SafeArea pivot='0.5,0.5'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("pivot", ex.Message);
        }

        [Test]
        public void SafeArea_variant_override_on_anchor_throws()
        {
            var xml = Header + "<SafeArea anchor.mobile='stretch'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("anchor", ex.Message);
        }

        [Test]
        public void SafeArea_allows_id_hidden_interactable_if()
        {
            var xml = Header + "<SafeArea id='sa' hidden='true' interactable='false' if='mobile'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }
    }
}
