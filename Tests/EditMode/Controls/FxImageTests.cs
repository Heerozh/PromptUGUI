using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>blur</c> / <c>glow</c> / <c>glowColor</c> as authored attributes (spec 2026-09-02 §3–§5).
    /// The vertex maths is covered by <see cref="FxMeshTests"/> and the sharing by
    /// <see cref="FxMaterialCacheTests"/>; this is about the attributes landing, the layout staying
    /// untouched, Variant round-trips, and — the point of the whole design — an icon that asks for
    /// nothing costing nothing.
    /// </summary>
    public class FxImageTests
    {
        private Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _sprite = MakeSprite();
            UI.SpriteResolver = _ => _sprite;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_sprite != null)
            {
                var tex = _sprite.texture;
                Object.DestroyImmediate(_sprite);
                if (tex != null) Object.DestroyImmediate(tex);
                _sprite = null;
            }
        }

        private static Sprite MakeSprite(Vector4 border = default)
        {
            var tex = new Texture2D(8, 8);
            return Sprite.Create(tex, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f),
                                 pixelsPerUnit: 1f, extrude: 0, meshType: SpriteMeshType.FullRect,
                                 border: border);
        }

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        private static FxImage FxOf(PromptUGUI.Controls.IControl c)
            => c.GameObject.GetComponent<UnityImage>() as FxImage;

        // ---- the zero-cost baseline ----

        [Test]
        public void Icon_and_Image_are_built_on_FxImage()
        {
            var s = Open("<Icon id='i' name='ui:x'/><Image id='m' sprite='ui:x' size='40x40'/>");

            Assert.IsNotNull(FxOf(s.Get<PromptUGUI.Controls.Icon>("i")));
            Assert.IsNotNull(FxOf(s.Get<PromptUGUI.Controls.Image>("m")));
        }

        [Test]
        public void Without_fx_there_is_no_material_and_no_canvas_channel()
        {
            var s = Open("<Icon id='i' name='ui:x'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));

            Assert.AreEqual(fx.defaultMaterial, fx.material, "an untouched icon still draws with UI/Default");
            Assert.IsFalse(fx.HasKeyForTests);
            Assert.AreEqual(0, FxMaterialCache.LiveMaterialCount);

            var canvas = fx.GetComponentInParent<Canvas>();
            Assert.AreEqual((AdditionalCanvasShaderChannels)0,
                            canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord2,
                            "no fx anywhere on the canvas means no extra vertex payload");
        }

        [Test]
        public void Without_fx_the_mesh_is_exactly_the_sprite_quad()
        {
            var s = Open("<Icon id='i' name='ui:x' size='40x40'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));

            using var vh = new VertexHelper();
            fx.BuildMeshForTests(vh);

            var b = Bounds(vh);
            Assert.AreEqual(40f, b.width, 0.01f);
            Assert.AreEqual(40f, b.height, 0.01f);
            Assert.AreEqual(Vector4.zero, Vert(vh, 0).uv1, "the fx channels are not even written");
        }

        // ---- attributes landing ----

        [Test]
        public void Glow_lands_and_two_icons_share_one_material()
        {
            var s = Open("<Icon id='a' name='ui:x' glow='6'/><Icon id='b' name='ui:x' glow='6'/>");
            var a = FxOf(s.Get<PromptUGUI.Controls.Icon>("a"));
            var b = FxOf(s.Get<PromptUGUI.Controls.Icon>("b"));

            Assert.AreEqual("UI/ImageFx", a.material.shader.name);
            Assert.AreSame(a.material, b.material, "same parameters must batch, not split the cache");
            Assert.AreEqual(1, FxMaterialCache.LiveMaterialCount);
            Assert.AreEqual(6f, a.material.GetFloat("_Glow"), 1e-4f);
            Assert.AreEqual(1f, a.material.GetFloat("_GlowSelf"), 1e-4f, "no glowColor written");

            var canvas = a.GetComponentInParent<Canvas>();
            var needed = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            Assert.AreEqual(needed, canvas.additionalShaderChannels & needed);
        }

        [Test]
        public void GlowColor_switches_the_glow_to_a_flat_colour_and_back()
        {
            var s = Open("<Icon id='i' name='ui:x' glow='6' glowColor='#ff0000'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));

            Assert.AreEqual(0f, fx.material.GetFloat("_GlowSelf"), 1e-4f);
            Assert.AreEqual(Color.red, fx.material.GetColor("_GlowColor"));

            fx.ClearGlowColor();
            fx.FlushParams();
            Assert.AreEqual(1f, fx.material.GetFloat("_GlowSelf"), 1e-4f);
        }

        [Test]
        public void Blur_and_glow_inflate_the_mesh_by_the_larger_radius()
        {
            var s = Open("<Image id='m' sprite='ui:x' type='simple' size='40x40' blur='3' glow='8'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Image>("m"));

            using var vh = new VertexHelper();
            fx.BuildMeshForTests(vh);

            var b = Bounds(vh);
            Assert.AreEqual(56f, b.width, 0.01f, "40 + 8 on each side");
            Assert.AreEqual(56f, b.height, 0.01f);

            var v = Vert(vh, 0);
            Assert.AreEqual(new Vector4(0f, 0f, 1f, 1f), v.uv1, "the whole (unpacked) texture is the rect");
            Assert.AreEqual(1f / 40f, v.uv2.x, 1e-5f, "uv per canvas unit");
        }

        // ---- what fx must NOT touch ----

        [Test]
        public void Layout_and_native_size_are_untouched_by_a_glow()
        {
            var s = Open("<Icon id='i' name='ui:x' glow='8' size='32x32'/>");
            var icon = s.Get<PromptUGUI.Controls.Icon>("i");

            Assert.AreEqual(new Vector2(32f, 32f), icon.RectTransform.rect.size);
            Assert.AreEqual(new Vector2(8f, 8f), icon.GetNativeSize());
        }

        [Test]
        public void A_glow_adds_no_mesh_effect_components()
        {
            var s = Open("<Icon id='i' name='ui:x' glow='8' blur='4'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));

            Assert.AreEqual(0, fx.GetComponents<BaseMeshEffect>().Length,
                "the quad is inflated inside OnPopulateMesh, ahead of every IMeshModifier");
        }

        // ---- reversibility ----

        [Test]
        public void A_variant_that_retracts_the_glow_returns_the_default_material()
        {
            var s = Open("<Icon id='i' name='ui:x' glow='6' glow.portrait=''/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));
            Assert.IsTrue(fx.HasKeyForTests);

            UI.Variants.Set("portrait", true);
            Assert.IsFalse(fx.HasKeyForTests);
            Assert.AreEqual(fx.defaultMaterial, fx.material);
            Assert.AreEqual(0, FxMaterialCache.LiveMaterialCount, "the material went back to the pool");

            UI.Variants.Set("portrait", false);
            Assert.IsTrue(fx.HasKeyForTests);
            Assert.AreEqual(6f, fx.material.GetFloat("_Glow"), 1e-4f);
        }

        [Test]
        public void Tweening_the_glow_does_not_leak_materials()
        {
            var s = Open("<Icon id='i' name='ui:x' glow='1'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));

            for (var i = 1; i <= 100; i++)
            {
                fx.Glow = i * 0.1f;
                fx.FlushParams();
            }

            Assert.AreEqual(1, FxMaterialCache.LiveMaterialCount);
            Assert.LessOrEqual(FxMaterialCache.SpareCount, 1);
        }

        // ---- the cases fx cannot serve ----

        [Test]
        public void A_nine_slice_sprite_keeps_its_borders_and_says_so_once()
        {
            Object.DestroyImmediate(_sprite);
            _sprite = MakeSprite(new Vector4(2f, 2f, 2f, 2f));
            UI.SpriteResolver = _ => _sprite;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("9-slice"));
            var s = Open("<Image id='m' sprite='ui:x' size='40x40' glow='6'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Image>("m"));

            Assert.AreEqual(UnityImage.Type.Sliced, fx.type);
            Assert.IsFalse(fx.HasGeometryFx);
            Assert.AreEqual(fx.defaultMaterial, fx.material,
                "a radius that cannot be drawn must not leave a material behind either");
        }

        [Test]
        public void The_same_sprite_forced_to_simple_does_get_the_glow()
        {
            Object.DestroyImmediate(_sprite);
            _sprite = MakeSprite(new Vector4(2f, 2f, 2f, 2f));
            UI.SpriteResolver = _ => _sprite;

            var s = Open("<Image id='m' sprite='ui:x' type='simple' size='40x40' glow='6'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Image>("m"));

            Assert.IsTrue(fx.HasGeometryFx);
            Assert.AreEqual("UI/ImageFx", fx.material.shader.name);
        }

        // ---- a live canvas ----

        [Test]
        public void A_glowing_icon_survives_a_canvas_rebuild()
        {
            var s = Open("<Icon id='i' name='ui:x' size='32x32' glow='6' blur='2'/>");
            var fx = FxOf(s.Get<PromptUGUI.Controls.Icon>("i"));

            Assert.DoesNotThrow(Canvas.ForceUpdateCanvases);

            Assert.AreEqual(1, fx.canvasRenderer.materialCount);
            Assert.AreEqual("UI/ImageFx", fx.canvasRenderer.GetMaterial(0).shader.name);
        }

        // ---- helpers ----

        private static UIVertex Vert(VertexHelper vh, int i)
        {
            var v = new UIVertex();
            vh.PopulateUIVertex(ref v, i);
            return v;
        }

        private static Rect Bounds(VertexHelper vh)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                var p = Vert(vh, i).position;
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }
}
