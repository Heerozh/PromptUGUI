using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Lint;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <see cref="FontFloorRules"/> judges a control without <c>fontSize</c> at the size the control
    /// builds with. That size lives in each control's source, so the table is pinned here against
    /// the real controls: change a control's default and this goes red until the lint follows.
    /// </summary>
    public class FontFloorDefaultsTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [TestCase("Btn", "<Btn id='c' text='OK'/>")]
        [TestCase("Tab", "<TabBar><Tab id='c' text='A'/></TabBar>")]
        [TestCase("Toggle", "<Toggle id='c' text='Sound'/>")]
        [TestCase("InputField", "<InputField id='c'/>")]
        [TestCase("Collapsible", "<Collapsible id='c' text='More'/>")]
        [TestCase("TabMenu", "<TabMenu id='c'/>")]
        [TestCase("Dropdown", "<Dropdown id='c'/>")]
        [TestCase("Cooldown", "<Cooldown id='c' countdown='true'/>")]
        public void Every_label_a_control_builds_is_at_the_table_default(string tag, string markup)
        {
            UI.LoadDocument("test",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                + markup + "</Screen></PromptUGUI>");
            var control = UI.Open("S").Get<PromptUGUI.Controls.IControl>("c");

            var expected = FontFloorRules.TextTags[tag].Default.Value;
            var labels = control.GameObject.GetComponentsInChildren<TMP_Text>(true);
            Assert.IsNotEmpty(labels, tag);
            foreach (var label in labels)
                Assert.AreEqual(expected, label.fontSize, 1e-4f, $"{tag}: {label.name}");
        }

        [Test]
        public void Markdown_body_is_at_the_table_default()
        {
            Assert.AreEqual(FontFloorRules.TextTags["Markdown"].Default.Value, new MarkdownStyle().BodySize);
        }

        [Test]
        public void Text_without_fontSize_builds_at_the_TMP_default_the_policy_carries()
        {
            Assert.IsNull(FontFloorRules.TextTags["Text"].Default, "the front end supplies it");
            UI.LoadDocument("test",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                + "<Text id='c' text='x'/></Screen></PromptUGUI>");
            var tmp = UI.Open("S").Get<PromptUGUI.Controls.IControl>("c")
                .GameObject.GetComponent<TMP_Text>();

            Assert.AreEqual(TMP_Settings.defaultFontSize, tmp.fontSize, 1e-4f);
        }
    }
}
