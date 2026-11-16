using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// Drag and drop between any controls, across containers and Screens (spec 2026-11-16-drag-drop §6).
        /// The XML declares what can be picked up (<c>drag="kind"</c>) and where it can go
        /// (<c>drop="kinds"</c>); C# hears what went where and keeps the data in step.
        /// </summary>
        public static class Drag
        {
            internal const int DefaultSortingOrder = 4000;
            internal const float DefaultPendingTimeout = 10f;

            /// <summary>Something is lifted and following the pointer (not while a drop waits for its answer).</summary>
            public static bool IsDragging => DragSession.IsDragging;

            /// <summary>A drop is waiting for <see cref="DropEvent.Accept"/> / <see cref="DropEvent.Reject"/>; nothing new lifts meanwhile.</summary>
            public static bool IsPending => DragSession.IsPending;

            /// <summary>The live drag — its <see cref="DragInfo.Target"/> follows the hover — or null.</summary>
            public static DragInfo Current => DragSession.Current;

            /// <summary>Something was lifted.</summary>
            public static Observable<DragInfo> OnLift => DragSession.Lifted;

            /// <summary>The hovered target changed (<see cref="DragInfo.Target"/> null: over nothing that takes it).</summary>
            public static Observable<DragInfo> OnHover => DragSession.Hovered;

            /// <summary>
            /// An accepted drop, anywhere — after the target's own <see cref="Control.OnDrop"/>. The structure is back to
            /// normal when it fires: update the data and push it right there, or <see cref="DropEvent.Defer"/>.
            /// </summary>
            public static Observable<DropEvent> OnDrop => DragSession.Dropped;

            /// <summary>Exactly once per drag, last: <see cref="DragInfo.Result"/> says how it ended.</summary>
            public static Observable<DragInfo> OnEnd => DragSession.Ended;

            /// <summary>Puts a drag back where it came from; ends a wait for an answer (spec §7.7, §7.10).</summary>
            public static void Cancel() => DragSession.CancelFromCode();

            /// <summary>
            /// The sortingOrder a lifted control draws at — above pages, Modal (1000), Toast (2000) and Tutorial (3000)
            /// by default, so the dragged thing never disappears under a layer.
            /// </summary>
            public static int SortingOrder { get; set; } = DefaultSortingOrder;

            /// <summary>
            /// How long (seconds) a deferred drop may wait for its answer before it is given up and goes back; 0 = no
            /// limit. A safety net — a forgotten Accept / Reject must not lock drag and drop for good.
            /// </summary>
            public static float PendingTimeout { get; set; } = DefaultPendingTimeout;

            internal static void ResetSettingsForTests()
            {
                SortingOrder = DefaultSortingOrder;
                PendingTimeout = DefaultPendingTimeout;
            }
        }
    }
}
