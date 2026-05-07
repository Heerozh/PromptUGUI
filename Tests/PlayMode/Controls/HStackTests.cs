using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls {
    public class HStackTests {
        [UnityTest]
        public IEnumerator Adds_HorizontalLayoutGroup() {
            var h = new HStack();
            var go = new GameObject("hstack", typeof(RectTransform));
            h.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<HorizontalLayoutGroup>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spacing_and_padding_apply() {
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
    }
}
