using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Application
{
    [TestFixture]
    public class ResolveSpriteTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();
        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void ResolveSprite_with_null_returns_null()
        {
            Assert.IsNull(UI.ResolveSprite(null));
        }

        [Test]
        public void ResolveSprite_with_empty_string_returns_null()
        {
            Assert.IsNull(UI.ResolveSprite(""));
        }

        [Test]
        public void ResolveSprite_with_colon_routes_to_SpriteResolver()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            string capturedKey = null;
            UI.SpriteResolver = key => { capturedKey = key; return stub; };

            var actual = UI.ResolveSprite("ui:bell");

            Assert.AreSame(stub, actual);
            Assert.AreEqual("ui:bell", capturedKey);
        }

        [Test]
        public void ResolveSprite_without_colon_does_not_call_SpriteResolver()
        {
            var resolverCalled = false;
            UI.SpriteResolver = _ => { resolverCalled = true; return null; };

            UI.ResolveSprite("path/to/sprite");

            Assert.IsFalse(resolverCalled,
                "Bare path should fall back to Resources.Load, not call SpriteResolver");
        }

        [Test]
        public void ResolveSprite_without_colon_missing_resource_returns_null_silently()
        {
            // Bare path returning null must NOT log; this is the existing Resources.Load
            // behaviour preserved for backwards-compat with sprite= callers.
            var actual = UI.ResolveSprite("does/not/exist/sprite");
            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_with_colon_and_null_resolver_logs_error_and_returns_null()
        {
            UI.SpriteResolver = null;
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));

            var actual = UI.ResolveSprite("ui:bell");

            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_with_colon_and_resolver_returns_null_logs_error()
        {
            UI.SpriteResolver = _ => null;
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("resolver returned null"));

            var actual = UI.ResolveSprite("ui:missing");

            Assert.IsNull(actual);
        }
    }
}
