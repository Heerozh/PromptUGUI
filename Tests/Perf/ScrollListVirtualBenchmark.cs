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
    /// The chat case the virtualization spec was written for (2026-09-29 scrolllist-virtualization §1): a full
    /// channel, one message arrives and the oldest is trimmed. Median cost of that push — alone, and with the
    /// frame's layout and mesh work — per item count and list kind. Each test logs a table.
    /// <para>Its own assembly, so the regression (which selects the test assemblies by name) never runs it: to the
    /// test framework an assembly filter is an explicit match, and [Explicit] alone would not keep it out. Run a
    /// test by its full name; the plain-list one holds the editor for minutes.</para>
    /// </summary>
    [Explicit, Category("Perf")]
    public class ScrollListVirtualBenchmark
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // The shape of the host's ChatLine: a time and a body that wraps.
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

        private enum Kind
        {
            Virtual,
            Keyed,
            Positional,
        }

        private const string Legend =
            "ScrollList: trim the oldest + append one — median ms per push. push = the push and the list's own tick "
            + "(bind, window sync / stick layout); frame = push + Canvas.ForceUpdateCanvases (layout + meshes)\n";

        [Test]
        public void Virtual_trim_front_and_append_one()
        {
            var table = new StringBuilder(Legend);
            table.AppendLine("     N | virtual+key push | virtual+key frame");
            foreach (var n in new[] { 20, 200, 1000, 10000, 100000 })
            {
                var (push, frame) = Measure(Kind.Virtual, n, 60);
                table.AppendLine($"{n,6} | {push,16:F2} | {frame,17:F2}");
            }
            Debug.Log(table.ToString());
        }

        // A plain 1000-row list re-lays out every row on every push: seconds per push, minutes in all.
        [Test, Timeout(900000)]
        public void Plain_trim_front_and_append_one()
        {
            var table = new StringBuilder(Legend);
            table.AppendLine("     N | plain+key push | plain+key frame | plain (by position) push | plain (by position) frame");
            foreach (var n in new[] { 20, 200, 1000 })
            {
                var (keyedPush, keyedFrame) = Measure(Kind.Keyed, n, 20);
                var (plainPush, plainFrame) = Measure(Kind.Positional, n, 20);
                table.AppendLine($"{n,6} | {keyedPush,14:F2} | {keyedFrame,15:F2} | {plainPush,24:F2} | {plainFrame,25:F2}");
            }
            Debug.Log(table.ToString());
        }

        private static (double Push, double Frame) Measure(Kind kind, int n, int pushes)
        {
            UI.ResetForTests();
            System.GC.Collect();
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + ChatLine
                    + "<Screen name='S'><Frame anchor='top-left' width='320' height='420'>"
                    + "<ScrollList id='sl' anchor='stretch' itemTemplate='ChatLine' spacing='2' padding='4'"
                    + $" stickToEnd='true' virtualize='{(kind == Kind.Virtual ? "true" : "false")}'/>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("bench", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");
            Canvas.ForceUpdateCanvases();

            var items = new List<Msg>(n + 1);
            long next = 0;
            for (var i = 0; i < n; i++) items.Add(new Msg(next++));
            var feed = new Subject<IReadOnlyList<Msg>>();
            if (kind == Kind.Positional)
                list.BindItems(feed, (IControl row, Msg m) => Bind(row, m));
            else
                list.BindItems(feed, (IControl row, Msg m) => Bind(row, m), key: m => m.Id);

            // Warm up: the first push builds the rows; a few more settle every cache.
            var incoming = new Msg[pushes + 5];
            for (var i = 0; i < incoming.Length; i++) incoming[i] = new Msg(next++);
            var used = 0;
            for (var i = 0; i < 5; i++)
            {
                Shift(items, incoming[used++]);
                feed.OnNext(items);
                ((IScrollTickHost)list).OnScrollLateUpdate();
                Canvas.ForceUpdateCanvases();
            }

            var pushTimes = new double[pushes];
            var frameTimes = new double[pushes];
            var watch = new Stopwatch();
            for (var i = 0; i < pushes; i++)
            {
                Shift(items, incoming[used++]);
                watch.Restart();
                feed.OnNext(items);
                ((IScrollTickHost)list).OnScrollLateUpdate();   // what LateUpdate does in Play mode
                pushTimes[i] = watch.Elapsed.TotalMilliseconds;
                Canvas.ForceUpdateCanvases();
                frameTimes[i] = watch.Elapsed.TotalMilliseconds;
            }
            if (kind == Kind.Virtual)
                Assert.Less(list.SlotCount, 40, "a virtual list holds a window, whatever N is");
            return (Median(pushTimes), Median(frameTimes));
        }

        private static void Shift(List<Msg> items, Msg incoming)
        {
            items.RemoveAt(0);
            items.Add(incoming);
        }

        private static double Median(double[] values)
        {
            System.Array.Sort(values);
            var mid = values.Length / 2;
            return values.Length % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
        }

        private static void Bind(IControl row, Msg m)
        {
            row.Get<Text>("time").TextValue = m.Time;
            row.Get<Text>("body").TextValue = m.Body;
        }
    }
}
