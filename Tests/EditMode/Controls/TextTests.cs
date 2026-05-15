using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TextTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Visual_ColorDefaultsToDarkGrey()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t'>hi</Text>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.AreEqual(ProceduralBuilders.DefaultLabelColor, text.TmpComponent.color);
        }

        [Test]
        public void Visual_ExplicitColorOverridesDefault()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t' color='#ff0000'>hi</Text>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.AreEqual(Color.red, text.TmpComponent.color);
        }
    }
}
