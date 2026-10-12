using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Modals
{
    /// <summary>
    /// The library's own documents (modals, toast, tutorial overlay) are every project's default skin,
    /// so they must clear the default font floor on the screens a project is most likely to target:
    /// the narrowest and the shortest modern phones, both ways round, and a 1080p laptop at 150%
    /// (2026-10-12 font-floor spec §15). A skin edit that shrinks their text turns this red.
    /// </summary>
    public class BuiltinFontFloorTests
    {
        private static readonly FontFloorPolicy Policy = new FontFloorPolicy
        {
            Floor = FontFloorPolicy.DefaultFloor,
            Targets = new[]
            {
                new FontFloorTarget { name = "Android 360dp", screen = "1080x2400@3x" },
                new FontFloorTarget { name = "Android 360dp (landscape)", screen = "2400x1080@3x" },
                new FontFloorTarget { name = "iPhone SE", screen = "750x1334@2x" },
                new FontFloorTarget { name = "iPhone SE (landscape)", screen = "1334x750@2x" },
                new FontFloorTarget { name = "Laptop 1080p @150%", screen = "1920x1080@1.5x" },
            },
        };

        [TestCase("PromptUGUI/Modals/CenteredSlideBox.ui")]
        [TestCase("PromptUGUI/Modals/InputBox.ui")]
        [TestCase("PromptUGUI/Modals/Loading.ui")]
        [TestCase("PromptUGUI/Modals/MarkdownBox.ui")]
        [TestCase("PromptUGUI/Modals/MessageBox.ui")]
        [TestCase("PromptUGUI/Navigation/FocusCursor.ui")]
        [TestCase("PromptUGUI/Toast.ui")]
        [TestCase("PromptUGUI/Tutorial/TutorialOverlay.ui")]
        public void A_builtin_document_clears_the_default_floor(string src)
        {
            var asset = Resources.Load<TextAsset>(src);
            Assert.IsNotNull(asset, src);
            var doc = UIDocumentParser.Parse(asset.text, src);
            Assert.IsEmpty(doc.Imports, "no closure to resolve, so the expanded pass — where the rule runs — does run");

            var findings = DocumentLinter.Walk(doc, src, fontFloor: Policy)
                .Where(i => i.Code == FontFloorRules.Code)
                .Select(i => $"{i.Line}: {i.Message}")
                .ToList();

            CollectionAssert.IsEmpty(findings);
        }
    }
}
