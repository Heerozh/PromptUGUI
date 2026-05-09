using System.Collections;
using NUnit.Framework;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Controls
{
    public class FrameTests
    {
        [UnityTest]
        public IEnumerator Frame_attaches_to_GameObject_and_exposes_RectTransform()
        {
            var frame = new Frame();
            var go = new GameObject("test", typeof(RectTransform));
            frame.AttachTo(go);

            Assert.IsNotNull(frame.RectTransform);
            Assert.AreEqual(go, frame.GameObject);

            Object.Destroy(go);
            yield return null;
        }
    }
}
