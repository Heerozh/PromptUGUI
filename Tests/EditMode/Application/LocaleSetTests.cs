using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using R3;

namespace PromptUGUI.Tests.Application {
    public class LocaleSetTests {
        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test] public void Initial_CurrentIsNull() {
            Assert.IsNull(UI.Locale.Current);
        }

        [Test] public void Set_UpdatesCurrent_FiresChanged() {
            int n = 0;
            UI.Locale.Changed += () => n++;
            UI.Locale.Set("en");
            Assert.AreEqual("en", UI.Locale.Current);
            Assert.AreEqual(1, n);
        }

        [Test] public void Set_SameValueTwice_DoesNotFire() {
            int n = 0;
            UI.Locale.Set("en");
            UI.Locale.Changed += () => n++;
            UI.Locale.Set("en");
            Assert.AreEqual(0, n);
        }

        [Test] public void Set_TogglesVariantNamedAfterLocale() {
            UI.Locale.Set("zh-Hans");
            Assert.IsTrue(UI.Variants.IsActive("zh-Hans"));
            UI.Locale.Set("en");
            Assert.IsFalse(UI.Variants.IsActive("zh-Hans"));
            Assert.IsTrue(UI.Variants.IsActive("en"));
        }

        [Test] public void Set_Null_ClearsVariantAndCurrent() {
            UI.Locale.Set("en");
            UI.Locale.Set(null);
            Assert.IsNull(UI.Locale.Current);
            Assert.IsFalse(UI.Variants.IsActive("en"));
        }

        [Test] public void Set_TriggersVariantChanged() {
            int n = 0;
            UI.VariantStore.Changed.Subscribe(_ => n++);
            UI.Locale.Set("en");
            Assert.GreaterOrEqual(n, 1);
        }

        [Test] public void Tr_StaticMethod_DelegatesToTrResolver() {
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry { Msgid = "hi", Msgstr = "Hello" },
            });
            UI.Locale.Set("en");
            Assert.AreEqual("Hello", UI.Tr("hi"));
        }
    }
}
