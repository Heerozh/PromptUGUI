using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class IconParserTests {
        const string Header = "<?xml version='1.0'?><PromptUGUI version='1'><Screen name='S'>";
        const string Footer = "</Screen></PromptUGUI>";

        [Test]
        public void Icon_with_valid_name_parses() {
            var xml = Header + "<Icon name='ui:settings'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Icon", icon.Tag);
            Assert.AreEqual("ui:settings", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_missing_name_throws() {
            var xml = Header + "<Icon/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("'name' is required", ex.Message);
        }

        [Test]
        public void Icon_name_without_colon_throws() {
            var xml = Header + "<Icon name='settings'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("must be 'set:icon'", ex.Message);
        }

        [Test]
        public void Icon_name_empty_namespace_throws() {
            var xml = Header + "<Icon name=':settings'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_empty_iconname_throws() {
            var xml = Header + "<Icon name='ui:'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_color_attr_passes_through() {
            var xml = Header + "<Icon name='ui:gear' color='#ff0000'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("#ff0000", icon.Attributes["color"]);
        }

        [Test]
        public void Native_size_on_Frame_throws() {
            var xml = Header + "<Frame size='native'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("native size only allowed on <Icon>", ex.Message);
        }

        [Test]
        public void Native_width_on_Frame_throws() {
            var xml = Header + "<Frame width='native'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Native_size_on_Icon_ok() {
            var xml = Header + "<Icon name='ui:x' size='native'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_variant_overrides_supported() {
            var xml = Header +
                "<Icon name='ui:sun' name.dark='ui:moon' color.dark='#000'/>"
                + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.IsTrue(icon.VariantOverrides.ContainsKey("name"));
            Assert.IsTrue(icon.VariantOverrides.ContainsKey("color"));
        }

        [Test]
        public void Native_size_via_variant_on_Frame_throws() {
            var xml = Header + "<Frame size.dark='native'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("native size only allowed on <Icon>", ex.Message);
        }

        [Test]
        public void Native_width_via_variant_on_Frame_throws() {
            var xml = Header + "<Frame width.dark='native'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_invalid_variant_name_throws() {
            var xml = Header + "<Icon name='ui:sun' name.dark='bad-format'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("name.dark", ex.Message);
        }

        [Test]
        public void Icon_empty_variant_name_throws() {
            var xml = Header + "<Icon name='ui:sun' name.dark=''/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_valid_variant_name_passes() {
            var xml = Header + "<Icon name='ui:sun' name.dark='ui:moon'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }
    }
}
