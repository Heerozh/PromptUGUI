using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Does blur / glow actually reach the screen, and does the sampling stay inside its own sprite?
    /// The attribute tests prove the parameters land on a material and the mesh tests prove the quad
    /// grows; both stay green while the shader draws nothing at all — and neither can see the failure
    /// this feature is most likely to have, which is a glow made of the NEIGHBOURING sprite's pixels.
    ///
    /// <para>So the fixture is a real two-sprite atlas built in code: a white disc on transparent
    /// black in the left half, a solid red block in the right half. Anything that samples past the
    /// disc's rectangle picks up red, and says so loudly. Same explicit <c>Camera.Render()</c> harness
    /// as <see cref="DecorRenderTests"/>, PNG dumps and all.</para>
    /// </summary>
    public class ImageFxRenderTests
    {
        private const int Size = 256;
        private const float HostW = 200f;
        private const float HostH = 200f;

        // Atlas: 64x32, two 32x32 tiles. The disc is radius 10 texels, centred in the left tile.
        private const int TileSize = 32;
        private const float DiscTexRadius = 10f;

        // The icon is drawn at 64x64 design px, so one texel is two design px and the disc's
        // silhouette lands at radius 20. Its own tile edge is 32 design px out from the centre.
        private const float IconSize = 64f;
        private const float DiscRadiusPx = IconSize * DiscTexRadius / TileSize;   // 20
        private const float TileEdgePx = IconSize * 0.5f;                          // 32

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;

        private Texture2D _atlas;
        private Sprite _disc;
        private Sprite _red;

        // The second fixture (spec §14): Bilinear, WITH a mip chain. 128 texels wide: a 64x64 tile
        // holding one vertical one-texel line, four texels of transparent padding (Unity's default),
        // then a solid red tile. Drawn at 64 design px, so one texel is one design px.
        private const int MipTile = 64;
        private const int MipPadding = 4;
        private const int MipAtlasWidth = 128;
        private Texture2D _mipAtlas;
        private Sprite _line;
        private Sprite _mipRed;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            BuildAtlas();
            BuildMipAtlas();
            UI.SpriteResolver = key => key switch
            {
                "ui:red" => _red,
                "ui:line" => _line,
                "ui:mipred" => _mipRed,
                _ => _disc,
            };

            _uiRt = new RenderTexture(Size, Size, 24) { name = "ImageFxUIRT" };
            _ui = new GameObject("ImageFxUICamera").AddComponent<Camera>();
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
            if (_disc != null) Object.DestroyImmediate(_disc);
            if (_red != null) Object.DestroyImmediate(_red);
            if (_atlas != null) Object.DestroyImmediate(_atlas);
            if (_line != null) Object.DestroyImmediate(_line);
            if (_mipRed != null) Object.DestroyImmediate(_mipRed);
            if (_mipAtlas != null) Object.DestroyImmediate(_mipAtlas);
        }

        /// <summary>
        /// The mip-path fixture. A one-texel line is the thinnest stroke there is — and the one the
        /// lod-0 kernel draws 25 separate copies of once its taps sit further apart than the stroke is
        /// wide (spec §14.1). Transparent texels carry WHITE rgb here, the way the importer's
        /// alphaIsTransparency dilation leaves them: a straight-alpha mip chain averaged over
        /// transparent black would darken the blur by itself, and that is the importer's doing, not the
        /// shader's (the Point fixture above keeps testing the shader's premultiplication).
        /// </summary>
        private void BuildMipAtlas()
        {
            _mipAtlas = new Texture2D(MipAtlasWidth, MipTile, TextureFormat.RGBA32, mipChain: true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "ImageFxMipAtlas",
            };

            var px = new Color32[MipAtlasWidth * MipTile];
            for (var y = 0; y < MipTile; y++)
            {
                for (var x = 0; x < MipAtlasWidth; x++)
                {
                    Color32 c;
                    if (x < MipTile)
                        c = x == MipTile / 2 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                    else if (x < MipTile + MipPadding)
                        c = new Color32(255, 255, 255, 0);
                    else
                        c = new Color32(255, 0, 0, 255);
                    px[y * MipAtlasWidth + x] = c;
                }
            }
            _mipAtlas.SetPixels32(px);
            _mipAtlas.Apply(updateMipmaps: true);
            Assert.Greater(_mipAtlas.mipmapCount, 1, "前置：the fixture must actually have a mip chain");

            _line = Sprite.Create(_mipAtlas, new Rect(0f, 0f, MipTile, MipTile), new Vector2(.5f, .5f),
                                  pixelsPerUnit: 1f, extrude: 0, meshType: SpriteMeshType.FullRect);
            _mipRed = Sprite.Create(_mipAtlas,
                                    new Rect(MipTile + MipPadding, 0f, MipAtlasWidth - MipTile - MipPadding, MipTile),
                                    new Vector2(.5f, .5f),
                                    pixelsPerUnit: 1f, extrude: 0, meshType: SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Two sprites packed in one texture, exactly as a SpriteAtlas would have them — which is the
        /// only way to test that a tap outside a sprite's rectangle reads as empty rather than as its
        /// neighbour. Transparent texels are transparent BLACK on purpose: averaging their RGB in
        /// with the visible ones (the mistake premultiplication avoids) then shows up as a dark ring.
        /// </summary>
        private void BuildAtlas()
        {
            _atlas = new Texture2D(TileSize * 2, TileSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "ImageFxAtlas",
            };

            var px = new Color32[TileSize * 2 * TileSize];
            var centre = new Vector2(TileSize * 0.5f - 0.5f, TileSize * 0.5f - 0.5f);
            for (var y = 0; y < TileSize; y++)
            {
                for (var x = 0; x < TileSize * 2; x++)
                {
                    Color32 c;
                    if (x < TileSize)
                    {
                        var inside = (new Vector2(x, y) - centre).magnitude <= DiscTexRadius;
                        c = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        c = new Color32(255, 0, 0, 255);
                    }
                    px[y * TileSize * 2 + x] = c;
                }
            }
            _atlas.SetPixels32(px);
            _atlas.Apply();

            _disc = Sprite.Create(_atlas, new Rect(0f, 0f, TileSize, TileSize), new Vector2(.5f, .5f),
                                  pixelsPerUnit: 1f, extrude: 0, meshType: SpriteMeshType.FullRect);
            _red = Sprite.Create(_atlas, new Rect(TileSize, 0f, TileSize, TileSize), new Vector2(.5f, .5f),
                                 pixelsPerUnit: 1f, extrude: 0, meshType: SpriteMeshType.FullRect);
        }

        /// <summary>Renders <paramref name="body"/> inside a fixed 200x200 host, which every probe is
        /// measured against.</summary>
        private PromptUGUI.Application.Screen Render(string body, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='host' anchor='center' width='{HostW}' height='{HostH}'>
    {body}
  </Frame>
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
            Debug.Log($"PromptUGUI image-fx render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("host").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                    Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.width, HostW * 0.5f,
                "the host rendered far smaller than its canvas size — probes would be meaningless");
            return screen;
        }

        /// <summary>Samples at a design-pixel offset from the host's centre (x right, y up).</summary>
        private Color AtPx(float dx, float dy)
        {
            var u = 0.5f + dx / HostW;
            var v = 0.5f + dy / HostH;
            return _shot.GetPixel(Mathf.RoundToInt(Mathf.Lerp(_rect.xMin, _rect.xMax, u)),
                                  Mathf.RoundToInt(Mathf.Lerp(_rect.yMin, _rect.yMax, v)));
        }

        private static float Luma(Color c) => (c.r + c.g + c.b) / 3f;

        private static string Icon(string attrs) =>
            $"<Icon id='i' name='ui:disc' anchor='center' size='{IconSize}x{IconSize}' {attrs}/>";

        // ---- 1. the fixture itself ----

        [Test]
        public void Fixture_DrawsTheDiscAndTheNeighbourIsNotInIt()
        {
            Render(Icon(""), "pugui-fx-fixture.png");

            Assert.Greater(Luma(AtPx(0f, 0f)), 0.9f, "the disc's middle");
            Assert.Less(Luma(AtPx(DiscRadiusPx + 6f, 0f)), 0.05f,
                "6px outside the silhouette, with no fx asked for: background");
            Assert.Less(Luma(AtPx(TileEdgePx + 6f, 0f)), 0.05f,
                "past the sprite's own rectangle: still background, never the neighbour");

            Render("<Icon id='i' name='ui:red' anchor='center' size='64x64'/>", "pugui-fx-fixture-red.png");
            var red = AtPx(0f, 0f);
            Assert.Greater(red.r, 0.5f, "the red tile really is red …");
            Assert.Less(red.g, 0.2f, "… and only red — this is what a bleed would look like");
        }

        // ---- 2. the glow ----

        [Test]
        public void Glow_LightsUpOutsideTheSilhouette_AndFadesToNothingByItsRadius()
        {
            Render(Icon("glow='8'"), "pugui-fx-glow-8.png");

            var near = Luma(AtPx(DiscRadiusPx + 2f, 0f));
            var mid = Luma(AtPx(DiscRadiusPx + 4f, 0f));
            var far = Luma(AtPx(DiscRadiusPx + 6f, 0f));

            Assert.Greater(near, 0.15f, "just outside the silhouette the glow must be visible");
            Assert.Greater(near, mid, "and fade outward");
            Assert.Greater(mid, far);
            Assert.Less(Luma(AtPx(DiscRadiusPx + 11f, 0f)), 0.05f, "gone past its radius");

            // The same probes with the feature off — the threshold above means nothing unless this
            // one holds.
            Render(Icon("glow='0'"), "pugui-fx-glow-0.png");
            Assert.Less(Luma(AtPx(DiscRadiusPx + 2f, 0f)), 0.05f,
                "with glow=0 the very same pixel is background");
        }

        [Test]
        public void Glow_NeverSamplesTheNeighbouringSprite()
        {
            // THE regression this fixture exists for. The quad is inflated past the sprite's
            // rectangle, so uv0 out there points at the red tile; only the uv1 clamp keeps it empty.
            Render(Icon("glow='8'"), "pugui-fx-glow-bleed.png");

            for (var dx = 2f; dx <= 7f; dx += 1f)
            {
                var c = AtPx(TileEdgePx + dx, 0f);
                Assert.LessOrEqual(c.r, Mathf.Max(c.g, c.b) + 0.05f,
                    $"{dx}px past the sprite's rectangle the glow has gone red — it is sampling the " +
                    "neighbour in the atlas");
            }

            Render("<Icon id='i' name='ui:red' anchor='center' size='64x64' glow='8'/>",
                   "pugui-fx-glow-bleed-red.png");
            for (var dx = 2f; dx <= 7f; dx += 1f)
            {
                var c = AtPx(-(TileEdgePx + dx), 0f);
                Assert.Less(c.g, 0.4f,
                    $"{dx}px left of the red tile the glow has picked up the white disc next door");
            }
        }

        [Test]
        public void GlowColor_paintsAFlatColour_AndTheDefaultFollowsTheTint()
        {
            Render(Icon("glow='8' glowColor='#ff0000'"), "pugui-fx-glow-red.png");
            var flat = AtPx(DiscRadiusPx + 2f, 0f);
            Assert.Greater(flat.r, flat.g + 0.15f, "an explicit glowColor paints in that colour");

            Render(Icon("glow='8' color='#00ff00'"), "pugui-fx-glow-self-green.png");
            var self = AtPx(DiscRadiusPx + 2f, 0f);
            Assert.Greater(self.g, self.r + 0.15f,
                "with no glowColor the glow takes the icon's own (tinted) colour");
        }

        [Test]
        public void Disabled_GreysTheGlowAsWellAsTheBody()
        {
            var s = Render("<Icon id='i' name='ui:red' anchor='center' size='64x64' glow='8'/>",
                           "pugui-fx-glow-colour-before.png");
            var lit = AtPx(TileEdgePx - 4f, 0f);
            Assert.Greater(lit.r, lit.g + 0.15f, "前置：red icon glows red");

            var fx = (FxImage)s.Get<PromptUGUI.Controls.Icon>("i").GameObject.GetComponent<UnityImage>();
            ((ISelfGrayscale)fx).SetDisabledGrayscale(true);
            Canvas.ForceUpdateCanvases();
            _ui.Render();
            RenderTexture.active = _uiRt;
            _shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            _shot.Apply();
            RenderTexture.active = null;

            var greyed = AtPx(TileEdgePx - 4f, 0f);
            Assert.Less(Mathf.Abs(greyed.r - greyed.g), 0.06f, "the glow greys with the body");
            Assert.Less(Mathf.Abs(greyed.g - greyed.b), 0.06f);
        }

        // ---- 3. the blur ----

        [Test]
        public void Blur_SoftensTheEdge_AndAWiderRadiusSoftensItFurther()
        {
            // Probed at half the radius either side of the silhouette: the transition a blur of R
            // produces is R wide, so a fixed probe distance would only be testing whichever radius
            // it happened to suit. (The sprite is 32 texels drawn at 64px, so R=8 is four texels.)
            const float R = 8f;
            const float Half = R * 0.5f;

            Render(Icon(""), "pugui-fx-blur-none.png");
            var sharpOutside = Luma(AtPx(DiscRadiusPx + Half, 0f));
            var sharpInside = Luma(AtPx(DiscRadiusPx - Half, 0f));
            Assert.Less(sharpOutside, 0.05f, "前置：a sharp edge is fully out just past it");
            Assert.Greater(sharpInside, 0.9f, "前置：and fully in just inside it");

            Render(Icon($"blur='{R}'"), "pugui-fx-blur-8.png");
            var blurredOutside = Luma(AtPx(DiscRadiusPx + Half, 0f));
            var blurredInside = Luma(AtPx(DiscRadiusPx - Half, 0f));
            Assert.Greater(blurredOutside, sharpOutside + 0.05f, "the edge spills outward");
            Assert.Less(blurredInside, sharpInside - 0.05f, "and the inside gives way to it");

            Render(Icon($"blur='{R * 0.5f}'"), "pugui-fx-blur-4.png");
            Assert.Less(Luma(AtPx(DiscRadiusPx + Half, 0f)), blurredOutside,
                "half the radius reaches half as far: the same pixel is dimmer");
        }

        [Test]
        public void Blur_HasNoDarkFringe()
        {
            // Blurring straight (non-premultiplied) colour averages the transparent texels' RGB —
            // black here — into the visible ones, which reads as a grey halo the moment the icon is
            // over anything light. On white, a correct blur stays white everywhere it covers.
            Render("<Image anchor='stretch' color='#ffffff'/>" + Icon("blur='6'"),
                   "pugui-fx-blur-fringe.png");

            for (var dx = -2f; dx <= 2f; dx += 1f)
            {
                var c = AtPx(DiscRadiusPx + dx, 0f);
                Assert.Greater(Luma(c), 0.9f,
                    $"at the blurred edge ({dx:+0.#;-0.#;0}px) the picture went grey — the blur is " +
                    "averaging transparent black into the colour instead of premultiplying");
            }
        }

        // ---- 3b. the mip path (spec §14) ----

        private const string LineIcon = "<Icon id='i' name='ui:line' anchor='center' size='64x64' ";

        [Test]
        public void Blur_OnAMipmappedSprite_DrawsOneSoftLine_NotTwentyFiveCopies()
        {
            // One texel wide, blurred by eight: at lod 0 the 25 taps sit ~2.8 texels apart and each
            // one sees its own copy of the line, so the cross-section is a comb. With the mip chain the
            // taps sample a level whose footprint covers that spacing and the comb collapses into one
            // hump: from the peak outward the profile only ever falls.
            Render(LineIcon + "blur='8'/>", "pugui-fx-mip-line-blur-8.png");

            const int Reach = 16;
            var profile = new float[Reach * 2 + 1];
            var peak = 0;
            for (var i = 0; i < profile.Length; i++)
            {
                profile[i] = Luma(AtPx(i - Reach, 0f));
                if (profile[i] > profile[peak]) peak = i;
            }
            Assert.Greater(profile[peak], 0.05f, "前置：the blurred line is visible at all");

            // The lod-0 kernel re-rises by 17/255 or more here (simulated); one soft hump wobbles by
            // under 1/255. 4/255 leaves room for the screenshot's own quantisation on both sides.
            const float Tolerance = 4f / 255f;
            foreach (var dir in new[] { 1, -1 })
            {
                var lowest = profile[peak];
                for (var i = peak + dir; i >= 0 && i < profile.Length; i += dir)
                {
                    Assert.LessOrEqual(profile[i], lowest + Tolerance,
                        $"{i - Reach:+0;-0}px from the line the profile climbs again after falling — the " +
                        "taps are drawing separate copies of the stroke instead of one blur");
                    lowest = Mathf.Min(lowest, profile[i]);
                }
            }
        }

        [Test]
        public void Blur_OnAMipmappedSprite_StillNeverSamplesTheNeighbour()
        {
            // Sixteen texels is lod 2.5: a tap's bilinear read at that level spans nearly six texels,
            // more than the four of padding, so a tap sitting on the sprite's edge would read the red
            // tile through the mip chain. The kernel pulls its taps in from the edge for exactly this.
            Render(LineIcon + "blur='16'/>", "pugui-fx-mip-bleed.png");

            for (var dx = 2f; dx <= 7f; dx += 1f)
            {
                var c = AtPx(TileEdgePx + dx, 0f);
                Assert.LessOrEqual(c.r, Mathf.Max(c.g, c.b) + 0.05f,
                    $"{dx}px past the sprite's rectangle the blur has gone red — a coarse mip level is " +
                    "reading the neighbour across the padding");
            }
        }

        // ---- 4. living with the rest of the library ----

        [Test]
        public void LinearTint_MatchesTheShaderItReplaced()
        {
            // tint="linear" moved from its own material into the fx shader; the pixels must not have
            // moved with it. Two identical icons, one on each path.
            var s = Render(
                "<Icon id='a' name='ui:disc' anchor='center-left' size='64x64' color='#c0c0c0' tint='linear'/>" +
                "<Icon id='b' name='ui:disc' anchor='center-right' size='64x64' color='#c0c0c0'/>",
                "pugui-fx-linear-parity.png");

            var legacy = Resources.Load<Material>("PromptUGUI/Material/UI-LinearLightTint");
            Assert.IsNotNull(legacy, "the material this test compares against must still exist");
            var b = s.Get<PromptUGUI.Controls.Icon>("b").GameObject.GetComponent<UnityImage>();
            b.material = legacy;

            Canvas.ForceUpdateCanvases();
            _ui.Render();
            RenderTexture.active = _uiRt;
            _shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            _shot.Apply();
            RenderTexture.active = null;

            var fx = AtPx(-HostW * 0.5f + IconSize * 0.5f, 0f);
            var old = AtPx(HostW * 0.5f - IconSize * 0.5f, 0f);
            Assert.AreEqual(old.r, fx.r, 0.01f, "r");
            Assert.AreEqual(old.g, fx.g, 0.01f, "g");
            Assert.AreEqual(old.b, fx.b, 0.01f, "b");
        }

        [Test]
        public void AnAncestorRectMask_ClipsTheGlow()
        {
            Render("<Frame anchor='center' width='40' height='40' mask='rect'>" +
                   Icon("glow='8'") + "</Frame>",
                   "pugui-fx-glow-masked.png");

            Assert.Greater(Luma(AtPx(0f, 0f)), 0.9f, "前置：the disc still draws inside the mask");
            Assert.Less(Luma(AtPx(24f, 0f)), 0.05f,
                "the glow reaches past the masking frame and must be clipped there");
        }
    }
}
