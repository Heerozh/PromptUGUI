using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// <c>canvas="camera"</c> only actually takes effect once a <c>worldCamera</c> is assigned, and
    /// Unity makes that easy to get wrong: the <see cref="Canvas.renderMode"/> <em>getter</em>
    /// reports <c>ScreenSpaceOverlay</c> for as long as the camera is null, even immediately after
    /// the setter was handed <c>ScreenSpaceCamera</c>. A configurator that gates on that getter can
    /// never fire, and the Screen silently stays Overlay — it still draws, so nothing looks broken,
    /// but it is no longer rendered by a camera and drops out of glass backdrops, post-processing
    /// and RenderTexture capture.
    /// </summary>
    public class CanvasModeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string CameraScreen = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S' canvas='camera'>
  <Frame id='f' height='40'/>
</Screen></PromptUGUI>";

        private static Camera NewCamera()
            => new GameObject("TestCamera").AddComponent<Camera>();

        [Test]
        public void UnityItself_HidesCameraModeUntilACameraIsAssigned()
        {
            // The behaviour everything below is guarding against, pinned as a fact rather than a
            // belief — if a future Unity stops doing this, this test says so first.
            var go = new GameObject("c", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode,
                "Unity reports Overlay while worldCamera is null");

            var cam = NewCamera();
            canvas.worldCamera = cam;
            Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode,
                "…and snaps back once a camera arrives, so the mode was remembered all along");

            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AssigningWorldCamera_ActivatesTheDeclaredCameraMode()
        {
            var cam = NewCamera();
            UI.CanvasConfigurator = (canvas, _) => canvas.worldCamera = cam;

            UI.LoadDocument("t", CameraScreen);
            var screen = UI.Open("S");
            var c = screen.RootGameObject.GetComponentInParent<Canvas>();

            Assert.AreEqual(RenderMode.ScreenSpaceCamera, c.renderMode);
            Assert.AreSame(cam, c.worldCamera);
            Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void AssigningUnconditionally_LeavesOverlayScreensAlone()
        {
            // This is what makes "just assign it" safe advice: a canvas that never declared camera
            // mode does not get dragged into it by the assignment.
            var cam = NewCamera();
            UI.CanvasConfigurator = (canvas, _) => canvas.worldCamera = cam;

            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' height='40'/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var c = screen.RootGameObject.GetComponentInParent<Canvas>();

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, c.renderMode);
            Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void CameraModeWithoutACamera_WarnsInsteadOfFailingSilently()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "declares canvas=\"camera\" but no worldCamera"));

            UI.LoadDocument("t", CameraScreen);
            var screen = UI.Open("S");

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay,
                screen.RootGameObject.GetComponentInParent<Canvas>().renderMode,
                "it does fall back — the point is that it says so");
        }

        [Test]
        public void TheBrokenIdiom_IsWhyTheWarningExists()
        {
            // Verbatim the check that used to be in the C# skill's example. It cannot fire, which is
            // exactly how the glass sample's backdrop ended up invisible to the capture.
            var cam = NewCamera();
            var configuratorFired = false;
            UI.CanvasConfigurator = (canvas, _) =>
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    configuratorFired = true;
                    canvas.worldCamera = cam;
                }
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "declares canvas=\"camera\" but no worldCamera"));

            UI.LoadDocument("t", CameraScreen);
            var screen = UI.Open("S");

            Assert.IsFalse(configuratorFired, "the getter reported Overlay, so the branch was dead");
            Assert.IsNull(screen.RootGameObject.GetComponentInParent<Canvas>().worldCamera);
            Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void OverlayScreens_DoNotWarn()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' height='40'/>
</Screen></PromptUGUI>");
            UI.Open("S");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
