using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class GplPaletteTests
    {
        private const string Sample = "GIMP Palette\nName: db8\nColumns: 4\n# comment\n" +
            "26 28 44\tdark-blue\n244 244 244\tWhite\n177 62 83\n";

        [Test]
        public void Parse_reads_entries_with_and_without_names()
        {
            var p = GplPalette.Parse(Sample);
            Assert.AreEqual(3, p.Entries.Count);
            Assert.AreEqual(new Color32(26, 28, 44, 255), p.Entries[0].color);
            Assert.AreEqual("dark-blue", p.Entries[0].name);
            Assert.IsNull(p.Entries[2].name); // unnamed entry
        }

        [Test]
        public void TryGetByName_normalizes_case_space_hyphen_underscore()
        {
            var p = GplPalette.Parse(Sample);
            Assert.IsTrue(p.TryGetByName("Dark Blue", out var c));
            Assert.AreEqual(new Color32(26, 28, 44, 255), c);
            Assert.IsTrue(p.TryGetByName("dark_blue", out _));
            Assert.IsTrue(p.TryGetByName("WHITE", out _));
            Assert.IsFalse(p.TryGetByName("nope", out _));
        }

        [Test]
        public void ContainsRgb_matches_ignoring_alpha()
        {
            var p = GplPalette.Parse(Sample);
            Assert.IsTrue(p.ContainsRgb(new Color32(177, 62, 83, 128)));
            Assert.IsFalse(p.ContainsRgb(new Color32(1, 2, 3, 255)));
        }

        [Test]
        public void Parse_missing_header_throws()
        {
            var ex = Assert.Throws<System.FormatException>(() => GplPalette.Parse("26 28 44\tx\n"));
            StringAssert.Contains("GIMP Palette", ex.Message);
        }

        [Test]
        public void Parse_malformed_entry_line_throws_with_line_number()
        {
            var ex = Assert.Throws<System.FormatException>(
                () => GplPalette.Parse("GIMP Palette\n26 28\tshort\n"));
            StringAssert.Contains("line 2", ex.Message);
        }
    }
}
