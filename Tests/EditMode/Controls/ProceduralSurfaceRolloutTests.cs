using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Lint;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// M2: the same contract <see cref="ProceduralSurfaceContractTests"/> pins down for
    /// <c>&lt;Btn&gt;</c>, held for every control wired up since. Driven off
    /// <see cref="ProceduralSurfaceRules.SurfaceTags"/> so a control cannot be added to that list —
    /// which is what makes the linter stop reporting its shape attributes as ignored — without
    /// arriving here at the same time.
    ///
    /// <para>Three of them differ on purpose and are called out where it matters:
    /// <c>&lt;Slider&gt;</c> keeps <c>targetGraphic</c> on its handle (spec §13.1),
    /// <c>&lt;Progress&gt;</c> keeps its surface inside a Bg layer that ships switched off, and
    /// <c>&lt;TabMenu&gt;</c>'s surface is its popup panel rather than the face of its handle.</para>
    /// </summary>
    public class ProceduralSurfaceRolloutTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static string[] Tags => ProceduralSurfaceRules.SurfaceTags.OrderBy(t => t).ToArray();

        // <Collapsible> owns its own height (header + body, PUI-COLLAPSIBLE-HEIGHT) — it is the one
        // surface control an author cannot give one to, so this fixture asks for the bar's height
        // instead and lets the panel work out the rest.
        private static string SizeAttrsFor(string tag)
            => tag == "Collapsible" ? "width='160' headerHeight='48'" : "width='160' height='48'";

        private static Control Load(string tag, string attrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <{tag} id='x' anchor='center' {SizeAttrsFor(tag)} {attrs}/>
</Screen></PromptUGUI>");
            return (Control)(object)UI.Open("S").Get<Control>("x");
        }

        private static ProceduralPanel PanelIn(Control c)
        {
            foreach (var p in c.GameObject.GetComponentsInChildren<ProceduralPanel>(true))
                if (p.gameObject.name == ProceduralSurface.NodeName) return p;
            return null;
        }

        [Test]
        public void EveryWiredControl_IsAProceduralControl()
        {
            foreach (var tag in Tags)
                Assert.IsTrue(
                    typeof(ProceduralControl).IsAssignableFrom(UI.Registry.Resolve(tag).ControlType),
                    $"<{tag}> is listed in SurfaceTags but does not derive from ProceduralControl");
        }

        [Test]
        public void EveryWiredControl_WithoutProceduralAttrs_HasNoSurface()
        {
            foreach (var tag in Tags)
                Assert.IsNull(PanelIn(Load(tag, "")),
                    $"<{tag}> must stay byte-for-byte what it was when nothing asks for a shape");
        }

        [Test]
        public void EveryWiredControl_AttachesAndDrawsASurface()
        {
            foreach (var tag in Tags)
            {
                var panel = PanelIn(Load(tag, "radius='8' color='#3366ff'"));
                Assert.IsNotNull(panel, $"<{tag} radius='8'> should attach a surface");
                Assert.AreEqual(0, panel.transform.GetSiblingIndex(),
                    $"<{tag}>'s surface must draw under the control's own content");
                Assert.IsFalse(panel.raycastTarget, $"<{tag}>'s surface must stay click-through");
                Assert.IsTrue(panel.IsPanelVisible,
                    $"<{tag}>'s surface is attached but draws nothing — the shape would be invisible");
            }
        }

        /// <summary>
        /// The surface goes INSIDE the layer the Image was on, so it inherits that layer's rect —
        /// a Toggle's shape covers its checkbox, not its label, and a Slider's covers its track.
        /// </summary>
        [Test]
        public void EveryWiredControl_SurfaceCoversItsHostLayerExactly()
        {
            foreach (var tag in Tags)
            {
                var rt = (RectTransform)PanelIn(Load(tag, "radius='8'")).transform;
                Assert.AreEqual(Vector2.zero, rt.anchorMin, tag);
                Assert.AreEqual(Vector2.one, rt.anchorMax, tag);
                Assert.AreEqual(Vector2.zero, rt.offsetMin, tag);
                Assert.AreEqual(Vector2.zero, rt.offsetMax, tag);
                Assert.IsNotNull(rt.parent.GetComponent<UnityImage>(),
                    $"<{tag}>'s surface must sit inside the layer whose Image it replaces");
            }
        }

        [Test]
        public void EveryWiredControl_RetiresTheHostImage_WithoutDisablingIt()
        {
            foreach (var tag in Tags)
            {
                var host = PanelIn(Load(tag, "radius='8'")).transform.parent.GetComponent<UnityImage>();

                Assert.IsNull(host.sprite, $"<{tag}>: a bitmap under the SDF face is what §7 forbids");
                Assert.AreEqual(0f, host.color.a, $"<{tag}>: the Image must stand down");
                Assert.IsTrue(host.enabled,
                    $"<{tag}>: uGUI only raycasts enabled Graphics — disabling the Image would "
                    + "silently stop the control responding to input");
            }
        }

        [Test]
        public void EveryWiredControl_RoundTripsAVariantWithoutRebuildingOrStacking()
        {
            foreach (var tag in Tags)
            {
                var c = Load(tag, "radius.mobile='8'");
                var go = c.GameObject;

                UI.Variants.Set("mobile", true);
                var panel = PanelIn(c);
                Assert.IsNotNull(panel, $"<{tag}>: the variant turns the mode on");

                UI.Variants.Set("mobile", false);
                Assert.IsFalse(panel.gameObject.activeSelf, $"<{tag}>: …and off again");

                UI.Variants.Set("mobile", true);
                Assert.AreSame(go, c.GameObject, $"<{tag}>: a variant flip must never rebuild");
                Assert.AreSame(panel, PanelIn(c), $"<{tag}>: nor re-create the surface");
                Assert.AreEqual(1, go.GetComponentsInChildren<ProceduralPanel>(true)
                        .Count(p => p.gameObject.name == ProceduralSurface.NodeName),
                    $"<{tag}>: surfaces must not stack");

                UI.Variants.Set("mobile", false);
            }
        }

        [Test]
        public void ControlsWithASelectable_MoveTargetGraphicToTheSurface()
        {
            // …except Slider and TabMenu, below.
            foreach (var tag in Tags)
            {
                if (tag == "Slider" || tag == "TabMenu") continue;
                var c = Load(tag, "radius='8'");
                var selectable = c.GameObject.GetComponent<Selectable>();
                if (selectable == null) continue;   // ScrollList / Progress have none of their own

                Assert.AreSame(PanelIn(c), selectable.targetGraphic,
                    $"<{tag}>: state colours drive targetGraphic, so it has to follow the surface");
            }
        }

        /// <summary>
        /// Spec §13.1. The Slider's primary surface is the track, but the part that reacts to hover
        /// and press is the handle — moving targetGraphic to the track would make the whole groove
        /// flash, which no slider does.
        /// </summary>
        [Test]
        public void Slider_KeepsTargetGraphicOnItsHandle()
        {
            var c = Load("Slider", "radius='8'");
            var slider = c.GameObject.GetComponent<UnityEngine.UI.Slider>();

            Assert.IsNotNull(PanelIn(c), "guard: the track did go procedural");
            Assert.AreEqual("Handle", slider.targetGraphic.gameObject.name);
        }

        /// <summary>
        /// The other controls here draw one face, and the surface replaces it — so the Selectable's
        /// targetGraphic has to follow. A TabMenu draws two things in two places: a handle that
        /// hovers and presses, and a menu panel that is what <c>radius</c> / <c>glass</c> describe
        /// (spec TM-D3). Pointing targetGraphic at the panel would tint the dropped menu on hovering
        /// the handle; the handle is deliberately transparent, so its caption carries the state.
        /// </summary>
        [Test]
        public void TabMenu_KeepsTargetGraphicOnItsCaption()
        {
            var c = Load("TabMenu", "radius='8'");
            var button = c.GameObject.GetComponent<UnityEngine.UI.Button>();

            Assert.IsNotNull(PanelIn(c), "guard: the panel did go procedural");
            Assert.AreEqual("Popup", PanelIn(c).transform.parent.name, "…on the popup, not the handle");
            Assert.AreEqual("Label", button.targetGraphic.gameObject.name);
        }

        /// <summary>
        /// Progress ships its Bg layer switched off — it only appears once <c>bg=</c> or
        /// <c>bgColor=</c> is authored. The surface lives inside that layer, so asking for a shape
        /// has to switch it on too, or the shape is drawn inside something invisible.
        /// </summary>
        [Test]
        public void Progress_SwitchesOnItsBgLayerForTheSurface()
        {
            var plain = Load("Progress", "");
            Assert.IsFalse(plain.GameObject.transform.Find("MaskWrapper/Bg").gameObject.activeSelf,
                "guard: Bg still ships off");

            var c = Load("Progress", "radius='8' bgColor='#3366ff'");
            var bg = c.GameObject.transform.Find("MaskWrapper/Bg").gameObject;

            Assert.IsTrue(bg.activeSelf);
            Assert.IsTrue(PanelIn(c).IsPanelVisible);
        }

        [Test]
        public void Progress_SurfaceAloneIsEnoughToShowTheBgLayer()
        {
            var c = Load("Progress", "radius='8'");

            Assert.IsTrue(c.GameObject.transform.Find("MaskWrapper/Bg").gameObject.activeSelf,
                "radius= alone says 'draw a background with this shape'");
        }
    }
}
