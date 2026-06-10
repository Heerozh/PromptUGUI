using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Control.LayoutHost（spec STW-D4/D5）：默认 = 自身 RectTransform；指向 wrapper 后
    // ApplyCommon 的父级判断、LayoutElement 落点、Hidden、Dispose 都以 wrapper 为宿主。
    public class LayoutHostRoutingTests
    {
        private GameObject _vstackGo;
        private GameObject _wrapperGo;
        private GameObject _textGo;
        private PromptUGUI.Controls.Text _control;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _vstackGo = new GameObject("vstack", typeof(RectTransform),
                                       typeof(VerticalLayoutGroup));
            _wrapperGo = new GameObject("wrapper", typeof(RectTransform));
            _wrapperGo.transform.SetParent(_vstackGo.transform, false);
            _textGo = new GameObject("text", typeof(RectTransform));
            _textGo.transform.SetParent(_wrapperGo.transform, false);
            _control = new PromptUGUI.Controls.Text();
            _control.AttachTo(_textGo);
            _control.LayoutHost = (RectTransform)_wrapperGo.transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_vstackGo != null) Object.DestroyImmediate(_vstackGo);
            UI.ResetForTests();
        }

        [Test]
        public void LayoutHost_defaults_to_own_RectTransform()
        {
            var bare = new GameObject("bare", typeof(RectTransform));
            var c = new PromptUGUI.Controls.Text();
            c.AttachTo(bare);
            Assert.AreEqual(c.RectTransform, c.LayoutHost);
            Assert.AreEqual(bare, c.HostGameObject);
            Object.DestroyImmediate(bare);
        }

        [Test]
        public void ApplyCommon_routes_LayoutElement_to_wrapper()
        {
            _control.ApplyCommon(null, null, "stretch", null, null, null, null, true);

            var le = _wrapperGo.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "LE should attach to the wrapper, not the inner GO");
            Assert.AreEqual(0f, le.preferredWidth);
            Assert.AreEqual(1f, le.flexibleWidth);
            Assert.IsNull(_textGo.GetComponent<LayoutElement>());
        }

        [Test]
        public void ApplyCommon_resets_inner_to_stretch_baseline()
        {
            _control.ApplyCommon(null, null, "stretch", null, null, null, null, true);

            var rt = _control.RectTransform;
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.pivot);
            Assert.AreEqual(Vector2.zero, rt.sizeDelta);
            Assert.AreEqual(Vector2.zero, rt.anchoredPosition);
        }

        [Test]
        public void Explicit_height_pins_wrapper_LE_min_and_preferred()
        {
            _control.ApplyCommon(null, null, "stretch", "40", null, null, null, true);

            var le = _wrapperGo.GetComponent<LayoutElement>();
            Assert.AreEqual(40f, le.preferredHeight);
            Assert.AreEqual(40f, le.minHeight);
            Assert.AreEqual(0f, le.flexibleHeight);
        }

        [Test]
        public void Omitted_height_leaves_wrapper_LE_sentinel_for_bridge()
        {
            // <Text> 是 UsesIntrinsicLayoutSize 控件：省略轴留 -1 哨兵（bridge 接管）。
            _control.ApplyCommon(null, null, "stretch", null, null, null, null, true);

            var le = _wrapperGo.GetComponent<LayoutElement>();
            Assert.AreEqual(-1f, le.preferredHeight);
            Assert.AreEqual(-1f, le.minHeight);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Hidden_toggles_wrapper_not_inner()
        {
            _control.Hidden = true;
            Assert.IsFalse(_wrapperGo.activeSelf);
            Assert.IsTrue(_textGo.activeSelf);
            Assert.IsTrue(_control.Hidden);
            _control.Hidden = false;
            Assert.IsTrue(_wrapperGo.activeSelf);
        }

        [Test]
        public void Dispose_destroys_wrapper()
        {
            _control.Dispose();
            Assert.IsTrue(_wrapperGo == null, "wrapper (host GO) should be destroyed");
        }
    }
}
