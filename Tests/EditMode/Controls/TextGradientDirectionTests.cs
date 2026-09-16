using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// TMP text and the directed gradient (spec 2026-09-17 §6.4): a two-colour ramp in any
    /// direction fits the four corners of a <c>VertexGradient</c> exactly, evaluated on the unit
    /// square (TMP paints per glyph and gives no per-glyph size, so a magic corner is the 45°
    /// family here). Anything the four corners cannot hold — a third colour, a moved stop, a hint —
    /// still warns, as it did for stops.
    /// </summary>
    public class TextGradientDirectionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TMPro.TMP_Text OpenText(string attrs)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Text id='t' {attrs}>x</Text></Screen></PromptUGUI>");
            return UI.Open("S").Get<Text>("t").GameObject.GetComponentInChildren<TMPro.TMP_Text>();
        }

        private static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, what + " (r)");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, what + " (g)");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, what + " (b)");
            Assert.AreEqual(expected.a, actual.a, 1e-3f, what + " (a)");
        }

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

        [Test]
        public void ToRight_ColoursTheColumns()
        {
            var tmp = OpenText("color='to right, #ffffff, #000000'");
            Assert.IsTrue(tmp.enableVertexGradient);
            AssertColor(Color.white, tmp.colorGradient.topLeft, "top-left");
            AssertColor(Color.white, tmp.colorGradient.bottomLeft, "bottom-left");
            AssertColor(Color.black, tmp.colorGradient.topRight, "top-right");
            AssertColor(Color.black, tmp.colorGradient.bottomRight, "bottom-right");
        }

        [Test]
        public void ToTop_IsTheDefaultFlipped()
        {
            var tmp = OpenText("color='to top, #ffffff, #000000'");
            AssertColor(Color.black, tmp.colorGradient.topLeft, "top-left");
            AssertColor(Color.white, tmp.colorGradient.bottomLeft, "bottom-left");
        }

        [Test]
        public void Corner_OnTheUnitSquare_PutsTheOtherCornersHalfway()
        {
            var tmp = OpenText("color='to bottom right, #ffffff, #000000'");
            AssertColor(Color.white, tmp.colorGradient.topLeft, "start corner");
            AssertColor(Color.black, tmp.colorGradient.bottomRight, "end corner");
            AssertColor(Color.Lerp(Color.white, Color.black, 0.5f), tmp.colorGradient.topRight, "50% line");
            AssertColor(Color.Lerp(Color.white, Color.black, 0.5f), tmp.colorGradient.bottomLeft, "50% line");
        }

        [Test]
        public void Direction_Alone_DoesNotWarn()
        {
            var warnings = WarningsWhile(() => OpenText("color='to right, #ffffff, #000000'"));
            Assert.IsFalse(warnings.Exists(m => m.Contains("PUI-GRADIENT-STOP-NO-SURFACE")),
                           "four corners hold any two-colour linear ramp");
        }

        [Test]
        public void ThirdColour_Warns_AndPaintsTheOuterStops()
        {
            TMPro.TMP_Text tmp = null;
            var warnings = WarningsWhile(() => tmp = OpenText("color='#ff0000, #00ff00, #0000ff'"));
            Assert.IsTrue(warnings.Exists(m => m.Contains("PUI-GRADIENT-STOP-NO-SURFACE")),
                          "a third colour has nowhere to live on four corners");
            Assert.IsTrue(tmp.enableVertexGradient);
            AssertColor(Color.red, tmp.colorGradient.topLeft, "start");
            AssertColor(Color.blue, tmp.colorGradient.bottomLeft, "end");
        }

        [Test]
        public void Label_TextColor_ToRight()
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Btn id='b' textColor='to right, #ffffff, #000000'>x</Btn></Screen></PromptUGUI>");
            var tmp = UI.Open("S").Get<Btn>("b").GameObject.GetComponentInChildren<TMPro.TMP_Text>();
            Assert.IsTrue(tmp.enableVertexGradient);
            AssertColor(Color.white, tmp.colorGradient.bottomLeft, "bottom-left");
            AssertColor(Color.black, tmp.colorGradient.topRight, "top-right");
        }
    }
}
