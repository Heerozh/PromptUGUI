using System;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Router
        {
            // ── Page band (spec 2026-10-01-router-page-layering §4) ──────────────────────────────
            //
            // Two Overlay root canvases with the same sortingOrder draw in an order Unity does not
            // define, and it flips when another root canvas comes or goes. So every routed Page gets
            // its own slot from its depth in the route tree: a child always draws above its parent,
            // siblings share a slot, and the whole band stays under Loading / modals / toasts.

            private const int DefaultPageSortingOrderStep = 10;
            private static int _pageSortingOrderStep = DefaultPageSortingOrderStep;

            /// <summary>
            /// sortingOrder of a root routed Page (depth 0). Default 0. Written after the Screen
            /// opens, so it overrides <see cref="UI.CanvasConfigurator"/> on routed Pages.
            /// </summary>
            public static int PageSortingOrderBase { get; set; }

            /// <summary>
            /// Gap between a routed Page and its child Page. Default 10. Each depth owns its slot,
            /// one below it (a page playing its exit) and up to two above it (an expanded TabMenu),
            /// so the step must keep those clear of the next depth.
            /// </summary>
            public static int PageSortingOrderStep
            {
                get => _pageSortingOrderStep;
                set
                {
                    var min = MinPageSortingOrderStep;
                    if (value < min)
                        throw new ArgumentOutOfRangeException(nameof(value), value,
                            $"UI.Router.PageSortingOrderStep must be at least {min}: a page's depth owns its " +
                            "slot, the value below it (the page while it plays its exit) and " +
                            $"{Controls.TabMenu.PopupSortingOffset} above it (an expanded TabMenu), and none " +
                            "of them may reach the next depth.");
                    _pageSortingOrderStep = value;
                }
            }

            private static int MinPageSortingOrderStep => Controls.TabMenu.PopupSortingOffset + 2;

            // The router's mark on a routed canvas (spec §4 / §6): where it sorts and, for a Modal,
            // Escape → Back. `index` is the node's place in _chain; activation is bottom-up, so what
            // sits ahead of it is exactly its registered ancestry and the result is a function of
            // the route map alone — re-applying it can never move a page that is already up.
            private static void ApplyRouteLayer(RouteNode def, Screen screen, int index)
            {
                var root = screen.RootGameObject;
                var canvas = root.GetComponent<Canvas>();
                if (def.Kind == RouteKind.Modal)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = UI.Modal.SortingOrderBase + CountAhead(index, RouteKind.Modal);
                    var esc = root.AddComponent<ModalEscapeListener>();
                    var captured = def.Name;
                    esc.OnEscape = () =>
                    {
                        if (UI.Tutorial.IsBlockingInput) return;
                        // 只栈顶 routed modal 响应;有 ad-hoc 模态在上时让位给它
                        if (IsTop(captured) && !UI.Modal.IsAnyOpen) _ = Back();
                    };
                    return;
                }

                var depth = CountAhead(index, RouteKind.Page);
                var slot = PageSortingOrderBase + depth * PageSortingOrderStep;
                canvas.sortingOrder = slot;
                if (slot >= Loading.SortingOrder)
                    Debug.LogWarning(
                        $"[PromptUGUI] Routed page '{def.Name}' (depth {depth}) sorts at {slot}, at or above " +
                        $"the Loading overlay (Loading.SortingOrder = {Loading.SortingOrder}), so a Loading " +
                        "overlay no longer covers it. Lower UI.Router.PageSortingOrderBase / " +
                        "PageSortingOrderStep, or raise Loading.SortingOrder.", root);
            }

            // Nodes of `kind` ahead of chain index `index`.
            private static int CountAhead(int index, RouteKind kind)
            {
                int n = 0;
                for (int i = 0; i < index && i < _chain.Count; i++)
                    if (_chain[i].Def.Kind == kind) n++;
                return n;
            }

            private static void ResetLayeringForTests()
            {
                PageSortingOrderBase = 0;
                _pageSortingOrderStep = DefaultPageSortingOrderStep;
            }
        }
    }
}
