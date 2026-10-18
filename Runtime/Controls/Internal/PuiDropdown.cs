using System.Collections.Generic;
using TMPro;
using UnityEngine;

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
            return blocker;
        }

        protected override void DestroyBlocker(GameObject blocker)
        {
            s_expanded.Remove(this);
            base.DestroyBlocker(blocker);
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
                if (i < s_expanded.Count) Hide(s_expanded[i]);
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
