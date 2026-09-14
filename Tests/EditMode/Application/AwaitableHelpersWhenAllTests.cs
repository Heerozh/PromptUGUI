using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// <c>AwaitableHelpers.WhenAll</c>：全部 await 完才完成；有失败时**仍把余下的全部 await 完**
    /// （<c>Awaitable</c> 是池化对象，每个都必须被恰好 await 一次），最后抛下标最小的那个异常。
    /// 2026-09-14 document-load spec §4.2。
    /// </summary>
    public class AwaitableHelpersWhenAllTests
    {
        private static async Awaitable Track(Awaitable inner, List<string> log, string name)
        {
            try { await inner; log.Add(name + ":ok"); }
            catch (Exception e) { log.Add(name + ":" + e.Message); throw; }
        }

        [Test]
        public void Empty_list_completes_synchronously()
        {
            AwaitableHelpers.WhenAll(new List<Awaitable>()).GetAwaiter().GetResult();
        }

        [Test]
        public void Completes_only_after_every_item_completes()
        {
            var a = new AwaitableCompletionSource();
            var b = new AwaitableCompletionSource();
            var done = false;
            var all = AwaitableHelpers.WhenAll(new List<Awaitable> { a.Awaitable, b.Awaitable });
            _ = Observe(all, () => done = true);

            b.SetResult();
            Assert.IsFalse(done, "a 还没完成");
            a.SetResult();
            Assert.IsTrue(done);
        }

        [Test]
        public void Failure_waits_for_the_rest_and_throws_the_lowest_index()
        {
            var log = new List<string>();
            var a = new AwaitableCompletionSource();
            var b = new AwaitableCompletionSource();
            var c = new AwaitableCompletionSource();
            var items = new List<Awaitable>
            {
                Track(a.Awaitable, log, "a"),
                Track(b.Awaitable, log, "b"),
                Track(c.Awaitable, log, "c"),
            };
            var all = AwaitableHelpers.WhenAll(items);
            Exception caught = null;
            var done = false;
            _ = Observe(all, () => done = true, e => caught = e);

            c.SetException(new InvalidOperationException("c failed"));
            b.SetException(new InvalidOperationException("b failed"));
            Assert.IsFalse(done, "a 还没结算，WhenAll 不能先抛");

            a.SetResult();
            Assert.IsTrue(done);
            Assert.IsNotNull(caught);
            Assert.AreEqual("b failed", caught.Message, "抛下标最小的失败项，不是最先失败的");
            // 三个都被 await 过
            CollectionAssert.AreEquivalent(new[] { "a:ok", "b:b failed", "c:c failed" }, log);
        }

        private static async Awaitable Observe(Awaitable target, Action onDone, Action<Exception> onError = null)
        {
            try { await target; }
            catch (Exception e) { onError?.Invoke(e); }
            onDone();
        }
    }
}
