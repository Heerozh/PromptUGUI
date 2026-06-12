using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Tab OpenTab(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Tab>("t");
        }

        [Test]
        public void Tab_Has_Bg_And_Toggle()
        {
            // Suppress the no-ancestor warning fired by OnAttached.
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            Assert.IsNotNull(t.GameObject.GetComponent<UnityImage>(), "bg UnityImage on self");
            Assert.IsNotNull(t.GameObject.GetComponent<UnityToggle>(), "UnityToggle on self");
        }

        [Test]
        public void Tab_Inside_TabBar_Has_ToggleGroup_Wired()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='t'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var tab = screen.Get<Tab>("t");
            var bar = screen.Get<TabBar>("bar");
            var toggle = tab.GameObject.GetComponent<UnityToggle>();
            var group = bar.GameObject.GetComponent<ToggleGroup>();
            Assert.AreSame(group, toggle.group, "Tab's UnityToggle.group is the TabBar's ToggleGroup");
        }

        [Test]
        public void Tab_Text_Sets_Label()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='Hello'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual("Hello", label.text);
        }

        [Test]
        public void Tab_NoText_Has_No_Label()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            Assert.IsNull(t.GameObject.transform.Find("Label"),
                "no Label GameObject when text attr absent (lazy label)");
        }

        [Test]
        public void Tab_FontSize_Sets_TMP_FontSize()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='X' fontSize='18'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(18f, label.fontSize);
        }

        [Test]
        public void Tab_Default_FontSize_Is_24()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='X'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(24f, label.fontSize);
        }

        [Test]
        public void Tab_With_Icon_Creates_Icon_Child()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            var t = OpenTab("<Tab id='t' text='X' icon='ui:nope'/>");
            var icon = t.GameObject.transform.Find("Icon") as RectTransform;
            Assert.IsNotNull(icon, "Icon RT child created");
            Assert.IsNotNull(icon.GetComponent<UnityImage>(), "Icon UnityImage");
            Assert.IsFalse(icon.GetComponent<UnityImage>().raycastTarget, "Icon does not block raycasts");
        }

        [Test]
        public void Tab_Without_Icon_Attr_Has_No_Icon_Child()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='X'/>");
            Assert.IsNull(t.GameObject.transform.Find("Icon"), "no Icon RT when icon attr absent");
        }

        [Test]
        public void Tab_IsOn_Roundtrip()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' isOn='true'/>");
            Assert.IsTrue(t.IsOn);
            t.IsOn = false;
            Assert.IsFalse(t.IsOn);
        }

        [Test]
        public void Tab_OnValueChanged_Fires_On_Set()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            bool? observed = null;
            using var sub = t.OnValueChanged.Subscribe(v => observed = v);
            t.IsOn = true;
            Assert.IsTrue(observed == true);
        }

        [Test]
        public void Tab_OnSelected_Fires_Only_On_False_To_True()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' isOn='true'/>");
            int fires = 0;
            using var sub = t.OnSelected.Subscribe(_ => fires++);
            t.IsOn = false;
            t.IsOn = true;
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Tab_Sprite_Applies_To_Bg_Image()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:tab_bg" ? stub : null;
            var t = OpenTab("<Tab id='t' sprite='ui:tab_bg'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.AreSame(stub, bg.sprite);
        }

        [Test]
        public void Tab_SelectedSprite_Swaps_OverrideSprite_When_IsOn()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:tab_sel" ? stub : null;
            var t = OpenTab("<Tab id='t' selectedSprite='ui:tab_sel'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            var toggle = t.GameObject.GetComponent<UnityToggle>();
            var authored = bg.sprite; // built-in 9-slice default, must stay untouched

            Assert.IsNull(t.GameObject.transform.Find("Overlay"), "no Overlay child in the single-image model");
            Assert.IsNull(toggle.graphic, "UnityToggle.graphic stays null (no overlay)");
            Assert.AreEqual(Selectable.Transition.None, toggle.transition, "selectedSprite flips transition off ColorTint");

            // Image.overrideSprite getter returns m_OverrideSprite ?? sprite, so "no override in
            // effect" appears as the authored sprite falling through — same observable contract as Btn.
            Assert.AreEqual(authored, bg.overrideSprite, "not selected -> no overrideSprite (falls back to authored)");
            t.IsOn = true;
            Assert.AreSame(stub, bg.overrideSprite, "selected -> bg shows selectedSprite via overrideSprite");
            Assert.AreEqual(authored, bg.sprite, "authored sprite is untouched during selection");
            t.IsOn = false;
            Assert.AreEqual(authored, bg.overrideSprite, "deselected -> override cleared, getter falls back to authored sprite");
            Assert.AreEqual(authored, bg.sprite, "authored sprite still untouched after deselect");
        }

        [Test]
        public void Tab_Without_SelectedSprite_No_Swap()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite;
            t.IsOn = true;
            Assert.AreEqual(authored, bg.overrideSprite, "no selectedSprite -> no swap even when selected (falls back to authored)");
        }

        [Test]
        public void Tab_Empty_SelectedSprite_No_Swap()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' selectedSprite=''/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite;
            t.IsOn = true;
            Assert.AreEqual(authored, bg.overrideSprite, "empty selectedSprite is a no-op even when selected (falls back to authored)");
        }

        [Test]
        public void Tab_Default_Bg_Has_Sprite()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg.sprite, "Tab ships a built-in 9-slice bg sprite by default");
        }

        [Test]
        public void Tab_Sprite_None_Clears_Default_Bg()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' sprite='none'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.IsNull(bg.sprite, "sprite='none' drops the built-in bg sprite");
        }

        [Test]
        public void Tab_Sprite_Empty_Clears_Default_Bg()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' sprite=''/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.IsNull(bg.sprite, "sprite='' drops the built-in bg sprite, same effect as 'none'");
        }

        [Test]
        public void Tab_None_SelectedSprite_No_Swap()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' selectedSprite='none'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite;
            t.IsOn = true;
            Assert.AreEqual(authored, bg.overrideSprite, "selectedSprite='none' is a no-op even when selected (falls back to authored)");
        }

        [Test]
        public void Tab_IconOnly_NoLabel_NoCrash()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => stub;
            // icon setter must NOT NRE when there is no label (lazy label).
            var t = OpenTab("<Tab id='t' icon='ui:gear'/>");
            Assert.IsNull(t.GameObject.transform.Find("Label"), "no Label when text absent");
            Assert.IsNotNull(t.GameObject.transform.Find("Icon"), "Icon created");
        }

        [Test]
        public void Tab_IconAndText_LabelShiftedRight()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => stub;
            var t = OpenTab("<Tab id='t' icon='ui:gear' text='Hi'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(32f, label.rectTransform.offsetMin.x,
                "label is shifted right to make room for icon, regardless of setter order");
        }

        [Test]
        public void Tab_Color_AppliesToBg()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' color='#FF0000'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), bg.color);
        }

        [Test]
        public void Tab_TransparentColor_AlphaZero()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' color='#00000000'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(0f, bg.color.a);
        }

        [Test]
        public void Tab_SelectedSprite_SurvivesReSolve()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:tab_sel" ? stub : null;
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Tab id='t' selectedSprite='ui:tab_sel'/></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Tab>("t");
            var bg = t.GameObject.GetComponent<UnityImage>();
            var toggle = t.GameObject.GetComponent<UnityToggle>();
            t.IsOn = true;
            Assert.AreSame(stub, bg.overrideSprite);
            Assert.AreEqual(Selectable.Transition.None, toggle.transition);

            screen.ReSolve();

            Assert.AreSame(stub, bg.overrideSprite, "selectedSprite swap survives ReSolve");
            Assert.AreEqual(Selectable.Transition.None, toggle.transition, "transition stays None across ReSolve");
        }

        // overrideSprite shares the Image's single `type` field, so the swap must re-derive 9-slice
        // vs simple from the displayed sprite. A bordered selectedSprite on a sprite="" tab (whose
        // normal type is Simple) must render Sliced while selected, and revert to Simple when not.
        [Test]
        public void Tab_SelectedSprite_With9SliceBorder_RendersSliced()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var tex = new Texture2D(16, 16);
            var bordered = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
            UI.SpriteResolver = key => key == "ui:tab_sel" ? bordered : null;
            var t = OpenTab("<Tab id='t' sprite='' selectedSprite='ui:tab_sel'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();

            Assert.AreEqual(UnityImage.Type.Simple, bg.type, "empty normal sprite -> Simple while not selected");
            t.IsOn = true;
            Assert.AreSame(bordered, bg.overrideSprite);
            Assert.AreEqual(UnityImage.Type.Sliced, bg.type, "selected bordered sprite renders 9-sliced");
            t.IsOn = false;
            Assert.AreEqual(UnityImage.Type.Simple, bg.type, "reverts to Simple for the empty normal sprite");
        }

        // Default-skin tab (no sprite=, no selectedSprite=): the built-in wood frame renders Tiled
        // (moss/grain edges tile, ApplyDefaultSlicedSprite). Selecting/deselecting must NOT flip it
        // to Sliced — ApplySelectedSprite re-derives the type and has to fall back to the base type,
        // not blanket "border -> Sliced".
        [Test]
        public void Tab_DefaultSkin_StaysTiled_AcrossSelection()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' selectedColor='#CDEBA8'/>");
            var bg = t.GameObject.GetComponent<UnityImage>();

            Assert.AreEqual("pugui_9slice_round", bg.sprite.name);
            Assert.AreEqual(UnityImage.Type.Tiled, bg.type, "default wood-frame skin tiles its textured edges");
            t.IsOn = true;
            Assert.AreEqual(UnityImage.Type.Tiled, bg.type, "selection must not flip the default skin to Sliced");
            t.IsOn = false;
            Assert.AreEqual(UnityImage.Type.Tiled, bg.type, "deselection keeps the default skin Tiled");
        }
    }
}
