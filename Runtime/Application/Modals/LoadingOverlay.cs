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
            public Screen Screen;     // 实例化前为 null
            public string Key;        // _open instance key,实例化前为 null
            public bool Closed;
        }

        private static readonly List<LoadingEntry> _entries = new();
        private static readonly Queue<LoadingEntry> _pending = new();
        private static bool _materializing;

        /// <summary>overlay 的 sortingOrder。须低于 <see cref="UI.Modal.SortingOrderBase"/>。</summary>
        public static int SortingOrder { get; set; } = 500;

        internal static int ActiveCount => _entries.Count;

        internal static IEnumerable<Screen> ActiveScreens
        {
            get
            {
                foreach (var e in _entries)
                    if (e.Screen != null) yield return e.Screen;
            }
        }

        internal static LoadingHandle Open(string text)
        {
            var entry = new LoadingEntry { Src = Loading.XmlSrc, Text = text };
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
            // overlay Screen 由 UnloadAll / ResetForTests 的 _open 循环统一关
        }

        private static async Awaitable MaterializePump()
        {
            if (_materializing) return;
            _materializing = true;
            try
            {
                while (_pending.Count > 0)
                {
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
                        canvas.sortingOrder = SortingOrder;

                        BindText(screen, entry.Text);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PromptUGUI] Loading overlay 显示失败: {ex}");
                        entry.Closed = true;
                        _entries.Remove(entry);
                    }
                }
            }
            finally { _materializing = false; }
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
