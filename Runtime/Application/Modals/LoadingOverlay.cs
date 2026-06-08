using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// Loading overlay 子系统。独立于 dialog 栈,坐在 dialog 之下的 sortingOrder
    /// 层带。每个 <see cref="Loading.Open"/> 各自一份 overlay Screen,由
    /// <see cref="LoadingHandle"/> 一一对应控制关闭。
    /// </summary>
    internal static class LoadingOverlay
    {
        internal sealed class LoadingEntry
        {
            public string Src;
            public string Text;
            public Action<IScreen> Configure;   // 可选 post-bind 钩子
            public Screen Screen;     // 实例化前为 null
            public string Key;        // _open instance key,实例化前为 null
            public bool Closed;
        }

        private static readonly List<LoadingEntry> _entries = new();
        private static readonly Queue<LoadingEntry> _pending = new();
        private static bool _materializing;
        // 每次 CancelAllForTeardown 自增。MaterializePump 启动时快照一份；快照与当前值不符
        // 即说明自己已被 teardown 抛弃，立刻退出、不再触碰 _materializing。
        private static int _materializeEpoch;

        internal static int ActiveCount => _entries.Count;

        internal static IEnumerable<Screen> ActiveScreens
        {
            get
            {
                foreach (var e in _entries)
                    if (e.Screen != null) yield return e.Screen;
            }
        }

        internal static LoadingHandle Open(string text, Action<IScreen> configure = null)
        {
            var entry = new LoadingEntry { Src = Loading.XmlSrc, Text = text, Configure = configure };
            var handle = new LoadingHandle(entry);
            _entries.Add(entry);
            _pending.Enqueue(entry);
            if (!_materializing) _ = MaterializePump();
            return handle;
        }

        internal static void CloseEntry(LoadingEntry entry)
        {
            if (entry.Closed) return;
            entry.Closed = true;
            if (entry.Screen != null)
            {
                UI.CloseModalScreen(entry.Key);
                entry.Screen = null;
            }
            _entries.Remove(entry);
        }

        internal static void CancelAllForTeardown()
        {
            foreach (var e in _entries) e.Closed = true;
            _entries.Clear();
            _pending.Clear();
            // 此刻可能还有一个 MaterializePump 挂在它的 await 上；自增 epoch 让它恢复时立刻
            // 退出，并清掉 latch，使 teardown 之后的 Open 能正常起一个新 pump。
            _materializeEpoch++;
            _materializing = false;
            // overlay Screen 由 UnloadAll / ResetForTests 的 _open 循环统一关
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
                    // await 期间若发生过 teardown，本 pump 已被抛弃 —— 立刻退出。
                    if (epoch != _materializeEpoch) return;

                    var entry = _pending.Dequeue();
                    if (entry.Closed) continue;          // 实例化前就 Close 了
                    try
                    {
                        await ModalDocCache.EnsureLoaded(entry.Src);
                        if (entry.Closed) continue;

                        var (screen, key) = UI.OpenModalScreen(entry.Src);
                        entry.Screen = screen;
                        entry.Key = key;

                        var canvas = screen.RootGameObject.GetComponent<Canvas>();
                        canvas.overrideSorting = true;
                        canvas.sortingOrder = Loading.SortingOrder;

                        BindText(screen, entry.Text);
                        entry.Configure?.Invoke(screen);   // post-bind 钩子,与 dialog 路径一致
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PromptUGUI] Loading overlay 显示失败: {ex}");
                        entry.Closed = true;
                        _entries.Remove(entry);
                    }
                }
            }
            finally
            {
                // 只有仍持有当前 epoch 的 pump 才清 latch —— 被抛弃的 pump 去清会误伤新 pump。
                if (epoch == _materializeEpoch) _materializing = false;
            }
        }

        private static void BindText(Screen screen, string text)
        {
            try
            {
                var t = screen.Get<PromptUGUI.Controls.Text>("text");
                if (string.IsNullOrEmpty(text)) t.GameObject.SetActive(false);
                else t.TextValue = text;
            }
            catch (KeyNotFoundException) { /* text 元素可选 */ }
        }
    }
}
