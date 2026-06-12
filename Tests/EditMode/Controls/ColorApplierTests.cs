using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ColorApplierTests
    {
        private GameObject _go;
        private Image _img;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("g", typeof(Image));
            _img = _go.GetComponent<Image>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Solid_SetsGraphicColor_NoTint()
        {
            ColorApplier.Apply(_img, ColorSpec.Solid(Color.red));

            Assert.AreEqual(Color.red, _img.color);
            Assert.IsNull(_img.GetComponent<GradientTint>());
        }

        [Test]
        public void Gradient_AddsEnabledTint_AndWhitensColor()
        {
            ColorApplier.Apply(_img, ColorSpec.Gradient(Color.red, Color.blue));

            var tint = _img.GetComponent<GradientTint>();
            Assert.IsNotNull(tint);
            Assert.IsTrue(tint.enabled);
            Assert.AreEqual(Color.red, tint.Top);
            Assert.AreEqual(Color.blue, tint.Bottom);
            Assert.AreEqual(Color.white, _img.color);
        }

        [Test]
        public void GradientThenSolid_DisablesTint_NotDestroyed()
        {
            ColorApplier.Apply(_img, ColorSpec.Gradient(Color.red, Color.blue));
            ColorApplier.Apply(_img, ColorSpec.Solid(Color.green));

            var tint = _img.GetComponent<GradientTint>();
            Assert.IsNotNull(tint);
            Assert.IsFalse(tint.enabled);
            Assert.AreEqual(Color.green, _img.color);
        }

        [Test]
        public void SolidThenGradient_ReEnablesSameTint()
        {
            ColorApplier.Apply(_img, ColorSpec.Gradient(Color.red, Color.blue));
            ColorApplier.Apply(_img, ColorSpec.Solid(Color.green));
            ColorApplier.Apply(_img, ColorSpec.Gradient(Color.cyan, Color.yellow));

            var tints = _img.GetComponents<GradientTint>();
            Assert.AreEqual(1, tints.Length);
            Assert.IsTrue(tints[0].enabled);
            Assert.AreEqual(Color.cyan, tints[0].Top);
            Assert.AreEqual(Color.yellow, tints[0].Bottom);
        }

        [Test]
        public void Peek_Solid_ReturnsGraphicColor()
        {
            _img.color = Color.magenta;

            var spec = ColorApplier.Peek(_img);

            Assert.IsFalse(spec.IsGradient);
            Assert.AreEqual(Color.magenta, spec.Top);
        }

        [Test]
        public void Peek_Gradient_ReturnsStops()
        {
            ColorApplier.Apply(_img, ColorSpec.Gradient(Color.red, Color.blue));

            var spec = ColorApplier.Peek(_img);

            Assert.IsTrue(spec.IsGradient);
            Assert.AreEqual(Color.red, spec.Top);
            Assert.AreEqual(Color.blue, spec.Bottom);
        }

        [Test]
        public void Peek_DisabledTint_ReadsSolid()
        {
            ColorApplier.Apply(_img, ColorSpec.Gradient(Color.red, Color.blue));
            ColorApplier.Apply(_img, ColorSpec.Solid(Color.green));

            var spec = ColorApplier.Peek(_img);

            Assert.IsFalse(spec.IsGradient);
            Assert.AreEqual(Color.green, spec.Top);
        }
    }
}
