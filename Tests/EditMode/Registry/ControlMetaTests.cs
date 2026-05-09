using NUnit.Framework;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Registry
{
    public class ControlMetaTests
    {

        private class Sample
        {
            [UIAttr] public string Text { get; set; }
            [UIAttr("count")] public int Count { get; set; }
            public string IgnoredProp { get; set; }
        }

        [Test]
        public void Builds_setter_for_each_UIAttr_property()
        {
            var meta = ControlMeta.Build(typeof(Sample));
            Assert.IsTrue(meta.HasAttribute("text"));
            Assert.IsTrue(meta.HasAttribute("count"));
            Assert.IsFalse(meta.HasAttribute("ignoredProp"));
        }

        [Test]
        public void String_setter_assigns_value_directly()
        {
            var meta = ControlMeta.Build(typeof(Sample));
            var s = new Sample();
            meta.Apply(s, "text", "Hello");
            Assert.AreEqual("Hello", s.Text);
        }

        [Test]
        public void Int_setter_parses_string_to_int()
        {
            var meta = ControlMeta.Build(typeof(Sample));
            var s = new Sample();
            meta.Apply(s, "count", "42");
            Assert.AreEqual(42, s.Count);
        }

        [Test]
        public void Default_attribute_name_is_camelCase_of_property()
        {
            var meta = ControlMeta.Build(typeof(Sample));
            Assert.IsTrue(meta.HasAttribute("text"));
        }

        [Test]
        public void Apply_unknown_attribute_throws()
        {
            var meta = ControlMeta.Build(typeof(Sample));
            var s = new Sample();
            Assert.Throws<System.ArgumentException>(() =>
                meta.Apply(s, "unknown", "x"));
        }
    }
}
