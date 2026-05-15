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
            Assert.IsNull(UI.IconResolver);
        }

        [Test]
        public void UseSpriteAtlasIconResolver_with_empty_list_builds_resolver()
        {
            IconResolverHelpers.UseSpriteAtlasIconResolver(Array.Empty<SpriteSet>());
            Assert.IsNotNull(UI.IconResolver);
            Assert.IsNull(UI.IconResolver("ui:nope"));
        }

        [Test]
        public void Duplicate_set_name_throws()
        {
            var a = MakeIconSet("ui");
            var b = MakeIconSet("ui");
            Assert.Throws<InvalidOperationException>(() =>
                IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { a, b }));
        }

        [Test]
        public void Null_atlas_does_not_throw()
        {
            var s = MakeIconSet("ui");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { s });
            Assert.IsNull(UI.IconResolver("ui:foo"));
        }

        [Test]
        public void Resolver_with_set_returns_non_null_delegate()
        {
            var s = MakeIconSet("ui");
            IconResolverHelpers.UseSpriteAtlasIconResolver(new[] { s });
            Assert.IsNotNull(UI.IconResolver);
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
