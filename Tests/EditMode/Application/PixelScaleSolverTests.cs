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
    }
}
