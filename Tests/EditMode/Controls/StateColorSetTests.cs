using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateColorSetTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void For_MapsEachState_NormalIsNull()
        {
            var set = new StateColorSet(Color.red, Color.green, Color.blue, Color.gray);
            Assert.AreEqual(Color.red, set.For(InteractState.Hover));
            Assert.AreEqual(Color.green, set.For(InteractState.Pressed));
            Assert.AreEqual(Color.blue, set.For(InteractState.Selected));
            Assert.AreEqual(Color.gray, set.For(InteractState.Disabled));
            Assert.IsNull(set.For(InteractState.Normal));
        }

        [Test]
        public void HasAny_TrueWhenAnyPresent_FalseWhenAllNull()
        {
            Assert.IsFalse(default(StateColorSet).HasAny);
            Assert.IsTrue(new StateColorSet(null, Color.red, null, null).HasAny);
            Assert.IsFalse(StateColorSet.Resolve("", null, " ", "").HasAny, "all-blank Resolve -> HasAny false");
        }

        [Test]
        public void Resolve_EmptyOrNull_BecomesNull_LiteralBecomesColor()
        {
            var set = StateColorSet.Resolve("", null, "#ff0000", "  ");
            Assert.IsNull(set.For(InteractState.Hover), "empty string -> null");
            Assert.IsNull(set.For(InteractState.Pressed), "null -> null");
            Assert.IsNull(set.For(InteractState.Disabled), "whitespace -> null");
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), set.For(InteractState.Selected));
        }
    }
}
