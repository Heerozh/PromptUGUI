using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// `&lt;Tab&gt;` 的 width / height 必须真正落到 RectTransform 上。
    ///
    /// 回归背景：TabBar 建 LayoutGroup 时从不配 childControl* / childForceExpand*，留在 Unity
    /// 默认的 childControl*=false —— 那种模式下 LayoutGroup **只摆位置、不改尺寸**，而
    /// Control.ApplyCommon 对 layout child 只写 LayoutElement、不碰 RectTransform。两边一对上，
    /// 每个 Tab 永远停在默认 100×100（撑穿轨道、相邻互相重叠）。VStack / HStack 一直是配好的。
    /// </summary>
    public class TabBarChildSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'>" + innerXml + "</Screen></PromptUGUI>");
            var s = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return s;
        }

        private static Rect RectOf(IControl c) => ((Control)c).RectTransform.rect;

        [Test]
        public void LayoutGroup_IsConfiguredLikeHStack()
        {
            var s = Open("<TabBar id='bar' width='400' height='40'><Tab id='a' text='A'/></TabBar>");
            var lg = ((Control)s.Get<TabBar>("bar")).GameObject
                     .GetComponent<HorizontalOrVerticalLayoutGroup>();
            Assert.IsNotNull(lg);
            Assert.IsTrue(lg.childControlWidth, "childControlWidth 必须 true，否则 LayoutElement 全落空");
            Assert.IsTrue(lg.childControlHeight);
            Assert.IsFalse(lg.childForceExpandWidth,
                "forceExpand 会把定尺寸子节点也拉伸（Unity 在 GetChildSizes 末尾强制 flexible>=1）");
            Assert.IsFalse(lg.childForceExpandHeight);
        }

        [Test]
        public void ExplicitWidth_LandsExactlyOnTheRect()
        {
            var s = Open(@"<TabBar id='bar' width='400' height='40' spacing='0'>
                             <Tab id='a' width='84' text='A'/>
                             <Tab id='b' width='84' text='B'/>
                           </TabBar>");
            Assert.AreEqual(84f, RectOf(s.Get<Tab>("a")).width, 0.01f);
            Assert.AreEqual(84f, RectOf(s.Get<Tab>("b")).width, 0.01f);
        }

        [Test]
        public void ExplicitHeight_LandsExactlyOnTheRect()
        {
            var s = Open(@"<TabBar id='bar' width='400' height='40'>
                             <Tab id='a' width='84' height='36' text='A'/>
                           </TabBar>");
            Assert.AreEqual(36f, RectOf(s.Get<Tab>("a")).height, 0.01f);
        }

        [Test]
        public void StretchWidth_SplitsRemainingSpace()
        {
            var s = Open(@"<TabBar id='bar' width='400' height='40' spacing='0'>
                             <Tab id='a' width='stretch' text='A'/>
                             <Tab id='b' width='stretch' text='B'/>
                           </TabBar>");
            Assert.AreEqual(200f, RectOf(s.Get<Tab>("a")).width, 0.5f);
            Assert.AreEqual(200f, RectOf(s.Get<Tab>("b")).width, 0.5f);
        }

        [Test]
        public void StretchWidth_AccountsForSpacing()
        {
            var s = Open(@"<TabBar id='bar' width='400' height='40' spacing='20'>
                             <Tab id='a' width='stretch' text='A'/>
                             <Tab id='b' width='stretch' text='B'/>
                           </TabBar>");
            Assert.AreEqual(190f, RectOf(s.Get<Tab>("a")).width, 0.5f);
        }

        [Test]
        public void OmittedWidth_UsesLabelNativeSize_NeverZero()
        {
            // childControlWidth=true 之后，没有 GetNativeSize 的 Tab 会把 preferred 解成 0 而塌掉。
            // Tab.GetNativeSize（镜像 Btn）就是为此加的。
            var s = Open(@"<TabBar id='bar' width='400' height='60'>
                             <Tab id='a' text='A fairly long tab label'/>
                           </TabBar>");
            Assert.Greater(RectOf(s.Get<Tab>("a")).width, 0f, "不写 width 的 Tab 不能塌成 0");
        }

        [Test]
        public void VerticalDirection_SizesChildrenToo()
        {
            var s = Open(@"<TabBar id='bar' direction='vertical' width='60' height='400' spacing='0'>
                             <Tab id='a' height='120' text='A'/>
                           </TabBar>");
            Assert.AreEqual(120f, RectOf(s.Get<Tab>("a")).height, 0.01f);
        }

        [Test]
        public void DirectionSwitch_KeepsChildSizingOnTheRebuiltGroup()
        {
            // direction 切换会 DestroyImmediate 旧组再 AddComponent 新组 —— 配置必须跟着重设。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>
              <Screen name='S'>
                <TabBar id='bar' width='400' height='400' spacing='0'
                        direction='horizontal' direction.tall='vertical'>
                  <Tab id='a' width='84' height='120' text='A'/>
                </TabBar>
              </Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Canvas.ForceUpdateCanvases();

            UI.Variants.Set("tall", true);
            Canvas.ForceUpdateCanvases();

            var lg = ((Control)s.Get<TabBar>("bar")).GameObject
                     .GetComponent<HorizontalOrVerticalLayoutGroup>();
            Assert.IsInstanceOf<VerticalLayoutGroup>(lg);
            Assert.IsTrue(lg.childControlWidth);
            Assert.IsFalse(lg.childForceExpandHeight);
            Assert.AreEqual(120f, RectOf(s.Get<Tab>("a")).height, 0.01f);
        }

        [Test]
        public void TabNativeSize_EmptyLabel_FallsBackToTapTarget()
        {
            var s = Open("<TabBar id='bar' width='400' height='60'><Tab id='a'/></TabBar>");
            var native = ((Control)s.Get<Tab>("a")).GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.Greater(native.Value.x, 0f);
            Assert.Greater(native.Value.y, 0f);
        }
    }
}
