using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class InstantiateNodeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Public_InstantiateNode_creates_subtree_under_parent()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>x</Text></HStack></Template>
  <Screen name='S'/>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            // Templates 自展开后挂在 ScreenDef.Templates；运行时控件通过 owner Screen 反查。
            var rowDef = UI.GetScreenDef("S").Templates["Row"];

            var instantiator = UI.GetInstantiator();
            var parent = new GameObject("Host", typeof(RectTransform));
            var root = instantiator.InstantiateNode(rowDef.Body, ((RectTransform)parent.transform), screen);

            Assert.IsNotNull(root);
            Assert.AreEqual(parent.transform, root.GameObject.transform.parent);
            Assert.IsNotNull(root.Get<Text>("label"));
        }
    }
}
