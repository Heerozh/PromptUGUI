using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// blur / glow on a live canvas: the EditMode tests never run uGUI's rebuild loop, where the
    /// material is actually pushed to the CanvasRenderer and where a mistake in the
    /// dirty-flag / rebuild-reentrancy dance would show.
    /// </summary>
    public class ImageFxPlayTests
    {
        private Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _sprite = Sprite.Create(new Texture2D(8, 8), new Rect(0f, 0f, 8f, 8f), new Vector2(.5f, .5f));
            UI.SpriteResolver = _ => _sprite;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_sprite != null)
            {
                var tex = _sprite.texture;
                Object.Destroy(_sprite);
                if (tex != null) Object.Destroy(tex);
                _sprite = null;
            }
        }

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                body + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        [UnityTest]
        public IEnumerator A_glowing_icon_reaches_the_canvas_renderer_and_can_give_it_back()
        {
            var s = Open("<Icon id='i' name='ui:x' size='32x32' glow='6' blur='2'/>");
            yield return null;

            var fx = (FxImage)s.Get<Icon>("i").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/ImageFx", fx.canvasRenderer.GetMaterial(0).shader.name);
            Assert.AreEqual(1, FxMaterialCache.LiveMaterialCount);

            // Retracting both radii (what a Variant or a tween back to zero does) must hand the
            // material back, not leave a no-op fx shader on every icon that ever glowed.
            fx.Glow = 0f;
            fx.Blur = 0f;
            yield return null;

            Assert.AreEqual(fx.defaultMaterial, fx.material);
            Assert.AreEqual(0, FxMaterialCache.LiveMaterialCount);
        }

        [UnityTest]
        public IEnumerator A_disabled_control_greys_its_glowing_icon_without_losing_the_glow()
        {
            var s = Open("<Btn id='b' interactable='false'><Icon id='i' name='ui:x' size='16x16' glow='6'/></Btn>");
            yield return null;

            var fx = (FxImage)s.Get<Btn>("b").GameObject.transform.Find("i")
                               .GetComponent<UnityImage>();
            Assert.AreEqual("UI/ImageFx", fx.material.shader.name);
            Assert.AreEqual(1f, fx.material.GetFloat("_Desaturate"), 1e-4f);
            Assert.AreEqual(6f, fx.material.GetFloat("_Glow"), 1e-4f);

            s.Get<Btn>("b").Interactable = true;
            yield return null;

            Assert.AreEqual(0f, fx.material.GetFloat("_Desaturate"), 1e-4f);
            Assert.AreEqual(6f, fx.material.GetFloat("_Glow"), 1e-4f);
        }
    }
}
