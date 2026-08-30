using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Stop positions land in the material key, which is the only place they can be honoured: the
    /// shader reads them per fragment. The vertex path cannot, and says so out loud.
    /// </summary>
    public class GradientStopPanelTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Load(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        // ── the shader path ─────────────────────────────────────────────────────

        [Test]
        public void Frame_Stops_ReachTheMaterialKey()
        {
            var p = Load("<Frame id='f' color='#ff0000 70%,#0000ff'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>();
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), p.CurrentParams.FillTop);
            Assert.AreEqual(new Color(0f, 0f, 1f, 1f), p.CurrentParams.FillBottom);
            Assert.AreEqual(0.7f, p.CurrentParams.FillStopTop, 1e-5f);
            Assert.AreEqual(1f, p.CurrentParams.FillStopBottom, 1e-5f);
        }

        [Test]
        public void Frame_PlainGradient_KeepsTheFullHeightDefault()
        {
            var p = Load("<Frame id='f' color='#ff0000,#0000ff'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>();
            Assert.AreEqual(0f, p.CurrentParams.FillStopTop, 1e-5f);
            Assert.AreEqual(1f, p.CurrentParams.FillStopBottom, 1e-5f);
        }

        [Test]
        public void Frame_Solid_KeepsTheFullHeightDefault()
        {
            var p = Load("<Frame id='f' color='#ff0000'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>();
            Assert.AreEqual(0f, p.CurrentParams.FillStopTop, 1e-5f);
            Assert.AreEqual(1f, p.CurrentParams.FillStopBottom, 1e-5f);
        }

        [Test]
        public void Btn_ProceduralSurface_CarriesStops()
        {
            var btn = Load("<Btn id='b' radius='8' color='#ff0000 30%,#0000ff 60%'>ok</Btn>").Get<Btn>("b");
            var panel = btn.GameObject.transform.Find(ProceduralSurface.NodeName)
                                               .GetComponent<ProceduralPanel>();
            Assert.AreEqual(0.3f, panel.CurrentParams.FillStopTop, 1e-5f);
            Assert.AreEqual(0.6f, panel.CurrentParams.FillStopBottom, 1e-5f);
        }

        [Test]
        public void Decor_CarriesStops()
        {
            var d = Load(@"<Frame id='host' width='200' height='100'>
                             <Decor id='d' kind='bracket' color='#ff0000 70%,#0000ff'/>
                           </Frame>").Get<Decor>("d");
            var panel = d.GameObject.GetComponentInChildren<DecorPanel>();
            Assert.IsNotNull(panel);
            Assert.AreEqual(0.7f, panel.CurrentParams.FillStopTop, 1e-5f);
        }

        // ── the material cache keys on them ─────────────────────────────────────

        [Test]
        public void PanelParams_DifferentStops_AreDifferentKeys()
        {
            var a = Load("<Frame id='f' color='#ff0000 30%,#0000ff'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>().CurrentParams;
            UI.ResetForTests();
            var b = Load("<Frame id='f' color='#ff0000 60%,#0000ff'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>().CurrentParams;
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void PanelParams_DefaultStops_HashTheSameAsBefore()
        {
            // Existing projects must not fan out into a second material per panel.
            var a = Load("<Frame id='f' color='#ff0000,#0000ff'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>().CurrentParams;
            UI.ResetForTests();
            var b = Load("<Frame id='f' color='#ff0000,#0000ff'/>")
                .Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>().CurrentParams;
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        // ── the vertex path says so ─────────────────────────────────────────────

        /// <summary>
        /// Captures warnings ourselves rather than through <c>LogAssert</c>: the interesting
        /// assertion is the NEGATIVE one ("a procedural node says nothing"), and LogAssert cannot
        /// express that without also failing on unrelated log noise from the load path.
        /// </summary>
        private static List<string> WarningsWhile(System.Action body)
        {
            var seen = new List<string>();
            void Handler(string condition, string stack, LogType type)
            {
                if (type == LogType.Warning) seen.Add(condition);
            }

            UnityEngine.Application.logMessageReceived += Handler;
            try { body(); }
            finally { UnityEngine.Application.logMessageReceived -= Handler; }
            return seen;
        }

        private static bool WarnedAboutStops(string body)
            => WarningsWhile(() => Load(body)).Exists(m => m.Contains("PUI-GRADIENT-STOP-NO-SURFACE"));

        [Test]
        public void Image_WithStops_Warns()
        {
            Assert.IsTrue(WarnedAboutStops("<Image id='g' color='#ff0000 70%,#0000ff'/>"));
        }

        [Test]
        public void Image_PlainGradient_IsQuiet()
        {
            Assert.IsFalse(WarnedAboutStops("<Image id='g' color='#ff0000,#0000ff'/>"));
        }

        [Test]
        public void Frame_WithStops_IsQuiet()
        {
            Assert.IsFalse(WarnedAboutStops("<Frame id='f' color='#ff0000 70%,#0000ff'/>"));
        }

        [Test]
        public void Btn_WithoutProceduralAttrs_Warns()
        {
            // The colour lands on the plain Image; the surface never turned on.
            Assert.IsTrue(WarnedAboutStops("<Btn id='b' color='#ff0000 70%,#0000ff'>ok</Btn>"));
        }

        [Test]
        public void Btn_WithRadius_IsQuiet()
        {
            Assert.IsFalse(WarnedAboutStops("<Btn id='b' radius='8' color='#ff0000 70%,#0000ff'>ok</Btn>"));
        }

        [Test]
        public void Text_WithStops_Warns()
        {
            Assert.IsTrue(WarnedAboutStops("<Text id='t' color='#ff0000 70%,#0000ff'>hi</Text>"));
        }
    }
}
