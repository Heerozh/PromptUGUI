using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class NamespaceLookupTests {

        DocumentLoader.LoadedDoc Make(
            ElementNode rootChild,
            (string ns, string name, ElementNode body)[] templates) {
            var l = new DocumentLoader.LoadedDoc {
                EntrySrc = "test",
            };
            var screen = new ScreenDef("S", new ElementNode("__root__"));
            screen.Root.Children.Add(rootChild);
            l.Screens.Add(screen);
            foreach (var (ns, name, body) in templates) {
                var t = new TemplateDef(name) { Body = body };
                l.Templates[new DocumentLoader.TemplateKey(ns, name)] = t;
            }
            return l;
        }

        [Test]
        public void Namespaced_template_invocation_resolved() {
            var body = new ElementNode("Frame");
            var inv = new ElementNode("Foo", "ns");
            var loaded = Make(inv, new[] {
                (ns: "ns", name: "Foo", body: body)
            });
            var expanded = TemplateExpander.Expand(loaded);
            Assert.AreEqual("Frame", expanded.Screens[0].Root.Children[0].Tag);
        }

        [Test]
        public void Plain_invocation_doesnt_match_namespaced_template() {
            // <Foo/> 应不命中 (ns="ns", name="Foo")，被当作未注册控件——
            // expander 不抛错，下游 instantiator 才会抛"未注册"。
            var body = new ElementNode("Frame");
            var inv = new ElementNode("Foo");   // ns=null
            var loaded = Make(inv, new[] {
                (ns: "ns", name: "Foo", body: body)
            });
            var expanded = TemplateExpander.Expand(loaded);
            Assert.AreEqual("Foo", expanded.Screens[0].Root.Children[0].Tag);
            Assert.IsNull(expanded.Screens[0].Root.Children[0].Namespace);
        }

        [Test]
        public void Unknown_namespace_throws() {
            var inv = new ElementNode("Foo", "missing");
            var loaded = Make(inv, System.Array.Empty<(string, string, ElementNode)>());
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(loaded));
        }
    }
}
