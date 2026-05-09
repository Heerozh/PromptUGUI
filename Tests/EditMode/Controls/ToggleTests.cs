using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parses_isOn_initial_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t' isOn='true'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Toggle>("t").IsOn);
        }

        [Test]
        public void Setter_triggers_OnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Toggle>("t");

            bool? last = null;
            t.OnValueChanged.Subscribe(v => last = v);
            t.IsOn = true;
            Assert.AreEqual(true, last);
        }

        [Test]
        public void Same_group_is_mutually_exclusive()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack>
    <Toggle id='a' group='g' isOn='true'/>
    <Toggle id='b' group='g'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Toggle>("a");
            var b = screen.Get<Toggle>("b");

            b.IsOn = true;
            Assert.IsTrue(b.IsOn);
            Assert.IsFalse(a.IsOn, "selecting b in same group should clear a");
        }

        [Test]
        public void Default_text_attr_routes_text_content()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'>静音</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Toggle>("t");
            // Toggle constructs a TMP_Text label child; verify text reached it.
            var labels = t.GameObject.GetComponentsInChildren<TMPro.TMP_Text>();
            Assert.That(labels, Has.Some.Property("text").EqualTo("静音"));
        }

        [Test]
        public void Geometry_RootHasNoImage()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var t = UI.Open("S").Get<Toggle>("t");
            Assert.IsNull(t.GameObject.GetComponent<UnityEngine.UI.Image>(),
                "root Toggle should have no Image (default prefab parity)");
        }

        [Test]
        public void Geometry_BackgroundIsTwentyByTwentyTopLeft()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var t = UI.Open("S").Get<Toggle>("t");
            var bg = t.GameObject.transform.Find("Background") as UnityEngine.RectTransform;
            Assert.IsNotNull(bg, "Background child must exist");
            Assert.AreEqual(new UnityEngine.Vector2(0, 1), bg.anchorMin);
            Assert.AreEqual(new UnityEngine.Vector2(0, 1), bg.anchorMax);
            Assert.AreEqual(new UnityEngine.Vector2(20, 20), bg.sizeDelta);
            Assert.AreEqual(new UnityEngine.Vector2(10, -10), bg.anchoredPosition);
            Assert.IsNotNull(bg.GetComponent<UnityEngine.UI.Image>());
        }

        [Test]
        public void Geometry_CheckmarkIsChildOfBackgroundAndCentered()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var t = UI.Open("S").Get<Toggle>("t");
            var bg = t.GameObject.transform.Find("Background");
            var checkmark = bg.Find("Checkmark") as UnityEngine.RectTransform;
            Assert.IsNotNull(checkmark, "Checkmark must be child of Background");
            Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), checkmark.anchorMin);
            Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), checkmark.anchorMax);
            Assert.AreEqual(new UnityEngine.Vector2(20, 20), checkmark.sizeDelta);
            Assert.AreEqual(UnityEngine.Vector2.zero, checkmark.anchoredPosition);
        }

        [Test]
        public void Geometry_LabelStretchesRightOfBackground()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'>X</Toggle></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var t = UI.Open("S").Get<Toggle>("t");
            var label = t.GameObject.transform.Find("Label") as UnityEngine.RectTransform;
            Assert.IsNotNull(label);
            Assert.AreEqual(new UnityEngine.Vector2(0, 0), label.anchorMin);
            Assert.AreEqual(new UnityEngine.Vector2(1, 1), label.anchorMax);
            Assert.AreEqual(new UnityEngine.Vector2(9, -0.5f), label.offsetMin);
            Assert.AreEqual(new UnityEngine.Vector2(-28, 0), label.offsetMax);
        }

        [Test]
        public void Visual_LabelRaycastTargetTrue()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'>X</Toggle></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var t = UI.Open("S").Get<Toggle>("t");
            var labelGo = t.GameObject.transform.Find("Label").gameObject;
            var tmp = labelGo.GetComponent<TMPro.TMP_Text>();
            Assert.IsTrue(tmp.raycastTarget,
                "Toggle label must be raycast target so clicks register on the right side of the toggle (default prefab behavior)");
        }
    }
}
