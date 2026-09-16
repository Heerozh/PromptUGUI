using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Do directions, third stops and gradient borders / glows actually reach the screen on a
    /// procedural surface? The material-key tests prove the values arrive; only a render proves the
    /// shader reads them. Same explicit <c>Camera.Render()</c> harness as
    /// <see cref="GradientStopRenderTests"/>. Probes are raw bytes with wide thresholds so the
    /// project's colour space cannot quietly rescale one.
    /// </summary>
    public class GradientDirectionRenderTests
    {
        private const int Size = 256;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Color32[] _pixels;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "GradientDirUIRT" };
            _ui = new GameObject("GradientDirUICamera").AddComponent<Camera>();
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

        private void RenderFrame(float w, float h, string attrs, string dumpName)
            => RenderBody($"<Frame id='g' anchor='center' width='{w}' height='{h}' {attrs}/>", "g", dumpName, h);

        private void RenderBody(string body, string probeId, string dumpName, float expectedHeight)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  {body}
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
            Debug.Log($"PromptUGUI gradient-direction render dump: {path}");

            var rt = (RectTransform)screen.Get<PromptUGUI.Controls.IControl>(probeId).GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                    Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.height, expectedHeight * 0.5f,
                "the frame rendered far smaller than its canvas size — probes would be meaningless");
        }

        /// <summary>Samples at shares measured from the LEFT edge and DOWN from the top edge.</summary>
        private Color At(float shareX, float shareY)
        {
            var x = Mathf.RoundToInt(Mathf.Lerp(_rect.xMin, _rect.xMax, shareX));
            var y = Mathf.RoundToInt(Mathf.Lerp(_rect.yMax, _rect.yMin, shareY));
            return Pixel(x, y);
        }

        /// <summary>Samples at canvas-unit offsets outside the rect (negative = left / above).</summary>
        private Color Outside(float dx, float dy)
        {
            var x = Mathf.RoundToInt(dx < 0 ? _rect.xMin + dx : dx > 0 ? _rect.xMax + dx : _rect.center.x);
            var y = Mathf.RoundToInt(dy < 0 ? _rect.yMax - dy : dy > 0 ? _rect.yMin - dy : _rect.center.y);
            return Pixel(x, y);
        }

        private Color Pixel(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Size - 1);
            y = Mathf.Clamp(y, 0, Size - 1);
            var p = _pixels[y * Size + x];
            return new Color(p.r / 255f, p.g / 255f, p.b / 255f, p.a / 255f);
        }

        // ── fixture ─────────────────────────────────────────────────────────────

        [Test]
        public void Fixture_PaintsASolidFrame()
        {
            RenderFrame(120f, 120f, "color='#ff0000'", "lg-fixture.png");
            Assert.Greater(At(0.5f, 0.5f).r, 0.9f, "the frame is not on screen at all");
            Assert.Less(At(0.5f, 0.5f).b, 0.1f);
        }

        // ── direction ───────────────────────────────────────────────────────────

        [Test]
        public void Default_StillRunsTopToBottom()
        {
            RenderFrame(120f, 120f, "color='#ff0000, #0000ff'", "lg-default.png");
            Assert.Greater(At(0.5f, 0.1f).r, At(0.5f, 0.1f).b, "top is red");
            Assert.Greater(At(0.5f, 0.9f).b, At(0.5f, 0.9f).r, "bottom is blue");
            Assert.Less(Mathf.Abs(At(0.1f, 0.5f).r - At(0.9f, 0.5f).r), 0.1f, "no change across");
        }

        [Test]
        public void ToRight_RunsLeftToRight()
        {
            RenderFrame(120f, 120f, "color='to right, #ff0000, #0000ff'", "lg-to-right.png");
            // Thresholds hold in gamma and linear alike: a linear 0.05 encodes to about 0.25 in sRGB.
            Assert.Greater(At(0.05f, 0.5f).r, 0.7f, "left is red");
            Assert.Less(At(0.05f, 0.5f).b, 0.4f);
            Assert.Greater(At(0.95f, 0.5f).b, 0.7f, "right is blue");
            Assert.Less(At(0.95f, 0.5f).r, 0.4f);
            Assert.Less(Mathf.Abs(At(0.5f, 0.1f).r - At(0.5f, 0.9f).r), 0.1f, "no change down");
        }

        [Test]
        public void ToTop_IsTheDefaultFlipped()
        {
            RenderFrame(120f, 120f, "color='to top, #ff0000, #0000ff'", "lg-to-top.png");
            Assert.Greater(At(0.5f, 0.9f).r, At(0.5f, 0.9f).b, "bottom is red");
            Assert.Greater(At(0.5f, 0.1f).b, At(0.5f, 0.1f).r, "top is blue");
        }

        [Test]
        public void ToBottomRight_ThreeStops_OnAWideFrame_LightsTheNamedCorners()
        {
            // The magic corner: on 2:1 the two OTHER corners sit exactly on the 50% line — green.
            RenderFrame(200f, 100f, "color='to bottom right, #ff0000, #00ff00 50%, #0000ff'", "lg-corner-3.png");
            var tl = At(0.03f, 0.06f);
            var br = At(0.97f, 0.94f);
            var tr = At(0.97f, 0.06f);
            var bl = At(0.03f, 0.94f);
            Assert.Greater(tl.r, 0.7f, "top-left is red");
            Assert.Greater(br.b, 0.7f, "bottom-right is blue");
            Assert.Greater(tr.g, 0.6f, "top-right is on the green 50% line");
            Assert.Less(tr.r + tr.b, 0.7f, "…and not much of either end colour");
            Assert.Greater(bl.g, 0.6f, "bottom-left is on the green 50% line");
            Assert.Less(bl.r + bl.b, 0.7f);
            Assert.Greater(At(0.5f, 0.5f).g, 0.8f, "centre is the middle stop");
        }

        [Test]
        public void Angle135_OnAWideFrame_DoesNotHitTheOtherCorners()
        {
            // The plain 45° family on 2:1: top-right is well past the 50% line, so it is NOT green —
            // this is what distinguishes "135deg" from "to bottom right" (spec §1).
            RenderFrame(200f, 100f, "color='135deg, #ff0000, #00ff00 50%, #0000ff'", "lg-angle-135.png");
            // Top-right sits at s ≈ 0.67 here: a third of the way from green to blue, where the magic
            // corner puts it exactly ON the green line with no blue at all.
            var tr = At(0.97f, 0.06f);
            Assert.Greater(tr.b, 0.15f, "top-right already carries blue on a plain diagonal");
            Assert.Less(tr.g, 0.9f, "…and is no longer pure green");
        }

        // ── the other slots ─────────────────────────────────────────────────────

        [Test]
        public void Border_Gradient_PaintsTheBandOnly()
        {
            RenderFrame(120f, 120f, "borderWidth='16' borderColor='#ff0000, #0000ff'", "lg-border.png");
            var top = At(0.5f, 0.05f);
            var bottom = At(0.5f, 0.95f);
            var centre = At(0.5f, 0.5f);
            Assert.Greater(top.r, 0.7f, "top band is red");
            Assert.Less(top.b, 0.3f);
            Assert.Greater(bottom.b, 0.7f, "bottom band is blue");
            Assert.Less(bottom.r, 0.3f);
            Assert.Less(centre.r + centre.g + centre.b, 0.2f, "no fill: the middle is the black background");
            var left = At(0.05f, 0.5f);
            Assert.Greater(left.r, 0.25f, "the side band at mid-height is the mid colour");
            Assert.Greater(left.b, 0.25f);
        }

        [Test]
        public void Border_ToRight_ThreeStops_LightsBothEnds()
        {
            // The reference-image button edge: bright at both ends, dark in the middle.
            RenderFrame(200f, 60f, "borderWidth='10' borderColor='to right, #00ffff, #00ffff20, #00ffff'", "lg-border-ends.png");
            var leftBand = At(0.02f, 0.5f);
            var midTop = At(0.5f, 0.06f);
            var rightBand = At(0.98f, 0.5f);
            Assert.Greater(leftBand.g, 0.7f, "left end bright");
            Assert.Greater(rightBand.g, 0.7f, "right end bright");
            // Linear alpha 0.125 over black encodes to about 0.39 in sRGB; the ends are at 1.
            Assert.Less(midTop.g, 0.55f, "middle of the top band is dim");
        }

        [Test]
        public void Glow_Unset_FollowsTheFillGradient()
        {
            RenderFrame(80f, 80f, "color='#ff0000, #0000ff' glow='40'", "lg-glow-follows.png");
            var above = Outside(0f, -8f);
            var below = Outside(0f, 8f);
            Assert.Greater(above.r, above.b, "the halo above the frame is red, like the top of the fill");
            Assert.Greater(below.b, below.r, "the halo below is blue, like the bottom");
        }

        [Test]
        public void Glow_Gradient_ToRight()
        {
            RenderFrame(80f, 80f, "color='#ffffff' glow='40' glowColor='to right, #ff0000, #0000ff'", "lg-glow-dir.png");
            var left = Outside(-8f, 0f);
            var right = Outside(8f, 0f);
            Assert.Greater(left.r, left.b, "halo on the left is red");
            Assert.Greater(right.b, right.r, "halo on the right is blue");
        }

        [Test]
        public void InnerGlow_Gradient_ToRight()
        {
            RenderFrame(120f, 120f, "color='#000000' innerGlow='30' innerGlowColor='to right, #ff0000, #0000ff'", "lg-inner-dir.png");
            var left = At(0.03f, 0.5f);
            var right = At(0.97f, 0.5f);
            Assert.Greater(left.r, left.b, "inner light on the left edge is red");
            Assert.Greater(right.b, right.r, "on the right edge, blue");
            Assert.Less(At(0.5f, 0.5f).r + At(0.5f, 0.5f).b, 0.3f, "the centre is still the dark fill");
        }

        // ── <Decor> shares the cginc ────────────────────────────────────────────

        [Test]
        public void Decor_Line_ToRight_Gradient()
        {
            RenderBody(
                "<Frame id='host' anchor='center' width='200' height='100'>" +
                "<Decor id='g' kind='line' at='bottom' extent='100%' thickness='24' inset='38' color='to right, #ff0000, #0000ff'/>" +
                "</Frame>", "host", "lg-decor-line.png", 100f);
            var left = At(0.05f, 0.5f);
            var right = At(0.95f, 0.5f);
            Assert.Greater(left.r, 0.6f, "left of the line is red");
            Assert.Greater(right.b, 0.6f, "right of the line is blue");
        }
    }
}
