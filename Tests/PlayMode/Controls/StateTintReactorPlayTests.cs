using System;
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// StateTintReactor 的颜色 tween 持有目标 Graphic 引用并逐帧写 <c>g.color</c>。当该 Graphic 在
    /// tween 进行中被销毁（真实诱因：Carousel.RebuildIndicator 在 locale / Theme / resize 触发的
    /// ReSolve 中销毁旧指示点，而指示点正处于选中态颜色淡变），LitMotion 的逐帧回调不得写已销毁对象，
    /// 否则抛 MissingReferenceException。需真实 player loop 让 LitMotion 自动推进，故为 PlayMode。
    /// </summary>
    public class StateTintReactorPlayTests
    {
        // 测试用状态源：手动 Push 一个 InteractState 触发 reactor 的颜色 tween。
        private sealed class FakeSource : MonoBehaviour, IStateSource
        {
            private readonly Subject<InteractState> _s = new();
            public InteractState Current { get; private set; } = InteractState.Normal;
            public Observable<InteractState> OnState => _s;
            public void Push(InteractState st) { Current = st; _s.OnNext(st); }
            public void RegisterShow(InteractState state, Action reevaluate) { }
            public bool IsShowStateClaimed(InteractState state) => false;
        }

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;   // 需要真 tween（非同步 apply）
        }

        [TearDown]
        public void TearDown()
        {
            StateTintReactor.TestForceInstant = false;
            UI.ResetForTests();
        }

        // fade 给大值：确保 Graphic 被销毁时 tween 仍远未结束，下一帧仍会被 LitMotion 更新。
        private static (GameObject go, UnityImage img, FakeSource src) MakeTinted()
        {
            var go = new GameObject("tinted", typeof(RectTransform));
            var img = go.AddComponent<UnityImage>();
            img.color = Color.white;
            var src = go.AddComponent<FakeSource>();
            var reactor = go.AddComponent<StateTintReactor>();
            var abs = StateColorSet.ResolveAbsolutes("#ff0000", null, null, null);
            reactor.Configure(abs, default, fade: 10f);
            return (go, img, src);
        }

        // 销毁整个宿主 GameObject —— 指示点的真实销毁方式。
        [UnityTest]
        public IEnumerator Tween_Does_Not_Crash_When_Host_GameObject_Destroyed()
        {
            var (go, _, src) = MakeTinted();
            yield return null;                 // advance past the born frame so the push starts a real tween (not the born-frame instant-snap)
            src.Push(InteractState.Hover);     // 起一个 10s 颜色 tween
            yield return null;                 // 让 tween 跑起来
            UnityEngine.Object.Destroy(go);    // play 模式：帧末销毁
            yield return null;
            yield return null;
            LogAssert.NoUnexpectedReceived();  // 不得抛 MissingReferenceException
        }

        // 仅销毁 Graphic 组件（宿主 GO 与 reactor 仍在 → OnDestroy 不触发）——
        // 直击根因：reactor 的 tween 必须容忍其 Graphic 先行消失。
        [UnityTest]
        public IEnumerator Tween_Does_Not_Crash_When_Graphic_Component_Destroyed()
        {
            var (go, img, src) = MakeTinted();
            yield return null;                 // advance past the born frame so the push starts a real tween (not the born-frame instant-snap)
            src.Push(InteractState.Hover);
            yield return null;
            UnityEngine.Object.Destroy(img);   // 只销毁 Image
            yield return null;
            yield return null;
            LogAssert.NoUnexpectedReceived();
            UnityEngine.Object.Destroy(go);
        }
    }
}
