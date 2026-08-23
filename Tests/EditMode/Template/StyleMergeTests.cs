using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Template
{
    /// <summary>
    /// `class=` is consumed entirely inside <see cref="TemplateExpander"/>. These tests assert on
    /// the expanded IR, which is exactly what ScreenInstantiator sees — including the fact that no
    /// `class` attribute survives into it.
    /// </summary>
    public class StyleMergeTests
    {
        private static UIDocument Expand(string body)
            => TemplateExpander.Expand(UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>"));

        private static ElementNode FirstChild(UIDocument doc) => doc.Screens[0].Root.Children[0];

        private static ElementNode FindById(ElementNode node, string id)
        {
            if (node.Id == id) return node;
            return node.Children.Select(c => FindById(c, id)).FirstOrDefault(f => f != null);
        }

        [Test]
        public void Class_MergesStyleAttributesOntoNode()
        {
            var doc = Expand(@"
                <Style name='card' color='#222' radius='16' borderWidth='1'/>
                <Screen name='S'><Frame id='f' class='card' height='220'/></Screen>");
            var f = FirstChild(doc);
            Assert.AreEqual("#222", f.Attributes["color"]);
            Assert.AreEqual("16", f.Attributes["radius"]);
            Assert.AreEqual("1", f.Attributes["borderWidth"]);
            Assert.AreEqual("220", f.Attributes["height"], "inline attributes survive untouched");
        }

        [Test]
        public void Class_IsConsumed_NotPresentInExpandedTree()
        {
            var doc = Expand(@"
                <Style name='card' color='#222'/>
                <Screen name='S'><Frame id='f' class='card'/></Screen>");
            Assert.IsFalse(FirstChild(doc).Attributes.ContainsKey("class"),
                "the runtime must never see class= — that is what makes the feature free");
        }

        [Test]
        public void Inline_BeatsClass()
        {
            var doc = Expand(@"
                <Style name='card' radius='16'/>
                <Screen name='S'><Frame id='f' class='card' radius='4'/></Screen>");
            Assert.AreEqual("4", FirstChild(doc).Attributes["radius"]);
        }

        [Test]
        public void InlineBaseValue_AlsoMasksStylesVariantOverride()
        {
            // Atomic by attribute NAME: writing radius= inline takes the whole 'radius' slot,
            // so the style's radius.mobile does not sneak in alongside it.
            var doc = Expand(@"
                <Style name='card' radius='16' radius.mobile='8'/>
                <Screen name='S'><Frame id='f' class='card' radius='4'/></Screen>");
            var f = FirstChild(doc);
            Assert.AreEqual("4", f.Attributes["radius"]);
            Assert.IsFalse(f.VariantOverrides.ContainsKey("radius"));
        }

        [Test]
        public void InlineVariantOverride_MasksStylesBaseValue()
        {
            // Mirror of the above: the node declaring only radius.mobile still owns 'radius'.
            var doc = Expand(@"
                <Style name='card' radius='16'/>
                <Screen name='S'><Frame id='f' class='card' radius.mobile='2'/></Screen>");
            var f = FirstChild(doc);
            Assert.IsFalse(f.Attributes.ContainsKey("radius"));
            Assert.AreEqual("2", f.VariantOverrides["radius"][0].Value);
        }

        [Test]
        public void StyleVariantOverrides_MergeIntoNodeVariantOverrides()
        {
            var doc = Expand(@"
                <Style name='card' radius='16' radius.mobile='8'/>
                <Screen name='S'><Frame id='f' class='card'/></Screen>");
            var f = FirstChild(doc);
            Assert.AreEqual("16", f.Attributes["radius"]);
            Assert.AreEqual(("mobile", "8"), f.VariantOverrides["radius"][0]);
        }

        [Test]
        public void MultipleClasses_RightOverridesLeft()
        {
            var doc = Expand(@"
                <Style name='card' color='#111' radius='16'/>
                <Style name='loud' color='#f00'/>
                <Screen name='S'><Frame id='f' class='card loud'/></Screen>");
            var f = FirstChild(doc);
            Assert.AreEqual("#f00", f.Attributes["color"]);
            Assert.AreEqual("16", f.Attributes["radius"], "names the right class does not declare stay");
        }

        [Test]
        public void MultipleClasses_RightMasksLeftAtomically()
        {
            var doc = Expand(@"
                <Style name='card' radius='16' radius.mobile='8'/>
                <Style name='sharp' radius='0'/>
                <Screen name='S'><Frame id='f' class='card sharp'/></Screen>");
            var f = FirstChild(doc);
            Assert.AreEqual("0", f.Attributes["radius"]);
            Assert.IsFalse(f.VariantOverrides.ContainsKey("radius"),
                "the right-hand class redeclares 'radius', so the left's variant value is masked too");
        }

        [Test]
        public void UnknownClass_Throws_ListingKnownNames()
        {
            var ex = Assert.Throws<TemplateException>(() => Expand(@"
                <Style name='card' color='#111'/>
                <Screen name='S'><Frame class='nope'/></Screen>"));
            StringAssert.Contains("nope", ex.Message);
            StringAssert.Contains("card", ex.Message);
        }

        [Test]
        public void EmptyClass_Throws()
        {
            var ex = Assert.Throws<TemplateException>(() => Expand(@"
                <Style name='card' color='#111'/>
                <Screen name='S'><Frame class='   '/></Screen>"));
            StringAssert.Contains("no style", ex.Message);
        }

        [Test]
        public void Class_AppliesInsideTemplateBody()
        {
            var doc = Expand(@"
                <Style name='card' color='#222'/>
                <Template name='Panel'><Frame id='inner' class='card'/></Template>
                <Screen name='S'><Panel id='p'/></Screen>");
            var inner = FirstChild(doc);
            Assert.AreEqual("#222", inner.Attributes["color"]);
            Assert.IsFalse(inner.Attributes.ContainsKey("class"));
        }

        [Test]
        public void Class_ValueCanComeFromTemplateParam()
        {
            var doc = Expand(@"
                <Style name='card' color='#222'/>
                <Style name='loud' color='#f00'/>
                <Template name='Panel'>
                  <Param name='skin' default='card'/>
                  <Frame id='inner' class='{{skin}}'/>
                </Template>
                <Screen name='S'><Panel id='p' skin='loud'/></Screen>");
            Assert.AreEqual("#f00", FirstChild(doc).Attributes["color"]);
        }

        [Test]
        public void Class_AppliesInsideVariantAddBlock()
        {
            var doc = Expand(@"
                <Style name='card' color='#222'/>
                <Screen name='S'>
                  <Frame id='host'/>
                  <Variant when='mobile'>
                    <Add into='host'><Frame id='extra' class='card'/></Add>
                  </Variant>
                </Screen>");
            var added = doc.Screens[0].Variants[0].Adds[0].Children[0];
            Assert.AreEqual("#222", added.Attributes["color"]);
            Assert.IsFalse(added.Attributes.ContainsKey("class"));
        }

        [Test]
        public void ClassOnTemplateInvocation_FeedsParamsAndCommonAttrs()
        {
            var doc = Expand(@"
                <Style name='wide' tone='#0f0' width='300'/>
                <Template name='Panel'>
                  <Param name='tone'/>
                  <Frame id='inner' color='{{tone}}'/>
                </Template>
                <Screen name='S'><Panel id='p' class='wide'/></Screen>");
            var root = FirstChild(doc);
            Assert.AreEqual("#0f0", root.Attributes["color"], "'tone' matched a <Param>");
            Assert.AreEqual("300", root.Attributes["width"], "'width' is a common attr → lands on the instance root");
        }

        [Test]
        public void ClassOnTemplateInvocation_DropsInapplicableNamesSilently()
        {
            // A style is broadcast, so an attribute the template can't accept must not become the
            // hard "unknown attribute" error an inline attribute would trigger.
            var doc = Expand(@"
                <Style name='card' color='#222' radius='16' width='300'/>
                <Template name='Panel'><Frame id='inner'/></Template>
                <Screen name='S'><Panel id='p' class='card'/></Screen>");
            var root = FirstChild(doc);
            Assert.AreEqual("300", root.Attributes["width"]);
            Assert.IsFalse(root.Attributes.ContainsKey("radius"));
            Assert.IsFalse(root.Attributes.ContainsKey("color"));
        }

        [Test]
        public void ClassOnTemplateInvocation_DropsVariantOverridesOnParams()
        {
            // ExpandInvocation hard-rejects a .variant suffix on a Param; a broadcast style must
            // not be able to trigger that on an unrelated node.
            Assert.DoesNotThrow(() => Expand(@"
                <Style name='card' tone.mobile='#f00' width.mobile='100'/>
                <Template name='Panel'>
                  <Param name='tone' default='#fff'/>
                  <Frame id='inner' color='{{tone}}'/>
                </Template>
                <Screen name='S'><Panel id='p' class='card'/></Screen>"));
        }

        [Test]
        public void ClassOnNestedChildren_AppliesThroughoutTree()
        {
            var doc = Expand(@"
                <Style name='card' color='#222'/>
                <Screen name='S'>
                  <Frame id='outer'><VStack><Frame id='deep' class='card'/></VStack></Frame>
                </Screen>");
            var deep = FindById(FirstChild(doc), "deep");
            Assert.IsNotNull(deep);
            Assert.AreEqual("#222", deep.Attributes["color"]);
        }

        [Test]
        public void NodeWithoutClass_IsUntouched()
        {
            var doc = Expand(@"
                <Style name='card' color='#222'/>
                <Screen name='S'><Frame id='f' height='10'/></Screen>");
            var f = FirstChild(doc);
            Assert.AreEqual(1, f.Attributes.Count);
            Assert.AreEqual("10", f.Attributes["height"]);
        }
    }
}
