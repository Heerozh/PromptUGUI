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
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>());
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
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>());
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
            var before = keep.material;
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(before, keep.material, "stateReact='false' 子节点禁用时不换材质");
        }

        [Test]
        public void NestedBtn_IsBoundary_InnerNotDesaturatedByOuter()
        {
            var outer = BuildBtnXml("", "<Btn id='inner'>x</Btn>");
            var inner = outer.Get<Btn>("inner");
            var innerBg = inner.GameObject.GetComponent<UnityEngine.UI.Image>();
            var before = innerBg.material;
            PuiOf(outer).SimulateState(Disabled);
            Assert.AreEqual(before, innerBg.material, "嵌套 Btn 图形不被外层去色");
        }

    }
}
