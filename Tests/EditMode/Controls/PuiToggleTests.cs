using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class PuiToggleTests
    {
        private const int Normal = 0, Highlighted = 1, Pressed = 2;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuiToggle NewPuiToggle()
        {
            var go = new GameObject("t", typeof(RectTransform));
            var pt = go.AddComponent<PuiToggle>();
            pt.InitStateBroadcast();
            return pt;
        }

        [Test]
        public void Transient_PushesThroughBroadcaster()
        {
            var pt = NewPuiToggle();
            var seen = new List<InteractState>();
            using var _ = pt.OnState.Subscribe(s => seen.Add(s));

            pt.SimulateState(Highlighted);
            pt.SimulateState(Pressed);
            pt.SimulateState(Normal);

            CollectionAssert.AreEqual(
                new[] { InteractState.Normal, InteractState.Hover, InteractState.Pressed, InteractState.Normal },
                seen);
        }

        [Test]
        public void IsOn_ReadsSelectedAtRest()
        {
            var pt = NewPuiToggle();
            Assert.AreEqual(InteractState.Normal, pt.Current);
            pt.isOn = true;                              // fires onValueChanged -> SetOn(true)
            Assert.AreEqual(InteractState.Selected, pt.Current);
            pt.SimulateState(Pressed);
            Assert.AreEqual(InteractState.Pressed, pt.Current);
            pt.SimulateState(Normal);
            Assert.AreEqual(InteractState.Selected, pt.Current);
        }
    }
}
