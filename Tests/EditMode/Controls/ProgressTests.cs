using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnityMask = UnityEngine.UI.Mask;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ProgressTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Progress Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Progress>("p");
        }

        // Same runtime-owned-state class as Tab/Toggle isOn: a declared value is the INITIAL value
        // only. Progress is code-driven; a window resize runs Screen.ReSolve, which must NOT snap a
        // runtime-set fill back to the declared value.
        [Test]
        public void Value_RuntimeChange_Survives_ReSolve()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Progress id='p' value='0.2'/></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var p = screen.Get<Progress>("p");
            p.Value = 0.7f;                  // runtime code sets the fill
            Assert.AreEqual(0.7f, p.Value);

            screen.ReSolve();                // window resize / scale recompute

            Assert.AreEqual(0.7f, p.Value, "runtime-set progress value survives ReSolve (not reset to declared 0.2)");
        }

        [Test]
        public void Empty_Progress_Has_MaskWrapper_Fill_Frame_Children()
        {
            var p = Open("<Progress id='p'/>");
            var maskWrapper = p.GameObject.transform.Find("MaskWrapper") as RectTransform;
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg") as RectTransform;
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            var frame = p.GameObject.transform.Find("Frame") as RectTransform;
            Assert.IsNotNull(maskWrapper, "MaskWrapper RT");
            Assert.IsNotNull(bg, "Bg RT (inside MaskWrapper)");
            Assert.IsNotNull(fill, "Fill RT (inside MaskWrapper)");
            Assert.IsNotNull(frame, "Frame RT");
            Assert.IsFalse(bg.gameObject.activeSelf, "Bg starts disabled");
            Assert.IsFalse(frame.gameObject.activeSelf, "Frame starts disabled");
            Assert.IsNull(maskWrapper.gameObject.GetComponent<UnityMask>(),
                "no mask= → no UI.Mask on MaskWrapper");
            Assert.IsNull(maskWrapper.gameObject.GetComponent<UnityImage>(),
                "no mask= → no UnityImage on MaskWrapper");
        }

        [Test]
        public void Progress_Root_Has_No_Image()
        {
            var p = Open("<Progress id='p'/>");
            Assert.IsNull(p.GameObject.GetComponent<UnityImage>(),
                "Progress root is a pure RectTransform host, no Graphic");
        }

        [Test]
        public void Value_Stores_InRangeAsIs()
        {
            var p = Open("<Progress id='p' value='0.5'/>");
            Assert.AreEqual(0.5f, p.Value);
        }

        [Test]
        public void Value_Below_Zero_Clamps_To_Zero()
        {
            var p = Open("<Progress id='p'/>");
            p.Value = -0.3f;
            Assert.AreEqual(0f, p.Value);
        }

        [Test]
        public void Value_Above_One_Clamps_To_One()
        {
            var p = Open("<Progress id='p' value='1.7'/>");
            Assert.AreEqual(1f, p.Value);
        }

        [Test]
        public void Scale_Horizontal_Value_Half_Anchors_Right_At_Half()
        {
            var p = Open("<Progress id='p' value='0.5'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(Vector2.zero, fill.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 1f), fill.anchorMax);
            Assert.AreEqual(Vector2.zero, fill.offsetMin);
            Assert.AreEqual(Vector2.zero, fill.offsetMax);
        }

        [Test]
        public void Scale_Horizontal_Value_Zero_Fill_Is_Zero_Width()
        {
            var p = Open("<Progress id='p' value='0'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0f, 1f), fill.anchorMax);
        }

        [Test]
        public void Scale_Horizontal_Value_One_Fill_Full_Width()
        {
            var p = Open("<Progress id='p' value='1'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax);
            Assert.AreEqual(Vector2.zero, fill.offsetMin, "offsetMin must be zeroed by ReconcileFill");
        }

        [Test]
        public void Value_Setter_At_Runtime_Reconciles_Fill()
        {
            var p = Open("<Progress id='p' value='0.2'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0.2f, 1f), fill.anchorMax, "initial XML state");
            p.Value = 0.7f;
            Assert.AreEqual(new Vector2(0.7f, 1f), fill.anchorMax, "runtime setter must repaint Fill");
        }

        [Test]
        public void Scale_ReverseHorizontal_Anchors_From_Right()
        {
            var p = Open("<Progress id='p' value='0.25' direction='reverse-horizontal'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0.75f, 0f), fill.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax);
        }

        [Test]
        public void Scale_Vertical_Anchors_From_Bottom()
        {
            var p = Open("<Progress id='p' value='0.4' direction='vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(Vector2.zero, fill.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0.4f), fill.anchorMax);
        }

        [Test]
        public void Scale_ReverseVertical_Anchors_From_Top()
        {
            var p = Open("<Progress id='p' value='0.4' direction='reverse-vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0f, 0.6f), fill.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax);
        }

        [Test]
        public void Direction_Setter_At_Runtime_Reconciles_Fill()
        {
            var p = Open("<Progress id='p' value='0.4'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(0.4f, 1f), fill.anchorMax, "initial horizontal");
            p.Direction = "vertical";
            Assert.AreEqual(new Vector2(1f, 0.4f), fill.anchorMax, "runtime direction switch must repaint Fill");
        }

        [Test]
        public void Fill_Horizontal_Sets_Type_Filled_And_FillAmount()
        {
            var p = Open("<Progress id='p' value='0.7' mode='fill'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Filled, fill.type);
            Assert.AreEqual(UnityImage.FillMethod.Horizontal, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginHorizontal.Left, fill.fillOrigin);
            Assert.AreEqual(0.7f, fill.fillAmount);
            var rt = fill.rectTransform;
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
        }

        [Test]
        public void Fill_ReverseHorizontal_Origin_Right()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' direction='reverse-horizontal'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.FillMethod.Horizontal, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginHorizontal.Right, fill.fillOrigin);
        }

        [Test]
        public void Fill_Vertical_Origin_Bottom()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' direction='vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.FillMethod.Vertical, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginVertical.Bottom, fill.fillOrigin);
        }

        [Test]
        public void Fill_ReverseVertical_Origin_Top()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' direction='reverse-vertical'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.FillMethod.Vertical, fill.fillMethod);
            Assert.AreEqual((int)UnityImage.OriginVertical.Top, fill.fillOrigin);
        }

        [Test]
        public void Switch_From_Fill_Back_To_Scale_Resets_Type_And_FillAmount()
        {
            var p = Open("<Progress id='p' value='0.6' mode='fill'/>");
            // sanity
            var fillImg = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Filled, fillImg.type, "starts as Filled");

            p.Mode = "scale";
            Assert.AreNotEqual(UnityImage.Type.Filled, fillImg.type,
                "switching back to scale must reset away from Filled");
            Assert.AreEqual(1f, fillImg.fillAmount, "scale mode resets fillAmount to 1");
            var rt = fillImg.rectTransform;
            Assert.AreEqual(new Vector2(0.6f, 1f), rt.anchorMax, "scale anchorMax reflects value");
        }

        [Test]
        public void Fill_Sprite_Resolves_Via_UI_ResolveSprite()
        {
            var p = Open("<Progress id='p' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.IsNotNull(fill.sprite, "sprite resolved from atlas key");
            Assert.AreEqual("pugui_9slice_round", fill.sprite.name);
        }

        [Test]
        public void Fill_9Slice_Sprite_Auto_Sliced_In_Scale_Mode()
        {
            var p = Open("<Progress id='p' value='0.5' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Sliced, fill.type);
        }

        [Test]
        public void Fill_9Slice_Sprite_Becomes_Filled_When_Mode_Is_Fill()
        {
            var p = Open("<Progress id='p' value='0.5' mode='fill' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Filled, fill.type,
                "mode=fill must force Filled even for 9-slice sprites");
        }

        [Test]
        public void FillColor_Parses_Hex()
        {
            var p = Open("<Progress id='p' fillColor='#ff0000'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(Color.red, fill.color);
        }

        [Test]
        public void Bg_Sprite_Activates_Bg_Layer()
        {
            var p = Open("<Progress id='p' bg='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsTrue(bg.gameObject.activeSelf, "Bg activated by bg=");
            var img = bg.GetComponent<UnityImage>();
            Assert.IsNotNull(img.sprite);
            Assert.AreEqual(UnityImage.Type.Sliced, img.type, "9-slice sprite auto-Sliced");
        }

        [Test]
        public void BgColor_Alone_Activates_Bg_Layer_With_Color()
        {
            var p = Open("<Progress id='p' bgColor='#222222'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsTrue(bg.gameObject.activeSelf, "Bg activated by bgColor= alone");
            var img = bg.GetComponent<UnityImage>();
            ColorUtility.TryParseHtmlString("#222222", out var expected);
            Assert.AreEqual(expected, img.color);
        }

        [Test]
        public void No_Bg_No_BgColor_Bg_Layer_Stays_Inactive()
        {
            var p = Open("<Progress id='p' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsFalse(bg.gameObject.activeSelf);
        }

        [Test]
        public void Frame_Sprite_Activates_Frame_Layer_With_Raycast_Off()
        {
            var p = Open("<Progress id='p' frame='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var frame = p.GameObject.transform.Find("Frame");
            Assert.IsTrue(frame.gameObject.activeSelf);
            var img = frame.GetComponent<UnityImage>();
            Assert.IsNotNull(img.sprite);
            Assert.AreEqual(UnityImage.Type.Sliced, img.type, "9-slice sprite auto-Sliced");
            Assert.IsFalse(img.raycastTarget, "Frame must not eat input (PB-D16)");
        }

        [Test]
        public void No_Frame_Frame_Layer_Stays_Inactive()
        {
            var p = Open("<Progress id='p' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var frame = p.GameObject.transform.Find("Frame");
            Assert.IsFalse(frame.gameObject.activeSelf);
        }

        [Test]
        public void FrameColor_With_Frame_Sprite_Tints_Frame()
        {
            var p = Open("<Progress id='p' frame='PromptUGUI/Defaults/pugui#pugui_9slice_round' frameColor='#ff0000'/>");
            var frame = p.GameObject.transform.Find("Frame").GetComponent<UnityImage>();
            Assert.AreEqual(Color.red, frame.color, "frameColor must tint the frame Image");
        }

        [Test]
        public void FrameColor_Alone_Activates_Frame_Layer_With_Color()
        {
            var p = Open("<Progress id='p' frameColor='#222222'/>");
            var frame = p.GameObject.transform.Find("Frame");
            Assert.IsTrue(frame.gameObject.activeSelf, "Frame activated by frameColor= alone");
            var img = frame.GetComponent<UnityImage>();
            ColorUtility.TryParseHtmlString("#222222", out var expected);
            Assert.AreEqual(expected, img.color);
        }

        [Test]
        public void FrameColor_Setter_At_Runtime_Updates_Tint()
        {
            var p = Open("<Progress id='p' frame='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var frame = p.GameObject.transform.Find("Frame").GetComponent<UnityImage>();
            p.FrameColor = "#00ff00";
            Assert.AreEqual(Color.green, frame.color, "runtime FrameColor= must repaint frame");
        }

        [Test]
        public void Mask_Alone_Adds_Image_Plus_Mask_With_ShowMaskGraphic_True()
        {
            var p = Open("<Progress id='p' mask='PromptUGUI/Defaults/pugui#pugui_9slice_mask'/>");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;
            var img = wrapper.GetComponent<UnityImage>();
            Assert.IsNotNull(img, "mask= adds UnityImage to wrapper");
            Assert.IsFalse(img.raycastTarget, "mask graphic must not eat input (PB-D16)");
            Assert.AreEqual(UnityImage.Type.Sliced, img.type, "9-slice mask sprite auto-Sliced (PB-D15)");
            var m = wrapper.GetComponent<UnityMask>();
            Assert.IsNotNull(m, "mask= adds UI.Mask");
            Assert.IsTrue(m.showMaskGraphic, "no bg → mask sprite visible (PB-D9)");
        }

        [Test]
        public void Mask_With_Bg_Sprite_Hides_Mask_Graphic()
        {
            var p = Open("<Progress id='p' mask='PromptUGUI/Defaults/pugui#pugui_9slice_mask' bg='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;
            var m = wrapper.GetComponent<UnityMask>();
            Assert.IsFalse(m.showMaskGraphic, "bg present → mask is invisible stencil only (PB-D10)");
        }

        [Test]
        public void Mask_With_BgColor_Only_Hides_Mask_Graphic()
        {
            var p = Open("<Progress id='p' mask='PromptUGUI/Defaults/pugui#pugui_9slice_mask' bgColor='#222222'/>");
            var m = p.GameObject.transform.Find("MaskWrapper").GetComponent<UnityMask>();
            Assert.IsFalse(m.showMaskGraphic, "bgColor alone also counts as bg present");
        }

        [Test]
        public void No_Mask_No_Image_No_Mask_Component_On_Wrapper()
        {
            var p = Open("<Progress id='p' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;
            Assert.IsNull(wrapper.GetComponent<UnityImage>());
            Assert.IsNull(wrapper.GetComponent<UnityMask>());
        }

        [Test]
        public void Mask_Then_Bg_At_Runtime_Updates_ShowMaskGraphic()
        {
            // Demonstrates the runtime ordering: mask set first, bg added later.
            var p = Open("<Progress id='p' mask='PromptUGUI/Defaults/pugui#pugui_9slice_mask'/>");
            var m = p.GameObject.transform.Find("MaskWrapper").GetComponent<UnityMask>();
            Assert.IsTrue(m.showMaskGraphic, "no bg yet → mask visible");
            p.Bg = "PromptUGUI/Defaults/pugui#pugui_9slice_round";
            Assert.IsFalse(m.showMaskGraphic, "runtime bg= activation must hide mask graphic");
        }

        [Test]
        public void GetNativeSize_Default_Is_160x16()
        {
            var p = Open("<Progress id='p'/>");
            var n = p.GetNativeSize();
            Assert.IsTrue(n.HasValue);
            Assert.AreEqual(new Vector2(160f, 16f), n.Value);
        }

        [Test]
        public void GetNativeSize_Falls_Back_To_Bg_When_No_Frame()
        {
            var p = Open("<Progress id='p' bg='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var n = p.GetNativeSize();
            Assert.IsTrue(n.HasValue);
            var img = p.GameObject.transform.Find("MaskWrapper/Bg").GetComponent<UnityImage>();
            var expected = new Vector2(img.sprite.rect.width / img.pixelsPerUnit,
                                       img.sprite.rect.height / img.pixelsPerUnit);
            Assert.AreEqual(expected, n.Value);
        }

        [Test]
        public void GetNativeSize_Prefers_Frame_Over_Bg()
        {
            var p = Open("<Progress id='p' bg='PromptUGUI/Defaults/pugui#pugui_9slice_round' frame='PromptUGUI/Defaults/pugui#pugui_9slice_mask'/>");
            var n = p.GetNativeSize();
            var img = p.GameObject.transform.Find("Frame").GetComponent<UnityImage>();
            var expected = new Vector2(img.sprite.rect.width / img.pixelsPerUnit,
                                       img.sprite.rect.height / img.pixelsPerUnit);
            Assert.AreEqual(expected, n.Value);
        }

        [Test]
        public void Value_Variant_Override_Reapplies_On_Activation()
        {
            var p = Open("<Progress id='p' value='1.0' value.low='0.2' fill='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax, "base value");
            try
            {
                UI.Variants.Set("low", true);
                Assert.AreEqual(new Vector2(0.2f, 1f), fill.anchorMax, "variant override should re-reconcile Fill");
            }
            finally { UI.Variants.Set("low", false); }
        }

        // ── Color token tests ─────────────────────────────────────────────────────

        private static void SeedLight(string primaryHex)
        {
            var d = new Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString(primaryHex, out var c);
            d["primary"] = c;
            ThemeStore.Instance.Register("light", null, d, "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("light");
        }

        [Test]
        public void FillColor_Token_Resolves()
        {
            SeedLight("#ff8800");
            var p = Open("<Progress id='p' fillColor='primary' value='0.5'/>");
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)fill.color);
        }

        [Test]
        public void BgColor_Token_Resolves_And_Activates_Bg()
        {
            SeedLight("#224466");
            var p = Open("<Progress id='p' bgColor='primary' value='0.5'/>");
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg");
            Assert.IsTrue(bg.gameObject.activeSelf, "bgColor token must activate Bg layer");
            var img = bg.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0x22, 0x44, 0x66, 0xff), (Color32)img.color);
        }

        [Test]
        public void FrameColor_Token_Resolves_And_Activates_Frame()
        {
            SeedLight("#aabbcc");
            var p = Open("<Progress id='p' frameColor='primary' value='0.5'/>");
            var frame = p.GameObject.transform.Find("Frame");
            Assert.IsTrue(frame.gameObject.activeSelf, "frameColor token must activate Frame layer");
            var img = frame.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xaa, 0xbb, 0xcc, 0xff), (Color32)img.color);
        }
    }
}
