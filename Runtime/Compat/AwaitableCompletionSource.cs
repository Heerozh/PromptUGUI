#if !UNITY_6000_0_OR_NEWER
using System;
using Cysharp.Threading.Tasks;

namespace UnityEngine
{
    // Polyfill for UnityEngine.AwaitableCompletionSource on Unity < 6, backed by
    // UniTaskCompletionSource. Mirrors Unity's API surface: both the throwing
    // Set* setters and the boolean TrySet* variants (the library uses both).
    public sealed class AwaitableCompletionSource
    {
        private readonly UniTaskCompletionSource _src = new UniTaskCompletionSource();
        public Awaitable Awaitable => _src.Task; // UniTask -> Awaitable (implicit)
        public void SetResult() => _src.TrySetResult();
        public void SetException(Exception exception) => _src.TrySetException(exception);
        public void SetCanceled() => _src.TrySetCanceled();
        public bool TrySetResult() => _src.TrySetResult();
        public bool TrySetException(Exception exception) => _src.TrySetException(exception);
        public bool TrySetCanceled() => _src.TrySetCanceled();
    }

    public sealed class AwaitableCompletionSource<T>
    {
        private readonly UniTaskCompletionSource<T> _src = new UniTaskCompletionSource<T>();
        public Awaitable<T> Awaitable => _src.Task; // UniTask<T> -> Awaitable<T> (implicit)
        public void SetResult(T value) => _src.TrySetResult(value);
        public void SetException(Exception exception) => _src.TrySetException(exception);
        public void SetCanceled() => _src.TrySetCanceled();
        public bool TrySetResult(T value) => _src.TrySetResult(value);
        public bool TrySetException(Exception exception) => _src.TrySetException(exception);
        public bool TrySetCanceled() => _src.TrySetCanceled();
    }
}
#endif
