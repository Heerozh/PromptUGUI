using NUnit.Framework;
using PromptUGUI.Application.Toasts;

namespace PromptUGUI.Tests.Toast
{
    public class ToastDurationTests
    {
        // 固定旋钮：base=1.0, perChar=0.06, min=1.5, max=5.0
        private static float Compute(string text, float over = 0f)
            => ToastDuration.Compute(text, over, 1.0f, 0.06f, 1.5f, 5.0f);

        [Test]
        public void Short_text_clamped_to_min()
            => Assert.AreEqual(1.5f, Compute("hi"), 1e-4f);   // 1.0+2*0.06=1.12 → min 1.5

        [Test]
        public void Scales_with_length()
            => Assert.AreEqual(2.8f, Compute(new string('x', 30)), 1e-4f);  // 1.0+30*0.06=2.8

        [Test]
        public void Long_text_clamped_to_max()
            => Assert.AreEqual(5.0f, Compute(new string('x', 200)), 1e-4f); // 1.0+12=13 → max 5

        [Test]
        public void Explicit_override_wins()
            => Assert.AreEqual(3.0f, Compute(new string('x', 200), 3.0f), 1e-4f);

        [Test]
        public void Null_text_is_min()
            => Assert.AreEqual(1.5f, Compute(null), 1e-4f);
    }
}
