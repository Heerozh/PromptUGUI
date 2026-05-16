using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class UIXmlLintMaskTests
    {
        // 一份 xml 同时触发 6 条 mask 规则 — 验证 IRWalker 全部能产出。
        [Test]
        public void EndToEnd_AllSixMaskRulesFire()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='f-self' mask='self'/>
    <Frame id='f-bogus' mask='circle'/>
    <Frame id='f-pad-no-rect' maskPadding='8'/>
    <Image id='i-self-no-sprite' mask='self'/>
    <Image id='i-show-no-self' mask='rect' showMask='false'/>
    <Image id='i-variant' sprite='x' mask.mobile='self'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();

            string[] expected =
            {
                MaskAttributeRules.FrameSelfCode,
                MaskAttributeRules.ValueCode,
                MaskAttributeRules.PaddingNoRectCode,
                MaskAttributeRules.SelfNoSpriteCode,
                MaskAttributeRules.ShowMaskNoSelfCode,
                MaskAttributeRules.VariantCode,
            };
            foreach (var code in expected)
                Assert.IsTrue(issues.Any(i => i.Code == code),
                    $"expected at least one issue with code {code}; got: {string.Join(", ", issues.Select(i => i.Code))}");
        }
    }
}
