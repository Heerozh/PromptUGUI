using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using R3;

namespace PromptUGUI.Tests.Application
{
    public class VariantStoreTests
    {
        [Test]
        public void IsActive_default_false()
        {
            var store = new VariantStore();
            Assert.IsFalse(store.IsActive("mobile"));
        }

        [Test]
        public void Set_true_then_false_toggles_state()
        {
            var store = new VariantStore();
            store.Set("mobile", true);
            Assert.IsTrue(store.IsActive("mobile"));
            store.Set("mobile", false);
            Assert.IsFalse(store.IsActive("mobile"));
        }

        [Test]
        public void Multiple_variants_active_simultaneously()
        {
            var store = new VariantStore();
            store.Set("a", true);
            store.Set("b", true);
            Assert.IsTrue(store.IsActive("a"));
            Assert.IsTrue(store.IsActive("b"));
        }

        [Test]
        public void Changed_fires_only_on_state_transition()
        {
            var store = new VariantStore();
            var events = 0;
            store.Changed.Subscribe(_ => events++);

            store.Set("a", true);
            Assert.AreEqual(1, events);

            store.Set("a", true);   // no-op (already active)
            Assert.AreEqual(1, events);

            store.Set("a", false);
            Assert.AreEqual(2, events);

            store.Set("a", false);  // no-op (already inactive)
            Assert.AreEqual(2, events);
        }

        [Test]
        public void Set_pair_writes_both_before_one_Changed()
        {
            var store = new VariantStore();
            store.Set("landscape", true);
            var seen = new List<string>();
            store.Changed.Subscribe(_ => seen.Add($"{store.IsActive("portrait")},{store.IsActive("landscape")}"));

            store.Set("portrait", true, "landscape", false);
            CollectionAssert.AreEqual(new[] { "True,False" }, seen);

            store.Set("portrait", true, "landscape", false);   // no-op (already there)
            CollectionAssert.AreEqual(new[] { "True,False" }, seen);

            store.Set("portrait", true, "landscape", true);    // only one half changes: still one event
            CollectionAssert.AreEqual(new[] { "True,False", "True,True" }, seen);
        }

        [Test]
        public void Reset_clears_all_active_variants()
        {
            var store = new VariantStore();
            store.Set("a", true);
            store.Set("b", true);
            store.Reset();
            Assert.IsFalse(store.IsActive("a"));
            Assert.IsFalse(store.IsActive("b"));
        }
    }
}
