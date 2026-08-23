using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// 内部图层的皮肤属性：Slider 的 fill / handle、Toggle 的 checkmark、Dropdown 的
    /// arrow / item* / scrollbar*、ScrollList 的 scrollbar*。命名规约沿用 &lt;Progress&gt;：
    /// 每层一对 <c>&lt;layer&gt;</c>（sprite）+ <c>&lt;layer&gt;Color</c>。
    /// </summary>
    public class InnerLayerSkinAttrTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'>" + innerXml + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static UnityImage Layer(IControl c, string childName)
        {
            foreach (var img in ((Control)c).GameObject.GetComponentsInChildren<UnityImage>(true))
                if (img.gameObject.name == childName) return img;
            return null;
        }

        // ---- Slider ----

        [Test]
        public void Slider_FillColor_LandsOnFillLayer()
        {
            var s = Open("<Slider id='x' fillColor='#00ff00'/>");
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)Layer(s.Get<Slider>("x"), "Fill").color);
        }

        [Test]
        public void Slider_HandleColor_LandsOnHandleLayer()
        {
            var s = Open("<Slider id='x' handleColor='#0000ff'/>");
            Assert.AreEqual(new Color32(0, 0, 0xff, 0xff), (Color32)Layer(s.Get<Slider>("x"), "Handle").color);
        }

        [Test]
        public void Slider_ColorAndFillColor_AreDifferentLayers()
        {
            var s = Open("<Slider id='x' color='#ff0000' fillColor='#00ff00'/>");
            var slider = s.Get<Slider>("x");
            Assert.AreEqual(new Color32(0xff, 0, 0, 0xff), (Color32)Layer(slider, "Background").color);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)Layer(slider, "Fill").color);
        }

        [Test]
        public void Slider_EmptyFillSprite_ClearsSprite()
        {
            // 默认皮给 Fill 挂了 sliced sprite；"" 清掉 = 纯色进度条。
            var s = Open("<Slider id='x' fill=''/>");
            Assert.IsNull(Layer(s.Get<Slider>("x"), "Fill").sprite);
        }

        [Test]
        public void Slider_HandleSprite_Cleared()
        {
            var s = Open("<Slider id='x' handle=''/>");
            Assert.IsNull(Layer(s.Get<Slider>("x"), "Handle").sprite);
        }

        [Test]
        public void Slider_FillColor_AcceptsTokenAlphaSuffix()
        {
            var s = Open("<Slider id='x' fillColor='#ffffff/0.5'/>");
            Assert.AreEqual(0.5f, Layer(s.Get<Slider>("x"), "Fill").color.a, 0.01f);
        }

        // ---- Toggle ----

        [Test]
        public void Toggle_CheckmarkColor_LandsOnCheckmark()
        {
            var t = Open("<Toggle id='x' checkmarkColor='#ff00ff'/>");
            Assert.AreEqual(new Color32(0xff, 0, 0xff, 0xff),
                            (Color32)Layer(t.Get<Toggle>("x"), "Checkmark").color);
        }

        [Test]
        public void Toggle_Sprite_NowTargetsTheBox_NotTheCheckmark()
        {
            // 行为修正：sprite= 过去落在 Checkmark 上，跟 color= 指的不是一层。
            // 现在两者都指外层的 20x20 box，与其它所有控件一致；换对勾图形用 checkmark=。
            var t = Open("<Toggle id='x' sprite='' checkmark=''/>");
            var toggle = t.Get<Toggle>("x");
            Assert.IsNull(Layer(toggle, "Background").sprite);
            Assert.IsNull(Layer(toggle, "Checkmark").sprite);
        }

        [Test]
        public void Toggle_ColorAndCheckmarkColor_AreDifferentLayers()
        {
            var t = Open("<Toggle id='x' color='#ff0000' checkmarkColor='#00ff00'/>");
            var toggle = t.Get<Toggle>("x");
            Assert.AreEqual(new Color32(0xff, 0, 0, 0xff), (Color32)Layer(toggle, "Background").color);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)Layer(toggle, "Checkmark").color);
        }

        // ---- Dropdown ----

        [Test]
        public void Dropdown_ArrowColor_LandsOnArrow()
        {
            var d = Open("<Dropdown id='x' arrowColor='#123456'/>");
            Assert.AreEqual(new Color32(0x12, 0x34, 0x56, 0xff),
                            (Color32)Layer(d.Get<Dropdown>("x"), "Arrow").color);
        }

        [Test]
        public void Dropdown_EmptyArrow_DisablesTheGraphic()
        {
            // 箭头是字形：没有 sprite 的 Image 会画成实心方块，所以直接关组件。
            var d = Open("<Dropdown id='x' arrow=''/>");
            Assert.IsFalse(Layer(d.Get<Dropdown>("x"), "Arrow").enabled);
        }

        [Test]
        public void Dropdown_ItemColor_OverridesHardcodedHighlight()
        {
            // 不设时是硬编码的 #F5F5F5，深色主题下必炸 —— 这个属性就是为它加的。
            var d = Open("<Dropdown id='x' itemColor='#202020'/>");
            Assert.AreEqual(new Color32(0x20, 0x20, 0x20, 0xff),
                            (Color32)Layer(d.Get<Dropdown>("x"), "Item Background").color);
        }

        [Test]
        public void Dropdown_CheckmarkColor_LandsOnItemCheckmark()
        {
            var d = Open("<Dropdown id='x' checkmarkColor='#00ffff'/>");
            Assert.AreEqual(new Color32(0, 0xff, 0xff, 0xff),
                            (Color32)Layer(d.Get<Dropdown>("x"), "Item Checkmark").color);
        }

        [Test]
        public void Dropdown_ScrollbarColors_LandOnTrackAndHandle()
        {
            var d = Open("<Dropdown id='x' scrollbarColor='#111111' scrollbarHandleColor='#eeeeee'/>");
            var dd = d.Get<Dropdown>("x");
            Assert.AreEqual(new Color32(0x11, 0x11, 0x11, 0xff), (Color32)Layer(dd, "Scrollbar").color);
            Assert.AreEqual(new Color32(0xee, 0xee, 0xee, 0xff), (Color32)Layer(dd, "Handle").color);
        }

        [Test]
        public void Dropdown_TextColorAndItemTextColor_AreDifferentLabels()
        {
            var d = Open("<Dropdown id='x' textColor='#ff0000' itemTextColor='#00ff00'/>");
            var go = ((Control)d.Get<Dropdown>("x")).GameObject;
            TMPro.TMP_Text caption = null, item = null;
            foreach (var t in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.gameObject.name == "Label") caption = t;
                if (t.gameObject.name == "Item Label") item = t;
            }
            Assert.AreEqual(new Color32(0xff, 0, 0, 0xff), (Color32)caption.color);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)item.color);
        }

        // ---- ScrollList ----

        [Test]
        public void ScrollList_ScrollbarColors_LandOnTrackAndHandle()
        {
            var s = Open("<ScrollList id='x' scrollbarColor='#111111' scrollbarHandleColor='#eeeeee'/>");
            var sl = s.Get<ScrollList>("x");
            Assert.AreEqual(new Color32(0x11, 0x11, 0x11, 0xff), (Color32)Layer(sl, "Scrollbar Vertical").color);
            Assert.AreEqual(new Color32(0xee, 0xee, 0xee, 0xff), (Color32)Layer(sl, "Handle").color);
        }

        [Test]
        public void ScrollList_ScrollbarSkin_SurvivesDirectionSwitch()
        {
            // 滚动条是懒建的，且 direction 切换会启用另一根 —— pending 值必须回放到新建的那根上。
            var s = Open("<ScrollList id='x' direction='horizontal' scrollbarHandleColor='#eeeeee'/>");
            var sl = s.Get<ScrollList>("x");
            Assert.AreEqual(new Color32(0xee, 0xee, 0xee, 0xff), (Color32)Layer(sl, "Handle").color,
                "horizontal 方向的滚动条也要吃到皮肤");
        }

        [Test]
        public void ScrollList_ScrollbarSprite_Cleared()
        {
            var s = Open("<ScrollList id='x' scrollbar='' scrollbarHandle=''/>");
            var sl = s.Get<ScrollList>("x");
            Assert.IsNull(Layer(sl, "Scrollbar Vertical").sprite);
            Assert.IsNull(Layer(sl, "Handle").sprite);
        }
    }
}
