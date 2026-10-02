using System.Collections.Generic;
using R3;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 变体激活集合 + 变更事件源。所有 attr.var 后缀解算都查这个 store；
    /// Screen 订阅 Changed 触发 ReSolve。
    /// </summary>
    public sealed class VariantStore
    {
        private readonly HashSet<string> _active = new();
        private readonly Subject<Unit> _changed = new();

        public Observable<Unit> Changed => _changed;

        public bool IsActive(string name) => _active.Contains(name);

        internal IReadOnlyCollection<string> Active => _active;

        public void Set(string name, bool active)
        {
            if (Apply(name, active)) _changed.OnNext(Unit.Default);
        }

        /// <summary>
        /// 两个变体一步切完，至多发一次 Changed。互斥的一对（portrait / landscape）分两次
        /// <see cref="Set(string, bool)"/> 会把中间态（两个都激活 / 都不激活）发布出去：每个打开的
        /// Screen 都按它重解算一遍，last-active-wins 拼出的属性组合可能根本不合法（拉伸轴上带尺寸 →
        /// ParseException）。
        /// </summary>
        internal void Set(string name1, bool active1, string name2, bool active2)
        {
            var first = Apply(name1, active1);
            var second = Apply(name2, active2);
            if (first || second) _changed.OnNext(Unit.Default);
        }

        private bool Apply(string name, bool active) => active ? _active.Add(name) : _active.Remove(name);

        /// <summary>测试用——清空所有激活变体，不发 Changed。</summary>
        internal void Reset() => _active.Clear();

        internal void NotifyChangedInternal() => _changed.OnNext(Unit.Default);
    }
}
