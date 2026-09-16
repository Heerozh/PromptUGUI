using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A <c>&lt;ScrollList&gt;</c> clips along its scroll axis only (spec 2026-09-16 §14.5): a vertical
    /// list (and a grid) leaves left / right open, a horizontal list top / bottom. Two mask modes, two
    /// mechanisms — <c>RectMask2D.padding</c> pushed out, or a stencil band stitched onto the 9-slice
    /// mask graphic between its corner pieces — and a border-less custom mask is left whole.
    /// </summary>
    public class ScrollListCrossAxisClipTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const float Reach = CrossAxisMaskImage.Reach;

        private static ScrollList Open(string attrs)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>"
                    + "<Frame id='box' anchor='top-left' width='400' height='600'>"
                    + $"<ScrollList id='sl' anchor='top-left' width='150' height='200' {attrs}><Frame height='30'/></ScrollList>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");
            Canvas.ForceUpdateCanvases();
            return list;
        }

        private static RectTransform ViewportOf(ScrollList list) => (RectTransform)list.GameObject.transform.Find("Viewport");

        private static List<UIVertex> Mesh(CrossAxisMaskImage img)
        {
            var vh = new VertexHelper();
            img.PopulateForTests(vh);
            var verts = new List<UIVertex>();
            vh.GetUIVertexStream(verts);   // 3 per triangle
            vh.Dispose();
            return verts;
        }

        private static (float xMin, float xMax, float yMin, float yMax) Bounds(List<UIVertex> verts)
        {
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var v in verts)
            {
                xMin = Mathf.Min(xMin, v.position.x); xMax = Mathf.Max(xMax, v.position.x);
                yMin = Mathf.Min(yMin, v.position.y); yMax = Mathf.Max(yMax, v.position.y);
            }
            return (xMin, xMax, yMin, yMax);
        }

        // ───── stencil mask (the default rounded one) ─────

        [Test]
        public void Vertical_list_mask_reaches_out_sideways_but_keeps_its_corners()
        {
            var list = Open("");
            var img = ViewportOf(list).GetComponent<CrossAxisMaskImage>();
            Assert.IsNotNull(img, "the viewport's mask graphic is the band-drawing subclass");
            Assert.IsTrue(img.enabled && ViewportOf(list).GetComponent<Mask>().enabled, "stencil mode");
            Assert.AreEqual(0, img.BandAxis, "vertical: the band runs along X");

            var verts = Mesh(img);
            Assert.AreEqual((9 + 1) * 6, verts.Count, "9 sliced pieces + one band, two triangles each");
            var (xMin, xMax, yMin, yMax) = Bounds(verts);
            Assert.AreEqual(-Reach, xMin, 0.5f, "open to the left");
            Assert.AreEqual(Reach, xMax, 0.5f, "open to the right");
            var r = img.GetPixelAdjustedRect();
            Assert.AreEqual(r.yMin, yMin, 0.01f, "nothing reaches out along the scroll axis");
            Assert.AreEqual(r.yMax, yMax, 0.01f);

            // The band stops short of the corner pieces: it does not touch the rect's top / bottom.
            var inset = img.sprite.border.y / img.PixelsPerUnitForTests;
            Assert.Greater(inset, 0f, "guard: the default mask is a 9-slice");
            var bandYs = new List<float>();
            foreach (var v in verts) if (Mathf.Abs(v.position.x) > Reach * 0.5f) bandYs.Add(v.position.y);
            Assert.AreEqual(r.yMin + inset, Mathf.Min(bandYs.ToArray()), 0.01f, "band starts above the bottom corners");
            Assert.AreEqual(r.yMax - inset, Mathf.Max(bandYs.ToArray()), 0.01f, "and ends below the top corners");
        }

        [Test]
        public void Horizontal_list_mask_reaches_out_up_and_down()
        {
            var list = Open("direction='horizontal'");
            var img = ViewportOf(list).GetComponent<CrossAxisMaskImage>();
            Assert.AreEqual(1, img.BandAxis);
            var (xMin, xMax, yMin, yMax) = Bounds(Mesh(img));
            var r = img.GetPixelAdjustedRect();
            Assert.AreEqual(-Reach, yMin, 0.5f);
            Assert.AreEqual(Reach, yMax, 0.5f);
            Assert.AreEqual(r.xMin, xMin, 0.01f);
            Assert.AreEqual(r.xMax, xMax, 0.01f);
        }

        [Test]
        public void Grid_is_a_vertical_list_for_clipping()
        {
            var list = Open("columns='2' cellSize='60x30'");
            Assert.AreEqual(0, ViewportOf(list).GetComponent<CrossAxisMaskImage>().BandAxis);
        }

        [Test]
        public void Switching_direction_turns_the_band()
        {
            var list = Open("");
            var img = ViewportOf(list).GetComponent<CrossAxisMaskImage>();
            Assert.AreEqual(0, img.BandAxis);
            list.Direction = "horizontal";
            Assert.AreEqual(1, img.BandAxis);
            list.Direction = "vertical";
            Assert.AreEqual(0, img.BandAxis);
        }

        [Test]
        public void A_border_less_custom_mask_keeps_its_whole_shape()
        {
            var list = Open("");
            var img = ViewportOf(list).GetComponent<CrossAxisMaskImage>();
            // Stand in for mask="hex#shape": a sprite with no 9-slice border says nothing about where
            // its straight edges are, so no band is added — the author's shape is the shape.
            var plain = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            img.sprite = plain;
            img.type = UnityEngine.UI.Image.Type.Simple;
            var verts = Mesh(img);
            Assert.AreEqual(6, verts.Count, "one quad, no band");
            var (xMin, xMax, _, _) = Bounds(verts);
            Assert.Greater(xMin, -Reach * 0.5f);
            Assert.Less(xMax, Reach * 0.5f);
            Object.DestroyImmediate(plain);
        }

        // ───── RectMask2D (sprite="" / mask="") ─────

        [Test]
        public void Square_vertical_list_pushes_the_rect_mask_out_sideways()
        {
            var list = Open("sprite=''");
            var rectMask = ViewportOf(list).GetComponent<RectMask2D>();
            Assert.IsNotNull(rectMask);
            Assert.IsTrue(rectMask.enabled, "RectMask2D mode");
            Assert.AreEqual(new Vector4(-Reach, 0f, -Reach, 0f), rectMask.padding,
                "negative left / right padding = the clip rect reaches out sideways; top / bottom stay");
        }

        [Test]
        public void Square_horizontal_list_pushes_the_rect_mask_out_up_and_down()
        {
            var list = Open("sprite='' direction='horizontal'");
            Assert.AreEqual(new Vector4(0f, -Reach, 0f, -Reach), ViewportOf(list).GetComponent<RectMask2D>().padding);
        }

        [Test]
        public void Explicit_empty_mask_gets_the_padding_too()
        {
            var list = Open("mask=''");
            Assert.AreEqual(new Vector4(-Reach, 0f, -Reach, 0f), ViewportOf(list).GetComponent<RectMask2D>().padding);
        }

        // ───── the edit-mode row sizing this used to hide behind ─────

        [Test]
        public void Rows_take_their_declared_size_in_edit_mode()
        {
            var list = Open("spacing='0' padding='0'");
            var content = (RectTransform)list.GameObject.transform.Find("Viewport/Content");
            var group = content.GetComponent<VerticalLayoutGroup>();
            Assert.IsTrue(group.childControlWidth && group.childControlHeight,
                "spelled out — AddComponent in edit mode leaves them off, play mode has them on");
            var row = (RectTransform)content.GetChild(0);
            Assert.AreEqual(30f, row.rect.height, 0.01f);
        }
    }
}
