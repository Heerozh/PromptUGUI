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
            private sealed class Slot
            {
                public readonly IModalEntry Entry;
                public readonly Screen Screen;
                public readonly string Key;
                public ModalEscapeListener Escape;
                public Slot(IModalEntry entry, Screen screen, string key)
                {
                    Entry = entry; Screen = screen; Key = key;
                }
            }

            private static readonly List<Slot> _stack = new();           // 自底向上
            private static readonly Queue<IModalEntry> _waiting = new();  // Queued,等栈空
            private static readonly Queue<IModalEntry> _pending = new();  // 待实例化
            private static bool _materializing;
            private static IModalEntry _inFlight;
            // 每次 teardown（CancelAllForTeardown / CloseAll）自增。MaterializePump 启动时
            // 快照一份；快照与当前值不符即说明自己已被 teardown 抛弃，必须立刻退出、不再触碰
            // 任何共享状态，把所有权让给 teardown 之后新起的 pump。
            private static int _materializeEpoch;

            public static int SortingOrderBase { get; set; } = 1000;

            public static int QueuedCount =>
                _stack.Count + _waiting.Count + _pending.Count + (_inFlight != null ? 1 : 0);

            public static bool IsAnyOpen => _stack.Count > 0;

            /// <summary>测试 / 诊断用:当前栈顶 modal 的 Screen,无则 null。</summary>
            internal static Screen TopScreen =>
                _stack.Count > 0 ? _stack[_stack.Count - 1].Screen : null;

            public static Awaitable<TResult> OpenAsync<TResult>(
                ModalRequest<TResult> request, ModalMode mode = ModalMode.Popup)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                var (entry, awaitable) = ModalEntry<TResult>.Create(request);
                if (mode == ModalMode.Queued && !IsIdle())
                    _waiting.Enqueue(entry);
                else
                    QueueForMaterialize(entry);
                return awaitable;
            }

            // dialog 系统完全空闲:无在屏、无待实例化、无 in-flight、pump 未运行。
            private static bool IsIdle() =>
                _stack.Count == 0 && _pending.Count == 0 && _inFlight == null && !_materializing;

            private static void QueueForMaterialize(IModalEntry entry)
            {
                _pending.Enqueue(entry);
                if (!_materializing) _ = MaterializePump();
            }

            private static async Awaitable MaterializePump()
            {
                if (_materializing) return;
                _materializing = true;
                int epoch = _materializeEpoch;
                try
                {
                    while (_pending.Count > 0)
                    {
                        // await 期间若发生过 teardown，本 pump 已被抛弃 —— 立刻退出：
                        // 既不消费新 entry，也不在下面的 finally 里清共享状态。
                        if (epoch != _materializeEpoch) return;

                        var entry = _pending.Dequeue();
                        if (entry.Resolved) continue;       // CloseAll 在实例化前取消
                        _inFlight = entry;
                        Slot slot = null;
                        try
                        {
                            // 不变量:这是 pump 里唯一的 await,且必须在 _stack.Add 之前。
                            // CloseAll / CancelAllForTeardown 取消 _inFlight 时靠这点保证
                            // 被取消的 entry 此刻还没建出 slot/screen —— 在 _stack.Add 之后
                            // 再加 await 会让被取消的 slot 漏在栈里、泄漏 GameObject。
                            await ModalDocCache.EnsureLoaded(entry.XmlSrc);
                            if (entry.Resolved) continue;   // CloseAll 在 await 期间取消

                            var (screen, key) = OpenModalScreen(entry.XmlSrc);
                            slot = new Slot(entry, screen, key);
                            _stack.Add(slot);

                            var canvas = screen.RootGameObject.GetComponent<Canvas>();
                            canvas.overrideSorting = true;
                            canvas.sortingOrder = SortingOrderBase + _stack.Count - 1;

                            var capturedSlot = slot;
                            var listener = screen.RootGameObject.AddComponent<ModalEscapeListener>();
                            listener.OnEscape = () => OnEscapePressed(capturedSlot);
                            slot.Escape = listener;

                            entry.RunBind(screen, () => OnEntryClosed(capturedSlot));
                            RefreshTopListener();   // 激活刚压入的新栈顶,禁用原栈顶的 ESC listener
                        }
                        catch (Exception ex)
                        {
                            entry.Cancel(ex);
                            if (slot != null) RemoveSlot(slot);
                        }
                        finally
                        {
                            // epoch 不符 = 已被 teardown 抛弃，_inFlight 现归新 pump，别动。
                            if (epoch == _materializeEpoch) _inFlight = null;
                        }
                    }
                }
                finally
                {
                    // 只有仍持有当前 epoch 的 pump 才负责清 latch —— 被抛弃的 pump 去清会
                    // 误伤 teardown 之后新起 pump 的状态。
                    if (epoch == _materializeEpoch)
                    {
                        _materializing = false;
                        PromoteWaiting();
                    }
                }
            }

            // modal 关闭(按钮 close 回调 / ESC):弹栈、销毁 Screen、提升等待队列。
            private static void OnEntryClosed(Slot slot)
            {
                if (!_stack.Contains(slot)) return;       // 已被移除(如 CloseAll)
                RemoveSlot(slot);
                RefreshTopListener();
                PromoteWaiting();
            }

            private static void OnEscapePressed(Slot slot)
            {
                if (_stack.Count == 0 || _stack[_stack.Count - 1] != slot) return;
                slot.Entry.TryEscape(() => OnEntryClosed(slot));
            }

            private static void RemoveSlot(Slot slot)
            {
                _stack.Remove(slot);
                CloseModalScreen(slot.Key);               // 销毁 Screen GameObject
            }

            private static void RefreshTopListener()
            {
                for (int i = 0; i < _stack.Count; i++)
                {
                    var esc = _stack[i].Escape;
                    if (esc != null) esc.enabled = (i == _stack.Count - 1);
                }
            }

            // 栈彻底空了 → 从等待队列拉下一个 Queued modal 作新栈底。
            private static void PromoteWaiting()
            {
                if (!IsIdle()) return;
                while (_waiting.Count > 0)
                {
                    var next = _waiting.Dequeue();
                    if (next.Resolved) continue;
                    QueueForMaterialize(next);
                    return;
                }
            }

            // teardown 收尾：此刻可能还有一个 MaterializePump 挂在它唯一的 await 上。自增
            // epoch 让那个 pump 恢复时立刻退出；清掉 latch / _inFlight，使 teardown 之后的
            // QueueForMaterialize 能正常起一个新 pump。调用方须已清空 _pending / _waiting / _stack。
            private static void AbandonInFlightPump()
            {
                _materializeEpoch++;
                _materializing = false;
                _inFlight = null;
            }

            public static void CloseAll()
            {
                var oce = new OperationCanceledException("Modal cancelled (CloseAll)");
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    _stack[i].Entry.Cancel(oce);
                    CloseModalScreen(_stack[i].Key);
                }
                _stack.Clear();
                _inFlight?.Cancel(oce);
                while (_pending.Count > 0) _pending.Dequeue().Cancel(oce);
                while (_waiting.Count > 0) _waiting.Dequeue().Cancel(oce);
                AbandonInFlightPump();
            }

            // UI.UnloadAll / UI.ResetForTests 调用:取消所有 await,但不关 Screen
            // —— modal Screen 在 UI._open 里,由 teardown 的 _open 循环统一关。
            internal static void CancelAllForTeardown()
            {
                var oce = new OperationCanceledException("Modal cancelled (UI teardown)");
                foreach (var slot in _stack) slot.Entry.Cancel(oce);
                _stack.Clear();
                _inFlight?.Cancel(oce);
                while (_pending.Count > 0) _pending.Dequeue().Cancel(oce);
                while (_waiting.Count > 0) _waiting.Dequeue().Cancel(oce);
                AbandonInFlightPump();
            }

#if UNITY_EDITOR
            internal static void InvalidateCacheForEditor(string src) =>
                ModalDocCache.Invalidate(src);
#endif
        }
    }
}
