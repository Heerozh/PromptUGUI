using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Toasts;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Toast
{
    public class ToastLifecyclePlayModeTests
    {
        private const string ToastXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Toast'>
    <Text id='text' anchor='center' align='center' fontSize='40' color='white'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "test/Toast" ? ToastXml : null);
            UI.Toast.XmlSrc = "test/Toast";
            UI.Toast.DefaultPosition = ToastPosition.Bottom;
            UI.Toast.DefaultStackMode = ToastStackMode.Stacked;
            // 收紧时长让测试快，但留足窗口避开帧 dt 抖动：
            // 单条生命周期 = FadeIn(0.1) + Hold(0.3) + FadeOut(0.1) = 0.5s。
            UI.Toast.FadeInSeconds = 0.1f;
            UI.Toast.FadeOutSeconds = 0.1f;
            UI.Toast.HoldBase = 0.3f;
            UI.Toast.HoldMin = 0.3f;
            UI.Toast.HoldMax = 0.5f;
            UI.Toast.HoldPerChar = 0f;
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Toast_fades_in_holds_then_self_destroys()
        {
            UI.Toast.Show("hi");
            yield return null;
            Assert.AreEqual(1, ToastOverlay.ActiveCount);

            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            var cg = screen.RootGameObject.GetComponent<CanvasGroup>();

            yield return new WaitForSeconds(0.05f);    // 淡入中（0~0.1）
            Assert.Greater(cg.alpha, 0f);

            yield return new WaitForSeconds(0.15f);    // t≈0.2：淡入已完成、停留中（0.1~0.4）
            Assert.AreEqual(1f, cg.alpha, 0.05f);

            yield return new WaitForSeconds(0.7f);      // t≈0.9 > 0.5 生命周期 → 自毁
            Assert.AreEqual(0, ToastOverlay.ActiveCount, "toast 应已自销毁");
        }

        [UnityTest]
        public IEnumerator Sequential_second_appears_after_first_gone()
        {
            UI.Toast.Show("a", mode: ToastStackMode.Sequential);
            UI.Toast.Show("b", mode: ToastStackMode.Sequential);
            yield return null;
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
            Assert.AreEqual(1, ToastOverlay.QueuedCount);

            yield return new WaitForSeconds(0.7f);     // a 走完整生命周期(0.5) → b 提升并开始
            // b 在 a 结束后才被提升，再走 0.5s；只断言最终全部归零（队列排空 + 无可见）
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(0, ToastOverlay.ActiveCount);
            Assert.AreEqual(0, ToastOverlay.QueuedCount);
        }

        [UnityTest]
        public IEnumerator Stacked_two_coexist_then_collapse()
        {
            UI.Toast.Show("first", ToastPosition.Bottom);
            UI.Toast.Show("second", ToastPosition.Bottom);
            yield return null;
            Assert.AreEqual(2, ToastOverlay.ActiveCount);

            yield return new WaitForSeconds(1.0f);     // 两条都走完生命周期(各 0.5s)
            Assert.AreEqual(0, ToastOverlay.ActiveCount);
        }
    }
}
