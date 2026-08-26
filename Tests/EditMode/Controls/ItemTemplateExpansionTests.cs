using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>itemTemplate</c> bodies must go through the same expansion every other node does.
    ///
    /// <para><c>ScreenDef.Templates</c> used to carry the RAW <c>&lt;Template&gt;</c> bodies straight
    /// from the loaded document, and <c>ScrollList</c> / <c>TabBar</c> / <c>Carousel</c> instantiate
    /// from them directly. Nothing expansion normally does had happened to them: <c>class=</c> was
    /// never merged, <c>{{param}}</c> was never substituted, nested Template invocations were never
    /// inlined.</para>
    ///
    /// <para>The last two threw inside the bind — <c>unregistered tag 'Inner'</c>, <c>unknown color
    /// token "{{tint}}"</c> — but R3 routes that to its unhandled-exception handler, so the call
    /// returned normally and the author got an EMPTY list plus a console error that names neither
    /// the list nor the template. Hence the slot-count assertions below: an empty list is the
    /// symptom, not an incidental detail.</para>
    /// </summary>
    public class ItemTemplateExpansionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static ScrollList OpenList(string declarations)
        {
            UI.LoadDocument("test",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + declarations +
                "<Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen></PromptUGUI>");
            return UI.Open("S").Get<ScrollList>("list");
        }

        // ScrollList exposes slots only through the bind callback; catch the one row there.
        private static IControl BindOne(ScrollList list)
        {
            IControl captured = null;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                (IControl slot, string s) => captured = slot);
            Assert.AreEqual(1, list.SlotCount,
                "instantiating the row must succeed; a body that throws leaves the list empty and "
                + "only logs, so an empty list IS the defect");
            return captured;
        }

        private static string ColorOf(IControl control, string id) =>
            ColorUtility.ToHtmlStringRGB(
                ((Control)control.Get<IControl>(id)).GameObject.GetComponent<UnityImage>().color);

        [Test]
        public void ClassOnAnItemTemplateNode_IsMerged()
        {
            var list = OpenList(
                "<Style name='card' color='#112233'/>" +
                "<Template name='Row'><HStack><Image id='bg' class='card'/></HStack></Template>");

            Assert.AreEqual("112233", ColorOf(BindOne(list), "bg"),
                "a style pack has to reach an item template like it reaches any other node");
        }

        [Test]
        public void TemplateParamDefault_IsSubstitutedInAnItemTemplate()
        {
            var list = OpenList(
                "<Template name='Row'><Param name='tint' default='#445566'/>" +
                "<HStack><Image id='bg' color='{{tint}}'/></HStack></Template>");

            Assert.AreEqual("445566", ColorOf(BindOne(list), "bg"),
                "with no invocation to supply arguments, the declared defaults are the arguments");
        }

        // The loudest symptom: not a wrong colour but a hard throw out of the control registry.
        [Test]
        public void NestedTemplateInvocation_InAnItemTemplate_IsInlined()
        {
            var list = OpenList(
                "<Template name='Inner'><Image id='bg' color='#778899'/></Template>" +
                "<Template name='Row'><HStack><Inner/></HStack></Template>");

            Assert.AreEqual("778899", ColorOf(BindOne(list), "bg"));
        }

        // Guard against the obvious way to get this wrong: expanding every template eagerly at load
        // would throw for any template with a required <Param>, breaking documents that only ever
        // invoke it normally — where the arguments do exist.
        [Test]
        public void TemplateWithRequiredParam_StillLoadsAndInvokesNormally()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Needs'><Param name='tint'/><Image id='bg' color='{{tint}}'/></Template>
  <Screen name='S'><Needs id='n' tint='#aabbcc'/></Screen>
</PromptUGUI>");

            var screen = UI.Open("S");
            Assert.AreEqual("AABBCC", ColorUtility.ToHtmlStringRGB(
                ((Control)(object)screen.Get<Control>("n")).GameObject.GetComponent<UnityImage>().color));
        }

        // ...and when such a template IS named as an itemTemplate, say so plainly instead of
        // instantiating a body whose {{tint}} was never substituted.
        [Test]
        public void TemplateWithRequiredParam_AsItemTemplate_ThrowsWithAnActionableMessage()
        {
            var ex = Assert.Throws<PromptUGUI.Parser.ParseException>(() => OpenList(
                "<Template name='Row'><Param name='tint'/><Image id='bg' color='{{tint}}'/></Template>"));

            StringAssert.Contains("tint", ex.Message);
            StringAssert.Contains("Row", ex.Message);
        }
    }
}
