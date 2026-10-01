using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode
{
    /// <summary>
    /// An on-demand runtime sprite set under real frame scheduling: the loader completes on a later frame,
    /// and the icon refreshes itself then (spec 2026-10-01-runtime-sprite-sets-design §7.3). The EditMode
    /// tests complete their loaders inside SetResult; this one does not.
    /// </summary>
    public class RuntimeSpriteSetPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static async Awaitable<RuntimeSprite> LoadOverTwoFrames(Sprite sprite)
        {
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();
            return sprite;
        }

        [UnityTest]
        public IEnumerator Ondemand_icon_arrives_after_real_frames()
        {
            var a = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.RegisterRuntimeSpriteSet("ugc", _ => LoadOverTwoFrames(a));
            UI.LoadDocument("inline",
                "<?xml version='1.0'?><PromptUGUI version='1'><Screen name='S'>" +
                "<Icon id='i' name='ugc:a' size='32x32'/></Screen></PromptUGUI>");
            var img = UI.Open("S").Get("i").GameObject.GetComponent<UnityImage>();
            Assert.IsNull(img.sprite, "still loading");

            for (var i = 0; i < 10 && img.sprite != a; i++) yield return null;

            Assert.AreSame(a, img.sprite);
        }
    }
}
