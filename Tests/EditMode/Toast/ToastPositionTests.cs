using NUnit.Framework;
using PromptUGUI.Application.Toasts;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class ToastPositionTests
    {
        private static RectTransform MakeCanvasRect(float w, float h)
        {
            var go = new GameObject("toastCanvas", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        [Test]
        public void Bottom_anchors_to_bottom_edge()
        {
            var rt = MakeCanvasRect(1920, 1080);
            Assert.IsTrue(ToastPosition.Bottom.TryResolve(rt, 120f, out var r));
            Assert.AreEqual(new Vector2(0.5f, 0f), r.Anchor);
            Assert.AreEqual(new Vector2(0.5f, 0f), r.Pivot);
            Assert.AreEqual(new Vector2(0f, 120f), r.BasePos);
            Assert.AreEqual(Vector2.up, r.Dir);
            Object.DestroyImmediate(rt.gameObject);
        }

        [Test]
        public void Top_grows_down()
        {
            var rt = MakeCanvasRect(1920, 1080);
            Assert.IsTrue(ToastPosition.Top.TryResolve(rt, 120f, out var r));
            Assert.AreEqual(new Vector2(0f, -120f), r.BasePos);
            Assert.AreEqual(Vector2.down, r.Dir);
            Object.DestroyImmediate(rt.gameObject);
        }

        [Test]
        public void Coord_is_center_relative()
        {
            var rt = MakeCanvasRect(1920, 1080);
            Assert.IsTrue(ToastPosition.At(new Vector2(0, 200)).TryResolve(rt, 120f, out var r));
            Assert.AreEqual(new Vector2(0.5f, 0.5f), r.Anchor);
            Assert.AreEqual(new Vector2(0, 200), r.BasePos);
            Object.DestroyImmediate(rt.gameObject);
        }

        [Test]
        public void Vector2_implicitly_converts()
        {
            ToastPosition p = new Vector2(10, 20);   // 隐式
            p.TryResolve(MakeCanvasRect(100, 100), 0f, out var r);
            Assert.AreEqual(new Vector2(10, 20), r.BasePos);
        }

        [Test]
        public void Unspecified_default_flag_set()
            => Assert.IsTrue(default(ToastPosition).IsUnspecified);

        [Test]
        public void Preset_is_not_unspecified()
            => Assert.IsFalse(ToastPosition.Bottom.IsUnspecified);

        [Test]
        public void GroupKey_presets_differ_coords_round()
        {
            Assert.AreNotEqual(ToastPosition.Top.GroupKey(), ToastPosition.Bottom.GroupKey());
            Assert.AreEqual(ToastPosition.At(new Vector2(0.4f, 0.4f)).GroupKey(),
                            ToastPosition.At(new Vector2(0.3f, 0.1f)).GroupKey());   // 同舍入到 (0,0)
        }

        [Test]
        public void ControlPath_miss_returns_false()
        {
            // 没有任何已开 Screen → 路径解析必失败 → TryResolve false（由 overlay 退默认）
            PromptUGUI.Application.UI.ResetForTests();
            Assert.IsFalse(ToastPosition.At("Nope/x").TryResolve(MakeCanvasRect(100, 100), 0f, out _));
        }
    }
}
