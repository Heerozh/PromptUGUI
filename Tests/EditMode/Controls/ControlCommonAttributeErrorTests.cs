using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // ApplyCommon 解析 anchor/size/width/height/margin/pivot 出错时, 作者看到的
    // ParseException 必须点名 (节点 + 属性 + 出错值), 而不是漏出 .NET 泛型的
    // "Input string was not in a correct format." / "Index was outside the bounds…"。
    // 触发场景来自真实报告: <Btn id='btnStartMatch' margin='0_0,_,_'>。
    public class ControlCommonAttributeErrorTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Malformed_margin_error_names_node_attribute_and_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='btnStartMatch' margin='0_0,_,_'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("btnStartMatch", ex.Message);   // 哪个节点
            StringAssert.Contains("margin", ex.Message);          // 哪个属性
            StringAssert.Contains("0_0", ex.Message);             // 哪个分量
            StringAssert.DoesNotContain(
                "Input string was not in a correct format", ex.Message);
        }

        [Test]
        public void Malformed_pivot_error_names_node_and_attribute()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='btnStartMatch' pivot='a,b'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("btnStartMatch", ex.Message);
            StringAssert.Contains("pivot", ex.Message);
            StringAssert.DoesNotContain(
                "Input string was not in a correct format", ex.Message);
        }

        [Test]
        public void Pivot_with_one_component_explains_xy_form()
        {
            // 旧实现 parts[1] → IndexOutOfRangeException, 完全没上下文。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='btnStartMatch' pivot='0.5'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("pivot", ex.Message);
            StringAssert.DoesNotContain(
                "Index was outside the bounds", ex.Message);
        }
    }
}
