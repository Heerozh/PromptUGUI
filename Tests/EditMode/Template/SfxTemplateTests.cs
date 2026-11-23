using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Template
{
    /// <summary>
    /// sfx= through templates, styles and the <c>&lt;Screen&gt;</c> element (spec 2026-11-23-ui-sfx-cues §5.4, §5.5), and the
    /// call-site common attributes that a theme re-merge used to throw away (P26).
    /// </summary>
    public class SfxTemplateTests
    {
        private static UIDocument Parse(string body) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>");

        private static UIDocument Expand(string body) => TemplateExpander.Expand(Parse(body));

        private static ElementNode FirstChild(UIDocument doc) => doc.Screens[0].Root.Children[0];

        [Test]
        public void A_call_site_sfx_lands_on_the_instance_root_over_the_body_value()
        {
            var root = FirstChild(Expand(
                "<Template name='Danger'><Btn sfx='danger'/></Template><Screen name='S'><Danger sfx='none'/></Screen>"));
            Assert.AreEqual("none", root.Attributes["sfx"]);
            CollectionAssert.AreEquivalent(new[] { "sfx" }, root.InvocationAttrNames);
        }

        [Test]
        public void A_class_on_the_call_site_brings_sfx_but_does_not_count_as_written_there()
        {
            var root = FirstChild(Expand(
                "<Style name='quiet' sfx='none'/><Template name='Danger'><Btn sfx='danger'/></Template>" +
                "<Screen name='S'><Danger class='quiet'/></Screen>"));
            Assert.AreEqual("none", root.Attributes["sfx"]);
            Assert.IsTrue(root.InvocationAttrNames == null || !root.InvocationAttrNames.Contains("sfx"),
                "lint judges what the call site spelled out, not what its class broadcast");
        }

        [Test]
        public void A_body_without_an_invocation_value_records_nothing()
        {
            var root = FirstChild(Expand("<Template name='Danger'><Btn sfx='danger'/></Template><Screen name='S'><Danger/></Screen>"));
            Assert.AreEqual("danger", root.Attributes["sfx"]);
            Assert.IsNull(root.InvocationAttrNames);
        }

        // P26: the body root's class= recorded these names in StyleAttrNames; ReMerge (every Open / ReSolve once any
        // theme carries a style) deleted them and laid the style's value back down over the call site's.
        [Test]
        public void Call_site_common_attributes_survive_the_theme_re_merge()
        {
            var doc = Expand(
                "<Style name='danger' sfx='danger' width='120'/><Template name='Danger'><Btn class='danger'/></Template>" +
                "<Screen name='S'><Danger sfx='none' width='200'/></Screen>");
            var root = FirstChild(doc);
            Assert.AreEqual("none", root.Attributes["sfx"]);
            Assert.AreEqual("200", root.Attributes["width"]);

            StyleMerger.ReMerge(root, doc.Screens[0].Styles);

            Assert.AreEqual("none", root.Attributes["sfx"], "the call site's value, not the style's");
            Assert.AreEqual("200", root.Attributes["width"], "every common attribute, not only sfx");
        }

        [Test]
        public void A_call_site_variant_survives_the_theme_re_merge()
        {
            var doc = Expand(
                "<Style name='danger' sfx='danger'/><Template name='Danger'><Btn class='danger'/></Template>" +
                "<Screen name='S'><Danger sfx.mobile='none'/></Screen>");
            var root = FirstChild(doc);

            StyleMerger.ReMerge(root, doc.Screens[0].Styles);

            Assert.IsTrue(root.VariantOverrides.TryGetValue("sfx", out var list), "the call site's variant is kept");
            Assert.AreEqual(("mobile", "none"), list[0]);
        }

        [Test]
        public void A_Param_named_sfx_is_refused()
        {
            var ex = Assert.Throws<ParseException>(() =>
                Parse("<Template name='B'><Param name='sfx' default=''/><Btn sfx='{{sfx}}'/></Template>"));
            StringAssert.Contains("sfx", ex.Message);
            StringAssert.Contains("common attribute", ex.Message);
        }

        [Test]
        public void Screen_sfx_and_its_variants_reach_the_root()
        {
            var root = Parse("<Screen name='S' sfx='open:a' sfx.mobile='none'/>").Screens[0].Root;
            Assert.AreEqual("open:a", root.Attributes["sfx"]);
            Assert.AreEqual(("mobile", "none"), root.VariantOverrides["sfx"][0]);

            var expanded = Expand("<Screen name='S' sfx='open:a'/>").Screens[0].Root;
            Assert.AreEqual("open:a", expanded.Attributes["sfx"], "screen-level attributes survive expansion");
        }
    }
}
