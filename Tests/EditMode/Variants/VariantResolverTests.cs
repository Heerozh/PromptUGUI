using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Variants;

namespace PromptUGUI.Tests.Variants
{
    public class VariantResolverTests
    {
        private static ElementNode Node(string baseAttr = null,
                         params (string variant, string value)[] overrides)
        {
            var n = new ElementNode("X");
            if (baseAttr != null) n.Attributes["size"] = baseAttr;
            if (overrides.Length > 0)
            {
                var list = new List<(string Variant, string Value)>();
                foreach (var (v, val) in overrides) list.Add((v, val));
                n.VariantOverrides["size"] = list;
            }
            return n;
        }

        [Test]
        public void Returns_base_when_no_variant_active()
        {
            var store = new VariantStore();
            Assert.AreEqual("100",
                VariantResolver.ResolveAttribute(Node("100"), "size", store));
        }

        [Test]
        public void Returns_override_when_single_variant_active()
        {
            var store = new VariantStore();
            store.Set("mobile", true);
            var n = Node("100", ("mobile", "200"));
            Assert.AreEqual("200", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Last_active_wins_when_multiple_variants_active()
        {
            // 声明顺序：mobile, tablet；都激活 → tablet 胜（声明在后）
            var store = new VariantStore();
            store.Set("mobile", true);
            store.Set("tablet", true);
            var n = Node("100", ("mobile", "200"), ("tablet", "150"));
            Assert.AreEqual("150", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Earlier_declared_wins_when_later_inactive()
        {
            var store = new VariantStore();
            store.Set("mobile", true);
            // tablet 未激活
            var n = Node("100", ("mobile", "200"), ("tablet", "150"));
            Assert.AreEqual("200", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Returns_base_when_only_inactive_variants_have_overrides()
        {
            var store = new VariantStore();
            store.Set("tablet", true);
            var n = Node("100", ("mobile", "200"));
            Assert.AreEqual("100", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Returns_null_when_no_base_and_no_active_variant()
        {
            var store = new VariantStore();
            var n = Node(null, ("mobile", "200"));
            Assert.IsNull(VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Variant_only_attr_returns_override_when_active()
        {
            var store = new VariantStore();
            store.Set("mobile", true);
            var n = Node(null, ("mobile", "200"));
            Assert.AreEqual("200", VariantResolver.ResolveAttribute(n, "size", store));
        }

        [Test]
        public void Returns_null_when_attr_not_present_at_all()
        {
            var store = new VariantStore();
            var n = new ElementNode("X");
            Assert.IsNull(VariantResolver.ResolveAttribute(n, "missing", store));
        }
    }
}
