#if !UNITY_6000_0_OR_NEWER
using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks.CompilerServices;
using UnityEngine;

namespace PromptUGUI.Compat.CompilerServices
{
    // Async method builders for UnityEngine.Awaitable(<T>) on Unity < 6.
    // Delegate to UniTask's builders (which correctly handle state-machine
    // boxing + pooling); Task property wraps the produced UniTask into our shim
    // via implicit conversion.
    public struct AwaitableAsyncMethodBuilder
    {
        private AsyncUniTaskMethodBuilder _inner;
        public static AwaitableAsyncMethodBuilder Create() =>
            new AwaitableAsyncMethodBuilder { _inner = AsyncUniTaskMethodBuilder.Create() };
        public Awaitable Task => _inner.Task; // UniTask -> Awaitable (implicit)
        public void SetResult() => _inner.SetResult();
        public void SetException(Exception e) => _inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _inner.SetStateMachine(stateMachine);
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            => _inner.Start(ref stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public struct AwaitableAsyncMethodBuilder<T>
    {
        private AsyncUniTaskMethodBuilder<T> _inner;
        public static AwaitableAsyncMethodBuilder<T> Create() =>
            new AwaitableAsyncMethodBuilder<T> { _inner = AsyncUniTaskMethodBuilder<T>.Create() };
        public Awaitable<T> Task => _inner.Task; // UniTask<T> -> Awaitable<T> (implicit)
        public void SetResult(T result) => _inner.SetResult(result);
        public void SetException(Exception e) => _inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _inner.SetStateMachine(stateMachine);
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            => _inner.Start(ref stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}
#endif
