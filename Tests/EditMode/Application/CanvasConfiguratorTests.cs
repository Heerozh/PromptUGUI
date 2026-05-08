using System;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application {
    public class CanvasConfiguratorTests {
        const string Xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
            <Screen name='S'><Frame id='a'/></Screen>
          </PromptUGUI>";

        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Default_renderMode_is_ScreenSpaceOverlay_when_no_configurator() {
            UI.LoadDocument("inline", Xml);
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        }

        [Test]
        public void Configurator_is_invoked_with_canvas_and_screen_name() {
            Canvas seen = null;
            string seenName = null;
            UI.CanvasConfigurator = (c, n) => { seen = c; seenName = n; };

            UI.LoadDocument("inline", Xml);
            var screen = UI.Open("S");

            Assert.IsNotNull(seen);
            Assert.AreSame(screen.RootGameObject.GetComponent<Canvas>(), seen);
            Assert.AreEqual("S", seenName);
        }

        [Test]
        public void Configurator_can_change_renderMode_to_Camera() {
            var camGO = new GameObject("TestCam", typeof(Camera));
            try {
                var cam = camGO.GetComponent<Camera>();
                UI.CanvasConfigurator = (c, _) => {
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.sortingOrder = 7;
                };

                UI.LoadDocument("inline", Xml);
                var screen = UI.Open("S");

                var canvas = screen.RootGameObject.GetComponent<Canvas>();
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
                Assert.AreSame(cam, canvas.worldCamera);
                Assert.AreEqual(7, canvas.sortingOrder);
            } finally {
                UnityEngine.Object.DestroyImmediate(camGO);
            }
        }

        [Test]
        public void ResetForTests_clears_configurator() {
            UI.CanvasConfigurator = (_, __) => throw new Exception("must not be called");
            UI.ResetForTests();

            // After reset, no configurator → opening a screen does not throw
            // and renderMode falls back to overlay.
            UI.LoadDocument("inline", Xml);
            var screen = UI.Open("S");
            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        }

        [Test]
        public void Xml_canvas_camera_attribute_sets_renderMode() {
            // Unity silently reverts ScreenSpaceCamera → Overlay if worldCamera is null,
            // so a real "camera mode" Screen always pairs the XML attr with a configurator
            // that supplies the Camera. That's what we test here.
            var camGO = new GameObject("Cam", typeof(Camera));
            try {
                var cam = camGO.GetComponent<Camera>();
                UI.CanvasConfigurator = (c, _) => c.worldCamera = cam;

                const string xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S' canvas='camera'><Frame id='a'/></Screen>
                  </PromptUGUI>";
                UI.LoadDocument("inline", xml);
                var screen = UI.Open("S");

                var canvas = screen.RootGameObject.GetComponent<Canvas>();
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
                Assert.AreSame(cam, canvas.worldCamera);
            } finally {
                UnityEngine.Object.DestroyImmediate(camGO);
            }
        }

        [Test]
        public void Xml_canvas_world_attribute_sets_renderMode() {
            const string xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S' canvas='world'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocument("inline", xml);
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode);
        }

        [Test]
        public void Configurator_runs_after_xml_canvas_and_can_override() {
            // XML says camera, configurator forces back to overlay → configurator wins.
            const string xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S' canvas='camera'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.CanvasConfigurator = (c, _) => c.renderMode = RenderMode.ScreenSpaceOverlay;

            UI.LoadDocument("inline", xml);
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        }

        [Test]
        public void Default_vertexColorAlwaysGammaSpace_is_true() {
            // Pixel-art / hand-tuned palettes need vertex colors written in gamma to
            // hit the canvas without the linear→sRGB roundtrip altering the result.
            UI.LoadDocument("inline", Xml);
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            Assert.IsTrue(canvas.vertexColorAlwaysGammaSpace);
        }

        [Test]
        public void Configurator_can_override_default_shader_channels() {
            UI.CanvasConfigurator = (c, _) =>
                c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;

            UI.LoadDocument("inline", Xml);
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            Assert.IsTrue((canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) != 0);
        }

        [Test]
        public void Configurator_runs_again_on_Reload() {
            var calls = 0;
            UI.CanvasConfigurator = (_, __) => calls++;

            UI.SourceResolver = src => Xml;
            UI.LoadDocumentFromSrc("main");
            UI.Open("S");
            Assert.AreEqual(1, calls);

            UI.Reload("S");
            Assert.AreEqual(2, calls);
        }
    }
}
