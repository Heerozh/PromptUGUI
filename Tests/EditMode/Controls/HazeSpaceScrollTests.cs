using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>hazeSpace="self"</c> inside a <c>&lt;ScrollList&gt;</c> (spec 2026-09-17 haze H-D9): the seed is
    /// taken by the real canvas rebuild, after layout, and scrolling is taken back out of it — so every
    /// row gets its own fog however far the list is scrolled when the row first draws.
    /// </summary>
    public class HazeSpaceScrollTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RowTemplate =
            "<Template name='Row'><Frame height='30'>" +
            "<Frame id='bg' anchor='stretch' color='#101828' haze='24' hazeColor='white' hazeSpace='self'/>" +
            "</Frame></Template>";

        private static ScrollList OpenList()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + RowTemplate
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + "<ScrollList id='sl' width='150' height='200' itemTemplate='Row' spacing='2' padding='4'/>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return screen.Get<ScrollList>("sl");
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        /// <summary>One keyed push — appending builds one row and keeps the others. Returns item → bg panel.</summary>
        private static Dictionary<string, ProceduralPanel> Push(ScrollList list, params string[] keys)
        {
            var panels = new Dictionary<string, ProceduralPanel>();
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(keys),
                (IControl row, string s) => panels[s] = row.Get<Frame>("bg").GameObject.GetComponent<ProceduralPanel>(),
                key: s => s);
            Canvas.ForceUpdateCanvases();
            return panels;
        }

        private static string[] Items(int count)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "i" + i;
            return items;
        }

        private static Transform RootOf(Component c) => c.GetComponentInParent<Canvas>().rootCanvas.transform;

        private static Vector2 CanvasCentre(ProceduralPanel p)
        {
            var rt = p.rectTransform;
            return RootOf(p).InverseTransformPoint(rt.TransformPoint(rt.rect.center));
        }

        /// <summary>The content's scroll offset, in canvas units.</summary>
        private static Vector2 ScrollOffset(ScrollList list)
        {
            var content = ContentOf(list);
            return RootOf(content).InverseTransformVector(content.parent.TransformVector(content.anchoredPosition));
        }

        private static Vector2 SeedOf(ProceduralPanel p)
        {
            var seed = p.HazeSeedForTests;
            Assert.IsTrue(seed.HasValue, $"'{p.transform.parent.name}' drew no mesh — the canvas rebuild never seeded it");
            return seed.Value;
        }

        [Test]
        public void Rows_AtTheStart_SeedWhereTheySit()
        {
            // After layout, not before: rows built before the layout group placed them would all
            // share the first slot's seed.
            var list = OpenList();
            var panels = Push(list, Items(5));
            var seen = new HashSet<Vector2>();
            foreach (var kv in panels)
            {
                var seed = SeedOf(kv.Value);
                var centre = CanvasCentre(kv.Value);
                Assert.AreEqual(centre.x, seed.x, 0.01f, kv.Key);
                Assert.AreEqual(centre.y, seed.y, 0.01f, kv.Key);
                Assert.IsTrue(seen.Add(seed), $"{kv.Key} shares a seed with another row");
            }
        }

        [Test]
        public void ARowAddedWhileScrolled_SeedsAsIfTheListWereAtItsStart()
        {
            // Scrolled by exactly three rows, a new row lands in the screen slot row i7 was seeded in.
            // A screen-position seed would hand it i7's fog — a chat stuck to its end does this on
            // every message.
            var list = OpenList();
            var before = Push(list, Items(10));
            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 32f * 3f);

            var after = Push(list, Items(11));
            Assert.AreSame(before["i7"], after["i7"], "guard: a keyed append keeps the old rows");

            var fresh = SeedOf(after["i10"]);
            var expected = CanvasCentre(after["i10"]) - ScrollOffset(list);
            Assert.Greater(ScrollOffset(list).magnitude, 1f, "guard: the list really is scrolled");
            Assert.AreEqual(expected.x, fresh.x, 0.01f);
            Assert.AreEqual(expected.y, fresh.y, 0.01f);
            foreach (var kv in after)
                if (kv.Key != "i10")
                    Assert.Greater((SeedOf(kv.Value) - fresh).magnitude, 1f, $"i10 got {kv.Key}'s fog");
        }
    }
}
