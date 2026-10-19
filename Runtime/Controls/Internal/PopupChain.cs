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

        /// <summary>No catcher: a press outside closes the popup AND goes through to what is under
        /// it — a <c>&lt;Popover dismiss="pass"&gt;</c> (spec 2026-10-19 §4.4 / §5.4).</summary>
        PassThrough,
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

        /// <summary>What it is placed against went away: destroyed, switched off, disabled, scrolled out
        /// of view, or its list row handed to another item (spec 2026-10-19 §4.6).</summary>
        AnchorLost,

        /// <summary>An unrelated popup opened (spec 2026-10-19 §4.4); used to be <see cref="Owner"/>.</summary>
        Replaced,
    }

    /// <summary>One open popup as <see cref="PopupChain"/> sees it.</summary>
    internal interface IPopupEntry
    {
        /// <summary>The control the popup hangs from. Its place in the hierarchy decides ancestry.</summary>
        public GameObject HostGameObject { get; }

        public RectTransform Panel { get; }

        /// <summary>The sortingOrder of the host's root canvas, read now.</summary>
        public int HostRootOrder { get; }

        /// <summary>What the popup is placed against when that is not its host — a <c>&lt;Popover&gt;</c>'s
        /// anchor (spec 2026-10-19 §5.2). Null for a popup whose panel hangs under its host.</summary>
        public Transform Anchor { get; }

        /// <summary>The full-screen catcher while open (<see cref="PopupDismiss.Blocker"/>), else null.</summary>
        public GameObject Blocker { get; }

        public PopupDismiss Dismiss { get; }

        /// <summary><c>Time.frameCount</c> when it last opened: a press from that frame did not happen outside it.</summary>
        public int OpenFrame { get; }

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
        private static bool s_prunePending;

        /// <summary>The top popup — after dropping any whose panel was switched off from above.</summary>
        internal static IPopupEntry Top
        {
            get
            {
                Prune();
                return s_stack.Count > 0 ? s_stack[s_stack.Count - 1] : null;
            }
        }

        internal static bool IsEmpty => s_stack.Count == 0;

        internal static int Count => s_stack.Count;

        internal static bool Contains(IPopupEntry entry) => s_stack.Contains(entry);

        internal static int DepthOf(IPopupEntry entry) => s_stack.IndexOf(entry);

        /// <summary>
        /// Opens <paramref name="entry"/> on the chain: every open entry that is not one of its
        /// ancestors is closed first, top down (<see cref="PopupCloseReason.Replaced"/>). Returns its
        /// depth (0 = the only popup).
        /// </summary>
        internal static int Push(IPopupEntry entry)
        {
            Prune();
            var at = s_stack.IndexOf(entry);
            if (at >= 0) return at;

            // The chain is a line of ancestors, so the first ancestor from the top down ends the walk:
            // everything under it is an ancestor too. Repeated until nothing changes: a close handler
            // may open another popup meanwhile, which would otherwise end up under this one.
            while (true)
            {
                var keep = s_stack.Count;
                while (keep > 0 && !IsAncestorOf(s_stack[keep - 1], entry)) keep--;
                if (keep == s_stack.Count) break;
                CloseFrom(keep, PopupCloseReason.Replaced);
            }

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
        /// Closes every entry whose panel is gone or no longer in the active hierarchy — its host was
        /// switched off from above (spec 2026-10-19 §1.2-5). Run before any question the chain answers;
        /// it cannot run from the panel's <c>OnDisable</c>, where Unity refuses the SetActive calls a
        /// close makes.
        /// </summary>
        internal static void Prune()
        {
            for (var i = 0; i < s_stack.Count; i++)
            {
                var panel = s_stack[i].Panel;
                if (panel != null && panel.gameObject.activeInHierarchy) continue;
                CloseFrom(i, PopupCloseReason.HostGone);
                return;
            }
        }

        /// <summary>A panel was switched off while open: prune next frame, so its close events fire
        /// even if nobody asks the chain anything meanwhile (Play mode).</summary>
        internal static void SchedulePrune()
        {
            if (s_prunePending) return;
            s_prunePending = true;
            _ = PruneNextFrame();
        }

        private static async Awaitable PruneNextFrame()
        {
            await Awaitable.NextFrameAsync();
            if (!s_prunePending) return;   // reset meanwhile
            s_prunePending = false;
            Prune();
        }

        /// <summary>
        /// A layer sorting at <paramref name="sortingOrder"/> appeared: closes every entry whose host
        /// sorts below it (spec 2026-10-18 §4.5), and hides an open <c>&lt;Dropdown&gt;</c> list
        /// there too — TMP sorts it above everything. Entries on the same or a higher layer stay.
        /// </summary>
        internal static void DismissBelow(int sortingOrder)
        {
            PuiDropdown.HideBelow(sortingOrder);
            Prune();
            // Nested entries share their ancestor's root canvas, so the lowest covered one decides.
            for (var i = 0; i < s_stack.Count; i++)
            {
                if (s_stack[i].HostRootOrder >= sortingOrder) continue;
                CloseFrom(i, PopupCloseReason.Covered);
                return;
            }
        }

        /// <summary>
        /// Closes every entry hosted — or anchored — under <paramref name="root"/>: a Screen that starts
        /// closing (<see cref="PopupCloseReason.HostGone"/>), a list row handed to another item
        /// (<see cref="PopupCloseReason.AnchorLost"/>, spec 2026-10-19 §5.5).
        /// </summary>
        internal static void DismissUnder(Transform root, PopupCloseReason reason = PopupCloseReason.HostGone)
        {
            if (root == null) return;
            Prune();
            for (var i = 0; i < s_stack.Count; i++)
            {
                var host = s_stack[i].HostGameObject;
                var anchor = s_stack[i].Anchor;
                var under = (host != null && host.transform.IsChildOf(root))
                            || (anchor != null && anchor.IsChildOf(root));
                if (!under) continue;
                CloseFrom(i, reason);
                return;
            }
        }

        /// <summary>
        /// Which entry a press on <paramref name="hit"/> closes from (spec 2026-10-19 §5.4), or -1. The
        /// top-most entry containing the hit (in its panel, in its anchor, on its catcher) is "inside";
        /// above it, the lowest <see cref="PopupDismiss.PassThrough"/> entry closes, with everything over
        /// it — unless it opened in the frame of the press (the press that opened it). Blocker and Owner
        /// entries are never closed here: a catcher closes its own, a host decides for the other.
        /// </summary>
        internal static int OutsideCloseFrom(Transform hit, int pressFrame)
        {
            var inside = -1;
            if (hit != null)
                for (var i = s_stack.Count - 1; i >= 0; i--)
                {
                    if (!Holds(s_stack[i], hit)) continue;
                    inside = i;
                    break;
                }
            for (var i = inside + 1; i < s_stack.Count; i++)
            {
                var entry = s_stack[i];
                if (entry.Dismiss != PopupDismiss.PassThrough) continue;
                return pressFrame > entry.OpenFrame ? i : -1;
            }
            return -1;
        }

        /// <summary>Closes what a press on <paramref name="hit"/> closes (<see cref="OutsideCloseFrom"/>).</summary>
        internal static bool DismissOutside(Transform hit, int pressFrame)
        {
            Prune();
            var from = OutsideCloseFrom(hit, pressFrame);
            if (from < 0) return false;
            CloseFrom(from, PopupCloseReason.OutsidePress);
            return true;
        }

        /// <summary>Whether any open entry closes on an outside press itself (the probe runs only then).</summary>
        internal static bool HasPassThrough
        {
            get
            {
                for (var i = 0; i < s_stack.Count; i++)
                    if (s_stack[i].Dismiss == PopupDismiss.PassThrough) return true;
                return false;
            }
        }

        private static bool Holds(IPopupEntry entry, Transform hit)
        {
            var panel = entry.Panel;
            if (panel != null && hit.IsChildOf(panel)) return true;
            var anchor = entry.Anchor;
            if (anchor != null && hit.IsChildOf(anchor)) return true;
            var blocker = entry.Blocker;
            return blocker != null && hit.IsChildOf(blocker.transform);
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

        // An entry is a child when its host OR its anchor sits in the other's panel — a Popover is
        // declared anywhere but opens from a control (spec 2026-10-19 §4.9).
        private static bool IsAncestorOf(IPopupEntry ancestor, IPopupEntry entry)
        {
            var panel = ancestor.Panel;
            if (panel == null) return false;
            var host = entry.HostGameObject;
            if (host != null && host.transform.IsChildOf(panel)) return true;
            var anchor = entry.Anchor;
            return anchor != null && anchor.IsChildOf(panel);
        }

        internal static void ResetForTests()
        {
            s_stack.Clear();
            s_prunePending = false;
        }
    }
}
