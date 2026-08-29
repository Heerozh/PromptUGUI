using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A <c>cut</c> may reach past the half-way line when the neighbouring corner leaves it the
    /// room (spec 2026-08-29 part 2, §14.1): the chamfer then continues into the neighbour's
    /// quadrant, which is what makes a real trapezoid — a slanted side running the full height —
    /// expressible at all. Every probe here is a point that the old half-edge clamp paints and the
    /// spill leaves bare (or vice versa), so a shader that still clamps at half fails loudly.
    ///
    /// <para>Same explicit <c>Camera.Render()</c> harness as <see cref="CornerFilletRenderTests"/>;
    /// insets are canvas units from the top-left corner of a 200×160 Frame unless stated.</para>
    /// </summary>
    public class CornerSpillRenderTests
    {
        private const int Size = 256;
        private const float W = 200f;
        private const float H = 160f;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "CornerSpillUIRT" };
            _ui = new GameObject("CornerSpillUICamera").AddComponent<Camera>();
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

        private void Render(string attrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='{W}' height='{H}' color='#3366ff' {attrs}/>
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

            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
            File.WriteAllBytes(path, _shot.EncodeToPNG());
            Debug.Log($"PromptUGUI corner-spill render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("f").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                   Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
            Assert.Greater(_rect.width, W * 0.5f, "the probed rect rendered far too small");
        }

        /// <summary>Canvas units in from the top-left corner.</summary>
        private Color At(float insetX, float insetY)
            => _shot.GetPixel(Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.xMin, _rect.xMax, insetX / W)),
                              Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.yMin, _rect.yMax, (H - insetY) / H)));

        private static void AssertPainted(Color c, string what)
        {
            Assert.Greater(c.b, 0.5f, $"{what} — expected the blue fill, got {c}");
            Assert.Less(c.r, 0.5f, $"{what} — expected the fill, but this is the white border: {c}");
        }

        private static void AssertBackground(Color c, string what)
            => Assert.Less(c.b, 0.2f, $"{what} — expected background, got {c}");

        // ---- the trapezoid ----------------------------------------------------------------------

        [Test]
        public void FullHeightChamfer_RunsFromTheTopCorner()
        {
            // Bottom corners cut 40 wide, 160 tall = the whole height. The top corners are square,
            // so they leave all 160 — the slant runs from the top-left corner to 40 in at the bottom:
            // x(y) = y / 4. Under the old half-height clamp it only ran from (0, 80) to (40, 160).
            Render("radius='0, 0, cut 40x160, cut 40x160'", "pugui-spill-trapezoid.png");
            AssertBackground(At(10f, 80f), "at mid height the slant is 20 in, so 10 in is bare");
            AssertBackground(At(4f, 40f), "…and at a quarter height it is 10 in — no vertical edge left");
            AssertPainted(At(30f, 40f), "…while 30 in is still fill");
            AssertBackground(At(30f, 150f), "…and near the bottom the slant has reached 37.5 in");
            AssertPainted(At(100f, 80f), "the middle is fill");
        }

        [Test]
        public void FullHeightChamfer_Fillet_RoundsTheTopVertex()
        {
            // The trapezoid's top corners are the acute 76° angle between the top edge and the
            // slant. r=20 fillets them with an arc whose centre sits 32.5 units down the bisector
            // (r / sin 38°) — 12.5 units of sagitta, which is exactly what the nav tab lacked.
            Render("radius='0, 0, cut 40x160, cut 40x160'", "pugui-spill-trapezoid.png");
            AssertPainted(At(3.15f, 2.46f), "4 units down the sharp vertex's bisector is fill");

            Render("radius='0, 0, cut 40x160 r20, cut 40x160 r20'", "pugui-spill-trapezoid-fillet.png");
            AssertBackground(At(3.15f, 2.46f), "…and the fillet has taken it away (28.5 from the arc centre)");
            AssertPainted(At(12.6f, 9.8f), "…while 16 units down the bisector is inside the arc (16.5 from it)");
            AssertPainted(At(100f, 2f), "the top edge itself is where it was");
        }

        // ---- who leaves room, and who doesn't ---------------------------------------------------

        [Test]
        public void Spill_IntoARoundNeighbour_StopsShortOfItsArc()
        {
            // TL is round 30, BL cuts 40x120: the room along the left edge is 160 − 30 = 130, so 120
            // fits. The slant starts 40 below the top (where the arc's straight run ends at 30) and
            // reaches 40 in at the bottom.
            Render("radius='30, 0, 0, cut 40x120'", "pugui-spill-round-neighbour.png");
            AssertBackground(At(3f, 60f), "20 below the slant's start it is 6.7 in — 3 in is bare");
            AssertPainted(At(12f, 60f), "…and 12 in is fill");
            AssertPainted(At(2f, 35f), "the vertical edge between the arc and the slant is intact");
            AssertBackground(At(2f, 2f), "the round corner is still round");
            AssertPainted(At(15f, 15f), "…with its arc where it always was");
        }

        [Test]
        public void MutualOverflow_KeepsBothAtHalf()
        {
            // Both left corners want the full height: nobody leaves room, both stay at half and meet
            // at the centre line — the hexagon rule, unchanged.
            Render("radius='cut 40x160, 0, 0, cut 40x160'", "pugui-spill-mutual.png");
            AssertPainted(At(6f, 80f), "the two chamfers meet in a tip on the edge at mid height");
            AssertBackground(At(10f, 20f), "…and 60 above it the top chamfer is 30 in");
            AssertPainted(At(35f, 20f), "…so 35 in is fill");
        }

        [Test]
        public void SpillNextToANotch_KeepsHalf()
        {
            // A notch quadrant does not receive a spill: the cut below it stays at half height.
            Render("radius='notch 20, 0, 0, cut 40x160'", "pugui-spill-notch-neighbour.png");
            AssertPainted(At(4f, 60f), "the vertical edge above the centre line is intact");
            AssertPainted(At(6f, 80f), "…and the chamfer starts at the centre line");
            AssertBackground(At(10f, 10f), "the notch is still bitten out");
        }

        [Test]
        public void HorizontalSpill_WorksTheSameWay()
        {
            // TR cuts 200 wide (the whole width) and 40 tall: the chamfer runs from the top-left
            // corner to 40 down the right edge, y(x) = x / 5. The old clamp stopped it at 100 wide.
            Render("radius='0, cut 200x40, 0, 0'", "pugui-spill-horizontal.png");
            AssertBackground(At(100f, 10f), "half-way across the slant is 20 down, so 10 down is bare");
            AssertPainted(At(100f, 30f), "…and 30 down is fill");
            AssertBackground(At(40f, 4f), "…40 in, 4 down is bare too (slant at 8)");
            AssertPainted(At(2f, 80f), "the left edge is untouched");
        }
    }
}
