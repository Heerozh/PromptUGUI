using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Controls
{
    public class SafeAreaTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown]
        public void TearDown()
        {
            SafeAreaTracker.SafeAreaOverride = null;
            SafeAreaTracker.ScreenSizeOverride = null;
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator SafeArea_offsets_settle_after_one_frame()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            yield return null;

            var rt = sa.RectTransform;
            // v2: anchor always stretch, offsets carry the per-edge inset.
            // safe (0, 100, 1080, 1820), screen (1080, 1920), default sf=1:
            //   insetB = 100, others 0.
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(0f, rt.offsetMin.x, 0.001f);
            Assert.AreEqual(100f, rt.offsetMin.y, 0.001f);
            Assert.AreEqual(0f, rt.offsetMax.x, 0.001f);
            Assert.AreEqual(0f, rt.offsetMax.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator Tracker_polls_provider_changes()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            var rt = sa.RectTransform;
            yield return null;

            Assert.AreEqual(100f, rt.offsetMin.y, 0.001f,
                "initial bottom inset = 100");

            // Notch switches sides: gesture bar lives at the top now.
            //   safe (0, 0, 1080, 1830) → device top inset = 1920-1830 = 90
            //   bottom inset = 0
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 0f, 1080f, 1830f);
            yield return null;

            Assert.AreEqual(0f, rt.offsetMin.y, 0.001f, "new bottom inset = 0");
            Assert.AreEqual(-90f, rt.offsetMax.y, 0.001f, "new top inset = 90 → offsetMax.y = -90");
        }

        [UnityTest]
        public IEnumerator SafeArea_inside_variant_add_block_works_after_toggle()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Variant when='mobile'>
    <Add into='@root'>
      <SafeArea id='sa'/>
    </Add>
  </Variant>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            screen.Variants.Set("mobile", true);
            yield return null;

            var sa = screen.Get<SafeArea>("sa");
            Assert.IsNotNull(sa);
            Assert.AreEqual(100f, sa.RectTransform.offsetMin.y, 0.001f);

            screen.Variants.Set("mobile", false);
            yield return null;
            Assert.IsFalse(sa.GameObject.activeSelf, "Add block goes inactive");

            screen.Variants.Set("mobile", true);
            yield return null;
            Assert.IsTrue(sa.GameObject.activeSelf);
            Assert.AreEqual(100f, sa.RectTransform.offsetMin.y, 0.001f,
                "tracker re-applies after reactivation via OnEnable + OnAfterApply");
        }

        [UnityTest]
        public IEnumerator SafeArea_with_margin_absorbs_inset_end_to_end()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa' margin='6,6,6,6'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            yield return null;

            var rt = sa.RectTransform;
            //   left  = max(6, 0)   = 6
            //   right = max(6, 0)   = 6 (encoded as -6 in offsetMax.x)
            //   bottom= max(6, 100) = 100 (inset absorbs)
            //   top   = max(6, 0)   = 6 (encoded as -6 in offsetMax.y)
            Assert.AreEqual(6f, rt.offsetMin.x, 0.001f);
            Assert.AreEqual(100f, rt.offsetMin.y, 0.001f);
            Assert.AreEqual(-6f, rt.offsetMax.x, 0.001f);
            Assert.AreEqual(-6f, rt.offsetMax.y, 0.001f);
        }
    }
}
