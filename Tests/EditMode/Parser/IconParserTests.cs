using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    public class IconParserTests
    {
        private const string Header = "<?xml version='1.0'?><PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [Test]
        public void Icon_with_valid_name_parses()
        {
            var xml = Header + "<Icon name='ui:settings'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Icon", icon.Tag);
            Assert.AreEqual("ui:settings", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_missing_name_throws()
        {
            var xml = Header + "<Icon/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("'name' is required", ex.Message);
        }

        [Test]
        public void Icon_name_without_colon_throws()
        {
            var xml = Header + "<Icon name='settings'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("must be 'set:icon'", ex.Message);
        }

        [Test]
        public void Icon_name_empty_namespace_throws()
        {
            var xml = Header + "<Icon name=':settings'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_empty_iconname_throws()
        {
            var xml = Header + "<Icon name='ui:'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_color_attr_passes_through()
        {
            var xml = Header + "<Icon name='ui:gear' color='#ff0000'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("#ff0000", icon.Attributes["color"]);
        }

        [Test]
        public void Native_size_on_Frame_throws()
        {
            var xml = Header + "<Frame size='native'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("native size only allowed on <Icon>", ex.Message);
        }

        [Test]
        public void Native_width_on_Frame_throws()
        {
            var xml = Header + "<Frame width='native'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Native_size_on_Icon_ok()
        {
            var xml = Header + "<Icon name='ui:x' size='native'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_variant_overrides_supported()
        {
            var xml = Header +
                "<Icon name='ui:sun' name.dark='ui:moon' color.dark='#000'/>"
                + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.IsTrue(icon.VariantOverrides.ContainsKey("name"));
            Assert.IsTrue(icon.VariantOverrides.ContainsKey("color"));
        }

        [Test]
        public void Native_size_via_variant_on_Frame_throws()
        {
            var xml = Header + "<Frame size.dark='native'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("native size only allowed on <Icon>", ex.Message);
        }

        [Test]
        public void Native_width_via_variant_on_Frame_throws()
        {
            var xml = Header + "<Frame width.dark='native'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_invalid_variant_name_throws()
        {
            var xml = Header + "<Icon name='ui:sun' name.dark='bad-format'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("name.dark", ex.Message);
        }

        [Test]
        public void Icon_empty_variant_name_throws()
        {
            var xml = Header + "<Icon name='ui:sun' name.dark=''/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_valid_variant_name_passes()
        {
            var xml = Header + "<Icon name='ui:sun' name.dark='ui:moon'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_with_space_in_iconname_passes()
        {
            // Real-world packs (Solar Icons, Material) ship PNGs like 'Alt Arrow Right.png'.
            // Renaming on every pack upgrade is friction; allow spaces in the icon-name half.
            var xml = Header + "<Icon name='solar:Alt Arrow Right'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("solar:Alt Arrow Right", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_name_with_space_in_setname_throws()
        {
            // Set name is a reference key (matches SpriteSet.setName); keep it strict.
            var xml = Header + "<Icon name='my set:Forward'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_variant_with_space_in_iconname_passes()
        {
            var xml = Header +
                "<Icon name='solar:Sun' name.dark='solar:Alt Moon Bold'/>"
                + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_with_slash_in_iconname_passes()
        {
            // Subfolder disambiguation: when two PNGs in different subfolders share
            // the same basename, the author writes `set:Subfolder/name` to point at
            // a specific one. '/' is allowed in the icon-name half.
            var xml = Header + "<Icon name='ui:Combat/heart'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("ui:Combat/heart", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_name_with_slash_in_setname_throws()
        {
            // Set name remains strict (it's a reference key matching SpriteSet.setName).
            var xml = Header + "<Icon name='my/set:heart'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_variant_with_slash_in_iconname_passes()
        {
            var xml = Header +
                "<Icon name='ui:UI/heart' name.dark='ui:Combat/heart'/>"
                + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_with_ampersand_in_iconname_passes()
        {
            // Solar Bold Duotone ships PNGs like 'Map & Location/Radar 2.png'.
            // '&' is a legal filesystem char; XML attribute parser decodes &amp; → &.
            var xml = Header + "<Icon name='solar:Map &amp; Location/Radar 2'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("solar:Map & Location/Radar 2", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_name_with_punctuation_in_iconname_passes()
        {
            // Real-world icon-pack PNGs use parens, commas, apostrophes, dots.
            // Icon-name half mirrors filesystem; only ':' (the delimiter) is forbidden.
            var xml = Header +
                "<Icon name=\"solar:Files (Group)/file 1.0,v2&apos;s\"/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var icon = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("solar:Files (Group)/file 1.0,v2's", icon.Attributes["name"]);
        }

        [Test]
        public void Icon_name_with_extra_colon_in_iconname_throws()
        {
            // Only the FIRST ':' is the set/icon delimiter. A second ':' in the
            // icon-name half is ambiguous and rejected.
            var xml = Header + "<Icon name='solar:Sub/Foo:Bar'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_full_template_placeholder_parses()
        {
            // Templates: `<Icon name="{{iconName}}"/>` is a Param substitution that
            // resolves at TemplateExpander time. Parser sees the unsubstituted form
            // and must not reject it (IconAtlasSyncer already treats '{{' as dynamic).
            var xml = "<?xml version='1.0'?><PromptUGUI version='1'>" +
                "<Template name='IconBtn'><Param name='iconName'/>" +
                "<Btn><Icon name='{{iconName}}'/></Btn></Template>" +
                "<Screen name='S'><Frame/></Screen></PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_name_partial_template_placeholder_parses()
        {
            // Set-side fixed, icon-name-side dynamic — `solar:{{name}}`. Final form
            // is determined at expansion; parser must accept the literal as-is.
            var xml = Header + "<Icon name='solar:{{name}}'/>" + Footer;
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Icon_variant_template_placeholder_parses()
        {
            // Variant override may also be a Template Param.
            var xml = "<?xml version='1.0'?><PromptUGUI version='1'>" +
                "<Template name='T'><Param name='dark'/>" +
                "<Icon name='solar:Sun' name.dark='{{dark}}'/></Template>" +
                "<Screen name='S'><Frame/></Screen></PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }
    }
}
