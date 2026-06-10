using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.Application
{
    // BindItems / itemTemplate 动态实例化的子树（ScrollList / Carousel / TabBar / Markdown
    // 共用 ScreenInstantiator.InstantiateNode）不进 Screen._nodeMap —— scale 必须仍由
    // Screen 统一应用（Nx / <r>r 依赖 _canvasFactor），并参与 resize / Variant 重算。
    public class DynamicSubtreeScaleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static IControl BindOne(IScreen screen, out ScrollList list)
        {
            list = screen.Get<ScrollList>("list");
            IControl captured = null;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                (IControl slot, string s) => captured = slot);
            Assert.IsNotNull(captured, "BindItems should instantiate one slot");
            return captured;
        }

        [Test]
        public void BindItems_card_plain_scale_applied_on_instantiation()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'><Text id='label' scale='0.5'>x</Text></Frame>
  </Template>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slot = BindOne(screen, out _);

            var rt = slot.Get<Text>("label").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(0.5f, rt.localScale.y, 1e-5f);
        }

        [Test]
        public void BindItems_card_device_scale_uses_canvas_factor()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // /1920x1080 = factor 3
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'><Text id='label' scale='2x'>x</Text></Frame>
  </Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slot = BindOne(screen, out _);

            var rt = slot.Get<Text>("label").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void BindItems_card_relative_scale_uses_canvas_factor()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'><Text id='label' scale='0.5r'>x</Text></Frame>
  </Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slot = BindOne(screen, out _);

            // round(3*0.5)=2 → 2/3 — same math as the static-node RelativeScale tests.
            var rt = slot.Get<Text>("label").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void BindItems_card_factor_scale_recomputes_on_resize()
        {
            // Factor scale exists ONLY in the dynamic template — the _hasFactorScale resize
            // gate must discover it from the dynamic subtree, not just _nodeMap.
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'><Text id='label' scale='1x'>x</Text></Frame>
  </Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slot = BindOne(screen, out _);
            var rt = slot.Get<Text>("label").RectTransform;
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);

            size = new UnityEngine.Vector2(3840f, 2160f); // factor 2
            var relay = screen.RootGameObject.GetComponent<RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void BindItems_card_box_preserving_does_not_accumulate_across_resizes()
        {
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            // The scaled node is INSIDE the card root (Frame), not a direct LayoutGroup
            // child — box-preserving compensation runs and must stay idempotent without
            // the ApplyCommon baseline reset that static nodes get from ReSolve.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'>
      <Frame id='inner' anchor='stretch' margin='10,10,10,10' scale='1x'/>
    </Frame>
  </Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slot = BindOne(screen, out _);
            var rt = slot.Get("inner").RectTransform;
            var relay = screen.RootGameObject.GetComponent<RectDimensionsRelay>();

            // factor 3: localScale 1/3, inv 3 → span 3 about 0.5 → [-1, 2]; sizeDelta -20*3 = -60.
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-1f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-60f, rt.sizeDelta.x, 1e-3f);

            // → factor 2: localScale 1/2, inv 2 → [-0.5, 1.5]; sizeDelta -40.
            size = new UnityEngine.Vector2(3840f, 2160f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-3f);

            // → back to factor 3: must equal first reading, NOT compounded.
            size = new UnityEngine.Vector2(5760f, 3240f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-1f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-60f, rt.sizeDelta.x, 1e-3f);
        }

        [Test]
        public void BindItems_rebuild_gives_new_cards_scale_too()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'><Text id='label' scale='1x'>x</Text></Frame>
  </Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");

            var src = new ReactiveProperty<IReadOnlyList<string>>(new[] { "a", "b" });
            IControl last = null;
            list.BindItems(src, (IControl slot, string s) => last = slot);
            Assert.AreEqual(1f / 3f, last.Get<Text>("label").RectTransform.localScale.x, 1e-5f);

            // Rebuild destroys the old cards; the replacement cards must get scale as well.
            src.Value = new[] { "x" };
            Assert.AreEqual(1, list.SlotCount);
            Assert.AreEqual(1f / 3f, last.Get<Text>("label").RectTransform.localScale.x, 1e-5f);
        }

        [Test]
        public void BindItems_card_variant_scale_applies_on_ReSolve()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame width='200' height='50'><Text id='label' scale.portrait='0.5'>x</Text></Frame>
  </Template>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slot = BindOne(screen, out _);
            var rt = slot.Get<Text>("label").RectTransform;

            // landscape (default): variant inactive → identity.
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);

            UI.Orientation.Set(isPortrait: false);
            // back to landscape: baseline restored, identity again.
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);
        }
    }
}
