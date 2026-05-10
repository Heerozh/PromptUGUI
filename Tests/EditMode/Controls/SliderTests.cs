using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class SliderTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parses_min_max_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' min='0' max='10' value='3'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            Assert.AreEqual(3f, s.Value);
        }

        [Test]
        public void Setter_triggers_OnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' min='0' max='1'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            float? last = null;
            s.OnValueChanged.Subscribe(v => last = v);
            s.Value = 0.5f;
            Assert.AreEqual(0.5f, last);
        }

        [Test]
        public void Direction_parses_to_unity_enum()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' direction='vertical'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            var u = s.GameObject.GetComponent<UnityEngine.UI.Slider>();
            Assert.AreEqual(UnityEngine.UI.Slider.Direction.BottomToTop, u.direction);
        }

        [Test]
        public void Geometry_RootHasNoImage()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S").Get<Slider>("s");
            Assert.IsNull(s.GameObject.GetComponent<UnityEngine.UI.Image>());
        }

        [Test]
        public void Geometry_BackgroundYInset()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S").Get<Slider>("s");
            var bg = s.GameObject.transform.Find("Background") as RectTransform;
            Assert.IsNotNull(bg);
            Assert.AreEqual(new Vector2(0, 0.25f), bg.anchorMin);
            Assert.AreEqual(new Vector2(1, 0.75f), bg.anchorMax);
        }

        [Test]
        public void Geometry_FillAreaInset()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S").Get<Slider>("s");
            var fa = s.GameObject.transform.Find("Fill Area") as RectTransform;
            Assert.IsNotNull(fa, "Fill Area child (two words, matches prefab)");
            Assert.AreEqual(new Vector2(0, 0.25f), fa.anchorMin);
            Assert.AreEqual(new Vector2(1, 0.75f), fa.anchorMax);
            Assert.AreEqual(new Vector2(-5, 0), fa.anchoredPosition);
            Assert.AreEqual(new Vector2(-20, 0), fa.sizeDelta);
        }

        [Test]
        public void Geometry_FillSizeDelta()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S").Get<Slider>("s");
            var fill = s.GameObject.transform.Find("Fill Area/Fill") as RectTransform;
            Assert.IsNotNull(fill);
            Assert.AreEqual(new Vector2(0, 0), fill.anchorMin);
            // anchorMax.y=1: Unity Slider.UpdateVisuals() forces y=1 for horizontal fill
            Assert.AreEqual(new Vector2(0, 1), fill.anchorMax);
            Assert.AreEqual(new Vector2(10, 0), fill.sizeDelta);
        }

        [Test]
        public void Geometry_HandleSlideArea()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S").Get<Slider>("s");
            var hsa = s.GameObject.transform.Find("Handle Slide Area") as RectTransform;
            Assert.IsNotNull(hsa, "Handle Slide Area child (three words, matches prefab)");
            Assert.AreEqual(new Vector2(0, 0), hsa.anchorMin);
            Assert.AreEqual(new Vector2(1, 1), hsa.anchorMax);
            Assert.AreEqual(new Vector2(-20, 0), hsa.sizeDelta);
        }

        [Test]
        public void Geometry_HandleIsSimpleNotPreserveAspect()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Slider id='s'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S").Get<Slider>("s");
            var handle = s.GameObject.transform.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, handle.type);
            Assert.IsFalse(handle.preserveAspect);
        }
    }
}
