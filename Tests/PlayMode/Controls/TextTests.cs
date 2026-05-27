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

        [UnityTest]
        public IEnumerator Autosize_true_enables_TMP_autosize_wd_only()
        {
            var t = new Text();
            var go = new GameObject("text", typeof(RectTransform));
            t.AttachTo(go);
            t.Size = 24;
            t.Autosize = true;
            var tmp = go.GetComponent<TMP_Text>();
            Assert.IsTrue(tmp.enableAutoSizing, "enableAutoSizing should be on");
            Assert.AreEqual(24f, tmp.fontSize, "fontSize unchanged");
            Assert.AreEqual(24f, tmp.fontSizeMin, "fontSizeMin locked to current size");
            Assert.AreEqual(24f, tmp.fontSizeMax, "fontSizeMax locked to current size");
            Assert.Greater(tmp.characterWidthAdjustment, 0f, "WD% squish enabled");
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Autosize_false_disables_TMP_autosize()
        {
            var t = new Text();
            var go = new GameObject("text", typeof(RectTransform));
            t.AttachTo(go);
            t.Size = 24;
            t.Autosize = true;
            t.Autosize = false;
            var tmp = go.GetComponent<TMP_Text>();
            Assert.IsFalse(tmp.enableAutoSizing, "enableAutoSizing should be off");
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Autosize_then_resize_relocks_min_max()
        {
            var t = new Text();
            var go = new GameObject("text", typeof(RectTransform));
            t.AttachTo(go);
            t.Autosize = true;
            t.Size = 48;
            var tmp = go.GetComponent<TMP_Text>();
            Assert.IsTrue(tmp.enableAutoSizing);
            Assert.AreEqual(48f, tmp.fontSize);
            Assert.AreEqual(48f, tmp.fontSizeMin, "Min re-locks after Size change");
            Assert.AreEqual(48f, tmp.fontSizeMax, "Max re-locks after Size change");
            Object.Destroy(go);
            yield return null;
        }
    }
}
