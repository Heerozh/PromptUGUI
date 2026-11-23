using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Where a control's sfx= comes from at runtime — inline, class=, a template body, the call site, a variant, a theme —
    /// and that it is written on every pass, so a cleared variant or theme falls back to the default (spec
    /// 2026-11-23-ui-sfx-cues §5.4, §7.15).
    /// </summary>
    public class SfxApplyTests
    {
        private List<SfxCue> _cues;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _cues = Record();
        }

        [TearDown]
        public void TearDown()
        {
            UI.Variants.Set("mobile", false);
            UI.ResetForTests();
        }

        private string KeyOfClick(PromptUGUI.Application.Screen s, string id)
        {
            _cues.Clear();
            Click(s.Get<Btn>(id).GameObject);
            Assert.AreEqual(1, _cues.Count, Describe(_cues));
            return _cues[0].Key;
        }

        [Test]
        public void Inline_class_template_body_and_call_site()
        {
            var s = Open(
                "<Style name='primary' sfx='confirm'/>" +
                "<Template name='Danger'><Btn sfx='danger'>!</Btn></Template>" +
                "<Screen name='S'>" +
                "<Btn id='inline' sfx='ping'>a</Btn><Btn id='styled' class='primary'>b</Btn>" +
                "<Danger id='body'/><Danger id='site' sfx='quiet'/>" +
                "</Screen>");

            Assert.AreEqual("ping", KeyOfClick(s, "inline"));
            Assert.AreEqual("confirm", KeyOfClick(s, "styled"));
            Assert.AreEqual("danger", KeyOfClick(s, "body"));
            Assert.AreEqual("quiet", KeyOfClick(s, "site"));
        }

        [Test]
        public void A_base_less_variant_falls_back_to_the_default_when_it_clears()
        {
            var s = OpenScreen("<Btn id='b' sfx.mobile='tap'>b</Btn>");
            Assert.AreEqual("btn.click", KeyOfClick(s, "b"));

            UI.Variants.Set("mobile", true);
            Assert.AreEqual("tap", KeyOfClick(s, "b"));

            UI.Variants.Set("mobile", false);
            Assert.AreEqual("btn.click", KeyOfClick(s, "b"), "written on every pass: nothing stays stuck");
        }

        [Test]
        public void A_theme_switch_re_reads_sfx_and_clears_it_with_the_theme()
        {
            // Through LoadDocumentAsync: the plain LoadDocument(label, xml) registers no <Theme> blocks.
            var files = new Dictionary<string, string>
            {
                ["main"] = Head +
                           "<Style name='btn' color='#ffffff'/>" +
                           "<Theme name='plain'><Color name='ink' value='#000'/></Theme>" +
                           "<Theme name='retro'><Style name='btn' sfx='retro_click'/></Theme>" +
                           "<Screen name='S'><Btn id='b' class='btn'>b</Btn></Screen></PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            var s = UI.Open("S");

            UI.Theme.Set("retro");
            Assert.AreEqual("retro_click", KeyOfClick(s, "b"));

            UI.Theme.Set("plain");
            Assert.AreEqual("btn.click", KeyOfClick(s, "b"), "the theme without it resets it");
        }

        [Test]
        public void A_malformed_value_warns_once_and_plays_the_default()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[PUI-SFX-VALUE\].*two keys"));
            var s = OpenScreen("<Btn id='b' sfx='a b' width.mobile='120'>b</Btn>");

            UI.Variants.Set("mobile", true);   // a second apply pass of the same value
            UI.Variants.Set("mobile", false);

            Assert.AreEqual("btn.click", KeyOfClick(s, "b"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
