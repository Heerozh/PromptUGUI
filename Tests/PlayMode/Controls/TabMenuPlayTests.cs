using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// The parts of <c>&lt;TabMenu&gt;</c> that need a running player loop: the LitMotion transition
    /// (EditMode never ticks it) and the real canvas sorting the panel relies on.
    /// </summary>
    public class TabMenuPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen OpenMenu(string attrs)
        {
            UI.LoadDocument("t", Header +
                $"<TabMenu id='m' {attrs}><Tab id='a' text='World'/><Tab id='b' text='Guild'/></TabMenu>" +
                Footer);
            return UI.Open("S");
        }

        private static RectTransform Popup(TabMenu m) => (RectTransform)m.RectTransform.Find("Popup");

        [UnityTest]
        public IEnumerator Expand_fades_the_panel_in()
        {
            var m = OpenMenu("transition='0.1s'").Get<TabMenu>("m");
            m.Expand();

            var cg = Popup(m).GetComponent<CanvasGroup>();
            Assert.Less(cg.alpha, 1f, "the panel starts transparent and fades in");

            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(1f, cg.alpha, 0.01f);
        }

        [UnityTest]
        public IEnumerator Collapse_hides_the_panel_only_after_the_transition()
        {
            var m = OpenMenu("transition='0.1s'").Get<TabMenu>("m");
            m.Expand();
            yield return new WaitForSeconds(0.25f);

            m.Collapse();
            Assert.IsFalse(m.IsExpanded, "the state flips immediately — only the visuals lag");
            Assert.IsTrue(Popup(m).gameObject.activeSelf, "…and the panel stays up to be animated out");

            yield return new WaitForSeconds(0.25f);
            Assert.IsFalse(Popup(m).gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Re_expanding_mid_collapse_keeps_the_panel_up()
        {
            var m = OpenMenu("transition='0.3s'").Get<TabMenu>("m");
            m.Expand();
            yield return new WaitForSeconds(0.4f);

            m.Collapse();
            yield return null;              // collapse is now animating out
            m.Expand();                     // …and the user changes their mind

            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(m.IsExpanded);
            Assert.IsTrue(Popup(m).gameObject.activeSelf,
                          "the cancelled collapse must not deactivate a re-opened panel");
            Assert.AreEqual(1f, Popup(m).GetComponent<CanvasGroup>().alpha, 0.01f);
        }

        [UnityTest]
        public IEnumerator Panel_and_blocker_sort_above_the_page()
        {
            var s = OpenMenu("transition='0'");
            var m = s.Get<TabMenu>("m");
            var rootOrder = s.RootGameObject.GetComponent<Canvas>().sortingOrder;

            m.Expand();
            yield return null;

            var panel = Popup(m).GetComponent<Canvas>();
            var blocker = s.RootGameObject.transform.Find(TabMenu.BlockerName).GetComponent<Canvas>();

            Assert.Greater(panel.sortingOrder, blocker.sortingOrder, "the panel sits above its catcher");
            Assert.Greater(blocker.sortingOrder, rootOrder, "…and the catcher above the page");
        }

        [UnityTest]
        public IEnumerator Closing_the_screen_destroys_the_blocker()
        {
            var s = OpenMenu("transition='0'");
            s.Get<TabMenu>("m").Expand();
            yield return null;

            var blocker = s.RootGameObject.transform.Find(TabMenu.BlockerName).gameObject;
            s.Close();
            yield return null;

            Assert.IsTrue(blocker == null, "the catcher lives outside the screen subtree — dispose owns it");
        }
    }
}
