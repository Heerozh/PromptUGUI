using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class GradientColorAttrTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                $"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        // 1. Image gradient — enabled GradientTint, top=white, bottom=black, graphic.color==white
        [Test]
        public void Image_Gradient_Applies_GradientTint()
        {
            var s = Open("<Image id='im' color='#ffffff,#000000'/>");
            var img = s.Get<Image>("im").GameObject.GetComponent<UnityImage>();
            var tint = img.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint component must be present");
            Assert.IsTrue(tint.enabled, "GradientTint must be enabled");
            Assert.AreEqual(Color.white, tint.Top, "top stop must be white");
            Assert.AreEqual(Color.black, tint.Bottom, "bottom stop must be black");
            Assert.AreEqual(Color.white, img.color, "graphic.color must be white for gradient");
        }

        // 2. Image solid no-regression — no enabled GradientTint, graphic.color==red
        [Test]
        public void Image_Solid_NoGradientTint()
        {
            var s = Open("<Image id='im' color='#ff0000'/>");
            var img = s.Get<Image>("im").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(Color.red, img.color, "solid color must be applied directly");
            var tint = img.GetComponent<GradientTint>();
            // Either no GradientTint component at all, or it must be disabled.
            Assert.IsTrue(tint == null || !tint.enabled,
                "no enabled GradientTint for solid color");
        }

        // 3. Btn bg gradient (static, no state colors) — bg graphic has enabled GradientTint
        [Test]
        public void Btn_Color_Gradient_Applies_GradientTint()
        {
            var s = Open("<Btn id='b' color='#ffffff,#000000'>x</Btn>");
            var bg = s.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            var tint = bg.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint must be present on Btn bg");
            Assert.IsTrue(tint.enabled, "GradientTint must be enabled");
            Assert.AreEqual(Color.white, tint.Top);
            Assert.AreEqual(Color.black, tint.Bottom);
        }

        // 4. Progress fill gradient — fill graphic has enabled GradientTint
        [Test]
        public void Progress_FillColor_Gradient_Applies_GradientTint()
        {
            var s = Open("<Progress id='p' fillColor='#ffffff,#000000'/>");
            var fill = s.Get<Progress>("p").GameObject.transform
                        .Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            var tint = fill.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint must be present on fill");
            Assert.IsTrue(tint.enabled, "GradientTint must be enabled on fill");
            Assert.AreEqual(Color.white, tint.Top);
            Assert.AreEqual(Color.black, tint.Bottom);
        }

        // 5. ScrollList frameColor gradient — frame graphic has enabled GradientTint
        [Test]
        public void ScrollList_FrameColor_Gradient_Applies_GradientTint()
        {
            // ScrollList requires an itemTemplate; use the full document form so the
            // <Template> lives at root level alongside <Screen>.
            const string xml =
                "<?xml version='1.0' encoding='utf-8'?>" +
                "<PromptUGUI version='1'>" +
                "<Template name='Slot'><Frame/></Template>" +
                "<Screen name='S'>" +
                "<ScrollList id='sl' itemTemplate='Slot' frameColor='#ffffff,#000000'/>" +
                "</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var sl = screen.Get<ScrollList>("sl");
            var frame = sl.GameObject.transform.Find("Frame");
            Assert.IsNotNull(frame, "Frame child must exist after frameColor is set");
            var img = frame.GetComponent<UnityImage>();
            Assert.IsNotNull(img);
            var tint = img.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint must be present on ScrollList frame");
            Assert.IsTrue(tint.enabled, "GradientTint must be enabled on frame");
            Assert.AreEqual(Color.white, tint.Top);
            Assert.AreEqual(Color.black, tint.Bottom);
        }

        // 6. Variant round-trip: solid↔gradient via ReSolve
        [Test]
        public void Variant_RoundTrip_SolidAndGradient()
        {
            // Base = gradient (#ffffff,#000000); mobile variant = solid (#ff0000).
            var s = Open("<Image id='im' color='#ffffff,#000000' color.mobile='#ff0000'/>");
            var img = s.Get<Image>("im").GameObject.GetComponent<UnityImage>();

            // Baseline: gradient active
            var tint = img.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint component must exist");
            Assert.IsTrue(tint.enabled, "GradientTint must be enabled at baseline");
            Assert.AreEqual(Color.white, tint.Top);
            Assert.AreEqual(Color.black, tint.Bottom);

            try
            {
                // Activate variant → solid red, tint disabled
                UI.Variants.Set("mobile", true);

                Assert.IsFalse(tint.enabled, "GradientTint must be disabled after switching to solid variant");
                Assert.AreEqual(Color.red, img.color, "solid variant color must be applied");

                // Deactivate → gradient restored
                UI.Variants.Set("mobile", false);

                Assert.IsTrue(tint.enabled, "GradientTint must be re-enabled after restoring gradient");
                Assert.AreEqual(Color.white, tint.Top, "gradient top restored");
                Assert.AreEqual(Color.black, tint.Bottom, "gradient bottom restored");
            }
            finally
            {
                UI.Variants.Set("mobile", false);
            }
        }

        // 7. hoverModulate with gradient value throws (UI.Theme.Resolve still rejects gradients)
        [Test]
        public void HoverModulate_Gradient_Throws_On_Open()
        {
            // hoverModulate still routes through UI.Theme.Resolve which throws on gradients.
            // ControlAttributeApplier wraps the System.Exception as a ParseException.
            var ex = Assert.Throws<ParseException>(() =>
                Open("<Btn id='b' hoverModulate='#ffffff,#000000'>x</Btn>"));
            StringAssert.Contains("does not support gradient", ex.Message);
        }

        // 8. Text gradient — enables TMP VertexGradient, top=white, bottom=black, color==white
        [Test]
        public void Text_Gradient_SetsTmpVertexGradient()
        {
            var s = Open("<Text id='t' color='#ffffff,#000000'>hello</Text>");
            var tmp = s.Get<PromptUGUI.Controls.Text>("t").GameObject
                       .GetComponentInChildren<TMPro.TMP_Text>();
            Assert.IsNotNull(tmp, "TMP_Text component must be present");
            Assert.IsTrue(tmp.enableVertexGradient, "enableVertexGradient must be true for gradient");
            Assert.AreEqual(Color.white, tmp.colorGradient.topLeft, "topLeft must be white");
            Assert.AreEqual(Color.white, tmp.colorGradient.topRight, "topRight must be white");
            Assert.AreEqual(Color.black, tmp.colorGradient.bottomLeft, "bottomLeft must be black");
            Assert.AreEqual(Color.black, tmp.colorGradient.bottomRight, "bottomRight must be black");
            Assert.AreEqual(Color.white, tmp.color, "color must be white when gradient is active");
        }

        // 9. Text solid color — no VertexGradient, color==red
        [Test]
        public void Text_Solid_NoVertexGradient()
        {
            var s = Open("<Text id='t' color='#ff0000'>hi</Text>");
            var tmp = s.Get<PromptUGUI.Controls.Text>("t").GameObject
                       .GetComponentInChildren<TMPro.TMP_Text>();
            Assert.IsNotNull(tmp, "TMP_Text component must be present");
            Assert.IsFalse(tmp.enableVertexGradient, "enableVertexGradient must be false for solid color");
            Assert.AreEqual(Color.red, tmp.color, "solid color must be applied directly");
        }

        // 10. Text Variant round-trip: gradient→solid reverts enableVertexGradient; solid→gradient restores it
        [Test]
        public void Text_Variant_GradientToSolid_RevertsVertexGradient()
        {
            var s = Open("<Text id='t' color='#ffffff,#000000' color.mobile='#ff0000'>x</Text>");
            var tmp = s.Get<PromptUGUI.Controls.Text>("t").GameObject
                       .GetComponentInChildren<TMPro.TMP_Text>();
            Assert.IsNotNull(tmp, "TMP_Text component must be present");

            // Baseline: gradient active
            Assert.IsTrue(tmp.enableVertexGradient, "enableVertexGradient must be true at baseline");
            Assert.AreEqual(Color.white, tmp.colorGradient.topLeft, "gradient top must be white at baseline");
            Assert.AreEqual(Color.black, tmp.colorGradient.bottomRight, "gradient bottom must be black at baseline");

            try
            {
                // Activate mobile variant → solid red, gradient disabled
                UI.Variants.Set("mobile", true);

                Assert.IsFalse(tmp.enableVertexGradient, "enableVertexGradient must be false after switching to solid variant");
                Assert.AreEqual(Color.red, tmp.color, "solid variant color must be applied");

                // Deactivate → gradient restored
                UI.Variants.Set("mobile", false);

                Assert.IsTrue(tmp.enableVertexGradient, "enableVertexGradient must be re-enabled after restoring gradient variant");
                Assert.AreEqual(Color.white, tmp.colorGradient.topLeft, "gradient top must be white after restore");
                Assert.AreEqual(Color.black, tmp.colorGradient.bottomRight, "gradient bottom must be black after restore");
            }
            finally
            {
                UI.Variants.Set("mobile", false);
            }
        }
    }
}
