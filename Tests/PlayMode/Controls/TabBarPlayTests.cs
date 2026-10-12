using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class TabBarPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator TabBar_Runtime_Switching_Mutex_And_Bind()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' bind='fa' isOn='true'/>
    <Tab id='b' bind='fb'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            yield return null;

            var a = screen.Get<Tab>("a");
            var b = screen.Get<Tab>("b");
            var fa = screen.Get<Frame>("fa");
            var fb = screen.Get<Frame>("fb");

            Assert.IsTrue(a.IsOn);
            Assert.IsFalse(b.IsOn);
            Assert.IsTrue(fa.GameObject.activeSelf);
            Assert.IsFalse(fb.GameObject.activeSelf);

            b.IsOn = true;
            yield return null;

            Assert.IsFalse(a.IsOn, "mutex demoted a");
            Assert.IsTrue(b.IsOn);
            Assert.IsFalse(fa.GameObject.activeSelf);
            Assert.IsTrue(fb.GameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator TabBar_Static_Template_Wrapper_Mutex_And_Bind()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='FileTab'>
    <Param name='isOn' default='false'/>
    <Param name='bind'/>
    <Frame><Tab id='tab' isOn='{{isOn}}' bind='{{bind}}'/></Frame>
  </Template>
  <Screen name='S'>
    <TabBar id='bar'>
      <FileTab isOn='true' bind='fa'/>
      <FileTab isOn='false' bind='fb'/>
    </TabBar>
    <Frame id='fa'/>
    <Frame id='fb'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            yield return null;
            var bar = screen.Get<TabBar>("bar");
            var fa = screen.Get<Frame>("fa");
            var fb = screen.Get<Frame>("fb");
            Assert.AreEqual(2, bar.Count);
            Assert.IsTrue(bar.GetAt(0).IsOn);
            Assert.IsTrue(fa.GameObject.activeSelf);
            Assert.IsFalse(fb.GameObject.activeSelf);
            bar.GetAt(1).IsOn = true;
            yield return null;
            Assert.IsFalse(fa.GameObject.activeSelf);
            Assert.IsTrue(fb.GameObject.activeSelf);
        }

        // Regression (user report — play mode only): <TabBar direction="horizontal"> crammed every Tab at
        // the origin and logged "Can't add 'HorizontalLayoutGroup' ... already added". Applying the explicit
        // direction during Open re-ran ApplyDirection, which destroyed+recreated the group; Object.Destroy is
        // deferred to end-of-frame in play mode, so the same-frame AddComponent collided with the not-yet-
        // removed group ([DisallowMultipleComponent]) — the add failed and the deferred destroy then left the
        // bar with NO layout group. EditMode could never catch this (DestroyImmediate is synchronous). After a
        // frame exactly one HLG must remain. (An unexpected error log would also fail this UnityTest.)
        [UnityTest]
        public IEnumerator TabBar_Explicit_Direction_KeepsSingleLayoutGroup_AfterFrame()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' direction='horizontal'><Tab id='a'/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            yield return null;   // a frame passes: any deferred Object.Destroy would fire here

            var go = bar.GameObject;
            Assert.AreEqual(1, go.GetComponents<LayoutGroup>().Length,
                "exactly one LayoutGroup survives (deferred destroy + same-frame add must not strand the bar)");
            Assert.IsNotNull(go.GetComponent<HorizontalLayoutGroup>(), "and it is the horizontal group");
        }

        // The portrait-variant flip swaps H<->V at runtime; in play mode the old deferred Destroy collided on
        // the re-add the same way. Each swap must leave exactly one correct group, no leftover, no error.
        [UnityTest]
        public IEnumerator TabBar_Direction_Swap_PlayMode_KeepsSingleLayoutGroup()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' direction='horizontal'><Tab id='a'/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            var go = bar.GameObject;
            yield return null;

            bar.Direction = "vertical";
            yield return null;   // deferred destroy of the old HLG would fire here under the old code
            Assert.AreEqual(1, go.GetComponents<LayoutGroup>().Length, "one group after H->V swap");
            Assert.IsNotNull(go.GetComponent<VerticalLayoutGroup>(), "vertical group present");
            Assert.IsNull(go.GetComponent<HorizontalLayoutGroup>(), "no leftover horizontal group");

            bar.Direction = "horizontal";
            yield return null;
            Assert.AreEqual(1, go.GetComponents<LayoutGroup>().Length, "one group after V->H swap");
            Assert.IsNotNull(go.GetComponent<HorizontalLayoutGroup>(), "horizontal group present");
            Assert.IsNull(go.GetComponent<VerticalLayoutGroup>(), "no leftover vertical group");
        }

        // ───── allowSwitchOff holds from the first tab on (play mode only: EditMode never runs the ToggleGroup) ─────

        // A bar's own attributes are applied after its children's (DFS post-order). While the tabs applied
        // isOn, the group still forbade switch-off — so uGUI could force a tab on, and a declared
        // isOn="false" bounced back to true and was recorded as the declared baseline. The first ReSolve
        // after the player opened that tab then read "unchanged since the last pass" and replayed
        // isOn="false": the page closed on the first rotation.
        private static IEnumerator AssertOpenedTabSurvivesResolve(string xml, string tabBarId, int index)
        {
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            yield return null;
            yield return null;   // ToggleGroup.Start has run
            var group = screen.Get<IControl>(tabBarId);
            Tab Tab(int i) => group is TabBar b ? b.GetAt(i) : ((TabMenu)group).GetAt(i);
            int Selected() => group is TabBar b ? b.SelectedIndex : ((TabMenu)group).SelectedIndex;

            Assert.AreEqual(-1, Selected(), "allowSwitchOff: nothing is selected at open");
            Assert.AreEqual("false", Tab(index)._lastAppliedRuntimeState,
                "the declared isOn=\"false\" is what was recorded, not a value uGUI forced");

            Tab(index).IsOn = true;   // the player opens it
            yield return null;
            screen.ReSolve();         // a rotation / theme switch / resize
            yield return null;

            Assert.IsTrue(Tab(index).IsOn, "the page the player opened stays open across a ReSolve");
            Assert.AreEqual(index, Selected());
        }

        [UnityTest]
        public IEnumerator AllowSwitchOff_first_static_tab_opened_by_the_player_survives_resolve()
        {
            return AssertOpenedTabSurvivesResolve(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' allowSwitchOff='true'>
    <Tab id='a' isOn='false' bind='fa'/>
    <Tab id='b' isOn='false' bind='fb'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
</Screen></PromptUGUI>", "bar", 0);
        }

        [UnityTest]
        public IEnumerator AllowSwitchOff_first_static_wrapper_row_opened_by_the_player_survives_resolve()
        {
            return AssertOpenedTabSurvivesResolve(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Param name='isOn' default='false'/>
    <VStack><Tab id='tab' height='40' isOn='{{isOn}}'/><Frame height='10'/></VStack>
  </Template>
  <Screen name='S'>
    <TabBar id='bar' direction='vertical' allowSwitchOff='true'><Row/><Row/><Row/></TabBar>
  </Screen>
</PromptUGUI>", "bar", 0);
        }

        [UnityTest]
        public IEnumerator AllowSwitchOff_TabMenu_first_row_opened_by_the_player_survives_resolve()
        {
            return AssertOpenedTabSurvivesResolve(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabMenu id='m' allowSwitchOff='true' transition='0'>
    <Tab id='a' isOn='false'>A</Tab>
    <Tab id='b' isOn='false'>B</Tab>
  </TabMenu>
</Screen></PromptUGUI>", "m", 0);
        }
    }
}
