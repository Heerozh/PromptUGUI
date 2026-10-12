using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The library's own UI shaders clip to a <c>RectMask2D</c> exactly like Unity's UI/Default: the same
    /// coverage ramp at the clip edge (not a hard step at the pixel centre, which drew one column further),
    /// and the mask's <c>softness</c> honoured. One mask, one row per shader, every row sampled across the
    /// mask's edge and compared with the UI/Default row. Same explicit <c>Camera.Render()</c> harness as
    /// <see cref="CornerFilletRenderTests"/>.
    /// </summary>
    public class ClipRectRenderTests
    {
        private const int Size = 256;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Texture2D _white;
        private Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _white = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "ClipRectWhite" };
            var px = new Color32[64];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            _white.SetPixels32(px);
            _white.Apply();
            _sprite = Sprite.Create(_white, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 100f);
            UI.SpriteResolver = _ => _sprite;

            _uiRt = new RenderTexture(Size, Size, 24) { name = "ClipRectUIRT" };
            _ui = new GameObject("ClipRectUICamera").AddComponent<Camera>();
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
            if (_sprite != null) Object.DestroyImmediate(_sprite);
            if (_white != null) Object.DestroyImmediate(_white);
        }

        // Rows wider than the mask, so its right edge crosses each row's uniform interior.
        private static readonly string[] Rows = { "fx", "linear", "panel" };

        private PromptUGUI.Application.Screen Render(float maskWidth, int softness, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='m' anchor='center' width='{maskWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}' height='130' mask='rect'>
    <Image id='plain'  anchor='top-left' margin='5,_,_,0'  width='240' height='20' sprite='ui:white'/>
    <Image id='fx'     anchor='top-left' margin='35,_,_,0' width='240' height='20' sprite='ui:white' blur='2'/>
    <Image id='linear' anchor='top-left' margin='65,_,_,0' width='240' height='20' sprite='ui:white' tint='linear'/>
    <Frame id='panel'  anchor='top-left' margin='95,_,_,0' width='240' height='20' color='#ffffff' radius='2'/>
  </Frame>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;
            screen.Get<Frame>("m").GameObject.GetComponent<RectMask2D>().softness = new Vector2Int(softness, 0);

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
            Debug.Log($"PromptUGUI clip-rect render dump: {path}");
            return screen;
        }

        private Vector2 ScreenPoint(Vector3 world) => RectTransformUtility.WorldToScreenPoint(_ui, world);

        private float MaskRightEdgePx(PromptUGUI.Application.Screen s)
        {
            var corners = new Vector3[4];
            s.Get("m").RectTransform.GetWorldCorners(corners);
            return ScreenPoint(corners[2]).x;
        }

        private int RowY(PromptUGUI.Application.Screen s, string id)
        {
            var corners = new Vector3[4];
            s.Get(id).RectTransform.GetWorldCorners(corners);
            return Mathf.RoundToInt((ScreenPoint(corners[0]).y + ScreenPoint(corners[1]).y) * 0.5f);
        }

        private float Luma(int x, int y) => _shot.GetPixel(x, y).r;

        private void AssertRowsClipLikeUIDefault(PromptUGUI.Application.Screen s, int reach)
        {
            var edge = MaskRightEdgePx(s);
            var plainY = RowY(s, "plain");
            var x0 = Mathf.FloorToInt(edge) - reach;
            var x1 = Mathf.CeilToInt(edge) + reach;
            Assume.That(Luma(x0 - 4, plainY), Is.GreaterThan(0.95f), "guard: the rows are drawn inside the mask");
            Assume.That(Luma(x1 + 4, plainY), Is.LessThan(0.05f), "guard: and clipped outside it");

            foreach (var row in Rows)
            {
                var y = RowY(s, row);
                for (var x = x0; x <= x1; x++)
                    Assert.AreEqual(Luma(x, plainY), Luma(x, y), 3f / 255f,
                        $"<{row}> column {x} (mask edge at {edge:F2}px) is clipped like UI/Default");
            }
        }

        [TestCase(101f)]
        [TestCase(101.37f)]
        [TestCase(101.71f)]
        public void Every_shader_clips_its_edge_like_UI_Default(float maskWidth)
        {
            var s = Render(maskWidth, softness: 0, $"cliprect_{maskWidth:F2}.png");
            AssertRowsClipLikeUIDefault(s, reach: 3);
        }

        [Test]
        public void Every_shader_honours_the_masks_softness()
        {
            var s = Render(101f, softness: 24, "cliprect_soft.png");
            var edge = MaskRightEdgePx(s);
            var plainY = RowY(s, "plain");
            Assume.That(Luma(Mathf.RoundToInt(edge) - 6, plainY), Is.InRange(0.1f, 0.9f),
                "guard: UI/Default fades over the softness band");

            AssertRowsClipLikeUIDefault(s, reach: 26);
        }
    }
}
