using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.Perf
{
    /// <summary>
    /// What hugging (<c>clamp(_, hug, N)</c>) costs a list while it scrolls at its cap. A hugging list has to hand
    /// its rows' size up to its root: a plain list links its Viewport (<see cref="LayoutLink"/>), a windowing one
    /// publishes from its sync instead — its window dirties Content on every frame it moves, and a link would carry
    /// each of those up. Median ms per scrolled frame (the list's tick + Canvas.ForceUpdateCanvases): a fixed height,
    /// the hug as shipped, and the hug with the link forced on (the design a windowing list does not use). Logs a
    /// table.
    /// </summary>
    [Explicit, Category("Perf")]
    public class ScrollListHugLinkBenchmark
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string ChatLine =
            "<Template name='ChatLine'><HStack width='stretch' spacing='4' childAlign='upper-left'>"
            + "<Text id='time' width='44' fontSize='14'/><Text id='body' width='stretch' wrap='true' fontSize='16'/>"
            + "</HStack></Template>";

        private sealed class Msg
        {
            public readonly long Id;
            public readonly string Time;
            public readonly string Body;

            public Msg(long id)
            {
                Id = id;
                Time = $"{id / 60 % 24:00}:{id % 60:00}";
                Body = id % 4 == 0
                    ? $"#{id} a longer message that wraps onto two or three lines in a chat panel this wide"
                    : $"#{id} short one";
            }
        }

        private enum Mode
        {
            Fixed,
            Hug,
            HugForcedLink,
        }

        [Test]
        public void Scroll_at_the_cap_fixed_vs_hug()
        {
            var table = new StringBuilder(
                "ScrollList scrolled 37 px per frame, capped at 300 — median ms per frame (tick + "
                + "ForceUpdateCanvases); mean in brackets. Virtual: 1000 items; plain: 200.\n");
            table.AppendLine("list    | where      | bar     | fixed 300      | hug            | hug + link forced");
            foreach (var virtualize in new[] { true, false })
                foreach (var inStack in new[] { false, true })
                    foreach (var overlay in new[] { false, true })
                    {
                        var row = $"{(virtualize ? "virtual" : "plain"),-7} | {(inStack ? "in a stack" : "free"),-10} | "
                                  + $"{(overlay ? "overlay" : "default"),-7}";
                        foreach (var mode in new[] { Mode.Fixed, Mode.Hug, Mode.HugForcedLink })
                        {
                            var (median, mean) = Measure(mode, virtualize, inStack, overlay, virtualize ? 1000 : 200, 300);
                            row += $" | {median,6:F2} ({mean,5:F2})";
                        }
                        table.AppendLine(row);
                    }
            Debug.Log(table.ToString());
        }

        private static (double Median, double Mean) Measure(
            Mode mode, bool virtualize, bool inStack, bool overlay, int n, int frames)
        {
            UI.ResetForTests();
            System.GC.Collect();

            var height = mode == Mode.Fixed ? "300" : "clamp(_, hug, 300)";
            var bar = overlay ? "<Scrollbar overlay='true'/>" : "";
            var list = $"<ScrollList id='sl' {(inStack ? "width='stretch'" : "anchor='top-left' width='320'")} "
                       + $"height='{height}' itemTemplate='ChatLine' spacing='2' padding='4' "
                       + $"virtualize='{(virtualize ? "true" : "false")}'>" + bar + "</ScrollList>";
            var siblings = new StringBuilder();
            for (var i = 0; i < 12; i++) siblings.Append($"<Text height='24' fontSize='16'>status line {i}</Text>");
            var body = inStack
                ? "<VStack anchor='top-left' width='320' height='1000' spacing='4'>"
                  + siblings + list + siblings + "</VStack>"
                : list;
            UI.LoadDocument("bench", "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + ChatLine
                + "<Screen name='S'><Frame anchor='top-left' width='320' height='1000'>" + body
                + "</Frame></Screen></PromptUGUI>");
            var sl = UI.Open("S").Get<ScrollList>("sl");
            Canvas.ForceUpdateCanvases();

            var items = new List<Msg>(n);
            for (var i = 0; i < n; i++) items.Add(new Msg(i));
            sl.BindItems(Observable.Return<IReadOnlyList<Msg>>(items), (IControl row, Msg m) => Bind(row, m),
                key: m => m.Id);
            var tick = (IScrollTickHost)sl;
            tick.OnScrollLateUpdate();
            Canvas.ForceUpdateCanvases();

            var viewport = sl.RectTransform.Find("Viewport");
            var link = viewport.GetComponent<LayoutLink>();
            if (mode == Mode.HugForcedLink)
            {
                link ??= viewport.gameObject.AddComponent<LayoutLink>();
                link.enabled = true;
            }
            Assert.AreEqual(300f, sl.RectTransform.rect.height, 0.5f, "guard: capped at 300, so it scrolls");

            var content = (RectTransform)sl.RectTransform.Find("Viewport/Content");
            var step = 37f;
            void Scroll()
            {
                var end = content.rect.height - 300f;
                if (content.anchoredPosition.y + step > end || content.anchoredPosition.y + step < 0f) step = -step;
                content.anchoredPosition += new Vector2(0f, step);
                tick.OnScrollLateUpdate();
                Canvas.ForceUpdateCanvases();
            }

            for (var i = 0; i < 30; i++) Scroll();
            var times = new double[frames];
            var watch = new Stopwatch();
            for (var i = 0; i < frames; i++)
            {
                watch.Restart();
                Scroll();
                times[i] = watch.Elapsed.TotalMilliseconds;
            }
            if (virtualize) Assert.Less(sl.SlotCount, 40, "guard: still a window");

            var sum = 0.0;
            foreach (var t in times) sum += t;
            System.Array.Sort(times);
            return (times[times.Length / 2], sum / times.Length);
        }

        private static void Bind(IControl row, Msg m)
        {
            row.Get<Text>("time").TextValue = m.Time;
            row.Get<Text>("body").TextValue = m.Body;
        }
    }
}
