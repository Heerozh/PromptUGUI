using System.Collections.Generic;
using R3;

namespace PromptUGUI.Application {
    /// <summary>
    /// 变体激活集合 + 变更事件源。所有 attr.var 后缀解算都查这个 store；
    /// Screen 订阅 Changed 触发 ReSolve。
    /// </summary>
    public sealed class VariantStore {
        readonly HashSet<string> _active = new();
        readonly Subject<Unit> _changed = new();

        public Observable<Unit> Changed => _changed;

        public bool IsActive(string name) => _active.Contains(name);

        internal IReadOnlyCollection<string> Active => _active;

        public void Set(string name, bool active) {
            bool changed = active ? _active.Add(name) : _active.Remove(name);
            if (changed) _changed.OnNext(Unit.Default);
        }

        /// <summary>测试用——清空所有激活变体，不发 Changed。</summary>
        internal void Reset() => _active.Clear();
    }
}
