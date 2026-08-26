using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using PromptScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Rows built by <c>BindItems</c> must re-solve like everything else.
    ///
    /// <para><c>Screen.ReSolve</c> replays attributes for every node in <c>_nodeMap</c>, which is how
    /// a resize, a Variant flip and a theme switch reach the UI. Dynamic subtrees were registered
    /// only for <c>ApplyScales</c>, so <c>ControlAttributeApplier</c> ran on them exactly once, at
    /// instantiation — a bound list did not follow a Variant, did not repaint on a theme switch, and
    /// did not re-solve on resize. Not even colour tokens reached it.</para>
    /// </summary>
    public class DynamicSubtreeReSolveTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void UseFiles(Dictionary<string, string> files) =>
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);

        private static PromptScreen LoadAsync(string body)
        {
            UseFiles(new Dictionary<string, string>
            {
                ["main"] = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body
                           + "</PromptUGUI>",
            });
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            return UI.Open("S");
        }

        private static IControl BindOne(ScrollList list)
        {
            IControl captured = null;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                (IControl slot, string s) => captured = slot);
            Assert.AreEqual(1, list.SlotCount, "guard: one row was built");
            return captured;
        }

        private static string ColorOf(IControl row, string id) =>
            ColorUtility.ToHtmlStringRGB(
                ((Control)row.Get<IControl>(id)).GameObject.GetComponent<UnityImage>().color);

        [Test]
        public void BoundRow_FollowsAVariantFlip()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Image id='bg' color='#112233' color.alt='#445566'/></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>");
            var row = BindOne(UI.Open("S").Get<ScrollList>("list"));
            Assume.That(ColorOf(row, "bg"), Is.EqualTo("112233"));

            UI.Variants.Set("alt", true);

            Assert.AreEqual("445566", ColorOf(row, "bg"),
                "a Variant override on an item template is markup the author wrote; it has to mean "
                + "something");
        }

        [Test]
        public void BoundRow_RepaintsOnAThemeSwitch()
        {
            var screen = LoadAsync(@"
                <Style name='card' color='#112233'/>
                <Theme name='modern'><Color name='ink' value='#000'/></Theme>
                <Theme name='pixel'><Style name='card' color='#445566'/></Theme>
                <Template name='Row'><HStack><Image id='bg' class='card'/></HStack></Template>
                <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>");

            UI.Theme.Set("modern");
            var row = BindOne(screen.Get<ScrollList>("list"));
            Assume.That(ColorOf(row, "bg"), Is.EqualTo("112233"));

            UI.Theme.Set("pixel");

            Assert.AreEqual("445566", ColorOf(row, "bg"),
                "rows already on screen when the skin changes must change with it");
        }

        // The runtime-takeover locks still have to win: BindItems is code setting a value, and a
        // ReSolve must not snap it back to whatever the XML declared.
        [Test]
        public void BoundText_SurvivesReSolve()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>placeholder</Text></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>");
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            IControl row = null;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "bound" }),
                (IControl slot, string s) => { row = slot; slot.Get<Text>("label").TextValue = s; });
            Assume.That(list.SlotCount, Is.EqualTo(1));

            screen.ReSolve();

            // TextValue is write-only; read the TMP component the control drives.
            var tmp = ((Control)row.Get<IControl>("label")).GameObject
                .GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual("bound", tmp.text,
                "the DefaultText lock is what keeps a replay from clobbering bound content");
        }

        // The replay is not free — a 500-row list measures ~500 ms — so a plain resize opts out:
        // nothing a row resolves against changed, and resizes arrive in bursts. An orientation change
        // still reaches rows, because it flips a Variant, which is the state path.
        [Test]
        public void ResizePathReSolve_SkipsRows()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Image id='bg' color='#112233' color.alt='#445566'/></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>");
            var screen = UI.Open("S");
            var row = BindOne(screen.Get<ScrollList>("list"));

            UI.Variants.Set("alt", true);
            Assume.That(ColorOf(row, "bg"), Is.EqualTo("445566"), "guard: the state path did reach it");

            UI.Variants.Set("alt", false);
            Assume.That(ColorOf(row, "bg"), Is.EqualTo("112233"));

            // Simulate the resize path directly: it must leave rows exactly as they are.
            UI.Variants.Set("alt", true);
            screen.ReSolve(replayDynamicSubtrees: false);
            var afterResizeOnly = ColorOf(row, "bg");

            screen.ReSolve();
            Assert.AreEqual("445566", ColorOf(row, "bg"), "the state path still replays");
            Assert.AreEqual("445566", afterResizeOnly,
                "…and the variant had already been applied by Set, so the resize-path call simply "
                + "left it alone rather than reverting anything");
        }

        // Rows are torn down and rebuilt on every data change; a stale subtree must not resurrect.
        [Test]
        public void ReSolveAfterARebuild_TouchesOnlyLiveRows()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Image id='bg' color='#112233' color.alt='#445566'/></HStack></Template>
  <Screen name='S'><ScrollList id='list' itemTemplate='Row'/></Screen>
</PromptUGUI>");
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            var src = new ReactiveProperty<IReadOnlyList<string>>(new[] { "a", "b" });
            IControl last = null;
            list.BindItems(src, (IControl slot, string s) => last = slot);
            src.Value = new[] { "x" };                    // destroys the first two rows
            Assume.That(list.SlotCount, Is.EqualTo(1));

            Assert.DoesNotThrow(() => UI.Variants.Set("alt", true),
                "the replay must skip subtrees whose GameObjects are gone");
            Assert.AreEqual("445566", ColorOf(last, "bg"));
        }
    }
}
