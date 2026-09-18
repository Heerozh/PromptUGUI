using NUnit.Framework;
using PromptUGUI.Editor.Preview;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// The UI Preview menu's decisions (2026-09-18 ui-preview-tool spec §4.1 / §4.2, §7-7 / §7-8),
    /// checked without entering Play.
    /// </summary>
    public class UIPreviewRulesTests
    {
        // ── F8 ─────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Decide_EditMode_Launches()
        {
            Assert.AreEqual(UIPreviewRules.MenuAction.Launch,
                UIPreviewRules.Decide(isPlayingOrWillChange: false, previewActive: false, compilationFailed: false, out var reason));
            Assert.IsNull(reason);
        }

        [Test]
        public void Decide_WhilePreviewing_TogglesThePanel()
        {
            Assert.AreEqual(UIPreviewRules.MenuAction.ToggleCollapse,
                UIPreviewRules.Decide(isPlayingOrWillChange: true, previewActive: true, compilationFailed: false, out _));
        }

        [Test]
        public void Decide_InSomeoneElsesPlaySession_Refuses()
        {
            Assert.AreEqual(UIPreviewRules.MenuAction.Refuse,
                UIPreviewRules.Decide(isPlayingOrWillChange: true, previewActive: false, compilationFailed: false, out var reason));
            StringAssert.Contains("Play", reason);
        }

        [Test]
        public void Decide_WithCompileErrors_Refuses_SoNoPendingFlagIsLeftBehind()
        {
            Assert.AreEqual(UIPreviewRules.MenuAction.Refuse,
                UIPreviewRules.Decide(isPlayingOrWillChange: false, previewActive: false, compilationFailed: true, out var reason));
            StringAssert.Contains("compile", reason);
        }

        // ── which scene ────────────────────────────────────────────────────────────────────────

        private const string BuiltIn = "Packages/com.promptugui.core/Editor/Preview/UIPreview.unity";

        [Test]
        public void PickScene_NothingConfigured_IsTheBuiltInScene_Silently()
        {
            var pick = UIPreviewRules.PickScene("", _ => null, _ => false, BuiltIn);

            Assert.AreEqual(BuiltIn, pick.Path);
            Assert.IsTrue(pick.IsBuiltIn);
            Assert.IsNull(pick.Warning, "the default is not a problem to warn about");
        }

        [Test]
        public void PickScene_AConfiguredSceneThatExists_IsUsed()
        {
            var pick = UIPreviewRules.PickScene("abc",
                guid => guid == "abc" ? "Assets/Tools/UI Preview/UIPreview.unity" : null,
                path => path.EndsWith(".unity"), BuiltIn);

            Assert.AreEqual("Assets/Tools/UI Preview/UIPreview.unity", pick.Path);
            Assert.IsFalse(pick.IsBuiltIn);
            Assert.IsNull(pick.Warning);
        }

        [Test]
        public void PickScene_AConfiguredSceneThatIsGone_FallsBackToBuiltIn_WithAWarning()
        {
            var pick = UIPreviewRules.PickScene("abc", _ => "", _ => false, BuiltIn);

            Assert.AreEqual(BuiltIn, pick.Path);
            Assert.IsTrue(pick.IsBuiltIn);
            StringAssert.Contains("Project Settings", pick.Warning, "the fix lives there; say so");
        }

        [Test]
        public void PickScene_AGuidThatNoLongerNamesAScene_FallsBackToo()
        {
            // The GUID resolves (the asset still exists) but it is not a scene any more — a
            // replaced asset, a folder — which playModeStartScene could not take.
            var pick = UIPreviewRules.PickScene("abc", _ => "Assets/Tools/Something.prefab", _ => false, BuiltIn);

            Assert.IsTrue(pick.IsBuiltIn);
            Assert.IsNotNull(pick.Warning);
        }
    }
}
