using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Does a procedural control actually put pixels on the screen? Every other test in this
    /// milestone reads back C# state — the surface exists, the panel holds the right parameters,
    /// <c>targetGraphic</c> points at it — and all of that stays green while the control renders
    /// nothing at all. That is not hypothetical: the same shape of bug bit twice in this repo (a
    /// missing <c>[RequireComponent(CanvasRenderer)]</c> with 2088 tests passing, and a mask source
    /// culled by <c>ComputeVisible</c> a week ago).
    ///
    /// <para>Same explicit <c>Camera.Render()</c> harness as <see cref="GlassRenderTests"/> and
    /// <see cref="ProceduralMaskRenderTests"/>, no URP needed — a plain SDF panel samples no
    /// backdrop.</para>
    /// </summary>
    public class ProceduralSurfaceRenderTests
    {
        private const int Size = 256;

        private Camera _ui;
        private RenderTexture _uiRt;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "ProceduralSurfaceUIRT" };
            _ui = new GameObject("ProceduralSurfaceUICamera").AddComponent<Camera>();
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = Color.black;
            _ui.targetTexture = _uiRt;
            _ui.cullingMask = ~0;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ui != null) Object.DestroyImmediate(_ui.gameObject);
            if (_uiRt != null)
            {
                _uiRt.Release();
                Object.DestroyImmediate(_uiRt);
            }
        }

        /// <summary>Renders one Btn and samples its middle and its top-left rect corner.</summary>
        private (Color Centre, Color Corner) Sample(string btnAttrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' anchor='center' width='160' height='160' {btnAttrs}/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            Canvas.ForceUpdateCanvases();
            _ui.Render();

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            try
            {
                var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"PromptUGUI procedural-surface render dump: {path}");

                var rt = (RectTransform)screen.Get<Btn>("b").GameObject.transform;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
                var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
                var rect = Rect.MinMaxRect(
                    Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

                return (tex.GetPixel((int)rect.center.x, (int)rect.center.y),
                        tex.GetPixel((int)rect.xMin + 4, (int)rect.yMin + 4));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void AProceduralBtn_DrawsItsRoundedShape()
        {
            var (centre, corner) = Sample("radius='60' color='#3366ff'", "promptugui-btn-procedural.png");

            Assert.Greater(centre.b, 0.5f, $"the button must be painting its blue fill, got {centre}");
            Assert.Less(centre.r, 0.4f, $"…and it must be blue, not the shader-error magenta, got {centre}");
            Assert.Less(corner.b, 0.2f,
                $"the square corner lies outside a radius-60 shape, so it must be background, got {corner}");
        }

        /// <summary>
        /// Without a colour of its own a procedural Btn inherits the control's built-in one, so
        /// `<Btn radius="8">` is a rounded button rather than an invisible one. This is the assertion
        /// that would have caught retiring the Image without handing its colour over.
        /// </summary>
        [Test]
        public void AProceduralBtn_WithNoColour_StillDraws()
        {
            var (centre, _) = Sample("radius='40'", "promptugui-btn-default-colour.png");

            Assert.Greater(centre.r + centre.g + centre.b, 0.15f,
                $"the retired Image's colour has to reach the surface, got {centre}");
        }

        /// <summary>Spec §13.5: greyed, not erased, and still the right shape.</summary>
        [Test]
        public void ADisabledProceduralBtn_IsGreyedNotErased()
        {
            var (centre, corner) = Sample("radius='60' color='#3366ff' interactable='false'",
                                          "promptugui-btn-disabled.png");

            Assert.Greater(centre.r + centre.g + centre.b, 0.15f,
                $"a disabled button is still visible, got {centre}");
            Assert.Less(Mathf.Abs(centre.r - centre.b), 0.12f,
                $"…and desaturated: the blue must have collapsed towards grey, got {centre}");
            Assert.Less(corner.r + corner.g + corner.b, 0.15f,
                $"…while keeping its rounded shape, got {corner}");
        }
    }
}
