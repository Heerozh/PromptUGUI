using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Show on="expand|collapse"&gt;</c> (spec 2026-10-19 §4.10 / §5.7): a persistent state like
    /// <c>checked</c> — visible while its source is open (expand) or closed (collapse). The source drives it
    /// (registration), so a <c>&lt;Collapsible&gt;</c> that starts open, an accordion's adjudication and a
    /// collapsible built in a template row are all right from the first frame.
    /// </summary>
    public class ShowExpandTests
    {
        private PuguiScreen _s;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private PuguiScreen Open(string body, string prelude = "")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                                 + "<Screen name='S'>" + body + "</Screen></PromptUGUI>");
            return _s = UI.Open("S");
        }

        private bool Shown(string id) => _s.Get<Show>(id).GameObject.activeSelf;

        [Test]
        public void Show_expand_follows_a_popover()
        {
            Open(@"<Btn id='a' anchor='center' size='40x40'>
                     <Show id='on' on='expand@pop'><Frame size='4x4'/></Show>
                     <Show id='off' on='collapse@pop'><Frame size='4x4'/></Show>
                   </Btn>
                   <Popover id='pop' on='click@a'><Frame width='40' height='20'/></Popover>");
            Assert.IsFalse(Shown("on"), "closed at open");
            Assert.IsTrue(Shown("off"));

            _s.Get<Btn>("a").SimulateClick();
            Assert.IsTrue(Shown("on"));
            Assert.IsFalse(Shown("off"));

            _s.Get<Btn>("a").SimulateClick();
            Assert.IsFalse(Shown("on"));
            Assert.IsTrue(Shown("off"));
        }

        [Test]
        public void Show_expand_follows_a_tabmenu()
        {
            Open(@"<TabMenu id='menu' anchor='top-left' transition='0'><Tab id='x' text='X' isOn='true'/></TabMenu>
                   <Show id='s' on='expand@menu'><Frame size='4x4'/></Show>");
            Assert.IsFalse(Shown("s"));
            _s.Get<TabMenu>("menu").Expand();
            Assert.IsTrue(Shown("s"));
            _s.Get<TabMenu>("menu").Collapse();
            Assert.IsFalse(Shown("s"));
        }

        [Test]
        public void A_collapsible_that_starts_expanded_shows_its_header_block_at_open()
        {
            Open(@"<Collapsible id='c' text='T' transition='0'>
                     <Header><Show id='open' on='expand'><Frame size='4x4'/></Show><Show id='shut' on='collapse'><Frame size='4x4'/></Show></Header>
                     <Frame height='20'/>
                   </Collapsible>");
            Assert.IsTrue(Shown("open"), "expanded by default: the expand block is up from the first frame");
            Assert.IsFalse(Shown("shut"));

            _s.Get<Collapsible>("c").Collapse();
            Assert.IsFalse(Shown("open"));
            Assert.IsTrue(Shown("shut"));
        }

        [Test]
        public void Accordion_members_that_lose_adjudication_show_collapse()
        {
            Open(@"<Collapsible id='a' group='g' text='A' transition='0'>
                     <Header><Show id='aShut' on='collapse'><Frame size='4x4'/></Show></Header><Frame height='20'/>
                   </Collapsible>
                   <Collapsible id='b' group='g' text='B' transition='0'>
                     <Header><Show id='bShut' on='collapse'><Frame size='4x4'/></Show></Header><Frame height='20'/>
                   </Collapsible>");
            var a = _s.Get<Collapsible>("a");
            var b = _s.Get<Collapsible>("b");
            Assume.That(a.IsExpanded != b.IsExpanded, "precondition: the group kept one open");

            Assert.AreEqual(!a.IsExpanded, Shown("aShut"));
            Assert.AreEqual(!b.IsExpanded, Shown("bShut"), "the one adjudicated shut shows its collapse block");
        }

        [Test]
        public void A_row_template_collapsible_is_right_from_its_first_frame()
        {
            Open("<Frame id='host' size='300x300'/>",
                @"<Template name='Card'><Collapsible text='C' expanded='false' transition='0'>
                    <Header><Show id='shut' on='collapse'><Frame size='4x4'/></Show></Header><Frame height='20'/>
                  </Collapsible></Template>");
            var card = _s.Instantiate("Card", _s.Get("host"));
            Assert.IsTrue(card.Get<Show>("shut").GameObject.activeSelf);
        }
    }
}
