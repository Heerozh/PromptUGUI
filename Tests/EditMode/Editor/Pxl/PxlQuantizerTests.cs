using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlQuantizerTests
    {
        [Test]
        public void Returns_input_verbatim_when_already_within_budget()
        {
            var colors = new List<Color32>
            { new Color32(1, 2, 3, 255), new Color32(9, 9, 9, 255) };
            var weights = new List<int> { 5, 1 };

            var result = PxlQuantizer.Quantize(colors, weights, 5);
            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void Reduces_a_grayscale_ramp_to_k_distinct_colors()
        {
            var colors = new List<Color32>(256);
            var weights = new List<int>(256);
            for (var i = 0; i < 256; i++)
            {
                colors.Add(new Color32((byte)i, (byte)i, (byte)i, 255));
                weights.Add(1);
            }

            var result = PxlQuantizer.Quantize(colors, weights, 4);
            Assert.AreEqual(4, result.Count, "produces exactly the budget");

            var distinct = new HashSet<int>();
            byte lo = 255, hi = 0;
            foreach (var c in result)
            {
                distinct.Add(c.r);
                if (c.r < lo) lo = c.r;
                if (c.r > hi) hi = c.r;
            }
            Assert.AreEqual(4, distinct.Count, "the four representatives are distinct");
            Assert.Less(lo, 64, "a representative sits near the dark end");
            Assert.Greater(hi, 192, "a representative sits near the light end");
        }

        [Test]
        public void Is_deterministic_across_runs()
        {
            var colors = new List<Color32>();
            var weights = new List<int>();
            for (var i = 0; i < 30; i++)
            {
                colors.Add(new Color32((byte)(i * 7), (byte)(255 - i * 5), (byte)(i * 3), 255));
                weights.Add(i + 1);
            }

            var a = PxlQuantizer.Quantize(colors, weights, 5);
            var b = PxlQuantizer.Quantize(colors, weights, 5);
            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].r, b[i].r);
                Assert.AreEqual(a[i].g, b[i].g);
                Assert.AreEqual(a[i].b, b[i].b);
                Assert.AreEqual(a[i].a, b[i].a);
            }
        }
    }
}
