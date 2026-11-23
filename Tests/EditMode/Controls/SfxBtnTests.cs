using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary><c>&lt;Btn&gt;</c> cues, deny, and the facade (spec 2026-11-23-ui-sfx-cues §6, §7.2, §7.13).</summary>
    public class SfxBtnTests
    {
        private List<SfxCue> _cues;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _cues = Record();
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void A_click_cues_btn_click_before_OnClick()
        {
            var s = OpenScreen("<Btn id='b'>Go</Btn>");
            var btn = s.Get<Btn>("b");
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add("cue:" + c.Key));
            btn.OnClick.Subscribe(_ => order.Add("click")).AddTo(s);

            Click(btn.GameObject);

            CollectionAssert.AreEqual(new[] { "cue:btn.click", "click" }, order);
            var cue = _cues.Single();
            Assert.AreEqual("btn", cue.Kind);
            Assert.AreEqual("click", cue.Action);
            Assert.AreEqual("btn.click", cue.Key);
            Assert.AreEqual("btn.click", cue.DefaultKey);
            Assert.AreSame(btn, cue.Source);
            Assert.AreSame(s, cue.Screen);
            Assert.IsNull(cue.Target);
        }

        [Test]
        public void Code_invoking_onClick_cues_nothing()
        {
            var s = OpenScreen("<Btn id='b'>Go</Btn>");
            var clicked = 0;
            s.Get<Btn>("b").OnClick.Subscribe(_ => clicked++).AddTo(s);

            s.Get<Btn>("b").GameObject.GetComponent<PuiButton>().onClick.Invoke();

            Assert.AreEqual(1, clicked);
            Assert.IsEmpty(_cues, "only a real input event is a user action");
        }

        [Test]
        public void A_right_click_cues_nothing()
        {
            var s = OpenScreen("<Btn id='b'>Go</Btn>");
            Click(s.Get<Btn>("b").GameObject, PointerEventData.InputButton.Right);
            Assert.IsEmpty(_cues);
        }

        [Test]
        public void A_disabled_Btn_cues_deny_and_does_not_click()
        {
            var s = OpenScreen("<Btn id='b' interactable='false'>Go</Btn>");
            var clicked = 0;
            s.Get<Btn>("b").OnClick.Subscribe(_ => clicked++).AddTo(s);

            Click(s.Get<Btn>("b").GameObject);

            Assert.AreEqual(0, clicked);
            Assert.AreEqual("btn.deny", _cues.Single().Key);
        }

        [Test]
        public void A_Btn_disabled_through_an_ancestor_or_from_code_cues_deny()
        {
            var s = OpenScreen("<Frame interactable='false' anchor='stretch'><Btn id='b'>Go</Btn></Frame><Btn id='c'>C</Btn>");
            s.Get<Btn>("c").Interactable = false;

            Click(s.Get<Btn>("b").GameObject);
            Click(s.Get<Btn>("c").GameObject);

            Assert.AreEqual("[btn.deny→btn.deny, btn.deny→btn.deny]", Describe(_cues));
        }

        [Test]
        public void Submit_cues_click_and_on_a_disabled_Btn_deny()
        {
            var s = OpenScreen("<Btn id='b'>Go</Btn><Btn id='off' interactable='false'>No</Btn>");
            Submit(s.Get<Btn>("b").GameObject);
            Submit(s.Get<Btn>("off").GameObject);
            Assert.AreEqual("[btn.click→btn.click, btn.deny→btn.deny]", Describe(_cues));
        }

        [Test]
        public void A_Submit_that_only_wakes_the_cursor_cues_nothing()
        {
            UI.UseGamepadNavigation();
            var s = OpenScreen("<Btn id='b'>Go</Btn><Btn id='off' interactable='false'>No</Btn>");

            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            Submit(s.Get<Btn>("b").GameObject);
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            Submit(s.Get<Btn>("off").GameObject);

            Assert.IsEmpty(_cues, "the first Submit after mouse use only brings the cursor back");
        }

        [Test]
        public void Sfx_overrides_mutes_and_deny()
        {
            var s = OpenScreen(
                "<Btn id='a' sfx='confirm'>A</Btn><Btn id='b' sfx='none'>B</Btn>" +
                "<Btn id='c' sfx='none' interactable='false'>C</Btn>" +
                "<Btn id='d' sfx='deny:locked' interactable='false'>D</Btn>" +
                "<Btn id='e' sfx='none deny:none' interactable='false'>E</Btn>");

            foreach (var id in new[] { "a", "b", "c", "d", "e" }) Click(s.Get<Btn>(id).GameObject);

            Assert.AreEqual("[btn.click→confirm, btn.deny→btn.deny, btn.deny→locked]", Describe(_cues),
                "none mutes the click, not the refusal; only deny: changes that");
            Assert.AreEqual("btn.click", _cues[0].DefaultKey, "the default stays at hand for a key the game has not mapped");
        }

        [Test]
        public void A_click_off_the_links_of_a_text_inside_a_Btn_is_the_Btns_click()
        {
            var s = OpenScreen("<Btn id='b' size='400x40'>" +
                               "<Text id='t' anchor='stretch' wrap='false'><![CDATA[<link=\"p:1\">Alice</link> says hi]]></Text>" +
                               "</Btn>");
            var text = s.Get<Text>("t");
            text.OnLinkClicked.Subscribe(_ => { }).AddTo(s);   // what puts TextLinkClicker on the text
            var tmp = text.TmpComponent;

            LinkPointer.Click(tmp, LinkPointer.OffLinks(tmp));

            Assert.AreEqual("btn.click", _cues.Single().Key, "TextLinkClicker hands the click on to the Btn");
        }

        // ───── the facade ─────

        [Test]
        public void ResetForTests_rebuilds_the_stream()
        {
            var s = OpenScreen("<Btn id='b'>Go</Btn>");
            UI.ResetForTests();
            var fresh = Record();
            s = OpenScreen("<Btn id='b'>Go</Btn>");

            Click(s.Get<Btn>("b").GameObject);

            Assert.IsEmpty(_cues, "a subscription from before the reset hears nothing");
            Assert.AreEqual(1, fresh.Count);
        }

        [Test]
        public void A_throwing_subscriber_does_not_stop_the_click()
        {
            var s = OpenScreen("<Btn id='b'>Go</Btn>");
            var clicked = 0;
            s.Get<Btn>("b").OnClick.Subscribe(_ => clicked++).AddTo(s);
            UI.Sfx.OnCue.Subscribe(_ => throw new System.InvalidOperationException("audio is broken"));
            LogAssert.Expect(LogType.Exception, new Regex("audio is broken"));

            Click(s.Get<Btn>("b").GameObject);

            Assert.AreEqual(1, clicked, "a sound handler's bug never becomes a dead button");
        }

        public class SfxProbe : Control
        {
            public void Press() => RaiseSfxCue("press");
        }

        [Test]
        public void A_custom_control_raises_with_its_tag_as_kind_and_its_sfx_honoured()
        {
            UI.Registry.Register<SfxProbe>("SfxProbe", null);
            var s = OpenScreen("<SfxProbe id='p' sfx='press:beep'/><SfxProbe id='q'/>");

            s.Get<SfxProbe>("p").Press();
            s.Get<SfxProbe>("q").Press();

            Assert.AreEqual("[sfxprobe.press→beep, sfxprobe.press→sfxprobe.press]", Describe(_cues));
        }
    }
}
