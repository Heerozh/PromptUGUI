using NUnit.Framework;
using PromptUGUI.Application.Tutorial;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Tutorial
{
    public class SpotlightMaskTests
    {
        private GameObject _canvasGo;
        private SpotlightMask _mask;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("canvas", typeof(Canvas));
            var go = new GameObject("mask", typeof(RectTransform));
            go.transform.SetParent(_canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(800, 600);   // 本地坐标系:中心原点,±400/±300
            _mask = go.AddComponent<SpotlightMask>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_canvasGo);

        [Test]
        public void NoHole_BlocksEverywhere()
        {
            _mask.SetHole(null);
            Assert.IsTrue(_mask.HitTestForTests(Vector2.zero));
            Assert.IsTrue(_mask.HitTestForTests(new Vector2(390, 290)));
        }

        [Test]
        public void Hole_PassesInside_BlocksOutside()
        {
            _mask.SetHole(new Rect(-50, -25, 100, 50));   // 中心 100x50 的洞
            Assert.IsFalse(_mask.HitTestForTests(Vector2.zero));            // 洞内 → 穿透
            Assert.IsFalse(_mask.HitTestForTests(new Vector2(49, 24)));     // 洞内边缘
            Assert.IsTrue(_mask.HitTestForTests(new Vector2(51, 0)));       // 洞外
            Assert.IsTrue(_mask.HitTestForTests(new Vector2(0, 26)));
        }

        [Test]
        public void Mesh_NoHole_SingleQuad_4Verts()
        {
            _mask.SetHole(null);
            Assert.AreEqual(4, _mask.PopulateMeshVertexCountForTests());
        }

        [Test]
        public void Mesh_WithHole_FourBands_16Verts()
        {
            _mask.SetHole(new Rect(-50, -25, 100, 50));
            Assert.AreEqual(16, _mask.PopulateMeshVertexCountForTests());
        }

        [Test]
        public void Hole_ClampedToRect_DegenerateBandsSkipped()
        {
            // 洞超出整个 rect → 等效无遮挡区,但不得产出反向 quad
            _mask.SetHole(new Rect(-1000, -1000, 2000, 2000));
            Assert.AreEqual(0, _mask.PopulateMeshVertexCountForTests());
            Assert.IsFalse(_mask.HitTestForTests(Vector2.zero));
        }
    }
}
