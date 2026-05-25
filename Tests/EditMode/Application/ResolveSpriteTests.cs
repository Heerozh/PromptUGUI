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

        [Test]
        public void ResolveSprite_unknown_set_name_message_calls_out_set_not_loaded()
        {
            // Set "ui" is loaded via UseSpriteSetResolver, but the caller asks for
            // sprite from set "icons" which was never registered. The error must
            // explicitly say "SpriteSet 'icons' is not loaded" so the author knows
            // the issue is registration, not a typo or atlas sync.
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeIconSet("ui") });
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"SpriteSet 'icons' is not loaded"));

            var actual = UI.ResolveSprite("icons:bell");

            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_unknown_set_name_message_lists_loaded_sets_and_both_registration_hints()
        {
            // Diagnose-without-callback: the user might have registered via either
            // UseAddressableSpriteSetResolver (label-based) or UseSpriteSetResolver
            // (Resources-subpath). We don't know which, so mention both. Also list
            // what IS loaded so the user can compare.
            SpriteResolverHelpers.UseSpriteSetResolver(
                new[] { MakeIconSet("ui"), MakeIconSet("game") });
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"UseAddressableSpriteSetResolver.*UseSpriteSetResolver",
                    System.Text.RegularExpressions.RegexOptions.Singleline));

            var actual = UI.ResolveSprite("icons:bell");

            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_known_set_missing_key_message_calls_out_atlas_sync()
        {
            // Set "ui" IS loaded but doesn't contain key "bell" → the registration
            // is fine; the user needs to add the sprite + run Sync Atlases.
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeIconSet("ui") });
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"SpriteSet 'ui' is loaded but doesn't contain 'bell'.*Sync Atlases",
                    System.Text.RegularExpressions.RegexOptions.Singleline));

            var actual = UI.ResolveSprite("ui:bell");

            Assert.IsNull(actual);
        }

        private static SpriteSet MakeIconSet(string name)
        {
            var s = UnityEngine.ScriptableObject.CreateInstance<SpriteSet>();
            var so = new UnityEditor.SerializedObject(s);
            so.FindProperty("setName").stringValue = name;
            so.ApplyModifiedProperties();
            return s;
        }

        [Test]
        public void ResolveSprite_with_hash_returns_named_slice_from_multi_sprite_texture()
        {
            var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui.png#pugui_9slice_round");

            Assert.IsNotNull(actual);
            Assert.AreEqual("pugui_9slice_round", actual.name);
        }

        [Test]
        public void ResolveSprite_with_hash_strips_image_extension_for_lookup()
        {
            var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui#pugui_caret");

            Assert.IsNotNull(actual);
            Assert.AreEqual("pugui_caret", actual.name);
        }

        [Test]
        public void ResolveSprite_with_hash_missing_slice_logs_error_and_returns_null()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("slice 'no_such_slice' not found"));

            var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui.png#no_such_slice");

            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_with_hash_missing_texture_returns_null_silently()
        {
            // Texture path itself doesn't exist → no sprites at all → silent like the
            // existing bare-path "missing resource" convention.
            var actual = UI.ResolveSprite("does/not/exist#anything");
            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_with_hash_strips_aseprite_extension()
        {
            // After whitelist removal, any extension should be stripped before LoadAll.
            var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui.aseprite#pugui_caret");

            Assert.IsNotNull(actual);
            Assert.AreEqual("pugui_caret", actual.name);
        }

        [Test]
        public void ResolveSprite_with_hash_strips_unknown_extension()
        {
            // Any extension after the last '.' (not in folder name) is dropped.
            var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui.xyz#pugui_caret");

            Assert.IsNotNull(actual);
            Assert.AreEqual("pugui_caret", actual.name);
        }

        [Test]
        public void ResolveSprite_does_not_strip_dot_in_folder_name()
        {
            // "v2.0/foo" — the dot is in the folder segment before the slash, so it must
            // NOT be stripped. LoadAll("v2.0/foo") returns empty → null is returned silently.
            var actual = UI.ResolveSprite("v2.0/foo#anything");
            Assert.IsNull(actual); // LoadAll finds nothing; no error expected either
        }
    }
}
