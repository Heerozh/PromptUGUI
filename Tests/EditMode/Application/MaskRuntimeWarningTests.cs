using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class MaskRuntimeWarningTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Frame_MaskSelf_LogsRuntimeWarning()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);

            // 匹配 FrameSelfCode 警告的特征句:Frame ... mask="self" requires an Image graphic
            LogAssert.Expect(LogType.Warning, new Regex(
                @"<Frame id='f'>.*mask=""self"" requires an Image graphic"));
            UI.Open("S");
        }

        [Test]
        public void Image_MaskSelfWithoutSprite_LogsRuntimeWarning()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);

            // 匹配 SelfNoSpriteCode 警告:Image ... mask="self" with no sprite=
            LogAssert.Expect(LogType.Warning, new Regex(
                @"<Image id='i'>.*mask=""self"" with no sprite"));
            UI.Open("S");
        }
    }
}
