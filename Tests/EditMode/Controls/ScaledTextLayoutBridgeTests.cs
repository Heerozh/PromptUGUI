using System.Reflection;
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

        // TMP の maxWidth/maxHeight は protected フィールド m_maxWidth/m_maxHeight で管理され、
        // public setter がない。リフレクションで直接書いて番兵値を注入する。
        private static void SetTmpMaxWidth(TMP_Text tmp, float value)
        {
            var fi = typeof(TMP_Text).GetField("m_maxWidth", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, "TMP_Text.m_maxWidth field not found — API may have changed");
            fi.SetValue(tmp, value);
        }

        private static void SetTmpMaxHeight(TMP_Text tmp, float value)
        {
            var fi = typeof(TMP_Text).GetField("m_maxHeight", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, "TMP_Text.m_maxHeight field not found — API may have changed");
            fi.SetValue(tmp, value);
        }

        [Test]
        public void Default_negative_max_sentinel_passes_through_unscaled()
        {
            _inner.localScale = new Vector3(0.5f, 0.5f, 1f);
            // TMP の maxWidth/maxHeight デフォルトは 0（no setter）。
            // 番兵として負値を注入し、乗算されずそのまま透過されることを確認する。
            SetTmpMaxWidth(_tmp, -1f);
            SetTmpMaxHeight(_tmp, -1f);
            Assert.AreEqual(-1f, _bridge.maxWidth, 1e-6f);
            Assert.AreEqual(-1f, _bridge.maxHeight, 1e-6f);
        }

        [Test]
        public void Positive_max_scales_like_preferred()
        {
            _inner.localScale = new Vector3(0.5f, 0.5f, 1f);
            // TMP の maxWidth/maxHeight に setter がないためリフレクションで設定する。
            SetTmpMaxWidth(_tmp, 300f);
            SetTmpMaxHeight(_tmp, 80f);
            Assert.AreEqual(150f, _bridge.maxWidth, 1e-3f);
            Assert.AreEqual(40f, _bridge.maxHeight, 1e-3f);
        }

        [Test]
        public void Destroyed_tmp_reports_fallbacks_not_throw()
        {
            Object.DestroyImmediate(_tmp.gameObject);   // Unity fake-null 路径（BindItems 拆卡时实际会发生）
            Assert.AreEqual(0f, _bridge.preferredWidth);
            Assert.AreEqual(0f, _bridge.minHeight);
            Assert.AreEqual(-1f, _bridge.flexibleWidth);
            Assert.AreEqual(-1f, _bridge.maxWidth);
        }
    }
}
