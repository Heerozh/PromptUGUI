using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // State-tint fade rule: a state colour change with a fully-transparent endpoint SNAPS instead of
    // tweening. A straight RGBA lerp between a fully-transparent colour and an opaque one drags RGB
    // through black (a visible flicker — e.g. a transparent Tab fading into its selectedColor on
    // select). Opaque <-> opaque transitions (hover / press feedback) still fade.
    public class StateTintFadeTests
    {
        [Test]
        public void CrossesTransparency_TransparentToOpaque_True()
            => Assert.IsTrue(StateTintReactor.CrossesTransparency(
                new Color(0f, 0f, 0f, 0f), new Color(0.1f, 0.6f, 0.9f, 1f)));

        [Test]
        public void CrossesTransparency_OpaqueToTransparent_True()
            => Assert.IsTrue(StateTintReactor.CrossesTransparency(
                new Color(0.1f, 0.6f, 0.9f, 1f), new Color(0f, 0f, 0f, 0f)));

        [Test]
        public void CrossesTransparency_OpaqueToOpaque_False()
            => Assert.IsFalse(StateTintReactor.CrossesTransparency(
                new Color(0.1f, 0.2f, 0.3f, 1f), new Color(0.4f, 0.5f, 0.6f, 1f)));

        [Test]
        public void CrossesTransparency_BothTransparent_True()
            => Assert.IsTrue(StateTintReactor.CrossesTransparency(
                new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0f)));
    }
}
