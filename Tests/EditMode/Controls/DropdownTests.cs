using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DropdownTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void BindOptions_strings_populates_tmp_dropdown()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var d = screen.Get<Dropdown>("d");

            d.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "Low", "High" }));
            var tmp = d.GameObject.GetComponentInChildren<TMPro.TMP_Dropdown>();
            Assert.AreEqual(2, tmp.options.Count);
            Assert.AreEqual("Low", tmp.options[0].text);
        }

        [Test]
        public void OnSelected_fires_when_value_setter_changes()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var d = screen.Get<Dropdown>("d");
            d.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "A", "B", "C" }));
            int? last = null;
            d.OnSelected.Subscribe(i => last = i);
            d.Value = 2;
            Assert.AreEqual(2, last);
        }
    }
}
