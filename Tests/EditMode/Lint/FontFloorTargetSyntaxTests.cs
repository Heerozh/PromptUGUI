using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class FontFloorTargetSyntaxTests
    {
        [TestCase("750x1334@2x", 750f, 1334f, 2f)]
        [TestCase("750x1334@2", 750f, 1334f, 2f)]
        [TestCase("1920x1080", 1920f, 1080f, 1f)]
        [TestCase("1920x1080@1.5", 1920f, 1080f, 1.5f)]
        [TestCase("1920x1080@1.5x", 1920f, 1080f, 1.5f)]
        [TestCase(" 1920x1080@2x ", 1920f, 1080f, 2f)]
        public void Parses_pixels_at_os_scale(string raw, float w, float h, float d)
        {
            Assert.IsTrue(FontFloorTargetSyntax.TryParse(raw, out var s, out var error), error);
            Assert.AreEqual(w, s.Width, 1e-6f);
            Assert.AreEqual(h, s.Height, 1e-6f);
            Assert.AreEqual(d, s.Density, 1e-6f);
        }

        [TestCase("750*1334")]
        [TestCase("0x10")]
        [TestCase("10x-5")]
        [TestCase("10x10@0")]
        [TestCase("10x10@")]
        [TestCase("10x10@x")]
        [TestCase("10x10x10")]
        [TestCase("")]
        [TestCase(null)]
        public void Rejects_malformed_values(string raw)
        {
            Assert.IsFalse(FontFloorTargetSyntax.TryParse(raw, out _, out var error));
            StringAssert.Contains("WxH[@D]", error);
        }

        [TestCase("750x1334@2x", "portrait")]
        [TestCase("1334x750@2x", "landscape")]
        [TestCase("1000x1000", "landscape")]   // square counts as landscape, like OrientationTracker
        public void Orientation_follows_the_aspect(string raw, string expected)
        {
            Assert.IsTrue(FontFloorTargetSyntax.TryParse(raw, out var s, out _));
            Assert.AreEqual(expected, s.Orientation);
        }

        [Test]
        public void Variants_split_on_spaces_and_commas()
        {
            CollectionAssert.AreEqual(new[] { "mobile", "tv", "low" },
                FontFloorTargetSyntax.SplitVariants(" mobile, tv  low,"));
            CollectionAssert.IsEmpty(FontFloorTargetSyntax.SplitVariants(null));
        }

        [Test]
        public void Validate_reports_a_bad_screen_and_a_hand_written_orientation()
        {
            Assert.IsNull(FontFloorTargetSyntax.Validate(
                new FontFloorTarget { name = "SE", screen = "750x1334@2x", variants = "mobile" }));

            StringAssert.Contains("screen=\"750*1334\"", FontFloorTargetSyntax.Validate(
                new FontFloorTarget { name = "SE", screen = "750*1334" }));

            var orientation = FontFloorTargetSyntax.Validate(
                new FontFloorTarget { name = "SE", screen = "750x1334@2x", variants = "mobile portrait" });
            StringAssert.Contains("portrait", orientation);
            StringAssert.Contains("aspect", orientation);
        }
    }
}
