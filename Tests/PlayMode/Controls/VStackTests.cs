using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls
{
    public class VStackTests
    {
        [UnityTest]
        public IEnumerator Adds_VerticalLayoutGroup()
        {
            var v = new VStack();
            var go = new GameObject("vstack", typeof(RectTransform));
            v.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<VerticalLayoutGroup>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spacing_writes_to_layout_group()
        {
            var v = new VStack();
            var go = new GameObject("vstack", typeof(RectTransform));
            v.AttachTo(go);
            v.Spacing = 12f;
            Assert.AreEqual(12f, go.GetComponent<VerticalLayoutGroup>().spacing);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Padding_uniform_writes_to_layout_group()
        {
            var v = new VStack();
            var go = new GameObject("vstack", typeof(RectTransform));
            v.AttachTo(go);
            v.Padding = "16";
            var p = go.GetComponent<VerticalLayoutGroup>().padding;
            Assert.AreEqual(16, p.top);
            Assert.AreEqual(16, p.right);
            Assert.AreEqual(16, p.bottom);
            Assert.AreEqual(16, p.left);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Padding_4_values_TRBL()
        {
            var v = new VStack();
            var go = new GameObject("vstack", typeof(RectTransform));
            v.AttachTo(go);
            v.Padding = "1,2,3,4";
            var p = go.GetComponent<VerticalLayoutGroup>().padding;
            Assert.AreEqual(1, p.top);
            Assert.AreEqual(2, p.right);
            Assert.AreEqual(3, p.bottom);
            Assert.AreEqual(4, p.left);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Locks_child_sizing_flags()
        {
            var v = new VStack();
            var go = new GameObject("vstack", typeof(RectTransform));
            v.AttachTo(go);
            var lg = go.GetComponent<VerticalLayoutGroup>();
            Assert.IsTrue(lg.childControlWidth,
                "VStack must let LayoutElement.preferredWidth drive child width");
            Assert.IsTrue(lg.childControlHeight,
                "VStack must let LayoutElement.preferredHeight drive child height");
            Assert.IsFalse(lg.childForceExpandWidth,
                "VStack must NOT force-expand children horizontally — that defeats fixed-size children");
            Assert.IsFalse(lg.childForceExpandHeight,
                "VStack must NOT force-expand children vertically — that defeats fixed-size children");
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Fixed_size_child_is_not_stretched_after_layout_rebuild()
        {
            // 镜像 spec 里那个 bug 场景：VStack height=84, Btn size=64x64, Text height=14。
            // 修复前 Btn 会被 VLG force-expand 拉到 ~41。
            var canvasGo = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var vs = new VStack();
            var stackGo = new GameObject("stack", typeof(RectTransform));
            stackGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            vs.AttachTo(stackGo);
            vs.Spacing = 2f;
            var stackRt = (RectTransform)stackGo.transform;
            stackRt.sizeDelta = new Vector2(70f, 84f);

            var btnGo = new GameObject("btn",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image),
                typeof(LayoutElement));
            btnGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var btnLe = btnGo.GetComponent<LayoutElement>();
            btnLe.preferredWidth = 64f;
            btnLe.preferredHeight = 64f;
            btnLe.flexibleWidth = 0f;
            btnLe.flexibleHeight = 0f;

            var textGo = new GameObject("text",
                typeof(RectTransform),
                typeof(LayoutElement));
            textGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var textLe = textGo.GetComponent<LayoutElement>();
            textLe.preferredHeight = 14f;
            textLe.flexibleHeight = 0f;

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stackRt);
            yield return null;

            var btnRt = (RectTransform)btnGo.transform;
            var textRt = (RectTransform)textGo.transform;
            Assert.AreEqual(64f, btnRt.rect.height, 0.5f,
                "Btn must not be stretched by VStack — LayoutElement.preferredHeight + flexibleHeight=0 is binding");
            Assert.AreEqual(14f, textRt.rect.height, 0.5f);
            Object.Destroy(canvasGo);
        }
    }
}
