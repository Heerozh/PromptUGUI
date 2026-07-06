using NUnit.Framework;
using PromptUGUI.Editor;

namespace PromptUGUI.Tests.Editor
{
    public class CreatePxlMenuTests
    {
        // The Assets/Create/PromptUGUI/Pxl Sprite starter must import cleanly — a broken
        // grid would ship a DefaultAsset (failed import) instead of a Sprite. Assert the
        // canned content parses to exactly one usable section.
        [Test]
        public void SampleContent_parses_to_a_usable_sprite_section()
        {
            var doc = PxlParser.Parse(CreateUiXmlMenu.PxlContent);

            Assert.IsNull(doc.PaletteRef, "starter should be self-contained (no external .gpl)");
            Assert.AreEqual(1, doc.Sections.Count);
            var s = doc.Sections[0];
            Assert.Greater(s.Width, 0);
            Assert.AreEqual(s.Height, s.Rows.Count);
        }
    }
}
