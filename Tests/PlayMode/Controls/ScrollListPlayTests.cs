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

        // ───── ScrollIntoView follows a row while it unfolds ─────

        // A tab row that unfolds a 60px detail line while selected: 40 tall at rest, 100 once open.
        private const string UnfoldingRow =
            "<Template name='Row'><VStack id='row'>"
            + "<Tab id='tab' height='40'/>"
            + "<Animation id='more' on='checked@tab' reverse-on='unchecked@tab' reveal='y' duration='0.2s'>"
            + "<Frame height='60'/></Animation>"
            + "</VStack></Template>";

        private static IEnumerator SelectLastRowAndScrollIntoView(string listHeight, int rows)
        {
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{UnfoldingRow}<Screen name='S'>
  <ScrollList id='sl' anchor='top-left' width='200' height='{listHeight}'>
    <Scrollbar thickness='0' overlay='true'/>
    <TabBar id='bar' direction='vertical' allowSwitchOff='true' itemTemplate='Row'/>
  </ScrollList>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("sl");
            var bar = screen.Get<TabBar>("bar");
            var items = new string[rows];
            for (var i = 0; i < rows; i++) items[i] = "r" + i;
            using var sub = bar.BindItems<string, IControl, string>(
                R3.Observable.Return<System.Collections.Generic.IReadOnlyList<string>>(items), (_, __) => { }, key: s => s);
            yield return null;
            yield return null;

            // What a host does when a pick elsewhere selects a row: select it, bring it into view — same frame,
            // before the unfold has started.
            var last = bar.GetAt(rows - 1);
            last.IsOn = true;
            var row = ((Control)last).Parent;
            list.ScrollIntoView(row);

            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);

            var rowRect = row.RectTransform;
            Assert.AreEqual(100f, rowRect.rect.height, 0.5f, "guard: the row did unfold");
            var t = new UnityEngine.Vector3[4];
            var v = new UnityEngine.Vector3[4];
            rowRect.GetWorldCorners(t);
            ((UnityEngine.RectTransform)list.GameObject.transform.Find("Viewport")).GetWorldCorners(v);
            Assert.GreaterOrEqual(t[0].y, v[0].y - 0.5f, "the unfolded row's bottom edge is inside the viewport");
            Assert.LessOrEqual(t[1].y, v[1].y + 0.5f, "…and so is its top edge");
        }

        [UnityTest]
        public IEnumerator ScrollIntoView_keeps_an_unfolding_row_whole_in_a_fixed_list()
            => SelectLastRowAndScrollIntoView("120", rows: 6);

        [UnityTest]
        public IEnumerator ScrollIntoView_keeps_an_unfolding_row_whole_in_a_list_hugging_up_to_a_cap()
            => SelectLastRowAndScrollIntoView("clamp(_, hug, 149)", rows: 3);
    }
}
