using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Application
{
    public class ScreenReferenceResolutionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parser_stores_reference_attr_on_screen_root()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = doc.Screens[0];
            Assert.AreEqual("1920x1080", screen.Root.Attributes["reference"]);
        }

        [Test]
        public void Parser_screen_without_reference_has_no_attr()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = doc.Screens[0];
            Assert.IsFalse(screen.Root.Attributes.ContainsKey("reference"));
        }

        [Test]
        public void Parser_stores_reference_variant_override()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = doc.Screens[0];
            Assert.AreEqual("1920x1080", screen.Root.Attributes["reference"]);
            Assert.IsTrue(screen.Root.VariantOverrides.ContainsKey("reference"));
            var list = screen.Root.VariantOverrides["reference"];
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("mobile", list[0].Variant);
            Assert.AreEqual("1080x1920", list[0].Value);
        }

        [Test]
        public void Parser_stores_reference_variant_empty_clears_base()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.tv=''>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.VariantOverrides["reference"];
            Assert.AreEqual("tv", list[0].Variant);
            Assert.AreEqual("", list[0].Value);
        }
    }
}
