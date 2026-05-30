using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// PUI-STATE-NO-SOURCE: a bare (no-@id) state-* trigger / animation / show that
    /// has no &lt;Btn&gt; ancestor cannot resolve a state source at runtime — flag it
    /// statically in the CLI. @id forms and Template bodies are deferred to runtime.
    /// </summary>
    public class StateTriggerRulesTests
    {
        private const string Code = "PUI-STATE-NO-SOURCE";

        private static UIDocument Parse(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            return UIDocumentParser.Parse(xml);
        }

        [Test]
        public void State_Show_Inside_Btn_No_Issue()
        {
            var doc = Parse("<Btn><Show on='state-pressed'><Icon name='ui:gear'/></Show></Btn>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, Code,
                "state-* inside a <Btn> has a valid ancestor source.");
        }

        [Test]
        public void State_Show_Without_Btn_Ancestor_Yields_One_Issue()
        {
            var doc = Parse("<Frame><Show on='state-pressed'/></Frame>");
            var issues = IRWalker.Walk(doc).Where(i => i.Code == Code).ToList();
            Assert.AreEqual(1, issues.Count,
                "state-* with no <Btn> ancestor must produce exactly one PUI-STATE-NO-SOURCE.");
        }

        [Test]
        public void State_Trigger_Without_Btn_Ancestor_Yields_Issue()
        {
            var doc = Parse("<Frame><Trigger on='state-hover'/></Frame>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, Code);
        }

        [Test]
        public void State_Animation_Without_Btn_Ancestor_Yields_Issue()
        {
            var doc = Parse("<Frame><Animation on='state-disabled'/></Frame>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, Code);
        }

        [Test]
        public void State_With_Id_Form_No_Issue()
        {
            // @id forms resolve against ScopedIds at runtime — can't be checked statically.
            var doc = Parse("<Frame><Show on='state-pressed@someId'/></Frame>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, Code,
                "state-*@<id> forms are deferred to runtime resolution.");
        }

        [Test]
        public void State_In_Template_Body_No_Issue()
        {
            // Template body is declaration-space; the <Btn> ancestor may only exist at
            // invocation. Exempt like PUI-TAB-PARENT to avoid false positives.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='PressFx'><Frame><Show on='state-pressed'/></Frame></Template>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, Code,
                "state-* in a Template body has its <Btn> ancestor only at invocation.");
        }

        [Test]
        public void State_In_TemplateInstanceRoot_No_Issue()
        {
            // After expansion the wrapper carries IsTemplateInstanceRoot; the <Btn> may be
            // supplied by the invocation site, so don't flag inside an instance root.
            var doc = UIDocumentParser.Parse(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='PressFx'><Frame><Show on='state-pressed'/></Frame></Template>
  <Screen name='S'><Btn><PressFx/></Btn></Screen>
</PromptUGUI>");
            var expanded = TemplateExpander.Expand(doc);
            var codes = IRWalker.Walk(expanded).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, Code,
                "state-* inside a Template-instance root must not be flagged.");
        }

        [Test]
        public void Non_State_On_Value_No_Issue()
        {
            // 'open' / 'click' etc. are not state-* — outside this rule's scope.
            var doc = Parse("<Frame><Trigger on='loop'/></Frame>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, Code);
        }
    }
}
