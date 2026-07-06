#if !UNITY_6000_0_OR_NEWER
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using PromptUGUI.Compat.CompilerServices;

namespace UnityEngine
{
    // Polyfill for UnityEngine.Awaitable on Unity < 6 (where the type does not
    // exist). Backed by UniTask. sealed class to mirror Unity's reference-type
    // semantics. Compiled out on Unity 6+ where the native type is used.
    [AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder))]
    public sealed class Awaitable
    {
        private readonly UniTask _task;
        public Awaitable(UniTask task) { _task = task; }
        public UniTask.Awaiter GetAwaiter() => _task.GetAwaiter();
        public static implicit operator Awaitable(UniTask task) => new Awaitable(task);
        public static implicit operator UniTask(Awaitable awaitable) => awaitable._task;

        // Static PlayerLoop-based helpers mirroring UnityEngine.Awaitable's API,
        // mapped onto UniTask (WebGL-safe, no threads). Consumer code / samples
        // use these even though the PromptUGUI runtime itself does not.
        public static Awaitable NextFrameAsync(CancellationToken cancellationToken = default)
            => UniTask.NextFrame(cancellationToken);
        public static Awaitable EndOfFrameAsync(CancellationToken cancellationToken = default)
            => UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, cancellationToken); // best-effort end-of-frame
        public static Awaitable FixedUpdateAsync(CancellationToken cancellationToken = default)
            => UniTask.Yield(PlayerLoopTiming.FixedUpdate, cancellationToken);
        public static Awaitable WaitForSecondsAsync(float seconds, CancellationToken cancellationToken = default)
            => UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken); // scaled time, matches Unity
    }

    [AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder<>))]
    public sealed class Awaitable<T>
    {
        private readonly UniTask<T> _task;
        public Awaitable(UniTask<T> task) { _task = task; }
        public UniTask<T>.Awaiter GetAwaiter() => _task.GetAwaiter();
        public static implicit operator Awaitable<T>(UniTask<T> task) => new Awaitable<T>(task);
        public static implicit operator UniTask<T>(Awaitable<T> awaitable) => awaitable._task;
    }
}
#endif
