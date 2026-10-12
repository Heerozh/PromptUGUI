using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabBarBindItemsTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TabBar OpenBar(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<TabBar>("bar");
        }

        [Test]
        public void BindItems_With_Default_Template_Instantiates_Tabs()
        {
            var bar = OpenBar("<TabBar id='bar'/>");
            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "A", "B", "C" }),
                (Tab tab, string s) => tab.Text = s);

            Assert.AreEqual(3, bar.Count);
            Assert.AreEqual("A", bar.GetAt(0).GameObject.transform.Find("Label").GetComponent<TMPro.TMP_Text>().text);
        }

        [Test]
        public void BindItems_Clears_Existing_Static_Tabs()
        {
            var bar = OpenBar("<TabBar id='bar'><Tab text='static1'/><Tab text='static2'/></TabBar>");
            Assert.AreEqual(2, bar.Count, "starts with 2 static tabs");

            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "dyn1" }),
                (Tab tab, string s) => tab.Text = s);

            Assert.AreEqual(1, bar.Count, "BindItems clears static and rebuilds from dynamic");
        }

        [Test]
        public void BindItems_With_Empty_List_Leaves_TabBar_Empty()
        {
            var bar = OpenBar("<TabBar id='bar'><Tab text='static'/></TabBar>");
            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new string[0]),
                (Tab tab, string s) => tab.Text = s);
            Assert.AreEqual(0, bar.Count);
            Assert.AreEqual(-1, bar.SelectedIndex);
        }

        [Test]
        public void ReSolve_After_BindItems_Replaced_Static_Tabs_Keeps_Dynamic_Tabs()
        {
            // 与 Carousel 同构：静态 <Tab> 被 BindItems 重建销毁后 ReSolve 不得崩溃；
            // 且 OnAfterApply 的 CollectStaticTabs 不得把已销毁的静态 Tab 收回 _tabs、
            // 丢掉动态建的 Tab。
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab text='static1'/><Tab text='static2'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var bar = screen.Get<TabBar>("bar");
            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "dyn1", "dyn2" }),
                (Tab tab, string s) => tab.Text = s);

            Assert.DoesNotThrow(() => screen.ReSolve());
            Assert.AreEqual(2, bar.Count, "dynamic tabs survive the ReSolve");
            Assert.AreEqual("dyn1", bar.GetAt(0).GameObject.transform.Find("Label")
                .GetComponent<TMPro.TMP_Text>().text);
        }

        [Test]
        public void BindItems_With_Custom_Template_Resolves_Tab_Via_GetComponentInChildren()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='MyTab'><Frame id='wrap'><Tab id='tab'/></Frame></Template>
  <Screen name='S'>
    <TabBar id='bar' itemTemplate='MyTab'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");
            using var sub = bar.BindItems<string, IControl>(
                Observable.Return<IReadOnlyList<string>>(new[] { "X", "Y" }),
                (slot, s) => slot.Get<Tab>("tab").Text = s);

            Assert.AreEqual(2, bar.Count);
            Assert.AreEqual("X", bar.GetAt(0).GameObject.transform.Find("Label").GetComponent<TMPro.TMP_Text>().text);
        }

        // ───── wrapper rows: a row is the template's root, not the Tab inside it ─────

        // A row whose root is NOT the <Tab>: the card face is a Tab, the row also carries siblings of
        // it (a status line, a button). The root is a VStack because a Tab cannot hug its content.
        private const string WrapperRow =
            "<Template name='Row'><VStack id='row'>"
            + "<Tab id='tab' height='40'/><Text id='status'>-</Text><Btn id='go' width='40' height='20'/>"
            + "</VStack></Template>";

        private static PromptUGUI.Application.Screen OpenWrapperBar(string barChildren = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{WrapperRow}<Screen name='S'>
  <TabBar id='bar' direction='vertical' allowSwitchOff='true' itemTemplate='Row'>{barChildren}</TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        // What the bar lays out: its active children (retired rows may sit in an inactive pool until
        // their deferred Destroy in Play mode).
        private static List<UnityEngine.Transform> LiveRows(TabBar bar)
        {
            var rows = new List<UnityEngine.Transform>();
            foreach (UnityEngine.Transform child in bar.RectTransform)
                if (child.gameObject.activeSelf) rows.Add(child);
            return rows;
        }

        [Test]
        public void Wrapper_rows_are_removed_whole_on_every_push()
        {
            var screen = OpenWrapperBar();
            var bar = screen.Get<TabBar>("bar");
            var feed = new Subject<IReadOnlyList<string>>();
            using var sub = bar.BindItems<string, IControl>(feed,
                (row, s) => row.Get<Text>("status").TextValue = s);

            foreach (var items in new[] { new[] { "a", "b", "c" }, new[] { "d" }, new[] { "e", "f" } })
            {
                feed.OnNext(items);

                Assert.AreEqual(items.Length, bar.Count);
                var live = LiveRows(bar);
                Assert.AreEqual(items.Length, live.Count,
                    $"after pushing {items.Length} rows only those rows are left in the bar — no orphaned "
                    + "status / button siblings of a disposed Tab");
                for (var i = 0; i < items.Length; i++)
                    Assert.AreEqual(items[i], live[i].Find("status").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual(items.Length, screen.LiveDynamicSubtreeCount,
                    "a retired row is no longer replayed by ReSolve");
            }
        }

        [Test]
        public void Wrapper_rows_release_their_AddTo_subscriptions_when_replaced()
        {
            var screen = OpenWrapperBar();
            var bar = screen.Get<TabBar>("bar");
            var feed = new Subject<IReadOnlyList<string>>();
            var released = 0;
            using var sub = bar.BindItems<string, IControl>(feed,
                (row, s) => Disposable.Create(() => released++).AddTo(row));

            feed.OnNext(new[] { "a", "b", "c" });
            Assert.AreEqual(0, released);

            feed.OnNext(new[] { "d" });
            Assert.AreEqual(3, released, "every replaced row's bind subscriptions go with it");
        }

        [Test]
        public void First_push_removes_static_wrapper_rows_whole()
        {
            var screen = OpenWrapperBar("<Row/><Row/>");
            var bar = screen.Get<TabBar>("bar");
            Assert.AreEqual(2, bar.Count, "two static rows to start with");

            using var sub = bar.BindItems<string, IControl>(
                Observable.Return<IReadOnlyList<string>>(new[] { "x" }),
                (row, s) => row.Get<Text>("status").TextValue = s);

            Assert.AreEqual(1, bar.Count);
            var live = LiveRows(bar);
            Assert.AreEqual(1, live.Count, "the static rows' roots went too, not only their Tabs");
            Assert.AreEqual("x", live[0].Find("status").GetComponent<TMPro.TMP_Text>().text);
            Assert.DoesNotThrow(() => screen.ReSolve(), "the static rows' nodes are skipped once destroyed");
        }
    }
}
