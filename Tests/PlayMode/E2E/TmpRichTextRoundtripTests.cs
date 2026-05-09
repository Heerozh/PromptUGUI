using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.I18n;
using TMPro;

namespace PromptUGUI.Tests.E2E {
    public class TmpRichTextRoundtripTests {
        const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text id='lbl'><![CDATA[<color=#ff0>警告</color>]]></Text>
  </Screen>
</PromptUGUI>";

        [SetUp] public void Setup() {
            UI.ResetForTests();
            TranslationStore.Instance.UnloadAll();
            UI.LoadDocument("S", Xml);
        }
        [TearDown] public void Teardown() {
            UI.ResetForTests();
            TranslationStore.Instance.UnloadAll();
        }

        [Test] public void CDataWithRichText_PreservedAtRuntime() {
            var screen = UI.Open("S");
            var go = ((Control)screen.Get<Text>("lbl")).GameObject;
            Assert.AreEqual("<color=#ff0>警告</color>", go.GetComponent<TMP_Text>().text);
        }

        [Test] public void CDataWithRichText_TranslatedPreservesTags() {
            TranslationStore.Instance.Load("en", new[] {
                new PoEntry {
                    Msgid = "<color=#ff0>警告</color>",
                    Msgstr = "<color=#ff0>WARN</color>",
                },
            });
            UI.Locale.Set("en");
            var screen = UI.Open("S");
            var go = ((Control)screen.Get<Text>("lbl")).GameObject;
            Assert.AreEqual("<color=#ff0>WARN</color>", go.GetComponent<TMP_Text>().text);
        }
    }
}
