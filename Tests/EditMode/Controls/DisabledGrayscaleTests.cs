using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DisabledGrayscaleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void GrayscaleShader_LoadsFromResources_WithExpectedName()
        {
            var shader = Resources.Load<Shader>("PromptUGUI/Material/UI-Grayscale");
            Assert.IsNotNull(shader, "UI-Grayscale shader must live in Resources");
            Assert.AreEqual("UI/Grayscale", shader.name);
        }

        // SelectionState 序号镜像（测试程序集无法命名 protected 嵌套类型）。
        private const int Normal = 0;
        private const int Disabled = 4;

        private static Btn BuildBtn(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Btn id='b' {attrs}>Hi</Btn></Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        private static UnityEngine.UI.Image BgOf(Btn b) => b.GameObject.GetComponent<UnityEngine.UI.Image>();
        private static TMPro.TMP_Text LabelOf(Btn b) => b.GameObject.GetComponentInChildren<TMPro.TMP_Text>();
        private static PromptUGUI.Controls.Internal.PuiButton PuiOf(Btn b)
            => b.GameObject.GetComponent<PromptUGUI.Controls.Internal.PuiButton>();

        private static Color Gray(Color c)
        {
            var l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(l, l, l, c.a);
        }

        private static void AssertColorEq(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), "b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), "a");
        }

        [Test]
        public void PlainBtn_Disabled_DesaturatesBgAndLabel_RevertsOnNormal()
        {
            var btn = BuildBtn();
            var bg = BgOf(btn);
            var label = LabelOf(btn);
            var labelBase = label.color;
            var pui = PuiOf(btn);

            pui.SimulateState(Disabled);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "bg 应换成灰度材质");
            AssertColorEq(Gray(labelBase), label.color);

            pui.SimulateState(Normal);
            Assert.AreEqual(bg.defaultMaterial, bg.material, "还原回默认材质");
            AssertColorEq(labelBase, label.color);
        }

        [Test]
        public void ImageChild_GreysFromTheInside_KeepingItsOwnEffects()
        {
            // An FxImage owns its material (blur / glow / linear tint live in it), so swapping in
            // UI-Grayscale would throw those away — and the next parameter change would write the fx
            // material straight back, undoing the greying. It desaturates itself instead
            // (ISelfGrayscale — the same route ProceduralPanel already takes). spec 2026-09-02 §4.4.
            var stub = Sprite.Create(new Texture2D(8, 8), new Rect(0f, 0f, 8f, 8f), new Vector2(.5f, .5f));
            UI.SpriteResolver = _ => stub;
            try
            {
                UI.LoadDocument("t",
                    "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                    "<Btn id='b'><Image id='m' sprite='ui:x' size='16x16' glow='6'/></Btn>" +
                    "</Screen></PromptUGUI>");
                var btn = UI.Open("S").Get<Btn>("b");
                var img = btn.GameObject.transform.Find("m").GetComponent<UnityEngine.UI.Image>();
                Assert.AreEqual("UI/ImageFx", img.material.shader.name, "前置：fx 材质已在位");

                PuiOf(btn).SimulateState(Disabled);
                Assert.AreEqual("UI/ImageFx", img.material.shader.name, "禁用不得换掉 fx 材质");
                Assert.AreEqual(1f, img.material.GetFloat("_Desaturate"), 1e-4f);
                Assert.AreEqual(6f, img.material.GetFloat("_Glow"), 1e-4f, "光晕没有随禁用丢掉");

                PuiOf(btn).SimulateState(Normal);
                Assert.AreEqual(0f, img.material.GetFloat("_Desaturate"), 1e-4f);
                Assert.AreEqual(6f, img.material.GetFloat("_Glow"), 1e-4f);
            }
            finally
            {
                var tex = stub != null ? stub.texture : null;
                if (stub != null) Object.DestroyImmediate(stub);
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void ImageChild_WithoutFx_StillGreys_ThroughTheFxShader()
        {
            // No blur, no glow, no tint: the graphic has no material until the disabled state asks
            // for one — and it must go back to none when the state clears.
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                "<Btn id='b'><Image id='m' size='16x16'/></Btn></Screen></PromptUGUI>");
            var btn = UI.Open("S").Get<Btn>("b");
            var img = btn.GameObject.transform.Find("m").GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(img.defaultMaterial, img.material, "前置：无 fx 即无材质");

            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual("UI/ImageFx", img.material.shader.name);
            Assert.AreEqual(1f, img.material.GetFloat("_Desaturate"), 1e-4f);

            PuiOf(btn).SimulateState(Normal);
            Assert.AreEqual(img.defaultMaterial, img.material, "还原到不挂任何材质");
        }

        [Test]
        public void PlainBtn_DefaultGrayscale_KeepsColorTintTransition()
        {
            var btn = BuildBtn();
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint, PuiOf(btn).transition,
                "灰度默认不得翻 transition=None（hover/press 反馈保留）");
        }

        [Test]
        public void InteractableFalse_AppliesGrayscaleImmediately()
        {
            var btn = BuildBtn("interactable='false'");
            Assert.AreEqual("UI/Grayscale", BgOf(btn).material.shader.name,
                "interactable='false' 首装即处于 Disabled，订阅重放应立即去色");
        }

        // ── Task 4: 边界 —— 覆盖 / none / 剪枝 ──────────────────────────────

        private static Btn BuildBtnXml(string attrs, string body)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Btn id='b' {attrs}>{body}</Btn></Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        [Test]
        public void DisabledColor_Authored_SuppressesGrayscale()
        {
            var btn = BuildBtn("disabledColor='#800000'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "写了 disabledColor 不应装灰度控制器");
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material, "走颜色路径，bg 不换灰度材质");
        }

        [Test]
        public void DisabledModulateColor_Authored_SuppressesGrayscale()
        {
            var btn = BuildBtn("disabledModulate='#888888'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "disabledModulate=<色> 不应装灰度控制器");
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material);
        }

        [Test]
        public void DisabledSprite_Authored_SuppressesGrayscale()
        {
            var stub = UnityEngine.Sprite.Create(UnityEngine.Texture2D.whiteTexture,
                new UnityEngine.Rect(0, 0, 1, 1), UnityEngine.Vector2.zero);
            UI.SpriteResolver = _ => stub;
            var btn = BuildBtn("disabledSprite='ui:x'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "disabledSprite 不应装灰度控制器");
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material, "disabledSprite 走 overrideSprite，不换灰度材质");
        }

        [Test]
        public void DisabledModulateNone_OptsOut_NoGrayscale_NoColor_NoThrow()
        {
            var btn = BuildBtn("disabledModulate='none'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "none = 显式关，不装灰度控制器");
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint, PuiOf(btn).transition,
                "none 不应触发颜色路径（transition 仍 ColorTint）");
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material, "none：禁用态无任何表现");
        }

        [Test]
        public void StateReactFalse_Child_NotDesaturated()
        {
            var btn = BuildBtnXml("", "<Image id='keep' color='#FF0000' stateReact='false'/>");
            var keep = btn.Get<Image>("keep").GameObject.GetComponent<UnityEngine.UI.Image>();
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(keep.defaultMaterial, keep.material,
                "stateReact='false' 子节点禁用时材质保持默认（未被去色）");
        }

        [Test]
        public void NestedBtn_IsBoundary_InnerNotDesaturatedByOuter()
        {
            var outer = BuildBtnXml("", "<Btn id='inner'>x</Btn>");
            var inner = outer.Get<Btn>("inner");
            var innerBg = inner.GameObject.GetComponent<UnityEngine.UI.Image>();
            PuiOf(outer).SimulateState(Disabled);
            Assert.AreEqual(innerBg.defaultMaterial, innerBg.material,
                "嵌套 Btn 图形不被外层去色（材质保持默认）");
        }

        // ── Task 6: Tab ──────────────────────────────────────────────────────

        private static Tab BuildTab(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><TabBar id='bar'><Tab id='t' {attrs}>Edit</Tab></TabBar></Screen></PromptUGUI>");
            return UI.Open("S").Get<Tab>("bar/t");
        }

        [Test]
        public void Tab_Disabled_DesaturatesBg_RevertsOnNormal()
        {
            var tab = BuildTab();
            var bg = tab.GameObject.GetComponent<UnityEngine.UI.Image>();
            var pui = tab.GameObject.GetComponent<PromptUGUI.Controls.Internal.PuiToggle>();

            pui.SimulateState(Disabled);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name);

            pui.SimulateState(Normal);
            Assert.AreEqual(bg.defaultMaterial, bg.material);
        }

        [Test]
        public void Tab_DisabledColor_Authored_SuppressesGrayscale()
        {
            var tab = BuildTab("disabledColor='#800000'");
            Assert.IsNull(tab.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "Tab 写了 disabledColor 不应装灰度控制器");
        }

        [Test]
        public void Tab_DisabledModulateNone_OptsOut()
        {
            var tab = BuildTab("disabledModulate='none'");
            var bg = tab.GameObject.GetComponent<UnityEngine.UI.Image>();
            var pui = tab.GameObject.GetComponent<PromptUGUI.Controls.Internal.PuiToggle>();
            Assert.IsNull(tab.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "none = 显式关，不装灰度控制器");
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint, pui.transition,
                "none 不应触发颜色路径（transition 仍 ColorTint）");
            pui.SimulateState(Disabled);
            Assert.AreEqual(bg.defaultMaterial, bg.material, "none：Tab 禁用态无去色");
        }

        // ── Task 7: Toggle ───────────────────────────────────────────────────

        private static Toggle BuildToggle(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Toggle id='t' {attrs}>On</Toggle></Screen></PromptUGUI>");
            return UI.Open("S").Get<Toggle>("t");
        }

        [Test]
        public void Toggle_Disabled_DesaturatesBg_RevertsOnEnable()
        {
            var tog = BuildToggle();
            var bg = tog.GameObject.transform.Find("Background").GetComponent<UnityEngine.UI.Image>();

            tog.Interactable = false;
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name);

            tog.Interactable = true;
            Assert.AreEqual(bg.defaultMaterial, bg.material);
        }

        [Test]
        public void Toggle_DisabledColor_Authored_SuppressesGrayscale()
        {
            var tog = BuildToggle("disabledColor='#800000'");
            Assert.IsNull(tog.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "Toggle 写了 disabledColor 不应装灰度控制器");
        }

        [Test]
        public void Toggle_DisabledModulateNone_OptsOut()
        {
            var tog = BuildToggle("disabledModulate='none'");
            var bg = tog.GameObject.transform.Find("Background").GetComponent<UnityEngine.UI.Image>();
            Assert.IsNull(tog.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "none = 显式关，不装灰度控制器");
            tog.Interactable = false;
            Assert.AreEqual(bg.defaultMaterial, bg.material, "none：Toggle 禁用态无去色");
        }

        // ── Task 5: capture-once 跨 ReSolve ─────────────────────────────────

        [Test]
        public void CaptureOnce_DisabledThenReSolve_ThenEnable_RevertsToDefault()
        {
            var btn = BuildBtn("interactable='false'");  // 持久禁用
            var bg = BgOf(btn);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "前置：已去色");

            UI.Variants.Set("dark", true);   // ReSolve（OnAfterApply 重跑）
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "ReSolve 中仍禁用 → 维持去色");

            btn.Interactable = true;          // 重新启用
            Assert.AreEqual(bg.defaultMaterial, bg.material,
                "capture-once：还原回原始默认材质，而非卡在灰度");
        }

        [Test]
        public void CaptureOnce_WithTintLinear_RevertsToAuthoredMaterial()
        {
            var btn = BuildBtn("interactable='false' tint='linear'");
            var bg = BgOf(btn);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "禁用 → 去色（覆盖 linear）");

            UI.Variants.Set("dark", true);    // tint setter 重跑把材质复位成 linear，灰度须重新盖回
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "ReSolve 后仍禁用 → 重新去色");

            btn.Interactable = true;
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name,
                "还原回作者材质（linear），而非默认或灰度");
        }

        // ===== capture-once must not outlive the value it captured =====

        private static Btn LoadThemed(string themedLabelColour)
        {
            var xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
  <Style name='btn' color='#E8D2A8' textColor='#4A3018'/>
  <Theme name='farm'><Color name='ink' value='#000'/></Theme>
  <Theme name='glass'><Style name='btn' color='#334455' textColor='" + themedLabelColour + @"'/></Theme>
  <Screen name='S'><Btn id='b' class='btn'>Hi</Btn></Screen>
</PromptUGUI>";
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadDocumentAsync("d").GetAwaiter().GetResult();
            UI.Theme.Set("farm");
            return UI.Open("S").Get<Btn>("b");
        }

        /// <summary>
        /// A theme switch used to lose every label colour, and only on controls that author no
        /// <c>disabled*</c> — i.e. the default ones.
        ///
        /// <para>This controller captures each graphic's original once and, on every re-Configure
        /// (which a ReSolve triggers), wrote the capture back. For a TMP label that write IS the
        /// colour, so it landed right after <c>textColor</c>'s setter and reverted it to the previous
        /// theme's value. The bg escaped only because the non-TMP branch writes <c>material</c>
        /// rather than <c>color</c>, so nothing about the symptom pointed here.</para>
        ///
        /// <para>Not greyed ⇒ what is on screen is what the attribute pipeline just wrote ⇒ that is
        /// the value worth capturing.</para>
        /// </summary>
        [Test]
        public void ThemeSwitch_UpdatesTheLabelColour_DespiteTheCaptureOnceOriginal()
        {
            var b = LoadThemed("#1C2B33");
            Assume.That(b.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(), Is.Not.Null,
                "guard: no disabled* authored, so the default grayscale controller is installed");
            Assume.That((Color32)LabelOf(b).color, Is.EqualTo(new Color32(0x4A, 0x30, 0x18, 0xff)));

            UI.Theme.Set("glass");

            Assert.AreEqual(new Color32(0x1C, 0x2B, 0x33, 0xff), (Color32)LabelOf(b).color,
                "the label has to follow the theme like every other colour");
        }

        /// <summary>…and the refreshed capture is what a later disable/enable restores.</summary>
        [Test]
        public void AfterAThemeSwitch_DisableAndEnable_RestoresTheNewColour()
        {
            var b = LoadThemed("#1C2B33");
            UI.Theme.Set("glass");

            PuiOf(b).SimulateState(Disabled);
            PuiOf(b).SimulateState(Normal);

            Assert.AreEqual(new Color32(0x1C, 0x2B, 0x33, 0xff), (Color32)LabelOf(b).color,
                "restoring must return to the CURRENT theme's colour, not the one captured at build");
        }
    }
}
