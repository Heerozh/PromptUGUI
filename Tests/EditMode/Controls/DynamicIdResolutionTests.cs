using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Spec 2026-10-19 §5.6: a subtree built at runtime (an <c>itemTemplate</c> row, <c>Instantiate</c>, an
    /// <c>&lt;Add&gt;</c> block a ReSolve switches on) applies each node as soon as it exists, so an
    /// <c>@id</c> pointing at a LATER sibling used to find nothing. Those references now resolve once the
    /// subtree is built — the way a whole Screen always did.
    /// </summary>
    public class DynamicIdResolutionTests
    {
        private const string Row = @"<Template name='Row'>
  <Frame height='20'>
    <Show id='s' on='checked@t'><Frame id='mark' size='4x4'/></Show>
    <Toggle id='t' isOn='true'/>
  </Frame>
</Template>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuguiScreen Open(string body, string prelude = "")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                                 + "<Screen name='S'>" + body + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        [Test]
        public void A_row_Show_may_point_at_a_later_sibling()
        {
            var s = Open("<ScrollList id='l' size='200x200' itemTemplate='Row'/>", Row);
            var list = s.Get<ScrollList>("l");

            using (list.BindItems(Observable.Return<IReadOnlyList<int>>(new[] { 1 }), (IControl row, int i) => { }))
            {
                var row = list.Slots[0];
                Assert.IsTrue(row.Get<Show>("s").GameObject.activeSelf, "the toggle it points at is on");
            }
        }

        [Test]
        public void An_instantiated_template_resolves_after_its_build()
        {
            var s = Open("<Frame id='host' size='200x200'/>", Row);
            var row = s.Instantiate("Row", s.Get("host"));
            Assert.IsTrue(row.Get<Show>("s").GameObject.activeSelf);
        }

        [Test]
        public void An_Add_block_switched_on_by_a_ReSolve_resolves_after_its_build()
        {
            var s = Open("<Frame id='host' size='200x200'/>"
                         + "<Variant when='m'><Add into='#host'>"
                         + "<Show id='s' on='checked@t'><Frame size='4x4'/></Show><Toggle id='t' isOn='true'/>"
                         + "</Add></Variant>");

            UI.Variants.Set("m", true);

            Assert.IsTrue(s.Get<Show>("s").GameObject.activeSelf);
        }

        [Test]
        public void Errors_still_surface_at_the_end_of_the_build()
        {
            var s = Open("<Frame id='host' size='200x200'/>",
                "<Template name='Bad'><Frame><Show on='checked@nope'><Frame/></Show></Frame></Template>");

            var ex = Assert.Catch(() => s.Instantiate("Bad", s.Get("host")));
            StringAssert.Contains("nope", ex.ToString());
        }
    }
}
