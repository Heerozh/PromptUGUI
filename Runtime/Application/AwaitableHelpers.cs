using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 把同步值包成已完成的 <see cref="Awaitable{T}"/>。
    /// Resources resolver 和 EditMode 测试 fake resolver 用。
    /// </summary>
    internal static class AwaitableHelpers
    {
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
    }
}
