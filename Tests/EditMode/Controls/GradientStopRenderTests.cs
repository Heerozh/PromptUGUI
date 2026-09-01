using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Do stops and hints actually reach the screen? The unit tests prove the mesh gets cut and the
    /// vertices get the right colours — both stay green if the effect never runs, if uGUI throws the
    /// modified stream away, or if the flip lands on top of it. Only a render tells.
    ///
    /// <para>Same explicit <c>Camera.Render()</c> harness as <see cref="DecorRenderTests"/>, PNG
    /// dumps and all. Pixels are read as raw bytes rather than through <c>GetPixel</c> so the project's
    /// colour space cannot quietly rescale a probe; every threshold below is set wide enough to hold
    /// in gamma and linear alike, and narrow enough that the full-height ramp this replaces fails it.</para>
    /// </summary>
    public class GradientStopRenderTests
    {
        private const int Size = 256;
        private const float W = 100f;
        private const float H = 200f;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Color32[] _pixels;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "GradientStopUIRT" };
            _ui = new GameObject("GradientStopUICamera").AddComponent<Camera>();
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = Color.black;
            _ui.targetTexture = _uiRt;
            _ui.cullingMask = ~0;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_shot != null) Object.DestroyImmediate(_shot);
            if (_ui != null) Object.DestroyImmediate(_ui.gameObject);
            if (_uiRt != null)
            {
                _uiRt.Release();
                Object.DestroyImmediate(_uiRt);
            }
        }

        private void Render(string imageAttrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='g' anchor='center' width='{W}' height='{H}' {imageAttrs}/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            Canvas.ForceUpdateCanvases();
            _ui.Render();

            if (_shot != null) Object.DestroyImmediate(_shot);
            _shot = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            _shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            _shot.Apply();
            RenderTexture.active = previous;
            _pixels = _shot.GetPixels32();

            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
            File.WriteAllBytes(path, _shot.EncodeToPNG());
            Debug.Log($"PromptUGUI gradient-stop render dump: {path}");

            var rt = (RectTransform)screen.Get<PromptUGUI.Controls.Image>("g").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                    Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.height, H * 0.5f,
                "the image rendered far smaller than its canvas size — probes would be meaningless");
        }

        /// <summary>Samples the middle column at a share measured DOWN from the image's top edge —
        /// the same direction stops are written in.</summary>
        private Color FromTop(float share)
        {
            var x = Mathf.RoundToInt(Mathf.Lerp(_rect.xMin, _rect.xMax, 0.5f));
            var y = Mathf.RoundToInt(Mathf.Lerp(_rect.yMax, _rect.yMin, share));
            x = Mathf.Clamp(x, 0, Size - 1);
            y = Mathf.Clamp(y, 0, Size - 1);
            var p = _pixels[y * Size + x];
            return new Color(p.r / 255f, p.g / 255f, p.b / 255f, p.a / 255f);
        }

        private float[] Column(int samples)
        {
            var values = new float[samples];
            for (var i = 0; i < samples; i++)
                values[i] = FromTop((i + 0.5f) / samples).r;
            return values;
        }

        // ── the fixture itself ──────────────────────────────────────────────────

        [Test]
        public void Fixture_PaintsASolidImage()
        {
            // Always first: if this fails every other probe below is measuring nothing.
            Render("color='#ff0000'", "vgs-fixture.png");

            Assert.Greater(FromTop(0.5f).r, 0.9f, "the image is not on screen at all");
            Assert.Less(FromTop(0.5f).b, 0.1f);
        }

        // ── stops ───────────────────────────────────────────────────────────────

        [Test]
        public void HardEdge_SplitsTheImageInTwo()
        {
            Render("color='#ff0000 50%,#0000ff 50%'", "vgs-hard-edge.png");

            var top = FromTop(0.25f);
            var bottom = FromTop(0.75f);
            Assert.Greater(top.r, 0.85f, "top half is red");
            Assert.Less(top.b, 0.1f, "no blue bleeding into the top half");
            Assert.Greater(bottom.b, 0.85f, "bottom half is blue");
            Assert.Less(bottom.r, 0.1f, "no red bleeding into the bottom half");
        }

        [Test]
        public void TopStop_KeepsTheTopHalfSolid()
        {
            Render("color='#ff0000 50%,#0000ff'", "vgs-top-stop.png");

            var quarter = FromTop(0.25f);
            Assert.Less(quarter.b, 0.15f, "above the stop the colour must not have started moving yet");
            Assert.Greater(quarter.r, 0.85f);

            var threeQuarters = FromTop(0.75f);
            Assert.Greater(threeQuarters.b, 0.25f, "below the stop the ramp is under way");
            Assert.Less(threeQuarters.b, 0.95f, "and has not finished before the bottom edge");
        }

        // ── hints ───────────────────────────────────────────────────────────────

        [Test]
        public void Hint_PutsTheHalfwayMixAtTheHint()
        {
            Render("color='#ffffff, 30%, #000000'", "vgs-hint.png");

            Assert.AreEqual(0.5f, FromTop(0.3f).r, 0.08f, "half and half at the hint, not at the middle");
            Assert.Less(FromTop(0.6f).r, 0.35f, "past the hint the ramp is already mostly black");
        }

        // ── transparent ends drop geometry ──────────────────────────────────────

        [Test]
        public void TransparentTail_LeavesTheBackground()
        {
            Render("color='#ffffff,#ffffff/0 50%'", "vgs-transparent-tail.png");

            Assert.Greater(FromTop(0.25f).r, 0.4f, "the visible part is still drawn");
            Assert.Less(FromTop(0.75f).r, 0.05f, "past the stop there is nothing left, not even faint white");
        }

        // ── the flip / gradient order, on screen ────────────────────────────────

        [Test]
        public void Flip_AttributeOrderIsIrrelevant()
        {
            Render("color='#ffffff,#000000 50%' flip='y'", "vgs-flip-color-first.png");
            var colourFirst = Column(20);
            Assert.Greater(colourFirst[1], 0.9f, "the first colour is the top of what you SEE");
            Assert.Less(colourFirst[18], 0.1f);

            Render("flip='y' color='#ffffff,#000000 50%'", "vgs-flip-flip-first.png");
            var flipFirst = Column(20);

            for (var i = 0; i < colourFirst.Length; i++)
                Assert.AreEqual(colourFirst[i], flipFirst[i], 2f / 255f,
                    $"row {i} differs between the two attribute orders");
        }

        // ── a mesh that is not one quad ─────────────────────────────────────────

        [Test]
        public void SlicedSprite_StopStillLandsInTheRightRow()
        {
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var fill = new Color32[16 * 16];
            for (var i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(fill);
            tex.Apply();
            var sliced = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
            UI.SpriteResolver = _ => sliced;

            Render("sprite='t:s' type='sliced' color='#ff0000 50%,#0000ff 50%'", "vgs-sliced.png");

            var top = FromTop(0.25f);
            var bottom = FromTop(0.75f);
            Assert.Greater(top.r, 0.85f, "nine-slice mesh, same hard edge");
            Assert.Less(top.b, 0.1f, "a full-height ramp would already be a third blue here");
            Assert.Greater(bottom.b, 0.85f);
            Assert.Less(bottom.r, 0.1f);

            Object.DestroyImmediate(tex);
        }
    }
}
