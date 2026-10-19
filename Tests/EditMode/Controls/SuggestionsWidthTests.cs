using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using InputField = PromptUGUI.Controls.InputField;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>popupWidth</c> and the <c>-start</c> / <c>-end</c> alignment of <c>placement</c> (spec
    /// 2026-10-19-suggestions-width-pickable §4.1): the panel may be wider (or narrower) than its field, and a
    /// wider one can grow left from the field's right edge instead of right from its left.
    /// </summary>
    public class SuggestionsWidthTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static (InputField, Suggestions) Open(string listAttrs)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                 + "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
                                 + "<Screen name='S'><InputField id='f' anchor='center' width='240' height='44'>"
                                 + $"<Suggestions id='s' itemTemplate='Row' transition='0' {listAttrs}/></InputField>"
                                 + "</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var field = screen.Get<InputField>("f");
            var list = screen.Get<Suggestions>("s");
            field.SetEditingForTests(true);
            list.NotifyUserInput();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b" }), (IControl row, string v) => { });
            Canvas.ForceUpdateCanvases();
            Assume.That(list.IsOpen);
            return (field, list);
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        [Test]
        public void Unset_it_is_as_wide_as_the_field()
        {
            var (_, list) = Open("");
            Assert.AreEqual(240f, list.RectTransform.rect.width, 0.5f);
        }

        [Test]
        public void popupWidth_widens_it()
        {
            var (_, list) = Open("popupWidth='360'");
            Assert.AreEqual(360f, list.RectTransform.rect.width, 0.5f);
        }

        [Test]
        public void popupWidth_narrower_than_the_field_is_honoured()
        {
            var (_, list) = Open("popupWidth='160'");
            Assert.AreEqual(160f, list.RectTransform.rect.width, 0.5f);
        }

        [Test]
        public void By_default_the_left_edges_line_up()
        {
            var (field, list) = Open("popupWidth='360'");
            Assert.AreEqual(WorldRect(field.RectTransform).xMin, WorldRect(list.RectTransform).xMin, 0.5f);
        }

        [Test]
        public void below_end_lines_up_the_right_edges()
        {
            var (field, list) = Open("popupWidth='360' placement='below-end'");
            Assert.AreEqual(WorldRect(field.RectTransform).xMax, WorldRect(list.RectTransform).xMax, 0.5f);
            Assert.AreEqual(1f, list.RectTransform.pivot.y, "still below");
        }

        [Test]
        public void above_end_is_above_and_right_aligned()
        {
            var (field, list) = Open("popupWidth='360' placement='above-end'");
            Assert.AreEqual(WorldRect(field.RectTransform).xMax, WorldRect(list.RectTransform).xMax, 0.5f);
            Assert.AreEqual(0f, list.RectTransform.pivot.y, "stands on the field");
        }

        [Test]
        public void A_negative_popupWidth_warns_and_falls_back_to_the_field()
        {
            LogAssert.Expect(LogType.Warning, new Regex("popupWidth"));
            var (_, list) = Open("popupWidth='-1'");
            Assert.AreEqual(240f, list.RectTransform.rect.width, 0.5f);
        }

        [Test]
        public void An_unknown_alignment_throws()
        {
            Assert.Catch(() => Open("placement='below-middle'"));
        }
    }
}
