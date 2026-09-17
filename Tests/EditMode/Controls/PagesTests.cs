using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Pages&gt;</c> — one direct child active at a time, <c>selected</c> as runtime-owned
    /// state (spec 2026-09-17-pages-design §5).
    /// </summary>
    public class PagesTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body, string prelude = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                      + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private const string ThreePages =
            "<Pages id='p' anchor='stretch'>" +
            "  <Frame id='a' anchor='stretch'/>" +
            "  <Frame id='b' anchor='stretch'/>" +
            "  <Frame id='c' anchor='stretch'/>" +
            "</Pages>";

        private static bool Shown(IScreen s, string id) => s.Get(id).GameObject.activeSelf;

        // ── Initial selection ──────────────────────────────────────────────────────────────

        [Test]
        public void Default_Selects_First_Page_And_Deactivates_Rest_After_Open()
        {
            var s = Open(ThreePages);
            var p = s.Get<Pages>("p");

            Assert.AreEqual("a", p.Selected, "no selected= → the first page");
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, p.PageIds, "declaration order");
            Assert.AreEqual("a", p.SelectedPage.Id);
            Assert.IsTrue(Shown(s, "a"));
            Assert.IsFalse(Shown(s, "b"), "every other page is deactivated once Open has measured");
            Assert.IsFalse(Shown(s, "c"));
        }

        [Test]
        public void Selected_Attr_Picks_Initial_Page()
        {
            var s = Open(ThreePages.Replace("id='p'", "id='p' selected='b'"));
            Assert.AreEqual("b", s.Get<Pages>("p").Selected);
            Assert.IsFalse(Shown(s, "a"));
            Assert.IsTrue(Shown(s, "b"));
            Assert.IsFalse(Shown(s, "c"));
        }

        [Test]
        public void Selected_Unknown_Id_Warns_And_Falls_Back_To_First()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Pages 'p'.*'zzz'"));
            var s = Open(ThreePages.Replace("id='p'", "id='p' selected='zzz'"));
            Assert.AreEqual("a", s.Get<Pages>("p").Selected);
            Assert.IsTrue(Shown(s, "a"));
        }

        [Test]
        public void Selected_Pointing_At_If_False_Child_Falls_Back_To_First()
        {
            // if= is a template-body feature: the page is dropped at expansion, so selected= names
            // a page that never existed in this instance.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Pages 'w'.*'b'"));
            var s = Open(
                "<Wrap id='w' detail='false'/>",
                prelude: "<Template name='Wrap'><Param name='detail' default='true'/>" +
                         "<Pages id='p' selected='b'><Frame id='a'/><Frame id='b' if='{{detail}}'/></Pages>" +
                         "</Template>");
            var p = s.Get<Pages>("w");
            CollectionAssert.AreEqual(new[] { "a" }, p.PageIds, "an if-dropped child is not a page");
            Assert.AreEqual("a", p.Selected);
        }

        [Test]
        public void Child_Without_Id_Warns_And_Stays_Inactive()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Pages 'p'.*without an id"));
            var s = Open("<Pages id='p'><Frame id='a'/><Frame/></Pages>");
            var p = s.Get<Pages>("p");
            CollectionAssert.AreEqual(new[] { "a" }, p.PageIds);
            Assert.IsTrue(Shown(s, "a"));
            Assert.IsFalse(p.Children[1].GameObject.activeSelf, "a page nobody can select is never shown");
        }

        // ── Runtime switching ─────────────────────────────────────────────────────────────

        [Test]
        public void Show_Switches_ActiveSelf_And_Fires_OnSelectedChanged()
        {
            var s = Open(ThreePages);
            var p = s.Get<Pages>("p");
            var received = new List<string>();
            p.OnSelectedChanged.Subscribe(received.Add);

            p.Show("c");

            Assert.AreEqual("c", p.Selected);
            Assert.AreEqual("c", p.SelectedPage.Id);
            Assert.IsFalse(Shown(s, "a"));
            Assert.IsFalse(Shown(s, "b"));
            Assert.IsTrue(Shown(s, "c"));
            CollectionAssert.AreEqual(new[] { "c" }, received);

            p.Selected = "b";                    // the setter is the same path
            Assert.IsTrue(Shown(s, "b"));
            Assert.IsFalse(Shown(s, "c"));
            CollectionAssert.AreEqual(new[] { "c", "b" }, received);
        }

        [Test]
        public void Show_Same_Id_Is_NoOp_Without_Event()
        {
            var s = Open(ThreePages);
            var p = s.Get<Pages>("p");
            var received = new List<string>();
            p.OnSelectedChanged.Subscribe(received.Add);

            p.Show("a");

            Assert.AreEqual("a", p.Selected);
            Assert.IsTrue(Shown(s, "a"));
            Assert.IsEmpty(received);
        }

        [Test]
        public void Show_Unknown_Id_Warns_Once_And_Keeps_Selection()
        {
            var s = Open(ThreePages);
            var p = s.Get<Pages>("p");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Pages 'p'.*'nope'"));

            p.Show("nope");
            p.Show("nope");                      // second call: no second warning

            Assert.AreEqual("a", p.Selected);
            Assert.IsTrue(Shown(s, "a"));
            Assert.IsFalse(Shown(s, "b"));
        }

        [Test]
        public void Nested_Pages_Inner_Show_Works_While_Outer_Page_Inactive()
        {
            var s = Open(
                "<Pages id='o' selected='x'>" +
                "  <Frame id='x'/>" +
                "  <Pages id='i'><Frame id='i1'/><Frame id='i2'/></Pages>" +
                "</Pages>");
            var o = s.Get<Pages>("o");
            var i = s.Get<Pages>("i");
            Assert.IsFalse(Shown(s, "i"), "the inner container is the outer's non-selected page");

            i.Show("i2");                        // switching a hidden container still books
            Assert.AreEqual("i2", i.Selected);
            Assert.IsTrue(Shown(s, "i2"));
            Assert.IsFalse(Shown(s, "i1"));
            Assert.IsFalse(s.Get("i2").GameObject.activeInHierarchy);

            o.Show("i");
            Assert.IsTrue(s.Get("i2").GameObject.activeInHierarchy);
            Assert.IsFalse(s.Get("i1").GameObject.activeInHierarchy);
        }

        // ── ReSolve ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Runtime_Selection_Survives_ReSolve()
        {
            var s = Open(ThreePages.Replace("id='p'", "id='p' selected='a'"));
            var p = s.Get<Pages>("p");

            p.Show("b");
            UI.Variants.Set("mobile", true);     // any ReSolve

            Assert.AreEqual("b", p.Selected, "selected= is runtime-owned once code has switched");
            Assert.IsTrue(Shown(s, "b"));
            Assert.IsFalse(Shown(s, "a"));

            s.ReSolve();                         // resize path
            Assert.AreEqual("b", p.Selected);
            Assert.IsTrue(Shown(s, "b"));
        }

        [Test]
        public void Selected_Variant_Override_Applies_When_Untouched()
        {
            var s = Open(ThreePages.Replace("id='p'", "id='p' selected='a' selected.portrait='b'"));
            var p = s.Get<Pages>("p");
            var received = new List<string>();
            p.OnSelectedChanged.Subscribe(received.Add);
            Assert.AreEqual("a", p.Selected);

            UI.Variants.Set("portrait", true);

            Assert.AreEqual("b", p.Selected, "a declared variant value is not the user's own state");
            Assert.IsTrue(Shown(s, "b"));
            Assert.IsFalse(Shown(s, "a"));
            CollectionAssert.AreEqual(new[] { "b" }, received, "a Variant-driven switch is still a switch");
        }

        [Test]
        public void Reconcile_On_ReSolve_Reasserts_Page_ActiveSelf()
        {
            var s = Open(ThreePages);
            s.Get("a").GameObject.SetActive(false);   // someone meddled behind the container's back
            s.Get("b").GameObject.SetActive(true);

            s.ReSolve();

            Assert.IsTrue(Shown(s, "a"), "the container owns its pages' activeSelf");
            Assert.IsFalse(Shown(s, "b"));
        }

        // ── Templates ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Template_Invocation_Is_A_Page()
        {
            var s = Open(
                "<Pages id='p'><Card id='one'/><Card id='two'/></Pages>",
                prelude: "<Template name='Card'><Frame anchor='stretch'><Text id='t'>x</Text></Frame></Template>");
            var p = s.Get<Pages>("p");
            CollectionAssert.AreEqual(new[] { "one", "two" }, p.PageIds, "the invocation's id names the page");
            Assert.IsTrue(Shown(s, "one"));
            Assert.IsFalse(Shown(s, "two"));

            p.Show("two");
            Assert.IsFalse(Shown(s, "one"));
            Assert.IsTrue(Shown(s, "two"));
            Assert.IsNotNull(s.Get<Text>("two/t"), "id paths into the page are untouched");
        }

        // ── Open-time deferral ────────────────────────────────────────────────────────────

        /// <summary>Records whether its GameObject was active in the hierarchy when it was applied.</summary>
        private sealed class Probe : Control
        {
            public static bool? ActiveAtApply;
            internal override void OnAfterApply() { ActiveAtApply ??= GameObject.activeInHierarchy; }
        }

        [Test]
        public void Initial_Hide_Is_Deferred_Until_Open_Measured()
        {
            // An Add block into a non-selected page is applied AFTER the <Pages> (Add nodes are
            // appended to the apply order). Had the container deactivated the page in its own apply,
            // the block's controls would measure their content on an inactive GameObject — the same
            // hazard Tab.bind defers around. The page must still be active while they apply, and
            // deactivated once Open is done.
            Probe.ActiveAtApply = null;
            UI.Registry.Register<Probe>("Probe", null);
            UI.Variants.Set("x", true);
            var s = Open(
                "<Pages id='p' selected='a'><Frame id='a'/><Frame id='b'/></Pages>" +
                "<Variant when='x'><Add into='#b'><Probe id='probe'/></Add></Variant>");

            Assert.AreEqual(true, Probe.ActiveAtApply, "page b was still active while the Add block applied");
            Assert.IsFalse(Shown(s, "b"), "…and is deactivated once Open has finished");
            Assert.IsFalse(s.Get("probe").GameObject.activeInHierarchy);
        }
    }
}
