using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabBarTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TabBar OpenBar(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<TabBar>("bar");
        }

        [Test]
        public void TabBar_Has_ToggleGroup_And_HorizontalLayoutGroup()
        {
            var bar = OpenBar("<TabBar id='bar'/>");
            Assert.IsNotNull(bar.GameObject.GetComponent<ToggleGroup>(), "ToggleGroup on self");
            Assert.IsNotNull(bar.GameObject.GetComponent<HorizontalLayoutGroup>(), "HLG default");
            Assert.IsFalse(bar.GameObject.GetComponent<ToggleGroup>().allowSwitchOff, "TB-D7 fixed false");
        }

        [Test]
        public void TabBar_Children_Share_ToggleGroup()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a'/>
    <Tab id='b'/>
  </TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var bar = screen.Get<TabBar>("bar");
            var a = screen.Get<Tab>("a").GameObject.GetComponent<UnityEngine.UI.Toggle>();
            var b = screen.Get<Tab>("b").GameObject.GetComponent<UnityEngine.UI.Toggle>();
            var group = bar.GameObject.GetComponent<UnityEngine.UI.ToggleGroup>();
            Assert.AreSame(group, a.group);
            Assert.AreSame(group, b.group);
        }

        [Test]
        public void Tab_Bind_Toggle_Switches_Frame_SetActive()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' bind='fa'/>
    <Tab id='b' bind='fb'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var fa = screen.Get<Frame>("fa");
            var fb = screen.Get<Frame>("fb");
            var a = screen.Get<Tab>("a");
            var b = screen.Get<Tab>("b");
            a.IsOn = true;
            Assert.IsTrue(fa.GameObject.activeSelf);
            b.IsOn = true;
            Assert.IsFalse(fa.GameObject.activeSelf);
            Assert.IsTrue(fb.GameObject.activeSelf);
        }

        [Test]
        public void Tab_Bind_To_Missing_Frame_Warns_Once_Then_Silent()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.bind='nope'.*did not resolve"));
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' bind='nope' isOn='true'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Tab>("a");
            a.IsOn = false;
            a.IsOn = true;
            // No further warn expected — LogAssert would fail if a 2nd warning fires.
        }

        [Test]
        public void TabBar_With_No_Initial_IsOn_Auto_Selects_First_Tab()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a'/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Tab>("a").IsOn);
            Assert.IsFalse(screen.Get<Tab>("b").IsOn);
        }

        // User report: a window resize (which runs Screen.ReSolve when scale="Nx"/"<r>r") snaps the
        // active tab back to the declared default — losing the user's selection and the page they
        // navigated to. The declared isOn is the INITIAL selection only; a runtime selection (and
        // its bound page) must survive a ReSolve.
        [Test]
        public void Tab_RuntimeSelection_Survives_ReSolve()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' isOn='true' bind='fa'/>
    <Tab id='b' bind='fb'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Tab>("a");
            var b = screen.Get<Tab>("b");
            var fa = screen.Get<Frame>("fa");
            var fb = screen.Get<Frame>("fb");

            b.IsOn = true;                   // user switches to tab b (a turns off via ToggleGroup)
            Assert.IsTrue(b.IsOn);
            Assert.IsFalse(a.IsOn);
            Assert.IsTrue(fb.GameObject.activeSelf, "page b shown after user switch");
            Assert.IsFalse(fa.GameObject.activeSelf);

            screen.ReSolve();                // window resize / scale recompute

            Assert.IsTrue(b.IsOn, "user's tab selection survives ReSolve");
            Assert.IsFalse(a.IsOn, "declared-default tab is NOT force-re-selected on ReSolve");
            Assert.IsTrue(fb.GameObject.activeSelf, "bound page b stays shown after ReSolve");
            Assert.IsFalse(fa.GameObject.activeSelf, "bound page a stays hidden after ReSolve");
        }

        [Test]
        public void TabBar_Direction_Vertical_Uses_VerticalLayoutGroup()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' direction='vertical'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            Assert.IsNull(bar.GameObject.GetComponent<HorizontalLayoutGroup>(), "HLG removed");
            Assert.IsNotNull(bar.GameObject.GetComponent<VerticalLayoutGroup>(), "VLG present");
        }

        // Regression (user report): adding direction="horizontal" crammed every Tab at the origin and
        // logged "Can't add 'HorizontalLayoutGroup' ... already added". Root cause: ApplyDirection always
        // destroyed+recreated the LayoutGroup, and Object.Destroy is DEFERRED to end-of-frame in play mode,
        // so the same-frame AddComponent collided with the not-yet-removed group ([DisallowMultipleComponent]);
        // the add failed and the deferred destroy then left the TabBar with NO layout group. Invisible in
        // EditMode (DestroyImmediate is synchronous), so we lock the mode-independent contract: re-applying
        // the SAME direction must reuse the existing group (a no-op on the component), never destroy+recreate.
        [Test]
        public void TabBar_ReapplySameDirection_ReusesLayoutGroup()
        {
            var bar = OpenBar("<TabBar id='bar' direction='horizontal'/>");
            var go = bar.GameObject;
            var first = go.GetComponent<HorizontalLayoutGroup>();
            Assert.IsNotNull(first, "HLG present after open");

            bar.Direction = "horizontal";   // mirrors ReSolve / explicit base re-apply of the unchanged value

            Assert.AreSame(first, go.GetComponent<HorizontalLayoutGroup>(),
                "same-direction re-apply must reuse the existing LayoutGroup (no destroy+recreate)");
            Assert.AreEqual(1, go.GetComponents<LayoutGroup>().Length, "exactly one LayoutGroup");
        }

        // Guard the genuine H<->V swap (the portrait-variant flip): exactly one correct group each way,
        // no orphaned/duplicate group. Exercises the DestroyImmediate-always swap path in both modes.
        [Test]
        public void TabBar_DirectionSwap_KeepsExactlyOneLayoutGroup()
        {
            var bar = OpenBar("<TabBar id='bar' direction='horizontal'/>");
            var go = bar.GameObject;

            bar.Direction = "vertical";
            Assert.IsNull(go.GetComponent<HorizontalLayoutGroup>(), "HLG gone after swap to vertical");
            Assert.IsNotNull(go.GetComponent<VerticalLayoutGroup>(), "VLG present after swap");
            Assert.AreEqual(1, go.GetComponents<LayoutGroup>().Length, "exactly one group after swap");

            bar.Direction = "horizontal";
            Assert.IsNull(go.GetComponent<VerticalLayoutGroup>(), "VLG gone after swap back");
            Assert.IsNotNull(go.GetComponent<HorizontalLayoutGroup>(), "HLG present after swap back");
            Assert.AreEqual(1, go.GetComponents<LayoutGroup>().Length, "exactly one group after swap back");
        }

        [Test]
        public void TabBar_Spacing_And_Padding_Apply_To_LayoutGroup()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' spacing='8' padding='4,6'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            var hlg = bar.GameObject.GetComponent<HorizontalLayoutGroup>();
            Assert.AreEqual(8f, hlg.spacing);
            // RectOffset is a reference type with no value-equality, assert fields individually.
            Assert.AreEqual(6, hlg.padding.left);
            Assert.AreEqual(6, hlg.padding.right);
            Assert.AreEqual(4, hlg.padding.top);
            Assert.AreEqual(4, hlg.padding.bottom);
        }

        [Test]
        public void TabBar_OnSelectionChanged_Fires_With_Newly_Selected_Tab()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' isOn='true'/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var bar = screen.Get<TabBar>("bar");
            Tab observed = null;
            using var sub = bar.OnSelectionChanged.Subscribe(t => observed = t);

            screen.Get<Tab>("b").IsOn = true;
            Assert.AreSame(screen.Get<Tab>("b"), observed);
        }

        [Test]
        public void TabBar_SelectedTab_And_SelectedIndex_Reflect_State()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a'/><Tab id='b' isOn='true'/><Tab id='c'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            Assert.AreEqual(1, bar.SelectedIndex);
            Assert.AreEqual("b", bar.SelectedTab.Id);
        }

        [Test]
        public void TabBar_With_Static_Template_Wrapper_Collects_Inner_Tab()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='FileTab'>
    <Param name='isOn' default='false'/>
    <Frame><Tab id='tab' isOn='{{isOn}}'/></Frame>
  </Template>
  <Screen name='S'>
    <TabBar id='bar'>
      <FileTab isOn='true'/>
      <FileTab isOn='false'/>
    </TabBar>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            Assert.AreEqual(2, bar.Count, "TabBar collected 2 inner Tabs via FindTabIn");
            Assert.AreEqual(0, bar.SelectedIndex, "first Tab IsOn");
        }

        [Test]
        public void TabBar_With_Template_Carrying_Sprite_Applies_To_Every_Instance()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:tab_bg" ? stub : null;
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='StyledTab'>
    <Param name='text' default=''/>
    <Tab id='tab' sprite='ui:tab_bg' text='{{text}}'/>
  </Template>
  <Screen name='S'>
    <TabBar id='bar'>
      <StyledTab text='A'/>
      <StyledTab text='B'/>
    </TabBar>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            var bgA = bar.GetAt(0).GameObject.GetComponent<UnityEngine.UI.Image>();
            var bgB = bar.GetAt(1).GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.AreSame(stub, bgA.sprite, "Tab[0] bg sprite from Template");
            Assert.AreSame(stub, bgB.sprite, "Tab[1] bg sprite from Template");
        }

        [Test]
        public void TabBar_With_Static_Template_Wrapper_Fires_OnSelectionChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='FileTab'>
    <Param name='isOn' default='false'/>
    <Frame><Tab id='tab' isOn='{{isOn}}'/></Frame>
  </Template>
  <Screen name='S'>
    <TabBar id='bar'>
      <FileTab isOn='true'/>
      <FileTab isOn='false'/>
    </TabBar>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            Tab observed = null;
            using var sub = bar.OnSelectionChanged.Subscribe(t => observed = t);
            bar.GetAt(1).IsOn = true;
            Assert.AreSame(bar.GetAt(1), observed);
        }

        [Test]
        public void TabBar_Non_Selected_Bind_Frames_Are_Deactivated_Initially()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' bind='fa' isOn='true'/>
    <Tab id='b' bind='fb'/>
    <Tab id='c' bind='fc'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
  <Frame id='fc'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Frame>("fa").GameObject.activeSelf);
            Assert.IsFalse(screen.Get<Frame>("fb").GameObject.activeSelf);
            Assert.IsFalse(screen.Get<Frame>("fc").GameObject.activeSelf);
        }
    }
}
