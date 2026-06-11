using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ScrollListTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private ScrollList OpenList(string attrs = "")
        {
            string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot' " + attrs + @"/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<ScrollList>("sl");
        }

        private static UnityEngine.RectTransform ViewportOf(ScrollList sl) =>
            (UnityEngine.RectTransform)sl.GameObject.transform.Find("Viewport");

        [Test]
        public void Mask_empty_swaps_stencil_for_RectMask2D()
        {
            var sl = OpenList(@"mask=''");
            var vp = ViewportOf(sl).gameObject;
            var rectMask = vp.GetComponent<UnityEngine.UI.RectMask2D>();
            Assert.IsNotNull(rectMask);
            Assert.IsTrue(rectMask.enabled);
            var mask = vp.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsTrue(mask == null || !mask.enabled, "stencil Mask must be off");
            var img = vp.GetComponent<UnityEngine.UI.Image>();
            Assert.IsTrue(img == null || !img.enabled, "viewport Image must be off (RectMask2D has no showMaskGraphic)");
        }

        [Test]
        public void Mask_custom_sprite_replaces_default_mask_sprite()
        {
            var sl = OpenList(@"mask='PromptUGUI/Defaults/pugui#pugui_9slice_round'");
            var vp = ViewportOf(sl).gameObject;
            var mask = vp.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsNotNull(mask);
            Assert.IsTrue(mask.enabled);
            Assert.IsFalse(mask.showMaskGraphic);
            var img = vp.GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual("pugui_9slice_round", img.sprite.name);
            Assert.AreEqual(1f, img.color.a, "alpha=1 critical (4af322b)");
            Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type, "AutoSlice: border 非零 → Sliced");
            Assert.IsNull(vp.GetComponent<UnityEngine.UI.RectMask2D>());
        }

        [Test]
        public void Mask_toggles_between_states_without_leftover_components()
        {
            var sl = OpenList();
            var vp = ViewportOf(sl).gameObject;
            sl.Mask = "";                                              // 圆角 → 直角
            sl.Mask = "PromptUGUI/Defaults/pugui#pugui_9slice_round";  // 直角 → 自定义
            sl.Mask = "";                                              // 自定义 → 直角

            Assert.AreEqual(1, vp.GetComponents<UnityEngine.UI.RectMask2D>().Length, "no duplicates");
            Assert.AreEqual(1, vp.GetComponents<UnityEngine.UI.Mask>().Length, "lazy-add keeps single instance");
            Assert.IsTrue(vp.GetComponent<UnityEngine.UI.RectMask2D>().enabled);
            Assert.IsFalse(vp.GetComponent<UnityEngine.UI.Mask>().enabled);
            Assert.IsFalse(vp.GetComponent<UnityEngine.UI.Image>().enabled);
        }

        [Test]
        public void BindItems_template_creates_one_slot_per_data_item()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>x</Text></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }),
                (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);

            Assert.AreEqual(3, list.SlotCount);
        }

        [Test]
        public void BindItems_rebuild_replaces_slots()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'/></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            var src = new ReactiveProperty<IReadOnlyList<string>>(new[] { "a", "b" });
            list.BindItems(src, (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);
            Assert.AreEqual(2, list.SlotCount);

            src.Value = new[] { "x" };
            Assert.AreEqual(1, list.SlotCount);
        }

        [Test]
        public void Unknown_itemTemplate_throws_at_screen_open()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><ScrollList id='list' itemTemplate='Nope'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<PromptUGUI.Parser.ParseException>(() => UI.Open("S"));
        }

        [Test]
        public void Viewport_HasStencilMaskAndMaskSpriteWithAlphaOne()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            var mask = sl.GameObject.GetComponentInChildren<UnityEngine.UI.Mask>(includeInactive: true);
            Assert.IsNotNull(mask, "Viewport should use stencil Mask");
            Assert.IsFalse(mask.showMaskGraphic);

            var img = mask.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(img);
            Assert.AreEqual(1f, img.color.a, "alpha=1 critical to avoid 4af322b alpha-discard regression");

            // Mask graphic 用专门的 pugui_9slice_mask (不是 bg 的 pugui_9slice_round)
            Assert.IsNotNull(img.sprite, "mask sprite must be loaded; otherwise stencil 写不出形状");
            Assert.AreEqual("pugui_9slice_mask", img.sprite.name);
            Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type);
        }

        [Test]
        public void Viewport_HasNoRectMask2D()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            Assert.IsNull(sl.GameObject.GetComponentInChildren<UnityEngine.UI.RectMask2D>(includeInactive: true));
        }

        [Test]
        public void Visual_BgColorIsTranslucentWhite()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            var img = sl.GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(img);
            Assert.AreEqual(1f, img.color.r);
            Assert.AreEqual(1f, img.color.g);
            Assert.AreEqual(1f, img.color.b);
            Assert.That(img.color.a, Is.EqualTo(0.392f).Within(0.005f));
        }

        [Test]
        public void ScrollRect_HasDefaultMovementParams()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            var sr = sl.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.AreEqual(UnityEngine.UI.ScrollRect.MovementType.Elastic, sr.movementType);
            Assert.That(sr.elasticity, Is.EqualTo(0.1f).Within(0.001f));
            Assert.IsTrue(sr.inertia);
            Assert.That(sr.decelerationRate, Is.EqualTo(0.135f).Within(0.001f));
            Assert.AreEqual(1f, sr.scrollSensitivity);
        }

        [Test]
        public void Has_VerticalScrollbarByDefaultDirection()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            var sb = sl.GameObject.transform.Find("Scrollbar Vertical") as UnityEngine.RectTransform;
            Assert.IsNotNull(sb, "default direction is vertical → Scrollbar Vertical exists");
            Assert.AreEqual(new UnityEngine.Vector2(1, 0), sb.anchorMin);
            Assert.AreEqual(new UnityEngine.Vector2(1, 1), sb.anchorMax);
            Assert.AreEqual(new UnityEngine.Vector2(20, 0), sb.sizeDelta);

            var scrollbar = sb.GetComponent<UnityEngine.UI.Scrollbar>();
            Assert.AreEqual(UnityEngine.UI.Scrollbar.Direction.BottomToTop, scrollbar.direction);

            var sr = sl.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.AreSame(scrollbar, sr.verticalScrollbar);
            Assert.AreEqual(UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport,
                sr.verticalScrollbarVisibility);
        }

        [Test]
        public void Has_HorizontalScrollbarWhenDirectionHorizontal()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' direction='horizontal' itemTemplate='Slot'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            var sb = sl.GameObject.transform.Find("Scrollbar Horizontal") as UnityEngine.RectTransform;
            Assert.IsNotNull(sb);
            Assert.AreEqual(new UnityEngine.Vector2(0, 0), sb.anchorMin);
            Assert.AreEqual(new UnityEngine.Vector2(1, 0), sb.anchorMax);
            Assert.AreEqual(new UnityEngine.Vector2(0, 20), sb.sizeDelta);

            var scrollbar = sb.GetComponent<UnityEngine.UI.Scrollbar>();
            Assert.AreEqual(UnityEngine.UI.Scrollbar.Direction.LeftToRight, scrollbar.direction);

            var sr = sl.GameObject.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.AreSame(scrollbar, sr.horizontalScrollbar);
        }

        [Test]
        public void Frame_creates_topmost_nonraycast_layer()
        {
            var sl = OpenList(@"frame='PromptUGUI/Defaults/pugui#pugui_9slice_round'");
            var root = sl.GameObject.transform;
            var frame = root.Find("Frame");
            Assert.IsNotNull(frame, "frame= should lazily create the Frame layer");
            Assert.AreEqual(root.childCount - 1, frame.GetSiblingIndex(), "frame must be the last sibling (above Viewport & Scrollbar)");
            var img = frame.GetComponent<UnityEngine.UI.Image>();
            Assert.IsFalse(img.raycastTarget);
            Assert.AreEqual("pugui_9slice_round", img.sprite.name);
            Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type);
            var rt = (UnityEngine.RectTransform)frame;
            Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
            Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
            Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMin);
            Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMax);
        }

        [Test]
        public void Frame_stays_topmost_after_horizontal_scrollbar_created()
        {
            // direction='horizontal' re-runs ApplyDirection → EnsureHorizontalScrollbar appends a
            // NEW last-sibling AFTER the frame already exists; only OnAfterApply's re-pin keeps the
            // border on top. (The default vertical path builds its scrollbar before the frame, so it
            // wouldn't catch a missing re-pin — this case does.)
            var sl = OpenList(@"frame='PromptUGUI/Defaults/pugui#pugui_9slice_round' direction='horizontal'");
            var root = sl.GameObject.transform;
            var frame = root.Find("Frame");
            Assert.IsNotNull(frame);
            Assert.IsNotNull(root.Find("Scrollbar Horizontal"), "horizontal scrollbar should exist in this direction");
            Assert.AreEqual(root.childCount - 1, frame.GetSiblingIndex(),
                "frame must remain the last sibling even though the horizontal scrollbar was created after it");
        }

        [Test]
        public void FrameColor_alone_activates_frame_layer()
        {
            var sl = OpenList(@"frameColor='#FF0000'");
            var frame = sl.GameObject.transform.Find("Frame");
            Assert.IsNotNull(frame);
            var img = frame.GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(1f, img.color.r);
            Assert.AreEqual(0f, img.color.g);
        }

        [Test]
        public void No_frame_attr_means_no_frame_node()
        {
            var sl = OpenList();
            Assert.IsNull(sl.GameObject.transform.Find("Frame"), "frame layer is lazy");
        }

        [Test]
        public void Mask_unset_auto_squares_when_sprite_empty()
        {
            // sprite="" clears the bg → mask must auto-follow to square (no orphan rounded clip)
            var sl = OpenList(@"sprite='' color='#00000000'");
            var vp = ViewportOf(sl).gameObject;
            Assert.IsTrue(vp.GetComponent<UnityEngine.UI.RectMask2D>() != null
                          && vp.GetComponent<UnityEngine.UI.RectMask2D>().enabled,
                "sprite='' with no explicit mask should auto-square the viewport clip");
            var mask = vp.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsTrue(mask == null || !mask.enabled, "stencil Mask must be off when auto-squared");
        }

        [Test]
        public void Mask_unset_stays_rounded_when_sprite_present()
        {
            // default list (bg has the default rounded sprite) keeps the rounded stencil — back-compat
            var sl = OpenList();
            var vp = ViewportOf(sl).gameObject;
            var mask = vp.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsNotNull(mask);
            Assert.IsTrue(mask.enabled, "default bg sprite present → rounded stencil mask");
            Assert.IsNull(vp.GetComponent<UnityEngine.UI.RectMask2D>());
        }

        [Test]
        public void Explicit_mask_wins_over_sprite_autotrack()
        {
            // explicit mask='...' must NOT be overridden by the sprite='' auto-track
            var sl = OpenList(@"sprite='' mask='PromptUGUI/Defaults/pugui#pugui_9slice_round'");
            var vp = ViewportOf(sl).gameObject;
            var mask = vp.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsNotNull(mask);
            Assert.IsTrue(mask.enabled, "explicit mask sprite wins despite sprite=''");
            Assert.AreEqual("pugui_9slice_round", vp.GetComponent<UnityEngine.UI.Image>().sprite.name);
            Assert.IsNull(vp.GetComponent<UnityEngine.UI.RectMask2D>());
        }

        [Test]
        public void Explicit_empty_mask_stays_square_even_with_sprite_present()
        {
            // mask='' is explicit → square, even though the default bg sprite is present (NOT auto-rounded)
            var sl = OpenList(@"mask=''");
            var vp = ViewportOf(sl).gameObject;
            Assert.IsTrue(vp.GetComponent<UnityEngine.UI.RectMask2D>().enabled);
            var mask = vp.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsTrue(mask == null || !mask.enabled);
        }
    }
}
