using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine.UI;
using PuiImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateSubtreeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Btn BuildBtn(string body)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Btn id='b'>{body}</Btn></Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        [Test]
        public void CollectGraphics_IncludesBgAndLabel_ExcludesStateReactFalseAndNestedSource()
        {
            var btn = BuildBtn("<Image id='keep' stateReact='false'/><Btn id='inner'>x</Btn><Text id='t'>hi</Text>");
            var graphics = StateSubtree.CollectGraphics(btn.GameObject, btn.Children);

            // bg (root Image) + the 'hi' Text 都在；'keep'（stateReact=false）与内层 Btn 的图形不在。
            var keep = btn.Get<PuiImage>("keep").GameObject.GetComponent<Graphic>();
            var innerBg = btn.Get<Btn>("inner").GameObject.GetComponent<Graphic>();
            var rootBg = btn.GameObject.GetComponent<Graphic>();

            var textGraphic = btn.Get<PromptUGUI.Controls.Text>("t").GameObject.GetComponent<Graphic>();
            Assert.IsNotNull(textGraphic, "Text 的 TMP_Text 应是 Graphic");

            Assert.Contains(rootBg, graphics, "root bg 应在内");
            Assert.Contains(textGraphic, graphics, "普通（未剪枝）Text 后代应在内");
            Assert.IsFalse(graphics.Contains(keep), "stateReact='false' 子树应被剪掉");
            Assert.IsFalse(graphics.Contains(innerBg), "嵌套 IStateSource 应被剪掉");
        }
    }
}
