using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    public enum TutorialMode { Block, Hint }

    public readonly struct Advance
    {
        internal enum Kind { Default = 0, TapTargetK, TapAnywhereK, WhenK, UntilK }
        internal readonly Kind K;
        internal readonly Func<bool> Predicate;
        internal readonly Func<Awaitable> Condition;
        private Advance(Kind k, Func<bool> p, Func<Awaitable> c) { K = k; Predicate = p; Condition = c; }
        public static Advance TapTarget => new(Kind.TapTargetK, null, null);
        public static Advance TapAnywhere => new(Kind.TapAnywhereK, null, null);
        public static Advance When(Func<bool> predicate) =>
            new(Kind.WhenK, predicate ?? throw new ArgumentNullException(nameof(predicate)), null);
        public static Advance Until(Func<Awaitable> condition) =>
            new(Kind.UntilK, null, condition ?? throw new ArgumentNullException(nameof(condition)));
    }

    /// <summary>一次 UI.Tutorial.Run 的会话句柄；Step 顺序编号，支撑断点续。</summary>
    public sealed class TutorialFlow
    {
        private readonly string _id;
        private readonly int _resume;
        private readonly Action<string, int> _save;
        private int _stepIndex;

        internal TutorialFlow(string id, int resume, Action<string, int> save)
        { _id = id; _resume = resume; _save = save; }

        public Awaitable Step(string target, string text = null,
            TutorialMode mode = TutorialMode.Block, Advance advance = default,
            Side place = Side.Auto, float padding = 8f, float timeout = -1f)
        {
            var kind = advance.K == Advance.Kind.Default
                ? (target != null ? Advance.Kind.TapTargetK : Advance.Kind.TapAnywhereK)
                : advance.K;
            if (kind == Advance.Kind.TapTargetK && target == null)
                throw new ArgumentException("Advance.TapTarget requires a target path");
            if (kind == Advance.Kind.TapAnywhereK && mode == TutorialMode.Hint)
                throw new ArgumentException("Advance.TapAnywhere requires TutorialMode.Block");

            int index = _stepIndex++;
            if (index < _resume) return AwaitableHelpers.Completed();   // fast-forward

            return RunStep(index, new StepConfig
            {
                Target = target,
                Text = text,
                Mode = mode,
                AdvanceKind = kind,
                Predicate = advance.Predicate,
                Condition = advance.Condition,
                Place = place,
                Padding = padding,
                Timeout = timeout,
            });
        }

        private async Awaitable RunStep(int index, StepConfig cfg)
        {
            var view = await UI.Tutorial.EnsureOverlay();
            var acs = new AwaitableCompletionSource();
            view.BeginStep(cfg, acs);
            try { await acs.Awaitable; }
            finally { view.EndStep(); }
            _save?.Invoke(_id, index + 1);
        }

        public Awaitable Navigate(string name, RouteQuery query = null)
        {
            UI.Router.BypassGuardsOnce();
            return UI.Router.Open(name, query);
        }
    }

    internal struct StepConfig
    {
        public string Target, Text;
        public TutorialMode Mode;
        public Advance.Kind AdvanceKind;
        public Func<bool> Predicate;
        public Func<Awaitable> Condition;
        public Side Place;
        public float Padding, Timeout;
    }
}
