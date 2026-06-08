using NUnit.Framework;
using PromptUGUI.Application.Toasts;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class ToastStackTests
    {
        // heights 按到达顺序 oldest→newest。newest（末位）落基准，旧的沿 dir 被顶开。
        [Test]
        public void Single_sits_at_base()
        {
            var t = ToastStack.ComputeTargets(new[] { 40f }, 10f, Vector2.up, new Vector2(0, 5));
            Assert.AreEqual(new Vector2(0, 5), t[0]);
        }

        [Test]
        public void Newer_pushes_older_up()
        {
            // i0 oldest=30, i1=50, i2 newest=100; spacing=10; dir=+y; base=(0,0)
            var t = ToastStack.ComputeTargets(new[] { 30f, 50f, 100f }, 10f, Vector2.up, Vector2.zero);
            Assert.AreEqual(new Vector2(0, 0), t[2]);     // newest at base
            Assert.AreEqual(new Vector2(0, 110), t[1]);   // +100+10
            Assert.AreEqual(new Vector2(0, 170), t[0]);   // +110+50+10
        }

        [Test]
        public void Direction_down_for_top_group()
        {
            var t = ToastStack.ComputeTargets(new[] { 40f, 60f }, 10f, Vector2.down, new Vector2(0, -20));
            Assert.AreEqual(new Vector2(0, -20), t[1]);   // newest at base
            Assert.AreEqual(new Vector2(0, -90), t[0]);   // base + down*(60+10)
        }

        [Test]
        public void Empty_returns_empty()
            => Assert.AreEqual(0, ToastStack.ComputeTargets(new float[0], 10f, Vector2.up, Vector2.zero).Length);
    }
}
