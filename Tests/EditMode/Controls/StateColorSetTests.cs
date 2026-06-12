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
            var set = new StateColorSet(
                ColorSpec.Solid(Color.red), ColorSpec.Solid(Color.green),
                ColorSpec.Solid(Color.blue), ColorSpec.Solid(Color.gray));
            Assert.AreEqual(Color.red, set.For(InteractState.Hover).Value.Top);
            Assert.AreEqual(Color.green, set.For(InteractState.Pressed).Value.Top);
            Assert.AreEqual(Color.blue, set.For(InteractState.Selected).Value.Top);
            Assert.AreEqual(Color.gray, set.For(InteractState.Disabled).Value.Top);
            Assert.IsNull(set.For(InteractState.Normal));
        }

        [Test]
        public void HasAny_TrueWhenAnyPresent_FalseWhenAllNull()
        {
            Assert.IsFalse(default(StateColorSet).HasAny);
            Assert.IsTrue(new StateColorSet(null, ColorSpec.Solid(Color.red), null, null).HasAny);
            Assert.IsFalse(StateColorSet.ResolveAbsolutes("", null, " ", "").HasAny, "all-blank Resolve -> HasAny false");
            Assert.IsFalse(StateColorSet.ResolveModulates("", null, " ", "").HasAny, "all-blank modulates -> HasAny false");
        }

        [Test]
        public void ResolveAbsolutes_EmptyOrNull_BecomesNull_LiteralBecomesColor()
        {
            var set = StateColorSet.ResolveAbsolutes("", null, "#ff0000", "  ");
            Assert.IsNull(set.For(InteractState.Hover), "empty string -> null");
            Assert.IsNull(set.For(InteractState.Pressed), "null -> null");
            Assert.IsNull(set.For(InteractState.Disabled), "whitespace -> null");
            var sel = set.For(InteractState.Selected).Value;
            Assert.IsFalse(sel.IsGradient, "solid literal -> non-gradient spec");
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), sel.Top);
        }

        [Test]
        public void ResolveAbsolutes_GradientLiteral_BecomesGradientSpec()
        {
            var set = StateColorSet.ResolveAbsolutes("#ffffff,#000000", null, null, null);
            var hover = set.For(InteractState.Hover).Value;
            Assert.IsTrue(hover.IsGradient, "comma literal -> gradient spec");
            Assert.AreEqual(Color.white, hover.Top);
            Assert.AreEqual(Color.black, hover.Bottom);
        }

        [Test]
        public void ResolveModulates_GradientLiteral_Throws()
        {
            // Modulates are solid-only: a gradient value routes through UI.Theme.Resolve, which throws.
            Assert.Throws<System.Exception>(
                () => StateColorSet.ResolveModulates("#ffffff,#000000", null, null, null),
                "gradient modulate must throw");
        }
    }
}
