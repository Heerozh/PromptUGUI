using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 把同步值包成已完成的 <see cref="Awaitable{T}"/>。
    /// Resources resolver 和 EditMode 测试 fake resolver 用。
    /// </summary>
    internal static class AwaitableHelpers
    {
        internal static Awaitable Completed()
        {
            var src = new AwaitableCompletionSource();
            src.SetResult();
            return src.Awaitable;
        }

        internal static Awaitable<T> Completed<T>(T value)
        {
            var src = new AwaitableCompletionSource<T>();
            src.SetResult(value);
            return src.Awaitable;
        }

        internal static Awaitable<T> Faulted<T>(Exception ex)
        {
            var src = new AwaitableCompletionSource<T>();
            src.SetException(ex);
            return src.Awaitable;
        }

        /// <summary>
        /// 等 <paramref name="items"/> 全部结算。按下标顺序逐个 await，中途有异常**不中断**——
        /// 余下的照样 await 完（<see cref="Awaitable"/> 是池化对象，每一个都必须被恰好 await 一次，
        /// 不能丢下不管），最后抛下标最小的那个异常，让错误报告顺序确定（与顺序取源一致）。
        /// 不用 <c>Task.WhenAll</c>：WebGL 没有线程池，库的异步一律 <c>Awaitable</c>。
        /// 2026-09-14 document-load spec §4.2。
        /// </summary>
        internal static async Awaitable WhenAll(IReadOnlyList<Awaitable> items)
        {
            Exception first = null;
            for (var i = 0; i < items.Count; i++)
            {
                try { await items[i]; }
                catch (Exception e) { first ??= e; }
            }
            if (first != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
        }
    }
}
