using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.IR;

namespace PromptUGUI.Tests.Variants
{
    public class VariantLookupTests
    {
        private static ElementNode Node(string baseValue, params (string variant, string value)[] overrides)
        {
            var n = new ElementNode("X");
            if (baseValue != null) n.Attributes["size"] = baseValue;
            if (overrides.Length > 0)
            {
                var list = new List<(string, string)>();
                foreach (var o in overrides) list.Add(o);
                n.VariantOverrides["size"] = list;
            }
            return n;
        }

        [Test]
        public void No_active_override_falls_back_to_the_base_value()
        {
            var n = Node("10", ("mobile", "20"));
            Assert.AreEqual("10", VariantLookup.Resolve(n, "size", _ => false, out var variant));
            Assert.IsNull(variant);
        }

        [Test]
        public void The_last_active_override_in_declaration_order_wins()
        {
            var n = Node("10", ("mobile", "20"), ("portrait", "30"));
            var active = new HashSet<string> { "mobile", "portrait" };
            Assert.AreEqual("30", VariantLookup.Resolve(n, "size", active.Contains, out var variant));
            Assert.AreEqual("portrait", variant);

            active.Remove("portrait");
            Assert.AreEqual("20", VariantLookup.Resolve(n, "size", active.Contains, out variant));
            Assert.AreEqual("mobile", variant);
        }

        [Test]
        public void An_unset_attribute_resolves_to_null()
        {
            var n = Node(null);
            Assert.IsNull(VariantLookup.Resolve(n, "size", _ => true));
        }

        [Test]
        public void An_override_without_a_base_resolves_only_while_active()
        {
            var n = Node(null, ("mobile", "20"));
            Assert.AreEqual("20", VariantLookup.Resolve(n, "size", v => v == "mobile"));
            Assert.IsNull(VariantLookup.Resolve(n, "size", _ => false));
        }
    }
}
