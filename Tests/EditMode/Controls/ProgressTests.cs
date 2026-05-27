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
    }
}
