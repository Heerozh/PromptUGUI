using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class SpriteRenderHintsTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Sprite MakeSprite() =>
            Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        [Test]
        public void Register_then_IsTiled_true_idempotent()
        {
            var s = MakeSprite();
            Assert.IsFalse(SpriteRenderHints.IsTiled(s));
            SpriteRenderHints.Register(s);
            SpriteRenderHints.Register(s); // 幂等
            Assert.IsTrue(SpriteRenderHints.IsTiled(s));
        }

        [Test]
        public void Null_safe()
        {
            SpriteRenderHints.Register((Sprite)null);
            Assert.IsFalse(SpriteRenderHints.IsTiled(null));
        }

        [Test]
        public void ResetForTests_clears()
        {
            var s = MakeSprite();
            SpriteRenderHints.Register(s);
            UI.ResetForTests();
            Assert.IsFalse(SpriteRenderHints.IsTiled(s));
        }
    }
}
