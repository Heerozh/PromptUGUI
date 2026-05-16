using System;
using System.Collections.Generic;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static class Modal
        {
            private static readonly Queue<IModalEntry> _queue = new();
            private static readonly HashSet<string> _loadedSrcs = new();
            private static IModalEntry _current;
            private static string _currentScreenName;
            private static AwaitableCompletionSource<bool> _currentWaiter;
            private static bool _pumping;

            public static int SortingOrderBase { get; set; } = 1000;
            public static int QueuedCount => _queue.Count + (_current != null ? 1 : 0);
            public static bool IsAnyOpen => _current != null;

            public static Awaitable<TResult> OpenAsync<TResult>(ModalRequest<TResult> request)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                var (entry, awaitable) = ModalEntry<TResult>.Create(request);
                _queue.Enqueue(entry);
                if (!_pumping) _ = PumpAsync();
                return awaitable;
            }

            internal static IModalEntry EnqueueRequest<TResult>(ModalRequest<TResult> request)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                var (entry, _) = ModalEntry<TResult>.Create(request);
                _queue.Enqueue(entry);
                if (!_pumping) _ = PumpAsync();
                return entry;
            }

            private static async Awaitable PumpAsync()
            {
                if (_pumping) return;
                _pumping = true;
                try
                {
                    while (_queue.Count > 0)
                    {
                        var entry = _queue.Dequeue();
                        if (entry.Resolved) continue;                                       // NEW (1)
                        _current = entry;
                        _currentScreenName = entry.XmlSrc;
                        _currentWaiter = new AwaitableCompletionSource<bool>();
                        try
                        {
                            if (!_loadedSrcs.Contains(entry.XmlSrc))
                            {
                                var xml = await ModalSourceLoader.LoadAsync(entry.XmlSrc);
                                LoadDocument(entry.XmlSrc, xml);
                                _loadedSrcs.Add(entry.XmlSrc);
                            }
                            if (entry.Resolved) continue;                                   // NEW (2)

                            var screen = Open(entry.XmlSrc);
                            var canvas = screen.RootGameObject.GetComponent<Canvas>();
                            canvas.overrideSorting = true;
                            canvas.sortingOrder = SortingOrderBase;

                            var waiter = _currentWaiter;
                            var captured = entry;
                            captured.SetWaker(() => waiter.TrySetResult(true));             // NEW (3)
                            captured.RunBind(screen, () => waiter.TrySetResult(true));

                            var listener = screen.RootGameObject
                                .AddComponent<ModalEscapeListener>();
                            listener.OnEscape = () =>
                                captured.TryEscape(() => waiter.TrySetResult(true));

                            await waiter.Awaitable;

                            if (entry.Resolved && _open.ContainsKey(entry.XmlSrc))
                                Close(entry.XmlSrc);
                        }
                        catch (Exception ex)
                        {
                            entry.Cancel(ex);
                            if (_open.ContainsKey(entry.XmlSrc))
                                Close(entry.XmlSrc);
                        }
                        finally
                        {
                            _current = null;
                            _currentScreenName = null;
                            _currentWaiter = null;
                        }
                    }
                }
                finally { _pumping = false; }
            }

            internal static void CancelAllForTeardown()
            {
                var oce = new OperationCanceledException("Modal cancelled (UI teardown)");
                _current?.Cancel(oce);
                while (_queue.Count > 0) _queue.Dequeue().Cancel(oce);
                _currentWaiter?.TrySetResult(true);
                _current = null;
                _currentScreenName = null;
                _currentWaiter = null;
                _loadedSrcs.Clear();
            }

            public static void CloseAll()
            {
                var oce = new OperationCanceledException("Modal cancelled (CloseAll)");
                _current?.Cancel(oce);
                while (_queue.Count > 0) _queue.Dequeue().Cancel(oce);
                if (_currentScreenName != null && _open.ContainsKey(_currentScreenName))
                    Close(_currentScreenName);
                _currentWaiter?.TrySetResult(true);
                _current = null;
                _currentScreenName = null;
                _currentWaiter = null;
            }

            internal static bool IsModalScreen(string screenName) =>
                _currentScreenName == screenName;

#if UNITY_EDITOR
            internal static void InvalidateCacheForEditor(string src)
            {
                if (string.IsNullOrEmpty(src)) return;
                if (_loadedSrcs.Remove(src))
                {
                    UnloadDocument(src);
                }
            }
#endif
        }
    }
}
