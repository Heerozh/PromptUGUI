using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Application
{
    public class ScreenScaleModeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parser_stores_scale_mode_attr_on_screen_root()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("pixel", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_stores_scale_mode_auto()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("auto", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_screen_without_scale_mode_has_no_attr()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.IsFalse(doc.Screens[0].Root.Attributes.ContainsKey("scale-mode"));
        }

        [Test]
        public void Parser_rejects_invalid_scale_mode_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel-perfect'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode", ex.Message);
            StringAssert.Contains("'auto'", ex.Message);
            StringAssert.Contains("'pixel'", ex.Message);
        }

        [Test]
        public void Parser_accepts_empty_scale_mode_as_unset_semantics()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode=''><Frame/></Screen>
</PromptUGUI>";
            // Empty string is stored verbatim; runtime treats it as 'inherit DefaultScaleMode'.
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_stores_scale_mode_variant_override()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'
          scale-mode='auto'
          scale-mode.portrait='pixel'
          scale-mode.landscape='auto'
          reference='1920x1080'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.VariantOverrides["scale-mode"];
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("portrait", list[0].Variant);
            Assert.AreEqual("pixel", list[0].Value);
            Assert.AreEqual("landscape", list[1].Variant);
            Assert.AreEqual("auto", list[1].Value);
        }

        [Test]
        public void Parser_rejects_invalid_scale_mode_variant_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode.mobile='nope'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode.mobile", ex.Message);
        }

        [Test]
        public void Parser_rejects_scale_mode_variant_with_extra_dot()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode.foo.bar='pixel'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode.foo.bar", ex.Message);
        }
    }
}
