using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Controls
{
    public class TextTests
    {
        [UnityTest]
        public IEnumerator Adds_TMP_Text_component_on_attach()
        {
            var t = new Text();
            var go = new GameObject("text", typeof(RectTransform));
            t.AttachTo(go);
            Assert.IsNotNull(go.GetComponent<TMP_Text>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Text_property_writes_to_TMP()
        {
            var t = new Text();
            var go = new GameObject("text", typeof(RectTransform));
            t.AttachTo(go);
            t.TextValue = "你好";
            Assert.AreEqual("你好", go.GetComponent<TMP_Text>().text);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Size_property_writes_to_fontSize()
        {
            var t = new Text();
            var go = new GameObject("text", typeof(RectTransform));
            t.AttachTo(go);
            t.Size = 32;
            Assert.AreEqual(32f, go.GetComponent<TMP_Text>().fontSize);
            Object.Destroy(go);
            yield return null;
        }
    }
}
