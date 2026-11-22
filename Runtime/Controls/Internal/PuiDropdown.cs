using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The <see cref="TMP_Dropdown"/> behind <c>&lt;Dropdown&gt;</c>. TMP clones the whole popup
    /// Template on every Show, and <c>Object.Instantiate</c> only carries serialized state — the
    /// <see cref="ProceduralPanel"/>s inside (a <c>&lt;Scrollbar radius=&gt;</c>'s track and handle)
    /// come out blank. The override of <see cref="CreateDropdownList"/> copies their parameters onto
    /// the clone (2026-09-12 spec §5.5), the way <see cref="PuiButton"/> / <see cref="PuiToggle"/> hook
    /// the other uGUI components.
    ///
    /// <para>It also keeps the set of open lists (spec 2026-10-18 §4.4): TMP closes its list on the
    /// EventSystem's Cancel, which the Escape listeners never hear about, so the
    /// <c>EscapeChain</c> closes an open list itself — and a layer that covers the page hides it,
    /// since TMP sorts the list above everything.</para>
    /// </summary>
    internal sealed class PuiDropdown : TMP_Dropdown
    {
        // Open = TMP's click catcher is up: it goes the moment Hide() starts the fade-out.
        private static readonly List<PuiDropdown> s_expanded = new();

        internal static int ExpandedCount => s_expanded.Count;

        protected override GameObject CreateDropdownList(GameObject template)
        {
            var clone = base.CreateDropdownList(template);
            ProceduralPanel.CopyStateToClone(template.transform, clone.transform);
            return clone;
        }

        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            var blocker = base.CreateBlocker(rootCanvas);
            if (!s_expanded.Contains(this)) s_expanded.Add(this);
            // After TMP's own Hide listener: by the time this runs the list is down, and it was the user who clicked.
            var button = blocker != null ? blocker.GetComponent<UnityEngine.UI.Button>() : null;
            if (button != null) button.onClick.AddListener(CueUserCloseJustNow);
            return blocker;
        }

        protected override void DestroyBlocker(GameObject blocker)
        {
            if (s_expanded.Remove(this))
            {
                _sfxClosedFrame = Time.frameCount;
                _sfxCloseCued = _sfxPickCued;
                // A row clicked that changed nothing: no select was raised, and the close is the user's own.
                if (_sfxPick) CueClose();
            }
            _sfxPick = false;
            _sfxPickCued = false;
            base.DestroyBlocker(blocker);
        }

        // ── UI sound cues (spec 2026-11-23-ui-sfx-cues §7.5) ───────────────────────────────────────

        /// <summary>The control whose cues this list raises; null for none.</summary>
        internal Control SfxOwner { get; set; }

        private bool _sfxPick;        // a row was clicked by the user while the list is up
        private bool _sfxPickCued;    // …and it raised select: the close that follows is its consequence
        private bool _sfxCloseCued;   // the last close already raised its cue
        private int _sfxClosedFrame = int.MinValue;

        private bool IsListUp => s_expanded.Contains(this);

        public override void OnPointerClick(PointerEventData eventData)
        {
            // TMP opens on any mouse button; only a left click is refused with a deny.
            if (eventData.button == PointerEventData.InputButton.Left && IsActive() && !IsInteractable())
                SfxOwner?.RaiseSfx(PromptUGUI.Lint.SfxRules.Deny);
            var wasUp = IsListUp;
            base.OnPointerClick(eventData);
            if (!wasUp && IsListUp) SfxOwner?.RaiseSfx(PromptUGUI.Lint.SfxRules.Expand);
        }

        // No focus-cursor wake gate here (unlike PuiButton / PuiToggle): the first Submit opens the list, and says so.
        public override void OnSubmit(BaseEventData eventData)
        {
            if (IsActive() && !IsInteractable()) SfxOwner?.RaiseSfx(PromptUGUI.Lint.SfxRules.Deny);
            var wasUp = IsListUp;
            base.OnSubmit(eventData);
            if (!wasUp && IsListUp) SfxOwner?.RaiseSfx(PromptUGUI.Lint.SfxRules.Expand);
        }

        public override void OnCancel(BaseEventData eventData)
        {
            var wasUp = IsListUp;
            base.OnCancel(eventData);
            if (wasUp && !IsListUp) CueClose();
        }

        protected override DropdownItem CreateItem(DropdownItem itemTemplate)
        {
            var item = base.CreateItem(itemTemplate);
            if (item != null && item.toggle != null)
            {
                // TMP writes each row's isOn while it builds the list, before the catcher exists: only a change once the
                // list is up is a user's click on a row.
                item.toggle.onValueChanged.AddListener(_ => { if (IsListUp) _sfxPick = true; });
                item.gameObject.AddComponent<SfxCancelRelay>().Init(this);
            }
            return item;
        }

        /// <summary>
        /// Called by the owner as the value changes: true once when the change is a user's pick (a row clicked while the
        /// list is up) — it raises select, and the close that follows raises nothing.
        /// </summary>
        internal bool TakeUserPick()
        {
            if (!_sfxPick) return false;
            _sfxPick = false;
            _sfxPickCued = true;
            return true;
        }

        /// <summary>The list closed in this frame because of the user (the catcher, a row's Cancel): one collapse.</summary>
        internal void CueUserCloseJustNow()
        {
            if (_sfxClosedFrame == Time.frameCount) CueClose();
        }

        private void CueClose()
        {
            if (_sfxCloseCued) return;
            _sfxCloseCued = true;
            SfxOwner?.RaiseSfx(PromptUGUI.Lint.SfxRules.Collapse);
        }

        protected override void OnDestroy()
        {
            s_expanded.Remove(this);
            base.OnDestroy();
        }

        /// <summary>Hides every open list. False when none was open.</summary>
        internal static bool HideExpanded()
        {
            if (s_expanded.Count == 0) return false;
            for (var i = s_expanded.Count - 1; i >= 0; i--)
            {
                if (i >= s_expanded.Count) continue;
                // The escape chain: the user's Esc / B, one collapse each (spec 2026-11-23-ui-sfx-cues §7.5).
                var dropdown = s_expanded[i];
                Hide(dropdown);
                if (dropdown != null) dropdown.CueUserCloseJustNow();
            }
            return true;
        }

        /// <summary>Hides the open lists whose root canvas sorts below <paramref name="sortingOrder"/>.</summary>
        internal static void HideBelow(int sortingOrder)
        {
            for (var i = s_expanded.Count - 1; i >= 0; i--)
                if (i < s_expanded.Count && RootOrder(s_expanded[i]) < sortingOrder) Hide(s_expanded[i]);
        }

        // Hide() takes the catcher down, and with it the entry. One that cannot (TMP is already
        // fading it out, or the object is gone) is dropped here so the set never holds a stale list.
        private static void Hide(PuiDropdown dropdown)
        {
            if (dropdown != null) dropdown.Hide();
            s_expanded.Remove(dropdown);
        }

        private static int RootOrder(PuiDropdown dropdown)
        {
            if (dropdown == null) return int.MinValue;
            var canvas = dropdown.GetComponentInParent<Canvas>(true);
            return canvas != null ? canvas.rootCanvas.sortingOrder : 0;
        }

        internal static void ResetForTests() => s_expanded.Clear();

        /// <summary>The override, callable without a Canvas / EventSystem: EditMode tests only.</summary>
        internal GameObject CloneListForTests() => CreateDropdownList(template.gameObject);
    }
}
