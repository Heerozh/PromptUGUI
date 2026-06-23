using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateOffsetSetTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void For_MapsPressedAndSelected_OthersZero()
        {
            var set = new StateOffsetSet(new Vector2(0, -4), new Vector2(1, -2));
            Assert.AreEqual(new Vector2(0, -4), set.For(InteractState.Pressed));
            Assert.AreEqual(new Vector2(1, -2), set.For(InteractState.Selected));
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Normal));
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Hover));
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Disabled));
        }

        [Test]
        public void For_UnsetState_ReturnsZero()
        {
            var set = new StateOffsetSet(new Vector2(0, -4), null);
            Assert.AreEqual(Vector2.zero, set.For(InteractState.Selected), "unset selected -> zero");
            Assert.AreEqual(new Vector2(0, -4), set.For(InteractState.Pressed));
        }

        [Test]
        public void HasAny_TrueWhenAnyPresent_FalseWhenDefault()
        {
            Assert.IsFalse(default(StateOffsetSet).HasAny);
            Assert.IsTrue(new StateOffsetSet(new Vector2(0, -1), null).HasAny);
            Assert.IsTrue(new StateOffsetSet(null, new Vector2(0, -1)).HasAny);
        }

        [Test]
        public void Parse_ValidPair_NegativeYIsDown()
        {
            var v = StateOffsetSet.Parse("0,-4");
            Assert.IsTrue(v.HasValue);
            Assert.AreEqual(new Vector2(0f, -4f), v.Value);
        }

        [Test]
        public void Parse_EmptyOrNone_ReturnsNull()
        {
            Assert.IsNull(StateOffsetSet.Parse(""));
            Assert.IsNull(StateOffsetSet.Parse("  "));
            Assert.IsNull(StateOffsetSet.Parse(null));
            Assert.IsNull(StateOffsetSet.Parse("none"));
        }

        [Test]
        public void Parse_BadFormat_Throws()
        {
            Assert.Throws<ArgumentException>(() => StateOffsetSet.Parse("5"));
            Assert.Throws<FormatException>(() => StateOffsetSet.Parse("a,b"));
        }
    }
}
