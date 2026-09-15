using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Lint;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

using Screen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;
using UnityRawImage = UnityEngine.UI.RawImage;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Pointer hit-testing is declared, not painted (2026-09-15 spec §3): only an interactive
    /// control's own hit layer and a node the author marked <c>raycastTarget="true"</c> enter the
    /// raycast list. Everything else — a painted Frame, an Image, a RawImage — is click-through by
    /// default, and a panel that should block what is behind it says so on its root.
    /// </summary>
    public class RaycastTargetTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string body, string top = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{top}<Screen name='S'>
  {body}
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static ProceduralPanel PanelOf(Frame f) => f.GameObject.GetComponent<ProceduralPanel>();

        // ===== <Frame> =====

        [Test]
        public void Frame_WithVisuals_IsClickThroughByDefault()
        {
            var f = Open("<Frame id='f' color='#fff' radius='8'/>").Get<Frame>("f");
            Assert.IsFalse(PanelOf(f).raycastTarget,
                "a drawn Frame is not a hit target until the author says so (spec §3)");
        }

        [Test]
        public void Frame_RaycastTargetTrue_MakesItsPanelAHitTarget()
        {
            var f = Open("<Frame id='f' color='#fff' raycastTarget='true'/>").Get<Frame>("f");
            Assert.IsTrue(PanelOf(f).raycastTarget);
        }

        [Test]
        public void Frame_RaycastTargetFalse_OnPaintedFrame_StaysOff()
        {
            var f = Open("<Frame id='f' color='#fff' raycastTarget='false'/>").Get<Frame>("f");
            Assert.IsFalse(PanelOf(f).raycastTarget);
        }

        [Test]
        public void Frame_RaycastTargetTrue_WithoutVisuals_AttachesAZeroGeometryCatcher()
        {
            var f = Open("<Frame id='f' raycastTarget='true'/>").Get<Frame>("f");
            var p = PanelOf(f);

            Assert.IsNotNull(p, "the catcher needs a Graphic for GraphicRaycaster to see");
            Assert.IsTrue(p.raycastTarget);
            Assert.IsFalse(p.IsPanelVisible, "nothing was declared, so nothing is drawn");
            using var vh = new UnityEngine.UI.VertexHelper();
            p.BuildMeshForTests(vh);
            Assert.AreEqual(0, vh.currentVertCount,
                "an invisible catcher emits no geometry — zero overdraw, unlike the alpha-0 Image hack");
        }

        [Test]
        public void Frame_RaycastTarget_FollowsVariantOverride()
        {
            var f = Open("<Frame id='f' color='#fff' raycastTarget='false' raycastTarget.mobile='true'/>")
                .Get<Frame>("f");
            Assume.That(PanelOf(f).raycastTarget, Is.False);

            UI.Variants.Set("mobile", true);
            Assert.IsTrue(PanelOf(f).raycastTarget, "Variant override applies on ReSolve");
            UI.Variants.Set("mobile", false);
            Assert.IsFalse(PanelOf(f).raycastTarget, "and the base comes back");
        }

        [Test]
        public void Frame_VariantOnlyRaycastTarget_TurnsOffAgainWhenTheVariantLeaves()
        {
            // No base value: the setter simply does not run once the variant is gone, and that
            // silence has to read as "unauthored" (the ProceduralSurface pass rule), not latch.
            var f = Open("<Frame id='f' color='#fff' raycastTarget.mobile='true'/>").Get<Frame>("f");
            UI.Variants.Set("mobile", true);
            Assume.That(PanelOf(f).raycastTarget, Is.True);

            UI.Variants.Set("mobile", false);

            Assert.IsFalse(PanelOf(f).raycastTarget);
        }

        [Test]
        public void Image_VariantOnlyRaycastTarget_TurnsOffAgainWhenTheVariantLeaves()
        {
            var i = Open("<Image id='i' raycastTarget.mobile='true'/>").Get<Image>("i");
            var img = i.GameObject.GetComponent<UnityImage>();
            UI.Variants.Set("mobile", true);
            Assume.That(img.raycastTarget, Is.True);

            UI.Variants.Set("mobile", false);

            Assert.IsFalse(img.raycastTarget);
        }

        [Test]
        public void Text_VariantOnlyRaycastTarget_TurnsOffAgainWhenTheVariantLeaves()
        {
            var t = Open("<Text id='t' raycastTarget.mobile='true'>x</Text>").Get<Text>("t");
            var tmp = t.GameObject.GetComponent<TMPro.TMP_Text>();
            UI.Variants.Set("mobile", true);
            Assume.That(tmp.raycastTarget, Is.True);

            UI.Variants.Set("mobile", false);

            Assert.IsFalse(tmp.raycastTarget);
        }

        [Test]
        public void Frame_RaycastTarget_ViaStyleClass()
        {
            var f = Open("<Frame id='f' class='panel' color='#fff'/>",
                         "<Style name='panel' raycastTarget='true'/>").Get<Frame>("f");
            Assert.IsTrue(PanelOf(f).raycastTarget, "an ordinary attribute: class= packs carry it");
        }

        [Test]
        public void Frame_RaycastTarget_IsNotAProceduralAttr()
        {
            CollectionAssert.DoesNotContain(ProceduralAttrNames.All, "raycastTarget",
                "it is not a shape and must not declare procedural mode on a ProceduralControl");

            // <Btn> does not expose it at all: its hit layer is always on. The attribute is skipped,
            // and skipping it must not drag the Btn into procedural mode.
            var b = Open("<Btn id='b' raycastTarget='true'>OK</Btn>").Get<Btn>("b");
            Assert.IsNull(b.GameObject.GetComponentInChildren<ProceduralPanel>(true));
        }

        // ===== <Image> / <RawImage> =====

        [Test]
        public void Image_IsClickThroughByDefault()
        {
            var i = Open("<Image id='i'/>").Get<Image>("i");
            Assert.IsFalse(i.GameObject.GetComponent<UnityImage>().raycastTarget,
                "uGUI's Graphic default is true; the library's default is false (spec §1.2)");
        }

        [Test]
        public void Image_RaycastTargetTrue()
        {
            var i = Open("<Image id='i' raycastTarget='true'/>").Get<Image>("i");
            Assert.IsTrue(i.GameObject.GetComponent<UnityImage>().raycastTarget);
        }

        [Test]
        public void RawImage_IsClickThroughByDefault()
        {
            var r = Open("<RawImage id='r'/>").Get<RawImage>("r");
            Assert.IsFalse(r.GameObject.GetComponent<UnityRawImage>().raycastTarget);
        }

        [Test]
        public void RawImage_RaycastTargetTrue()
        {
            var r = Open("<RawImage id='r' raycastTarget='true'/>").Get<RawImage>("r");
            Assert.IsTrue(r.GameObject.GetComponent<UnityRawImage>().raycastTarget);
        }

        [Test]
        public void Image_PointerSubscription_TurnsRaycastOn()
        {
            var i = Open("<Image id='i'/>").Get<Image>("i");
            var img = i.GameObject.GetComponent<UnityImage>();
            Assume.That(img.raycastTarget, Is.False);

            using var _ = i.OnPointerDown.Subscribe(__ => { });

            Assert.IsTrue(img.raycastTarget,
                "a pointer stream nobody can ever hit is a silent failure; subscribing is the intent");
        }

        [Test]
        public void RawImage_PointerSubscription_TurnsRaycastOn()
        {
            var r = Open("<RawImage id='r'/>").Get<RawImage>("r");
            using var _ = r.OnPointerEnter.Subscribe(__ => { });
            Assert.IsTrue(r.GameObject.GetComponent<UnityRawImage>().raycastTarget);
        }

        [Test]
        public void Image_HoverTrigger_TurnsRaycastOn()
        {
            var s = Open("<Trigger id='t' on='hover-enter@i'><Image id='i'/></Trigger>");
            var img = s.Get<Image>("i").GameObject.GetComponent<UnityImage>();
            Assert.IsTrue(img.raycastTarget, "<Trigger on='hover-enter@…'> subscribes through the same relay");
        }

        [Test]
        public void Image_ExplicitFalse_PlusPointerSource_StaysOff_AndWarnsOnce()
        {
            var warnings = 0;
            void Count(string msg, string stack, LogType type)
            {
                if (type == LogType.Warning && msg.Contains("raycastTarget")) warnings++;
            }
            UnityEngine.Application.logMessageReceived += Count;
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("raycastTarget"));
                var s = Open("<Image id='i' raycastTarget='false' color='#fff' color.mobile='#000'/>");
                var i = s.Get<Image>("i");
                var img = i.GameObject.GetComponent<UnityImage>();

                using var _ = i.OnPointerDown.Subscribe(__ => { });

                Assert.IsFalse(img.raycastTarget, "the author's explicit false wins over the subscription");
                Assert.AreEqual(1, warnings, "…but the contradiction is reported");

                // ReSolve replays raycastTarget='false' — same answer, and no second warning.
                UI.Variants.Set("mobile", true);
                UI.Variants.Set("mobile", false);
                Assert.IsFalse(img.raycastTarget);
                Assert.AreEqual(1, warnings, "warn once per control, not once per ReSolve");
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= Count;
            }
        }

        [Test]
        public void Image_ReSolve_KeepsRelayDrivenRaycast()
        {
            var s = Open("<Image id='i' color='#fff' color.mobile='#000'/>");
            var i = s.Get<Image>("i");
            var img = i.GameObject.GetComponent<UnityImage>();
            using var _ = i.OnPointerEnter.Subscribe(__ => { });
            Assume.That(img.raycastTarget, Is.True);

            UI.Variants.Set("mobile", true);

            Assert.IsTrue(img.raycastTarget, "a ReSolve must not forget that a subscriber needs the hit");
        }

        // ===== unchanged neighbours =====

        [Test]
        public void Text_IsClickThroughByDefault()
        {
            var t = Open("<Text id='t'>x</Text>").Get<Text>("t");
            Assert.IsFalse(t.GameObject.GetComponent<TMPro.TMP_Text>().raycastTarget,
                "TMP defaults to true — and the docs always claimed <Text> was false. It is now.");
        }

        [Test]
        public void Btn_HitLayer_IsAlwaysOn()
        {
            var b = Open("<Btn id='b'>OK</Btn>").Get<Btn>("b");
            Assert.IsTrue(b.GameObject.GetComponent<UnityImage>().raycastTarget);
        }
    }
}
