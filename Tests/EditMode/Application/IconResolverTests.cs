using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Tests.Application
{
    public class IconResolverTests
    {
        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
        }
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void Null_resolver_default_state()
        {
            Assert.IsNull(UI.SpriteResolver);
        }

        [Test]
        public void UseSpriteSetResolver_with_empty_list_builds_resolver()
        {
            SpriteResolverHelpers.UseSpriteSetResolver(Array.Empty<SpriteSet>());
            Assert.IsNotNull(UI.SpriteResolver);
            Assert.IsNull(UI.SpriteResolver("ui:nope"));
        }

        [Test]
        public void Duplicate_set_name_throws()
        {
            var a = MakeIconSet("ui");
            var b = MakeIconSet("ui");
            Assert.Throws<InvalidOperationException>(() =>
                SpriteResolverHelpers.UseSpriteSetResolver(new[] { a, b }));
        }

        [Test]
        public void Null_atlas_does_not_throw()
        {
            var s = MakeIconSet("ui");
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { s });
            Assert.IsNull(UI.SpriteResolver("ui:foo"));
        }

        [Test]
        public void Resolver_with_set_returns_non_null_delegate()
        {
            var s = MakeIconSet("ui");
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { s });
            Assert.IsNotNull(UI.SpriteResolver);
        }

        private static SpriteSet MakeIconSet(string name)
        {
            var s = ScriptableObject.CreateInstance<SpriteSet>();
            var so = new UnityEditor.SerializedObject(s);
            so.FindProperty("setName").stringValue = name;
            so.ApplyModifiedProperties();
            return s;
        }
    }
}
