using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class PixelScaleSolverTests
    {
        [TestCase(1920f, 1080f, 1920f, 1080f, 1f)]
        [TestCase(3840f, 2160f, 1920f, 1080f, 2f)]
        [TestCase(5760f, 3240f, 1920f, 1080f, 3f)]
        [TestCase(7680f, 4320f, 1920f, 1080f, 4f)]
        // 21:9 ultrawide screen with 16:9 design: vertical axis is tighter -> 1x
        [TestCase(3840f, 1620f, 1920f, 1080f, 1f)]
        // 9:16 tall screen with 16:9 design: horizontal axis is tighter -> 1x
        [TestCase(1920f, 2160f, 1920f, 1080f, 1f)]
        // Sub-1x snaps to 1/2^n
        [TestCase(1366f, 768f, 1920f, 1080f, 0.5f)]
        [TestCase(1280f, 720f, 1920f, 1080f, 0.5f)]
        [TestCase(960f, 540f, 1920f, 1080f, 0.5f)]
        [TestCase(480f, 270f, 1920f, 1080f, 0.25f)]
        [TestCase(240f, 135f, 1920f, 1080f, 0.125f)]
        [TestCase(100f, 100f, 1920f, 1080f, 0.03125f)]
        // Degenerate inputs fall back to 1
        [TestCase(0f, 100f, 1920f, 1080f, 1f)]
        [TestCase(100f, 0f, 1920f, 1080f, 1f)]
        [TestCase(1920f, 1080f, 0f, 0f, 1f)]
        [TestCase(-1f, 100f, 1920f, 1080f, 1f)]
        public void Solve_returns_expected_factor(
            float sw, float sh, float dw, float dh, float expected)
        {
            var f = PixelScaleSolver.Solve(new Vector2(sw, sh), new Vector2(dw, dh));
            Assert.AreEqual(expected, f, 1e-6f);
        }

        // powerOfTwoOnly: the magnify segment (raw >= 1) snaps DOWN to the largest
        // power of two <= raw, so the whole ladder is ...0.25, 0.5, 1, 2, 4, 8...
        // (no 3x/5x/6x/7x). The sub-1x segment is already 1/2^n, so it is unchanged.
        [TestCase(1920f, 1080f, 1920f, 1080f, 1f)]    // raw 1   -> 1
        [TestCase(3840f, 2160f, 1920f, 1080f, 2f)]    // raw 2   -> 2
        [TestCase(5760f, 3240f, 1920f, 1080f, 2f)]    // raw 3   -> 2
        [TestCase(7680f, 4320f, 1920f, 1080f, 4f)]    // raw 4   -> 4
        [TestCase(9600f, 5400f, 1920f, 1080f, 4f)]    // raw 5   -> 4
        [TestCase(13440f, 7560f, 1920f, 1080f, 4f)]   // raw 7   -> 4
        [TestCase(15360f, 8640f, 1920f, 1080f, 8f)]   // raw 8   -> 8
        // sub-1x is already a power of two -> identical to the default ladder
        [TestCase(1366f, 768f, 1920f, 1080f, 0.5f)]   // raw ~0.71 -> 0.5
        [TestCase(480f, 270f, 1920f, 1080f, 0.25f)]   // raw 0.25  -> 0.25
        // degenerate inputs still fall back to 1
        [TestCase(0f, 100f, 1920f, 1080f, 1f)]
        public void Solve_powerOfTwoOnly_snaps_to_power_of_two(
            float sw, float sh, float dw, float dh, float expected)
        {
            var f = PixelScaleSolver.Solve(
                new Vector2(sw, sh), new Vector2(dw, dh), powerOfTwoOnly: true);
            Assert.AreEqual(expected, f, 1e-6f);
        }
    }
}
