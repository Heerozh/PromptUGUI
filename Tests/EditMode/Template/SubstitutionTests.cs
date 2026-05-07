using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class SubstitutionTests {
        Dictionary<string, string> P(params (string k, string v)[] kv) {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        [Test]
        public void Replaces_single_placeholder() {
            Assert.AreEqual("背包",
                Substitution.Apply("{{title}}", P(("title", "背包"))));
        }

        [Test]
        public void Replaces_with_surrounding_text() {
            Assert.AreEqual("icons/sword.png",
                Substitution.Apply("icons/{{icon}}.png", P(("icon", "sword"))));
        }

        [Test]
        public void Replaces_multiple_placeholders() {
            Assert.AreEqual("a-b-c",
                Substitution.Apply("{{x}}-{{y}}-{{z}}",
                    P(("x", "a"), ("y", "b"), ("z", "c"))));
        }

        [Test]
        public void Whitespace_inside_braces_allowed() {
            Assert.AreEqual("foo",
                Substitution.Apply("{{  name  }}", P(("name", "foo"))));
        }

        [Test]
        public void Throws_on_unknown_param() {
            Assert.Throws<TemplateException>(() =>
                Substitution.Apply("{{missing}}", P(("other", "x"))));
        }

        [Test]
        public void Returns_input_when_no_placeholders() {
            Assert.AreEqual("plain text",
                Substitution.Apply("plain text", P(("title", "x"))));
        }

        [Test]
        public void Null_input_returns_null() {
            Assert.IsNull(Substitution.Apply(null, P(("x", "y"))));
        }
    }
}
