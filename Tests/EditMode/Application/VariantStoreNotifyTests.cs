using NUnit.Framework;
using PromptUGUI.Application;
using R3;

namespace PromptUGUI.Tests.Application {
    public class VariantStoreNotifyTests {
        [Test] public void NotifyChangedInternal_FiresChangedWithoutMutatingActiveSet() {
            var store = new VariantStore();
            int count = 0;
            store.Changed.Subscribe(_ => count++);
            store.Set("a", true);                 // 1
            // pre-condition: count==1
            typeof(VariantStore)
                .GetMethod("NotifyChangedInternal",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)
                .Invoke(store, null);             // 2
            Assert.AreEqual(2, count);
            Assert.IsTrue(store.IsActive("a"));
        }
    }
}
