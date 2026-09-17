using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using R3;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// A container whose direct children are pages, exactly one of them active at a time — the
    /// headless page switcher (Qt's <c>QStackedWidget</c>, Flutter's <c>IndexedStack</c>) that a
    /// page's code-driven sub-views (list ↔ detail, form ↔ result) switch through. Spec
    /// <c>2026-09-17-pages-design.md</c>.
    ///
    /// <para><b>The container owns its pages' activeSelf.</b> A page never declares <c>hidden</c>
    /// (lint <c>PUI-PAGES-CHILD-HIDDEN</c>): declared <c>hidden</c> is replayed by every ReSolve and
    /// would hide the selected page. <c>selected</c> is the one attribute, and it is runtime-owned
    /// (<c>RuntimeStateAttr</c>): the declared value is the initial page, a <see cref="Show"/> from
    /// code survives resize / Variant / Theme, and an untouched <c>selected.portrait</c> still
    /// reaches it — the same contract as <c>Tab.isOn</c>.</para>
    ///
    /// <para>Pure container: no Graphic, no ProceduralPanel. For a background, wrap it in a
    /// <c>&lt;Frame&gt;</c>.</para>
    /// </summary>
    public sealed class Pages : Control
    {
        private readonly Subject<string> _selectedChanged = new();
        private string _selected;
        private HashSet<string> _warnedIds;
        private bool _warnedNoId;

        // DSS-D13, same as Frame: a region, so an axis without a size fills the parent.
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(
                sizeSpec.HasHeight ? AnchorVertical.Top : AnchorVertical.Stretch,
                sizeSpec.HasWidth ? AnchorHorizontal.Left : AnchorHorizontal.Stretch);

        /// <summary>
        /// The id of the active page. Setting it is <see cref="Show"/>. Declared <c>selected=</c> is
        /// the initial page; omitted, the first page.
        /// </summary>
        [UIAttr, Preserve]
        public string Selected
        {
            get => _selected;
            set => Show(value);
        }

        /// <summary>The pages' ids in declaration order (a child without an id is not listed).</summary>
        public IReadOnlyList<string> PageIds
        {
            get
            {
                var ids = new List<string>(Children.Count);
                foreach (var child in Children)
                    if (!string.IsNullOrEmpty(child.Id)) ids.Add(child.Id);
                return ids;
            }
        }

        public IControl SelectedPage => FindPage(_selected);

        /// <summary>
        /// Fires with the new page id whenever the active page actually changes — from code or from
        /// a Variant re-apply alike. Setting the current page again is a no-op and does not fire.
        /// </summary>
        public Observable<string> OnSelectedChanged => _selectedChanged;

        /// <summary>
        /// Activates the page with <paramref name="id"/> and deactivates every other one. An id that
        /// names no page is refused with one warning per id and leaves the selection alone.
        /// </summary>
        public void Show(string id)
        {
            if (id == _selected) return;
            if (FindPage(id) == null)
            {
                _warnedIds ??= new HashSet<string>();
                if (_warnedIds.Add(id ?? ""))
                    UILog.Warn(this,
                        $"Pages '{Id}': no page with id '{id}' (pages: {string.Join(", ", PageIds)}); ignoring.");
                return;
            }
            var previous = _selected;
            _selected = id;
            Reconcile();
            if (previous != null) _selectedChanged.OnNext(id);
        }

        internal override string PeekRuntimeState() => _selected;

        // Runs after every attribute pass — the initial one and each ReSolve. The default page is
        // settled here rather than in OnAttached because the children are only linked in by then,
        // and the reconcile re-asserts every page's activeSelf: a page declares no hidden, so
        // ApplyCommon never touches it, and this is the single writer.
        internal override void OnAfterApply()
        {
            if (_selected == null)
                foreach (var child in Children)
                    if (!string.IsNullOrEmpty(child.Id)) { _selected = child.Id; break; }
            Reconcile();
        }

        private IControl FindPage(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var child in Children)
                if (child.Id == id) return child;
            return null;
        }

        private void Reconcile()
        {
            var owner = UI.OwnerScreenOf(this) as PromptUGUI.Application.Screen;
            foreach (var child in Children)
            {
                if (string.IsNullOrEmpty(child.Id) && !_warnedNoId)
                {
                    _warnedNoId = true;
                    var tag = (child as Control)?.SourceNode?.Tag ?? "?";
                    UILog.Warn(this,
                        $"Pages '{Id}': direct child <{tag}> without an id can never be selected; it stays inactive.");
                }
                if (child.Id != null && child.Id == _selected)
                {
                    child.Hidden = false;            // shows are immediate: the page must measure
                    continue;
                }
                // Deactivating during Screen.Open would happen before controls applied later in
                // the pass (an <Add> block into this page, ApplyScales) have measured on an active
                // GameObject — the same hazard Tab.bind defers around. The deferred action re-reads
                // the selection at drain time so an Open-time switch ends up right either way.
                if (owner != null && owner.IsOpening)
                {
                    var page = child;
                    owner.DeferDuringOpen(() =>
                    {
                        if (page.GameObject != null) page.Hidden = page.Id != _selected;
                    });
                }
                else
                    child.Hidden = true;
            }
        }

        public override void Dispose()
        {
            _selectedChanged.Dispose();
            base.Dispose();
        }
    }
}
