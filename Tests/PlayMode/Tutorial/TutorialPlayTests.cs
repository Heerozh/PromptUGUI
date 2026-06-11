using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Tutorial
{
    public class TutorialPlayTests
    {
        private const string MainXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='main'>
  <Btn id='target' anchor='center' size='200x80'>GO</Btn>
</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static EventSystem EnsureES() =>
            EventSystem.current ?? new GameObject("ES", typeof(EventSystem)).GetComponent<EventSystem>();

        // 真实 LateUpdate 驱动:Block 步骤的挖洞 raycast filter 在洞内穿透、洞外拦截。
        [UnityTest]
        public IEnumerator BlockStep_MaskFilter_BlocksOutsideHole_PassesInside()
        {
            UI.LoadDocument("main", MainXml);
            UI.Open("main");
            var run = UI.Tutorial.Run("t", async t => await t.Step("main/target", text: "go"));
            yield return null; yield return null;   // 让 LateUpdate 解析目标 + 开洞

            var view = UI.Tutorial.ViewForTests;
            Assert.IsNotNull(view, "overlay view should exist");
            Assert.IsTrue(view.Mask.HoleForTests.HasValue, "hole should be open after target resolves");

            var target = UI.Get("main").Get<Btn>("target");
            // 洞内点 = 目标中心屏幕坐标(overlay 模式相机为 null)
            Vector2 inside = RectTransformUtility.WorldToScreenPoint(null, target.RectTransform.position);
            Assert.IsFalse(view.Mask.IsRaycastLocationValid(inside, null),
                "洞内:filter 应返回 false(穿透到真实控件)");
            // 洞外点:由实时洞矩形右缘 + 余量(mask 本地坐标)反推屏幕坐标,
            // 与分辨率无关(不依赖固定屏幕角落),保证恒在洞外。
            var hole = view.Mask.HoleForTests.Value;
            Vector3 outsideWorld = view.Mask.rectTransform.TransformPoint(
                new Vector3(hole.xMax + 50f, hole.center.y, 0f));
            Vector2 outside = RectTransformUtility.WorldToScreenPoint(null, outsideWorld);
            Assert.IsTrue(view.Mask.IsRaycastLocationValid(outside, null),
                "洞外:filter 应返回 true(拦截)");

            // 收尾:点击目标推进结束
            ExecuteEvents.Execute(target.GameObject, new PointerEventData(EnsureES()),
                ExecuteEvents.pointerClickHandler);
            yield return null;
            run.GetAwaiter().GetResult();
        }

        // TapTarget:对目标 GO 派发真实 click → relay → 步骤推进 → Run 结束。
        [UnityTest]
        public IEnumerator TapTarget_ClickAdvances_StepCompletes()
        {
            UI.LoadDocument("main", MainXml);
            UI.Open("main");
            var run = UI.Tutorial.Run("t", async t => await t.Step("main/target", text: "go"));
            yield return null; yield return null;

            Assert.IsTrue(UI.Tutorial.IsActive, "引导应在进行中");
            var target = UI.Get("main").Get<Btn>("target");
            Assert.IsNotNull(target.GameObject.GetComponent<TutorialClickRelay>(),
                "TapTarget 应在目标 GO 挂 relay");

            ExecuteEvents.Execute(target.GameObject, new PointerEventData(EnsureES()),
                ExecuteEvents.pointerClickHandler);
            yield return null;
            Assert.IsFalse(UI.Tutorial.IsActive, "点击目标应推进并结束单步引导");
            run.GetAwaiter().GetResult();
        }

        // When:不手动 tick,纯靠 LateUpdate 逐帧轮询谓词;翻真后自动推进。
        [UnityTest]
        public IEnumerator AdvanceWhen_PredicateFlips_AdvancesNextFrame()
        {
            bool flag = false;
            var run = UI.Tutorial.Run("t",
                async t => await t.Step(null, text: "x", advance: Advance.When(() => flag)));
            yield return null; yield return null;
            Assert.IsTrue(UI.Tutorial.IsActive, "谓词未满足时引导应继续");

            flag = true;
            yield return null; yield return null;   // LateUpdate 轮询到 true
            Assert.IsFalse(UI.Tutorial.IsActive, "谓词翻真后应自动推进并结束");
            run.GetAwaiter().GetResult();
        }
    }
}
