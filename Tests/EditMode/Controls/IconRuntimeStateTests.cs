using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Icon name&gt;</c> is runtime-owned (spec 2026-09-17-common-attr-runtime-state-design
    /// §4.6): a code-written <c>Name</c> survives a ReSolve, an untouched one still follows a
    /// locale / variant override.
    /// </summary>
    public class IconRuntimeStateTests
    {
        private readonly Dictionary<string, Sprite> _sprites = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _sprites.Clear();
            foreach (var key in new[] { "ui:a", "ui:b", "ui:c" })
                _sprites[key] = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => _sprites.TryGetValue(key, out var s) ? s : null;
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                      + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static Sprite SpriteOf(IScreen s, string id) => s.Get(id).GameObject.GetComponent<UnityImage>().sprite;

        [Test]
        public void Name_set_by_code_survives_resolve()
        {
            var s = Open("<Icon id='i' name='ui:a'/>");
            Assume.That(SpriteOf(s, "i"), Is.SameAs(_sprites["ui:a"]));

            s.Get<Icon>("i").Name = "ui:b";
            s.ReSolve();
            UI.Variants.Set("portrait", true);

            Assert.AreSame(_sprites["ui:b"], SpriteOf(s, "i"), "a code-written name is runtime-owned");
        }

        [Test]
        public void Untouched_name_variant_override_still_applies()
        {
            var s = Open("<Icon id='i' name='ui:a' name.zh-Hans='ui:c'/>");

            UI.Variants.Set("zh-Hans", true);
            Assert.AreSame(_sprites["ui:c"], SpriteOf(s, "i"), "the locale override reaches an untouched icon");

            UI.Variants.Set("zh-Hans", false);
            Assert.AreSame(_sprites["ui:a"], SpriteOf(s, "i"));
        }

        [Test]
        public void Touched_name_ignores_a_later_variant_override()
        {
            var s = Open("<Icon id='i' name='ui:a' name.zh-Hans='ui:c'/>");
            s.Get<Icon>("i").Name = "ui:b";

            UI.Variants.Set("zh-Hans", true);

            Assert.AreSame(_sprites["ui:b"], SpriteOf(s, "i"));
        }
    }
}
