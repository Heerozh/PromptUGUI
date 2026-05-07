using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.Controls {
    public class ImageTests {
        [UnityTest]
        public IEnumerator Adds_UI_Image_component_on_attach() {
            var img = new Image();
            var go = new GameObject("img", typeof(RectTransform));
            img.AttachTo(go);

            Assert.IsNotNull(go.GetComponent<UnityImage>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Color_property_parses_hex_and_applies() {
            var img = new Image();
            var go = new GameObject("img", typeof(RectTransform));
            img.AttachTo(go);

            img.Color = "#FF8800";

            var ui = go.GetComponent<UnityImage>();
            Assert.That(ui.color.r, Is.EqualTo(1f).Within(0.01f));
            Assert.That(ui.color.g, Is.EqualTo(0.533f).Within(0.01f));
            Assert.That(ui.color.b, Is.EqualTo(0f).Within(0.01f));

            Object.Destroy(go);
            yield return null;
        }
    }
}
