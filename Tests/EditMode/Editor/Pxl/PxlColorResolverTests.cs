using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlColorResolverTests
    {
        private static GplPalette Palette() => GplPalette.Parse(
            "GIMP Palette\n26 28 44\tdark-blue\n244 244 244\twhite\n100 100 100\n");

        private static PxlDocument Doc(params (char, string)[] chars)
        {
            var d = new PxlDocument();
            foreach (var (k, v) in chars) d.Chars[k] = v;
            return d;
        }

        [Test]
        public void Resolve_inline_hex_and_transparent()
        {
            var map = PxlColorResolver.Resolve(Doc(('K', "#1a1c2c"), ('T', "transparent")), null);
            Assert.AreEqual(new Color32(0x1a, 0x1c, 0x2c, 255), map['K']);
            Assert.AreEqual(new Color32(0, 0, 0, 0), map['T']);
            Assert.AreEqual(new Color32(0, 0, 0, 0), map['.']);
        }

        [Test]
        public void Resolve_hex_with_alpha()
        {
            var map = PxlColorResolver.Resolve(Doc(('S', "#1a1c2c80")), null);
            Assert.AreEqual(new Color32(0x1a, 0x1c, 0x2c, 0x80), map['S']);
        }

        [Test]
        public void Resolve_palette_name()
        {
            var doc = Doc(('K', "dark-blue"));
            doc.PaletteRef = "main";
            var map = PxlColorResolver.Resolve(doc, Palette());
            Assert.AreEqual(new Color32(26, 28, 44, 255), map['K']);
        }

        [Test]
        public void Resolve_palette_mode_hex_must_be_on_palette()
        {
            var doc = Doc(('K', "#1a1c2c"), ('X', "#010203"));
            doc.PaletteRef = "main";
            var ex = Assert.Throws<PxlParseException>(() => PxlColorResolver.Resolve(doc, Palette()));
            StringAssert.Contains("'X'", ex.Message);
            StringAssert.Contains("#010203", ex.Message);
        }

        [Test]
        public void Resolve_palette_hex_alpha_variant_allowed()
        {
            var doc = Doc(('S', "#1a1c2c80")); // RGB 在板上，alpha 自由
            doc.PaletteRef = "main";
            var map = PxlColorResolver.Resolve(doc, Palette());
            Assert.AreEqual((byte)0x80, map['S'].a);
        }

        [Test]
        public void Resolve_name_without_palette_throws()
        {
            var ex = Assert.Throws<PxlParseException>(
                () => PxlColorResolver.Resolve(Doc(('K', "dark-blue")), null));
            StringAssert.Contains("palette:", ex.Message);
        }

        [Test]
        public void Resolve_unknown_name_throws()
        {
            var doc = Doc(('K', "magenta"));
            doc.PaletteRef = "main";
            var ex = Assert.Throws<PxlParseException>(() => PxlColorResolver.Resolve(doc, Palette()));
            StringAssert.Contains("magenta", ex.Message);
        }

        [Test]
        public void Resolve_bad_hex_throws()
        {
            Assert.Throws<PxlParseException>(
                () => PxlColorResolver.Resolve(Doc(('K', "#12345")), null));
        }

        [Test]
        public void Resolve_hex_with_inner_whitespace_throws()
        {
            Assert.Throws<PxlParseException>(
                () => PxlColorResolver.Resolve(Doc(('K', "# a1c2c")), null));
        }

        [Test]
        public void Resolve_palette_hex_matches_unnamed_entry()
        {
            var doc = Doc(('G', "#646464")); // 100,100,100 无名条目，只能被 hex 命中
            doc.PaletteRef = "main";
            var map = PxlColorResolver.Resolve(doc, Palette());
            Assert.AreEqual(new Color32(100, 100, 100, 255), map['G']);
        }

        [Test]
        public void Resolve_palette_name_is_normalized()
        {
            var doc = Doc(('K', "Dark Blue")); // .gpl 里是 dark-blue
            doc.PaletteRef = "main";
            var map = PxlColorResolver.Resolve(doc, Palette());
            Assert.AreEqual(new Color32(26, 28, 44, 255), map['K']);
        }
    }
}
