using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Tests.Layout
{
    public class AnchorResolverTests
    {
        [TestCase(AnchorVertical.Top, AnchorHorizontal.Left, 0f, 1f, 0f, 1f, 0f, 1f)]
        [TestCase(AnchorVertical.Top, AnchorHorizontal.Right, 1f, 1f, 1f, 1f, 1f, 1f)]
        [TestCase(AnchorVertical.Bottom, AnchorHorizontal.Center, 0.5f, 0f, 0.5f, 0f, 0.5f, 0f)]
        [TestCase(AnchorVertical.Center, AnchorHorizontal.Center, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f)]
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Stretch, 0f, 0f, 1f, 1f, 0.5f, 0.5f)]
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Left, 0f, 0f, 0f, 1f, 0f, 0.5f)]
        [TestCase(AnchorVertical.Top, AnchorHorizontal.Stretch, 0f, 1f, 1f, 1f, 0.5f, 1f)]
        public void Resolves_anchor_min_max_and_pivot(
            AnchorVertical v, AnchorHorizontal h,
            float minX, float minY, float maxX, float maxY, float pivotX, float pivotY)
        {
            var preset = new AnchorPreset(v, h);
            AnchorResolver.Resolve(preset,
                out var min, out var max, out var pivot);
            Assert.AreEqual(new Vector2(minX, minY), min);
            Assert.AreEqual(new Vector2(maxX, maxY), max);
            Assert.AreEqual(new Vector2(pivotX, pivotY), pivot);
        }
    }

    public class AnchorPresetTests
    {
        [TestCase("top-left", AnchorVertical.Top, AnchorHorizontal.Left)]
        [TestCase("bottom-right", AnchorVertical.Bottom, AnchorHorizontal.Right)]
        [TestCase("stretch-left", AnchorVertical.Stretch, AnchorHorizontal.Left)]
        [TestCase("top-stretch", AnchorVertical.Top, AnchorHorizontal.Stretch)]
        public void Parses_v_h_form(string s, AnchorVertical v, AnchorHorizontal h)
        {
            var a = AnchorPreset.Parse(s);
            Assert.AreEqual(v, a.V);
            Assert.AreEqual(h, a.H);
        }

        [Test]
        public void Center_alias_expands_to_center_center()
        {
            var a = AnchorPreset.Parse("center");
            Assert.AreEqual(AnchorVertical.Center, a.V);
            Assert.AreEqual(AnchorHorizontal.Center, a.H);
        }

        [Test]
        public void Stretch_alias_expands_to_stretch_stretch()
        {
            var a = AnchorPreset.Parse("stretch");
            Assert.AreEqual(AnchorVertical.Stretch, a.V);
            Assert.AreEqual(AnchorHorizontal.Stretch, a.H);
        }

        [Test]
        public void Fill_alias_equals_stretch()
        {
            Assert.AreEqual(AnchorPreset.Parse("stretch"), AnchorPreset.Parse("fill"));
        }

        [TestCase("")]
        [TestCase("topleft")]
        [TestCase("up-left")]
        [TestCase("top-middle")]
        public void Throws_on_invalid(string bad)
        {
            Assert.Throws<System.ArgumentException>(() => AnchorPreset.Parse(bad));
        }
    }
}
