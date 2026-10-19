using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// <c>&lt;Popover&gt;</c> in a running player loop (spec 2026-10-19 §9): the transition, following in
    /// <c>LateUpdate</c>, the next-frame prune, and <c>InputField.Insert</c> against a field TMP really edits.
    /// Real device presses are the fixture below.
    /// </summary>
    public class PopoverPlayTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame height='40'><Btn id='more' anchor='center' size='30x30'>…</Btn></Frame></Template>
  <Screen name='S'>
    <Frame id='host' anchor='center' size='600x400'>
      <Btn id='a' anchor='center' size='60x40'>A</Btn>
      <Popover id='pop' transition='0.3'><Btn id='inside' width='80' height='30'>1</Btn></Popover>
    </Frame>
    <ScrollList id='list' anchor='top-left' size='200x200' itemTemplate='Row'/>
    <InputField id='f' anchor='bottom-center' width='240' height='44'/>
  </Screen>
</PromptUGUI>";

        private Screen _s;
        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            if (EventSystem.current == null)
            {
#if ENABLE_INPUT_SYSTEM
                _ownEs = new GameObject("EventSystem", typeof(EventSystem),
                                        typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
                _ownEs = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            }
            UI.LoadDocument("t", Xml);
            _s = UI.Open("S");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ownEs != null) Object.Destroy(_ownEs);
        }

        private Popover Pop => _s.Get<Popover>("pop");
        private Btn A => _s.Get<Btn>("a");

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        private static IEnumerator Seconds(float s)
        {
            var end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator The_transition_fades_and_slides_from_the_anchor_side()
        {
            Pop.Open(A);
            yield return null;
            var group = Pop.GameObject.GetComponent<CanvasGroup>();
            var early = WorldRect(Pop.RectTransform);
            Assert.Less(group.alpha, 1f, "fading in");

            yield return Seconds(0.5f);

            var rest = WorldRect(Pop.RectTransform);
            Assert.AreEqual(1f, group.alpha, 1e-3f);
            Assert.AreEqual(rest.xMin, early.xMin, 0.5f, "below the anchor: the slide is vertical only");
            Assert.AreNotEqual(rest.yMax, early.yMax, "it slid in");
        }

        [UnityTest]
        public IEnumerator Following_during_the_slide_lands_on_the_anchor()
        {
            Pop.Open(A);
            yield return null;
            A.RectTransform.anchoredPosition += new Vector2(40f, 30f);   // mid-slide

            yield return Seconds(0.5f);

            var anchor = WorldRect(A.RectTransform);
            var panel = WorldRect(Pop.RectTransform);
            var scale = Pop.RectTransform.lossyScale.y;
            Assert.AreEqual(anchor.yMin - Popover.DefaultGap * scale, panel.yMax, 1f, "below the moved anchor, gap apart");
            Assert.AreEqual(anchor.xMin, panel.xMin, 1f, "start-aligned with the moved anchor");
        }

        [UnityTest]
        public IEnumerator It_follows_a_scrolling_list_in_LateUpdate()
        {
            var list = _s.Get<ScrollList>("list");
            using var _ = list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c", "d", "e", "f", "g" }),
                                         (IControl row, string v) => { });
            yield return null;
            var more = list.Slots[1].Get<Btn>("more");
            Pop.Dismiss = "block";   // nothing here presses; keep the probe out of it
            Pop.Open(more);
            yield return Seconds(0.4f);
            var before = WorldRect(Pop.RectTransform);
            var anchorBefore = WorldRect(more.RectTransform);

            var content = (RectTransform)list.GameObject.transform.Find("Viewport/Content");
            content.anchoredPosition += new Vector2(0f, 20f);
            yield return null;

            Assert.IsTrue(Pop.IsOpen, "still in view");
            var moved = WorldRect(more.RectTransform).y - anchorBefore.y;
            Assert.AreEqual(before.y + moved, WorldRect(Pop.RectTransform).y, 0.5f, "moved with its row");
        }

        [UnityTest]
        public IEnumerator Hiding_an_ancestor_prunes_it_next_frame()
        {
            var closes = new List<PopoverCloseReason>();
            Pop.OnClosed.Subscribe(closes.Add);
            Pop.Open(A);
            yield return null;

            _s.Get<Frame>("host").Hidden = true;
            yield return null;
            yield return null;

            Assert.IsFalse(Pop.IsOpen);
            Assert.IsTrue(PopupChain.IsEmpty, "nothing hidden is left on the chain to eat Escape");
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.HostGone }, closes);
        }

        [UnityTest]
        public IEnumerator Destroying_the_screen_of_an_open_popover_takes_it_off_the_chain()
        {
            Pop.Open(A);
            yield return null;

            Object.Destroy(_s.RootGameObject);   // a scene change, not UI.Close
            yield return null;
            yield return null;

            Assert.IsTrue(PopupChain.IsEmpty, "a later DismissBelow / Prune has nothing dead to trip over");
        }

        // ── InputField.Insert against a field TMP really edits ─────────────────────────────────

        private InputField Field => _s.Get<InputField>("f");
        private PuiInputField Pui => Field.GameObject.GetComponent<PuiInputField>();

        private IEnumerator Edit(string text)
        {
            Field.TextValue = text;
            EventSystem.current.SetSelectedGameObject(Field.GameObject);
            yield return null;
            yield return null;
            Assert.IsTrue(Field.IsEditing, "precondition: editing");
        }

        [UnityTest]
        public IEnumerator Insert_while_editing_replaces_the_live_selection_and_keeps_editing()
        {
            yield return Edit("hello");
            Pui.selectionStringAnchorPosition = 1;
            Pui.selectionStringFocusPosition = 3;

            Assert.IsTrue(Field.Insert("EE"));
            yield return null;

            Assert.AreEqual("hEElo", Field.TextValue);
            Assert.IsTrue(Field.IsEditing, "the focus is left alone");
            Assert.AreEqual(3, Pui.stringPosition, "the caret is after the inserted text");
        }

        [UnityTest]
        public IEnumerator Insert_raises_no_OnInput()
        {
            yield return Edit("hi");
            int input = 0, changed = 0;
            using var a = Field.OnInput.Subscribe(_ => input++);
            using var b = Field.OnValueChanged.Subscribe(_ => changed++);

            Field.Insert(":)");
            yield return null;

            Assert.AreEqual(0, input, "a code write, not typing");
            Assert.AreEqual(1, changed);
        }

        [UnityTest]
        public IEnumerator Tapping_a_Btn_in_the_popover_records_the_caret_on_deselect()
        {
            Pop.Open(A);
            yield return Edit("hello");
            Pui.stringPosition = 2;   // and in the same frame, before any LateUpdate could record it:

            EventSystem.current.SetSelectedGameObject(_s.Get<Btn>("inside").GameObject);
            yield return null;
            Assert.IsFalse(Field.IsEditing, "precondition: the edit ended");

            Field.Insert("X");
            Assert.AreEqual("heXllo", Field.TextValue, "inserted where the edit left the caret");
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Real device presses through <c>InputSystemUIInputModule</c> (spec 2026-10-19 §4.7 / §5.4): a press outside
    /// closes a pass-through popover and still reaches what it landed on; block mode eats it; the panel, the
    /// trigger and a dropdown list inside keep it; B / Escape close it and give the focus back.
    /// </summary>
    public class PopoverDevicePlayTests : InputTestFixture
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Btn id='a' anchor='center' size='80x40'>A</Btn>
    <Btn id='other' anchor='top-left' size='120x60'>Other</Btn>
    <Frame id='area' anchor='bottom-right' size='160x120' color='#334455' raycastTarget='true'/>
    <Popover id='pop' on='click@a' transition='0' padding='10' spacing='6'>
      <Btn id='inside' width='100' height='30'>1</Btn>
      <Grid id='grid' cellSize='40x40' columns='2' width='hug' height='hug'>
        <Btn id='g0'>0</Btn><Btn id='g1'>1</Btn><Btn id='g2'>2</Btn><Btn id='g3'>3</Btn>
      </Grid>
      <Dropdown id='dd' width='120' height='30'/>
    </Popover>
  </Screen>
</PromptUGUI>";

        private Screen _s;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            new GameObject("EventSystem", typeof(EventSystem),
                           typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            _mouse = InputSystem.AddDevice<Mouse>();
            UI.LoadDocument("t", Xml);
            _s = UI.Open("S");
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            base.TearDown();
        }

        private Popover Pop => _s.Get<Popover>("pop");
        private Btn B(string id) => _s.Get<Btn>(id);

        private static Vector2 ScreenOf(RectTransform rt, Vector2 offset = default)
            => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center + offset));

        private IEnumerator Click(Vector2 at)
        {
            Set(_mouse.position, at);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
        }

        private IEnumerator Open()
        {
            yield return Click(ScreenOf(B("a").RectTransform));
            Assert.IsTrue(Pop.IsOpen, "precondition: the trigger opened it");
        }

        [UnityTest]
        public IEnumerator A_mouse_press_outside_closes_it_and_still_clicks_what_is_below()
        {
            var clicks = 0;
            using var _ = B("other").OnClick.Subscribe(__ => clicks++);
            yield return Open();

            yield return Click(ScreenOf(B("other").RectTransform));

            Assert.IsFalse(Pop.IsOpen);
            Assert.AreEqual(1, clicks, "pass-through: the press went on to the button");
        }

        [UnityTest]
        public IEnumerator Pressing_the_trigger_again_closes_it()
        {
            var closes = new List<PopoverCloseReason>();
            Pop.OnClosed.Subscribe(closes.Add);
            yield return Open();

            yield return Click(ScreenOf(B("a").RectTransform));

            Assert.IsFalse(Pop.IsOpen, "not closed by the press and reopened by the click");
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.Trigger }, closes);
        }

        [UnityTest]
        public IEnumerator A_press_on_the_panel_keeps_it()
        {
            yield return Open();
            var panel = Pop.RectTransform;

            // Into the padding: the panel itself, not a control in it.
            yield return Click(ScreenOf(panel, new Vector2(0f, panel.rect.height * 0.5f - 4f)));

            Assert.IsTrue(Pop.IsOpen);
        }

        [UnityTest]
        public IEnumerator A_touch_outside_closes_it()
        {
            var touch = InputSystem.AddDevice<Touchscreen>();
            yield return Open();

            var at = ScreenOf(B("other").RectTransform);
            yield return null;   // a touch in the frame it opened would count as the opening press
            BeginTouch(1, at, screen: touch);
            yield return null;
            EndTouch(1, at, screen: touch);
            yield return null;

            Assert.IsFalse(Pop.IsOpen);
        }

        // TMP reads its Escape from IMGUI, which in the Editor can come a frame after the Input System callback
        // the chain closed the list in; the fixture's keys never reach IMGUI, so the test hands that KeyDown over.
        [UnityTest]
        public IEnumerator Escape_in_a_search_popover_closes_only_the_suggestions_though_TMP_reads_it_a_frame_late()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            UI.LoadDocument("search", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>
  <Screen name='Search'>
    <Btn id='a' anchor='center' size='80x40'>A</Btn>
    <Popover id='pop' on='click@a' transition='0' closeOnEditEnd='true'>
      <InputField id='q' width='200' height='40'><Suggestions id='s' itemTemplate='Row' transition='0'/></InputField>
    </Popover>
  </Screen>
</PromptUGUI>");
            UI.CloseImmediate("S");
            _s = UI.Open("Search");
            var closes = new List<PopoverCloseReason>();
            Pop.OnClosed.Subscribe(closes.Add);
            yield return Open();
            var q = _s.Get<InputField>("q");
            var list = _s.Get<Suggestions>("s");
            Assert.IsTrue(q.BeginEdit());
            yield return null;
            yield return null;
            Assert.IsTrue(q.IsEditing, "precondition: editing");
            q.TextValue = "ap";
            list.Open();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "apple" }), (IControl row, string v) => { });
            Assert.IsTrue(list.IsOpen, "precondition: the list is up");

            Press(keyboard.escapeKey);
            yield return null;   // the chain closes the list
            Assert.IsFalse(list.IsOpen);
            yield return null;   // TMP's KeyDown for that press
            q.GameObject.GetComponent<PuiInputField>().HandleKeyDown(Event.KeyboardEvent("escape"));
            Release(keyboard.escapeKey);
            for (var i = 0; i < 3; i++) yield return null;

            Assert.IsTrue(q.IsEditing, "still editing");
            Assert.AreEqual("ap", q.TextValue, "no rollback");
            Assert.IsTrue(Pop.IsOpen, "the popover stays");
            CollectionAssert.IsEmpty(closes);
        }

        private sealed class OpenOnPointerDown : MonoBehaviour, IPointerDownHandler
        {
            public Popover Popover;

            public void OnPointerDown(PointerEventData e) => Popover.OpenAt(e.position);
        }

        [UnityTest]
        public IEnumerator OpenAt_from_a_pointer_down_does_not_close_itself()
        {
            var area = _s.Get("area").GameObject;
            area.AddComponent<OpenOnPointerDown>().Popover = Pop;

            yield return Click(ScreenOf(area.GetComponent<RectTransform>()));
            yield return null;

            Assert.IsTrue(Pop.IsOpen, "the press that opened it is not a press outside it");
        }

        [UnityTest]
        public IEnumerator Block_mode_eats_the_press()
        {
            Pop.Dismiss = "block";
            var clicks = 0;
            using var _ = B("other").OnClick.Subscribe(__ => clicks++);
            yield return Open();

            yield return Click(ScreenOf(B("other").RectTransform));

            Assert.IsFalse(Pop.IsOpen);
            Assert.AreEqual(0, clicks, "the blocker took it");
        }

        [UnityTest]
        public IEnumerator Picking_from_a_Dropdown_inside_keeps_the_popover()
        {
            var dd = _s.Get<Dropdown>("dd");
            using var _ = dd.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "one", "two", "three" }));
            yield return Open();

            yield return Click(ScreenOf(dd.RectTransform));
            var list = dd.GameObject.transform.root.GetComponentInChildren<TMPro.TMP_Dropdown>()
                           .transform.Find("Dropdown List");
            Assert.IsNotNull(list, "precondition: the list is open");
            var items = list.GetComponentsInChildren<UnityEngine.UI.Toggle>();
            Assert.Greater(items.Length, 1);

            yield return Click(ScreenOf((RectTransform)items[1].transform));
            yield return null;

            Assert.AreEqual(1, dd.Value, "the pick went through");
            Assert.IsTrue(Pop.IsOpen, "the list hangs off the root canvas, yet it is not outside the popover");
        }

        [UnityTest]
        public IEnumerator Gamepad_B_and_Escape_close_it_and_focus_returns_to_the_trigger()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            UI.UseGamepadNavigation();
            EventSystem.current.SetSelectedGameObject(B("a").GameObject);
            Pop.Open(B("a"));
            yield return null;
            Assert.AreSame(B("inside").GameObject, EventSystem.current.currentSelectedGameObject, "caged on open");

            Press(gamepad.buttonEast);
            yield return null;
            Release(gamepad.buttonEast);
            yield return null;
            Assert.IsFalse(Pop.IsOpen);
            Assert.AreSame(B("a").GameObject, EventSystem.current.currentSelectedGameObject);

            Pop.Open(B("a"));
            yield return null;
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(Pop.IsOpen);
            Assert.AreSame(B("a").GameObject, EventSystem.current.currentSelectedGameObject);
        }

        // Game code reading its own back key in Update sees the same press: the flag tells it the UI took it.
        [UnityTest]
        public IEnumerator An_Escape_that_closes_it_is_reported_consumed()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Pop.Open(B("a"));
            yield return null;
            Assert.IsFalse(UI.EscapeConsumedThisFrame);

            Press(keyboard.escapeKey);
            yield return null;   // the frame whose input update takes the press: the game's Update sees it now

            Assert.IsFalse(Pop.IsOpen);
            Assert.IsTrue(UI.EscapeConsumedThisFrame);
            Release(keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(UI.EscapeConsumedThisFrame, "only that frame");
        }

        [UnityTest]
        public IEnumerator The_dpad_stays_inside_a_caged_grid()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            UI.UseGamepadNavigation();
            Pop.Open(B("a"));
            yield return null;
            EventSystem.current.SetSelectedGameObject(B("g2").GameObject);   // bottom-left cell
            var panel = Pop.GameObject.transform;

            foreach (var dir in new[] { gamepad.dpad.left, gamepad.dpad.down, gamepad.dpad.down, gamepad.dpad.right, gamepad.dpad.right })
            {
                Press(dir);
                yield return null;
                Release(dir);
                yield return null;
                var sel = EventSystem.current.currentSelectedGameObject;
                Assert.IsNotNull(sel);
                Assert.IsTrue(sel.transform.IsChildOf(panel), $"{dir.name}: {sel.name} is outside the panel");
            }
        }
    }
#endif
}
