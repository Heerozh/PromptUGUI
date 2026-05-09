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
    }
}
