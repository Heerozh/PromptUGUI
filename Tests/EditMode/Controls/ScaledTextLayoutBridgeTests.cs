using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // ScaledTextLayoutBridge：挂在 wrapper GO 上的 ILayoutElement，把内层 TMP 的
    // min/preferred × s（s = 内层 localScale.x）报告给父 LayoutGroup；flexible 原样
    // 透传（权重无量纲）；layoutPriority=0 与 TMP 持平，被显式 LayoutElement(priority 1)
    // 逐属性压过。spec STW-D6。
    public class ScaledTextLayoutBridgeTests
    {
        private GameObject _canvasGo;
        private GameObject _wrapperGo;
        private RectTransform _inner;
        private TMP_Text _tmp;
        private ScaledTextLayoutBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            // TMP 量算需要 Canvas 祖先（与 Btn GetNativeSize 的既有 EditMode 测试同前提）。
            _canvasGo = new GameObject("canvas", typeof(Canvas));
            _wrapperGo = new GameObject("wrapper", typeof(RectTransform));
            _wrapperGo.transform.SetParent(_canvasGo.transform, false);
            var textGo = new GameObject("text", typeof(RectTransform));
            textGo.transform.SetParent(_wrapperGo.transform, false);
            _tmp = textGo.AddComponent<TextMeshProUGUI>();
            _tmp.text = "hello world";
            _tmp.fontSize = 12;
            _inner = (RectTransform)textGo.transform;
            _bridge = _wrapperGo.AddComponent<ScaledTextLayoutBridge>();
            _bridge.Configure(_tmp, _inner);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvasGo);

        [Test]
        public void Preferred_and_min_scale_with_inner_localScale()
        {
            _inner.localScale = new Vector3(0.5f, 0.5f, 1f);
            Assert.AreEqual(_tmp.preferredWidth * 0.5f, _bridge.preferredWidth, 1e-3f);
            Assert.AreEqual(_tmp.preferredHeight * 0.5f, _bridge.preferredHeight, 1e-3f);
            Assert.AreEqual(_tmp.minWidth * 0.5f, _bridge.minWidth, 1e-3f);
            Assert.AreEqual(_tmp.minHeight * 0.5f, _bridge.minHeight, 1e-3f);
        }

        [Test]
        public void Identity_scale_is_passthrough()
        {
            _inner.localScale = Vector3.one;
            Assert.AreEqual(_tmp.preferredWidth, _bridge.preferredWidth, 1e-3f);
            Assert.AreEqual(_tmp.preferredHeight, _bridge.preferredHeight, 1e-3f);
        }

        [Test]
        public void Flexible_passes_through_unscaled()
        {
            _inner.localScale = new Vector3(0.5f, 0.5f, 1f);
            Assert.AreEqual(_tmp.flexibleWidth, _bridge.flexibleWidth, 1e-6f);
            Assert.AreEqual(_tmp.flexibleHeight, _bridge.flexibleHeight, 1e-6f);
        }

        [Test]
        public void Priority_is_zero_like_tmp()
        {
            Assert.AreEqual(0, _bridge.layoutPriority);
        }

        [Test]
        public void Unconfigured_bridge_reports_zero_not_throw()
        {
            var bare = new GameObject("bare", typeof(RectTransform))
                .AddComponent<ScaledTextLayoutBridge>();
            Assert.AreEqual(0f, bare.preferredWidth);
            Assert.AreEqual(0f, bare.preferredHeight);
            Object.DestroyImmediate(bare.gameObject);
        }
    }
}
