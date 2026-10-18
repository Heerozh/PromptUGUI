using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>What an outside press does to an open popup (spec 2026-10-18 §4.1, PC-D7).</summary>
    internal enum PopupDismiss
    {
        /// <summary>A full-screen catcher eats the press and closes the popup — a menu.</summary>
        Blocker,

        /// <summary>No catcher: the press goes through, and the host decides when to close — a
        /// suggestion list closes when its field stops editing.</summary>
        Owner,
    }

    /// <summary>Why a popup closed (spec 2026-10-18 §4.3). Hosts react to some of them — a
    /// suggestion list ends its field's editing when it is <see cref="Covered"/>.</summary>
    internal enum PopupCloseReason
    {
        /// <summary>The host asked: a row picked, a <c>Collapse()</c>, the host disabled or hidden,
        /// another popup opened elsewhere.</summary>
        Owner,

        Escape,

        /// <summary>The catcher was clicked.</summary>
        OutsidePress,

        /// <summary>A higher layer appeared over the host (a modal, a routed page, the Loading overlay).</summary>
        Covered,

        /// <summary>The host went away: deactivated, its Screen started closing, or disposed.</summary>
        HostGone,
    }

    /// <summary>One open popup as <see cref="PopupChain"/> sees it.</summary>
    internal interface IPopupEntry
    {
        /// <summary>The control the popup hangs from. Its place in the hierarchy decides ancestry.</summary>
        public GameObject HostGameObject { get; }

        public RectTransform Panel { get; }

        /// <summary>The sortingOrder of the host's root canvas, read now.</summary>
        public int HostRootOrder { get; }

        /// <summary>Close this popup only. The chain has already taken it off the stack and closes
        /// whatever sat above it first.</summary>
        public void CloseFromChain(PopupCloseReason reason);
    }

    /// <summary>
    /// The popups that are open right now, bottom to top (spec 2026-10-18 §4.2). It replaces the
    /// single <c>TabMenu.s_expanded</c> slot: a popup whose host sits inside an open popup's panel
    /// opens on top of it, anything else first closes the whole chain — so with no nesting this is
    /// exactly "opening one closes the other" (TM-D9).
    /// </summary>
    internal static class PopupChain
    {
        /// <summary>How far above its host's root canvas a depth-0 panel sorts; its catcher sits one
        /// below. Router layering keeps the next page depth clear of it (RPL §4.3).</summary>
        internal const int SortingOffset = 2;

        private static readonly List<IPopupEntry> s_stack = new();

        internal static IPopupEntry Top => s_stack.Count > 0 ? s_stack[s_stack.Count - 1] : null;

        internal static bool IsEmpty => s_stack.Count == 0;

        internal static int Count => s_stack.Count;

        internal static bool Contains(IPopupEntry entry) => s_stack.Contains(entry);

        /// <summary>
        /// Opens <paramref name="entry"/> on the chain: every open entry that is not one of its
        /// ancestors is closed first, top down. Returns its depth (0 = the only popup).
        /// </summary>
        internal static int Push(IPopupEntry entry)
        {
            var at = s_stack.IndexOf(entry);
            if (at >= 0) return at;

            // The chain is a line of ancestors, so the first ancestor from the top down ends the walk:
            // everything under it is an ancestor too.
            var keep = s_stack.Count;
            while (keep > 0 && !IsAncestorOf(s_stack[keep - 1], entry)) keep--;
            if (keep < s_stack.Count) CloseFrom(keep, PopupCloseReason.Owner);

            s_stack.Add(entry);
            return s_stack.Count - 1;
        }

        /// <summary>Closes <paramref name="entry"/> and, first, everything opened on top of it.</summary>
        internal static void Close(IPopupEntry entry, PopupCloseReason reason)
        {
            var at = s_stack.IndexOf(entry);
            if (at >= 0) CloseFrom(at, reason);
        }

        /// <summary>Takes <paramref name="entry"/> off the chain without closing anything — its host is
        /// being disposed, and so are the hosts of anything above it.</summary>
        internal static void Remove(IPopupEntry entry) => s_stack.Remove(entry);

        internal static int PanelOrder(IPopupEntry entry)
        {
            var depth = Mathf.Max(0, s_stack.IndexOf(entry));
            return entry.HostRootOrder + SortingOffset + 2 * depth;
        }

        internal static int BlockerOrder(IPopupEntry entry) => PanelOrder(entry) - 1;

        /// <summary>
        /// A layer sorting at <paramref name="sortingOrder"/> appeared: closes every entry whose host
        /// sorts below it (spec 2026-10-18 §4.5). Entries on the same or a higher layer stay.
        /// </summary>
        internal static void DismissBelow(int sortingOrder)
        {
            // Nested entries share their ancestor's root canvas, so the lowest covered one decides.
            for (var i = 0; i < s_stack.Count; i++)
            {
                if (s_stack[i].HostRootOrder >= sortingOrder) continue;
                CloseFrom(i, PopupCloseReason.Covered);
                return;
            }
        }

        /// <summary>Closes every entry hosted under <paramref name="root"/> — a Screen that starts closing.</summary>
        internal static void DismissUnder(Transform root)
        {
            if (root == null) return;
            for (var i = 0; i < s_stack.Count; i++)
            {
                var host = s_stack[i].HostGameObject;
                if (host == null || !host.transform.IsChildOf(root)) continue;
                CloseFrom(i, PopupCloseReason.HostGone);
                return;
            }
        }

        // Top down to `index`, each taken off before it is told, so a close that reacts by opening or
        // closing another popup sees the chain as it now is.
        private static void CloseFrom(int index, PopupCloseReason reason)
        {
            while (s_stack.Count > index)
            {
                var last = s_stack.Count - 1;
                var entry = s_stack[last];
                s_stack.RemoveAt(last);
                entry.CloseFromChain(reason);
            }
        }

        private static bool IsAncestorOf(IPopupEntry ancestor, IPopupEntry entry)
        {
            var panel = ancestor.Panel;
            var host = entry.HostGameObject;
            return panel != null && host != null && host.transform.IsChildOf(panel);
        }

        // ── Escape bookkeeping carried over unchanged from TabMenu (TM-D16) ──────────────────────
        // Until EscapeChain takes over (spec 2026-10-18 §4.4), a popup's own listener and UI.Modal's
        // both answer one press in an undefined order; whichever runs second must see it as spent.

        private static int s_escapeFrame = int.MinValue;

        internal static void NoteEscapeConsumed() => s_escapeFrame = Time.frameCount;

        /// <summary>
        /// Closes the top popup, if any, and reports whether this Escape belongs to a popup — also
        /// when a popup's own listener already closed it this frame.
        /// </summary>
        internal static bool TryConsumeEscape()
        {
            var top = Top;
            if (top != null)
            {
                NoteEscapeConsumed();
                Close(top, PopupCloseReason.Escape);
                return true;
            }
            return s_escapeFrame == Time.frameCount;
        }

        internal static void ForgetEscapeFrameForTests() => s_escapeFrame = int.MinValue;

        internal static void ResetForTests()
        {
            s_stack.Clear();
            s_escapeFrame = int.MinValue;
        }
    }
}
