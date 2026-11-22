using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// UI sound cues through a real EventSystem, raycasts and input devices (spec 2026-11-23-ui-sfx-cues §11): what EditMode
    /// cannot show — a disabled control swallowing a real click, a closing Screen's sealed input, a pass-through dismiss,
    /// TMP's Dropdown with its coroutines and the Cancel that goes to its selected row.
    /// </summary>
    public class SfxPlayTests
#if ENABLE_INPUT_SYSTEM
        : InputTestFixture
#endif
    {
        private const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";

#if ENABLE_INPUT_SYSTEM
        private Mouse _mouse;
        private Keyboard _kb;
        private Gamepad _pad;
        private List<SfxCue> _cues;   // the controls'
        private List<SfxCue> _all;    // with the Screens' own open / close
        private string _messageBoxSrc;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            DestroyAllEventSystems();
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            _mouse = InputSystem.AddDevice<Mouse>();
            _kb = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _cues = new List<SfxCue>();
            _all = new List<SfxCue>();
            UI.Sfx.OnCue.Subscribe(c =>
            {
                _all.Add(c);
                if (c.Kind != "screen" && c.Kind != "modal") _cues.Add(c);
            });
            _messageBoxSrc = MessageBox.XmlSrc;
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            DestroyAllEventSystems();
            MessageBox.XmlSrc = _messageBoxSrc;
            base.TearDown();
        }

        private static void DestroyAllEventSystems()
        {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        private static Screen Open(string inner, string before = "")
        {
            UI.LoadDocument("sfx-play", Head + before + "<Screen name='S'>" + inner + "</Screen></PromptUGUI>");
            var s = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return s;
        }

        private static Vector2 ScreenOf(RectTransform rt, Vector2 offset = default)
            => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center + offset));

        private static Vector2 ScreenOf(IControl c) => ScreenOf(c.RectTransform);

        private IEnumerator Click(Vector2 at)
        {
            Set(_mouse.position, at);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
        }

        private IEnumerator Tap(ButtonControl button)
        {
            Press(button);
            yield return null;
            Release(button);
            yield return null;
        }

        private static string Describe(IEnumerable<SfxCue> cues) =>
            "[" + string.Join(", ", cues.Select(c => $"{c.Kind}.{c.Action}→{c.Key}")) + "]";
#endif

        // ───── deny, a re-click, a sealed Screen ─────

        [UnityTest]
        public IEnumerator A_real_click_on_a_disabled_Btn_cues_deny()
        {
#if ENABLE_INPUT_SYSTEM
            var s = Open("<Btn id='b' anchor='center' size='160x60' interactable='false'>No</Btn>");
            yield return Click(ScreenOf(s.Get<Btn>("b")));
            Assert.AreEqual("[btn.deny→btn.deny]", Describe(_cues), "a disabled control still takes the click");
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator A_real_click_on_the_selected_tab_cues_nothing()
        {
#if ENABLE_INPUT_SYSTEM
            var s = Open("<TabBar id='bar' anchor='center' size='300x60'><Tab id='a' isOn='true'>A</Tab><Tab id='b'>B</Tab></TabBar>");
            var announced = 0;
            s.Get<TabBar>("bar").OnSelectionChanged.Subscribe(_ => announced++).AddTo(s);

            yield return Click(ScreenOf(s.Get<Tab>("a")));

            Assert.IsEmpty(_cues, Describe(_cues));
            // uGUI re-sends onValueChanged(true) for it; the tab announces nothing (spec 2026-11-23-input-correctness §3).
            Assert.AreEqual(0, announced, "OnSelectionChanged on a re-click of the selected tab");
#else
            yield break;
#endif
        }

        private const string Fading =
            "<Animation anchor='stretch' on='open' reverse-on='close' fade='0:1' duration='0.5s'>{0}</Animation>";

        [UnityTest]
        public IEnumerator A_closing_screen_takes_no_clicks()
        {
#if ENABLE_INPUT_SYSTEM
            var s = Open(string.Format(Fading, "<Btn id='b' anchor='center' size='160x60'>B</Btn>"));
            yield return new WaitForSeconds(0.6f);
            var at = ScreenOf(s.Get<Btn>("b"));

            UI.Close("S");
            Assume.That(s.IsClosing, "precondition: a ghost playing its exit");
            yield return Click(at);

            Assert.IsEmpty(_cues, Describe(_cues));
            Assert.AreEqual("[screen.open→screen.open, screen.close→screen.close]", Describe(_all));
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator A_reorder_released_on_a_closing_screen_cues_nothing()
        {
#if ENABLE_INPUT_SYSTEM
            var s = Open(string.Format(Fading,
                    "<ScrollList id='sl' anchor='top-left' width='150' height='200' itemTemplate='Row' reorder='true'/>"),
                "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>");
            var list = s.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }), (IControl row, string t) => rows.Add(row));
            yield return new WaitForSeconds(0.6f);
            Canvas.ForceUpdateCanvases();

            var p0 = ScreenOf(((Control)rows[0]).LayoutHost);
            Set(_mouse.position, p0);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            for (var i = 1; i <= 6; i++)
            {
                Set(_mouse.position, p0 + new Vector2(0f, -12f * i));
                yield return null;
            }
            Assume.That(_cues.Select(c => c.Key), Has.Member("reorder.lift"), "precondition: the row is up");

            UI.Close("S");
            Release(_mouse.leftButton);
            yield return null;
            yield return null;

            Assert.AreEqual("[reorder.lift→reorder.lift]", Describe(_cues), "the ghost's controls raise nothing");
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator A_pass_through_dismiss_cues_collapse_on_the_press_and_the_click_below_on_the_release()
        {
#if ENABLE_INPUT_SYSTEM
            var s = Open("<Btn id='t' anchor='center' size='120x60'>T</Btn>" +
                         "<Btn id='other' anchor='top-left' size='120x60'>O</Btn>" +
                         "<Popover id='p' on='click@t' transition='0'><Btn id='in' width='100' height='30'>1</Btn></Popover>");
            yield return Click(ScreenOf(s.Get<Btn>("t")));
            Assume.That(s.Get<Popover>("p").IsOpen);
            _cues.Clear();

            Set(_mouse.position, ScreenOf(s.Get<Btn>("other")));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Assert.AreEqual("[popover.collapse→popover.collapse]", Describe(_cues), "closed on the press");
            Release(_mouse.leftButton);
            yield return null;

            Assert.AreEqual("[popover.collapse→popover.collapse, btn.click→btn.click]", Describe(_cues));
            Assert.AreSame(s.Get<Btn>("other"), _cues[1].Source);
#else
            yield break;
#endif
        }

        // ───── Dropdown (TMP) ─────

#if ENABLE_INPUT_SYSTEM
        private static (Dropdown dd, TMP_Dropdown tmp) OpenDropdown(string attrs = "")
        {
            var s = Open($"<Dropdown id='dd' anchor='top-left' margin='20,_,_,20' width='200' height='40' {attrs}/>");
            var dd = s.Get<Dropdown>("dd");
            dd.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "a", "b", "c" })).AddTo(s);
            return (dd, dd.GameObject.GetComponent<TMP_Dropdown>());
        }

        private static RectTransform Row(TMP_Dropdown tmp, int index)
        {
            var list = tmp.transform.Find("Dropdown List");
            Assert.IsNotNull(list, "the list is up");
            foreach (var toggle in list.GetComponentsInChildren<UnityEngine.UI.Toggle>(false))
                if (toggle.name.StartsWith($"Item {index}")) return (RectTransform)toggle.transform;
            Assert.Fail($"row {index}");
            return null;
        }

        private IEnumerator Settle() { yield return new WaitForSecondsRealtime(0.35f); }   // TMP fades the list out
#endif

        [UnityTest]
        public IEnumerator A_dropdown_expands_selects_and_collapses()
        {
#if ENABLE_INPUT_SYSTEM
            var (dd, tmp) = OpenDropdown();

            yield return Click(ScreenOf(dd));                // open
            yield return Click(ScreenOf(Row(tmp, 1)));       // pick another: select only
            yield return Settle();
            yield return Click(ScreenOf(dd));
            yield return Click(ScreenOf(Row(tmp, 1)));       // re-pick the current one: the user's close
            yield return Settle();
            yield return Click(ScreenOf(dd));
            yield return Click(new Vector2(UnityEngine.Screen.width - 5f, 5f));   // the catcher
            yield return Settle();

            Assert.AreEqual(
                "[dropdown.expand→dropdown.expand, dropdown.select→dropdown.select, " +
                "dropdown.expand→dropdown.expand, dropdown.collapse→dropdown.collapse, " +
                "dropdown.expand→dropdown.expand, dropdown.collapse→dropdown.collapse]",
                Describe(_cues));
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Escape_and_gamepad_B_on_a_page_collapse_the_list()
        {
#if ENABLE_INPUT_SYSTEM
            var (dd, tmp) = OpenDropdown();

            yield return Click(ScreenOf(dd));
            yield return Tap(_kb.escapeKey);
            yield return Settle();
            Assert.IsFalse(tmp.IsExpanded);

            yield return Click(ScreenOf(dd));
            yield return Tap(_pad.buttonEast);
            yield return Settle();
            Assert.IsFalse(tmp.IsExpanded);

            Assert.AreEqual(
                "[dropdown.expand→dropdown.expand, dropdown.collapse→dropdown.collapse, " +
                "dropdown.expand→dropdown.expand, dropdown.collapse→dropdown.collapse]",
                Describe(_cues));
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Escape_on_a_list_inside_a_dialog_cues_one_collapse()
        {
#if ENABLE_INPUT_SYSTEM
            var files = new Dictionary<string, string>
            {
                ["test/DropBox"] = Head + @"
  <Screen name='test/DropBox'>
    <Image id='backdrop' anchor='stretch' color='#0000007F' raycastTarget='true'/>
    <Frame id='dialog' anchor='center' size='400x300'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title'/><Text id='text'/><Dropdown id='dd' width='200' height='40'/>
        <Btn id='ok'>OK</Btn><Btn id='cancel'>Cancel</Btn><Btn id='yes'>Yes</Btn><Btn id='no'>No</Btn><Btn id='close'>Close</Btn>
      </VStack>
    </Frame>
  </Screen></PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/DropBox";
            _ = MessageBox.Open("pick", MsgBtn.OK | MsgBtn.Cancel);
            yield return null;
            var dd = UI.Modal.TopScreen.Get<Dropdown>("dd");
            dd.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "a", "b" })).AddTo(UI.Modal.TopScreen);
            yield return null;
            Canvas.ForceUpdateCanvases();

            yield return Click(ScreenOf(dd));
            yield return Tap(_kb.escapeKey);
            yield return Settle();

            Assert.IsTrue(UI.Modal.IsAnyOpen, "only the list closed");
            Assert.AreEqual("[dropdown.expand→dropdown.expand, dropdown.collapse→dropdown.collapse]", Describe(_cues));
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Options_pushed_while_the_list_is_open_cue_nothing_and_a_disabled_dropdown_cues_deny()
        {
#if ENABLE_INPUT_SYSTEM
            var s = Open("<Dropdown id='dd' anchor='top-left' margin='20,_,_,20' width='200' height='40'/>" +
                         "<Dropdown id='off' anchor='top-right' margin='20,20,_,_' width='200' height='40' interactable='false'/>");
            var dd = s.Get<Dropdown>("dd");
            var options = new Subject<IEnumerable<string>>();
            dd.BindOptions(options).AddTo(s);
            options.OnNext(new[] { "a", "b" });

            // First, while no list's catcher covers the page.
            yield return Click(ScreenOf(s.Get<Dropdown>("off")));
            Assert.AreEqual("[dropdown.deny→dropdown.deny]", Describe(_cues));

            yield return Click(ScreenOf(dd));
            _cues.Clear();
            options.OnNext(new[] { "a", "b", "c" });   // SetOptions: Hide + Show
            yield return null;
            Assert.IsEmpty(_cues, Describe(_cues));
#else
            yield break;
#endif
        }
    }
}
