#if !UNITY_6000_0_OR_NEWER
#pragma warning disable CS1998 // async without await: intentional for sync-completion tests
using NUnit.Framework;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace PromptUGUI.Tests.Compat
{
    public class AwaitableShimTests
    {
        private static async Awaitable<int> ProduceImmediate() => 7;

        private static async Awaitable<int> ProduceAfterYield()
        {
            await UniTask.Yield();
            return 9;
        }

        [Test]
        public void AsyncBuilder_SyncCompletion_ReturnsValue()
        {
            // async Awaitable<int> with no await completes synchronously;
            // exercises the builder's SetResult + Task property path.
            var v = ProduceImmediate().GetAwaiter().GetResult();
            Assert.AreEqual(7, v);
        }

        [Test]
        public void ImplicitConversion_UniTask_To_Awaitable_And_Back()
        {
            UniTask<int> u = UniTask.FromResult(5);
            Awaitable<int> a = u;      // implicit UniTask<int> -> Awaitable<int>
            UniTask<int> back = a;      // implicit Awaitable<int> -> UniTask<int>
            Assert.AreEqual(5, back.GetAwaiter().GetResult());
        }

        [Test]
        public void NonGeneric_Awaitable_Awaits()
        {
            UniTask u = UniTask.CompletedTask;
            Awaitable a = u;            // implicit UniTask -> Awaitable
            a.GetAwaiter().GetResult(); // completes without throwing
            Assert.Pass();
        }
    }
}
#endif
