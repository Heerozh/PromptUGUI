using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class ScrollListPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Same play-mode-only bug as TabBar (see TabBarPlayTests), in ScrollList.ApplyDirection — the
        // LayoutGroup lives on the Content child. An explicit direction= re-runs ApplyDirection during Open;
        // in play mode Object.Destroy is deferred to end-of-frame, so the same-frame AddComponent collides
        // with the not-yet-removed group ([DisallowMultipleComponent]); the add fails and the deferred destroy
        // then leaves Content with NO layout group (items collapse). EditMode can't catch it (DestroyImmediate
        // is synchronous). After a frame exactly one LayoutGroup must remain. (An unexpected error log would
        // also fail this UnityTest.)
        [UnityTest]
        public IEnumerator ScrollList_Explicit_Direction_KeepsSingleLayoutGroup_AfterFrame()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot' direction='vertical'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            yield return null;   // a frame passes: any deferred Object.Destroy would fire here

            var content = sl.GameObject.transform.Find("Viewport/Content");
            Assert.IsNotNull(content, "Content child exists");
            Assert.AreEqual(1, content.GetComponents<LayoutGroup>().Length,
                "exactly one LayoutGroup survives on Content (deferred destroy + same-frame add must not strand it)");
            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>(), "and it is the vertical group");
        }

        [UnityTest]
        public IEnumerator ScrollList_Direction_Swap_PlayMode_KeepsSingleLayoutGroup()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot' direction='vertical'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var sl = UI.Open("S").Get<ScrollList>("sl");
            var content = sl.GameObject.transform.Find("Viewport/Content");
            yield return null;

            sl.Direction = "horizontal";
            yield return null;   // deferred destroy of the old VLG would fire here under the old code
            Assert.AreEqual(1, content.GetComponents<LayoutGroup>().Length, "one group after V->H swap");
            Assert.IsNotNull(content.GetComponent<HorizontalLayoutGroup>(), "horizontal group present");
            Assert.IsNull(content.GetComponent<VerticalLayoutGroup>(), "no leftover vertical group");

            sl.Direction = "vertical";
            yield return null;
            Assert.AreEqual(1, content.GetComponents<LayoutGroup>().Length, "one group after H->V swap");
            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>(), "vertical group present");
            Assert.IsNull(content.GetComponent<HorizontalLayoutGroup>(), "no leftover horizontal group");
        }
    }
}
