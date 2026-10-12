using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Image sprite&gt;</c> and <c>&lt;Text color&gt;</c> are runtime-owned, the contract of
    /// <c>&lt;Icon name&gt;</c> (spec 2026-09-17-common-attr-runtime-state-design §4.6): the declared value is
    /// the initial state, a code-written value survives every ReSolve (resize / Variant / Theme), and an
    /// untouched node still follows its <c>.variant</c> override and its theme.
    /// </summary>
    public class ImageTextRuntimeStateTests
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

        private static PromptUGUI.Application.Screen Open(string body, string prelude = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                      + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static Sprite SpriteOf(IControl c) => c.GameObject.GetComponent<UnityImage>().sprite;

        private static Color ColorOf(IControl c) => c.GameObject.GetComponent<TMP_Text>().color;

        // ── <Image sprite> ────────────────────────────────────────────────────────────────

        [Test]
        public void Image_sprite_set_by_code_survives_resolve()
        {
            var s = Open("<Image id='i' size='20x20' sprite='ui:a'/>");
            var image = s.Get<PromptUGUI.Controls.Image>("i");
            Assume.That(SpriteOf(image), Is.SameAs(_sprites["ui:a"]));

            image.Sprite = "ui:b";
            s.ReSolve();
            UI.Variants.Set("portrait", true);

            Assert.AreSame(_sprites["ui:b"], SpriteOf(image), "a code-written sprite is runtime-owned");
        }

        [Test]
        public void Image_untouched_sprite_variant_override_still_applies()
        {
            var s = Open("<Image id='i' size='20x20' sprite='ui:a' sprite.portrait='ui:c'/>");
            var image = s.Get<PromptUGUI.Controls.Image>("i");

            UI.Variants.Set("portrait", true);
            Assert.AreSame(_sprites["ui:c"], SpriteOf(image));

            UI.Variants.Set("portrait", false);
            Assert.AreSame(_sprites["ui:a"], SpriteOf(image));
        }

        [Test]
        public void Image_sprite_written_by_a_bind_survives_resolve()
        {
            // A row template whose picture comes from a Param default: the bind callback picks each row's
            // own picture, and a rotation used to put the default back on every row.
            var s = Open(
                "<ScrollList id='list' anchor='center' size='200x100' itemTemplate='Row'/>",
                prelude: "<Template name='Row'><Param name='pic' default='ui:a'/>"
                         + "<HStack height='20'><Image id='pic' size='20x20' sprite='{{pic}}'/></HStack></Template>");
            IControl pic = null;
            s.Get<ScrollList>("list").BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "ui:b" }),
                (IControl row, string key) =>
                {
                    pic = row.Get("pic");
                    ((PromptUGUI.Controls.Image)pic).Sprite = key;
                });
            Assume.That(SpriteOf(pic), Is.SameAs(_sprites["ui:b"]));

            s.ReSolve();

            Assert.AreSame(_sprites["ui:b"], SpriteOf(pic));
        }

        // ── <Text color> ──────────────────────────────────────────────────────────────────

        [Test]
        public void Text_color_set_by_code_survives_resolve()
        {
            var s = Open("<Text id='t' color='#ff0000'>x</Text>");
            var text = s.Get<Text>("t");

            text.Color = "#00ff00";
            s.ReSolve();
            UI.Variants.Set("portrait", true);

            Assert.AreEqual(Color.green, ColorOf(text), "a code-written colour is runtime-owned");
        }

        [Test]
        public void Text_untouched_color_variant_override_still_applies()
        {
            var s = Open("<Text id='t' color='#ff0000' color.portrait='#0000ff'>x</Text>");
            var text = s.Get<Text>("t");

            UI.Variants.Set("portrait", true);
            Assert.AreEqual(Color.blue, ColorOf(text));

            UI.Variants.Set("portrait", false);
            Assert.AreEqual(Color.red, ColorOf(text));
        }

        // Themes only register through the async load path (the sync overload bypasses it).
        private static void LoadAsync(string body)
        {
            var files = new Dictionary<string, string>
            {
                ["main"] = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
        }

        private const string TwoThemes = @"
            <Theme name='day'><Color name='alert' value='#ff0000'/><Color name='ink' value='#000000'/></Theme>
            <Theme name='night'><Color name='alert' value='#0000ff'/><Color name='ink' value='#ffffff'/></Theme>
            <Screen name='S'><Text id='t' color='ink'>x</Text></Screen>";

        [Test]
        public void Text_untouched_color_token_follows_the_theme()
        {
            LoadAsync(TwoThemes);
            UI.Theme.Set("day");
            var text = UI.Open("S").Get<Text>("t");
            Assume.That(ColorOf(text), Is.EqualTo(Color.black));

            UI.Theme.Set("night");

            Assert.AreEqual(Color.white, ColorOf(text));
        }

        [Test]
        public void Text_color_token_written_by_code_still_follows_the_theme()
        {
            // Owned by code means the code's value is kept — and a token is a value the theme resolves.
            LoadAsync(TwoThemes);
            UI.Theme.Set("day");
            var text = UI.Open("S").Get<Text>("t");
            text.Color = "alert";
            Assume.That(ColorOf(text), Is.EqualTo(Color.red));

            UI.Theme.Set("night");

            Assert.AreEqual(Color.blue, ColorOf(text), "the code's token, resolved by the new theme — not the declared ink");
        }
    }
}
