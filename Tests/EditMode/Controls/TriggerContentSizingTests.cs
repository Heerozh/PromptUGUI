using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.UI;
using Animation = PromptUGUI.Controls.Animation;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Trigger/Animation 是装饰器型 wrapper，自身无视觉。在 LayoutGroup 子节点上
    // 应当把唯一直接子节点的 resolved size 当作自身 preferred 暴露出来，
    // 否则父 LayoutGroup 看到 0×0 槽位会让 wrapper 内的子节点 visually 重叠
    // （loading-dots 类布局会塌掉）。
    public class TriggerContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Animation_in_HStack_with_sized_Image_reports_image_size_as_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='80' height='20' spacing='10'>
    <Animation id='a' fade='0.25:1' duration='0.3s' on='manual'>
      <Image width='14' height='14' color='#ffffff'/>
    </Animation>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var le = anim.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "Animation under HStack with a single sized child should auto-attach LayoutElement (auto-proxy)");
            Assert.AreEqual(14f, le.preferredWidth, 0.5f);
            Assert.AreEqual(14f, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Trigger_in_HStack_with_sized_Image_reports_image_size_as_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='80' height='20'>
    <Trigger id='t' on='manual'>
      <Image width='14' height='14' color='#ffffff'/>
    </Trigger>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var trig = screen.Get<Trigger>("t");
            var le = trig.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Base Trigger gets the same auto-proxy treatment");
            Assert.AreEqual(14f, le.preferredWidth, 0.5f);
            Assert.AreEqual(14f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void Animation_in_HStack_with_native_Btn_reports_btn_native_size()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='60'>
    <Animation id='a' type='pulse' on='manual'>
      <Btn id='b'>OK</Btn>
    </Animation>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var btn = screen.Get<Btn>("a/b");
            var btnNative = btn.GetNativeSize().Value;
            var le = anim.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(btnNative.x, le.preferredWidth, 0.5f,
                "Animation wraps Btn → preferred matches Btn.GetNativeSize() (propagates native through wrapper)");
            Assert.AreEqual(btnNative.y, le.preferredHeight, 0.5f);
        }

        [Test]
        public void Animation_in_HStack_with_stretched_child_no_LayoutElement()
        {
            // anchor=stretch + no margin → child.sizeDelta = (0,0): size info indeterminate,
            // auto-proxy must NOT report a fake size. Existing "no native" path applies (no LE).
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='80' height='20'>
    <Animation id='a' fade='0:1' on='manual'>
      <Image anchor='stretch' color='#ffffff'/>
    </Animation>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var le = anim.GameObject.GetComponent<LayoutElement>();
            Assert.IsNull(le, "child has no size info → no LE attached on Animation");
        }

        [Test]
        public void Animation_in_HStack_with_multiple_children_no_LayoutElement()
        {
            // Multiple siblings inside _offsetProxy overlap (no internal LayoutGroup);
            // there's no well-defined bounding size → fall through to current "no LE" path.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='80' height='20'>
    <Animation id='a' fade='0:1' on='manual'>
      <Image width='14' height='14' color='#ffffff'/>
      <Image width='10' height='10' color='#888888'/>
    </Animation>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var le = anim.GameObject.GetComponent<LayoutElement>();
            Assert.IsNull(le, "ambiguous content size → auto-proxy declines");
        }

        [Test]
        public void Animation_in_HStack_explicit_size_overrides_auto_proxy()
        {
            // Existing explicit-size path wins (LE.preferred = Animation's own width/height).
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='80' height='20'>
    <Animation id='a' fade='0:1' on='manual' width='30' height='20'>
      <Image width='14' height='14' color='#ffffff'/>
    </Animation>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var le = anim.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(30f, le.preferredWidth, 0.5f);
            Assert.AreEqual(20f, le.preferredHeight, 0.5f);
        }
    }
}
