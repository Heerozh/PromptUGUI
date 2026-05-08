using NUnit.Framework;
using PromptUGUI.Application;
using R3;

namespace PromptUGUI.Tests.Application {
    public class VariantStoreTests {
        [Test]
        public void IsActive_default_false() {
            var s = new VariantStore();
            Assert.IsFalse(s.IsActive("mobile"));
        }

        [Test]
        public void Set_true_then_false_toggles_state() {
            var s = new VariantStore();
            s.Set("mobile", true);
            Assert.IsTrue(s.IsActive("mobile"));
            s.Set("mobile", false);
            Assert.IsFalse(s.IsActive("mobile"));
        }

        [Test]
        public void Multiple_variants_active_simultaneously() {
            var s = new VariantStore();
            s.Set("a", true);
            s.Set("b", true);
            Assert.IsTrue(s.IsActive("a"));
            Assert.IsTrue(s.IsActive("b"));
        }

        [Test]
        public void Changed_fires_only_on_state_transition() {
            var s = new VariantStore();
            int events = 0;
            s.Changed.Subscribe(_ => events++);

            s.Set("a", true);
            Assert.AreEqual(1, events);

            s.Set("a", true);   // no-op (already active)
            Assert.AreEqual(1, events);

            s.Set("a", false);
            Assert.AreEqual(2, events);

            s.Set("a", false);  // no-op (already inactive)
            Assert.AreEqual(2, events);
        }

        [Test]
        public void Reset_clears_all_active_variants() {
            var s = new VariantStore();
            s.Set("a", true);
            s.Set("b", true);
            s.Reset();
            Assert.IsFalse(s.IsActive("a"));
            Assert.IsFalse(s.IsActive("b"));
        }
    }
}
