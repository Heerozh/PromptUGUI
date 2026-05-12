using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls
{
    public class HStackTests
    {
        [UnityTest]
        public IEnumerator Adds_HorizontalLayoutGroup()
        {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<HorizontalLayoutGroup>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spacing_and_padding_apply()
        {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            h.Spacing = 8f;
            h.Padding = "4,8";
            var lg = go.GetComponent<HorizontalLayoutGroup>();
            Assert.AreEqual(8f, lg.spacing);
            Assert.AreEqual(4, lg.padding.top);
            Assert.AreEqual(8, lg.padding.right);
            Assert.AreEqual(4, lg.padding.bottom);
            Assert.AreEqual(8, lg.padding.left);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Locks_child_sizing_flags()
        {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            var lg = go.GetComponent<HorizontalLayoutGroup>();
            Assert.IsTrue(lg.childControlWidth);
            Assert.IsTrue(lg.childControlHeight);
            Assert.IsFalse(lg.childForceExpandWidth);
            Assert.IsFalse(lg.childForceExpandHeight);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Default_child_alignment_is_middle_left()
        {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            Assert.AreEqual(TextAnchor.MiddleLeft,
                go.GetComponent<HorizontalLayoutGroup>().childAlignment,
                "HStack must default to MiddleLeft so children shorter than the cross-axis are vertically centered");
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChildAlign_attribute_overrides_default()
        {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            h.ChildAlign = "upper-right";
            Assert.AreEqual(TextAnchor.UpperRight,
                go.GetComponent<HorizontalLayoutGroup>().childAlignment);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Fixed_size_child_is_not_stretched_after_layout_rebuild()
        {
            var canvasGo = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var hs = new HStack();
            var stackGo = new GameObject("stack", typeof(RectTransform));
            stackGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            hs.AttachTo(stackGo);
            hs.Spacing = 4f;
            var stackRt = (RectTransform)stackGo.transform;
            stackRt.sizeDelta = new Vector2(200f, 60f);

            var aGo = new GameObject("a", typeof(RectTransform), typeof(LayoutElement));
            aGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var aLe = aGo.GetComponent<LayoutElement>();
            aLe.preferredWidth = 64f;
            aLe.flexibleWidth = 0f;

            var bGo = new GameObject("b", typeof(RectTransform), typeof(LayoutElement));
            bGo.transform.SetParent(stackGo.transform, worldPositionStays: false);
            var bLe = bGo.GetComponent<LayoutElement>();
            bLe.preferredWidth = 80f;
            bLe.flexibleWidth = 0f;

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(stackRt);
            yield return null;

            Assert.AreEqual(64f, ((RectTransform)aGo.transform).rect.width, 0.5f);
            Assert.AreEqual(80f, ((RectTransform)bGo.transform).rect.width, 0.5f);
            Object.Destroy(canvasGo);
        }
    }
}
