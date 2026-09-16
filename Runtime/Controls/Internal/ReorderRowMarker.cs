using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Stamps a <see cref="ScrollList"/>'s Content so a <c>&lt;Trigger on="lift"&gt;</c> nested anywhere
    /// inside a row can walk up the transforms and recognise the row: the row is the child of the
    /// marked node on the way up. Installed once in <c>ScrollList.OnAttached</c>, so it is there
    /// before any row — hooks bind while the row is still being instantiated, before the list has
    /// even seen it.
    /// </summary>
    internal sealed class ScrollListContentMarker : MonoBehaviour
    {
        internal ScrollList Owner;
    }

    /// <summary>
    /// The <c>lift</c> / <c>drop</c> event source of one row (spec 2026-09-16 §4.2). Created on demand
    /// by the first hook that resolves to the row — so a row without hooks carries none, and its
    /// absence (or <see cref="HasLiftHook"/> being false) is what tells the driver to apply its own
    /// default lift look (§4.3). Lives on the row's layout host, the direct child of Content.
    ///
    /// <para>Same shape as <c>ExpandableMarker</c> for <c>expand</c> / <c>collapse</c>: controls are
    /// plain C# objects, and a component is what a <c>GetComponentInParent</c>-style walk can find.</para>
    /// </summary>
    internal sealed class ReorderRowMarker : MonoBehaviour
    {
        private readonly Subject<Unit> _lifted = new();
        private readonly Subject<Unit> _dropped = new();
        private List<Action> _shows;

        internal bool IsLifted { get; private set; }

        /// <summary>An authored <c>on="lift"</c> (Trigger / Animation / Show) exists on this row.</summary>
        internal bool HasLiftHook { get; private set; }

        internal Observable<Unit> OnLifted => _lifted;
        internal Observable<Unit> OnDropped => _dropped;

        internal void NoteLiftHook() => HasLiftHook = true;

        /// <summary>
        /// A <c>&lt;Show on="lift"&gt;</c>'s re-evaluation callback: called once now (the block
        /// establishes itself hidden) and on every lift / drop — the registration-style visibility
        /// <c>checked</c> uses, never a subscription the Show owns.
        /// </summary>
        internal void RegisterLiftShow(Action reevaluate)
        {
            (_shows ??= new List<Action>()).Add(reevaluate);
            reevaluate();
        }

        internal void Lift()
        {
            IsLifted = true;
            _lifted.OnNext(Unit.Default);
            Reevaluate();
        }

        internal void Drop()
        {
            IsLifted = false;
            _dropped.OnNext(Unit.Default);
            Reevaluate();
        }

        private void Reevaluate()
        {
            if (_shows == null) return;
            foreach (var show in _shows) show();
        }

        private void OnDestroy()
        {
            _lifted.Dispose();
            _dropped.Dispose();
        }
    }
}
