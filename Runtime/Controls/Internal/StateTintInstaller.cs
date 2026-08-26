using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Installs <see cref="StateTintReactor"/>s on a state-source control's bg + every descendant
    /// <see cref="Graphic"/>, skipping <c>stateReact="false"</c> children and any nested
    /// <see cref="IStateSource"/> subtree (a deeper Btn/Tab/Toggle owns its own graphics), then
    /// switches the Selectable transition to None so the reactors are the single source of truth.
    /// Shared by Btn / Tab / Toggle. Idempotent: re-runs on each Variant ReSolve, reusing reactors.
    /// </summary>
    internal static class StateTintInstaller
    {
        internal static StateTintReactor Install(
            GameObject root,
            Selectable selectable,
            IReadOnlyList<IControl> children,
            StateColorSet absolutes,
            StateColorSet modulates,
            ColorSpec? selectedBase = null,
            bool selected = false,
            ColorSpec? authoredBase = null)
        {
            if (!absolutes.HasAny && !modulates.HasAny && !selectedBase.HasValue) return null;

            selectable.transition = Selectable.Transition.None;

            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                StateSubtree.CollectBlocked(child as Control, blocked);

            var fade = StateTintReactor.DefaultFade;
            var target = selectable.targetGraphic;
            StateTintReactor targetReactor = null;
            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                var isTarget = ReferenceEquals(g, target);
                // Descendants only matter for the fan-out multiplier: with no modulates, a descendant
                // reactor would be a no-op (base × white). Skip them so we don't add idle MonoBehaviours
                // + OnState subscriptions. The targetGraphic always installs (it carries the absolutes
                // and the selection-aware base).
                if (!isTarget && !modulates.HasAny) continue;
                // Absolutes + selectedBase apply ONLY to the control's base graphic (targetGraphic) —
                // fanning them out would paint label/icon the bg colour. Descendants get the multiplier only.
                var abs = isTarget ? absolutes : default;
                ColorSpec? selBase = isTarget ? selectedBase : null;
                // The control's color= describes ITS bg. A descendant carries its own, which this
                // code cannot see, so those keep the first-init Peek.
                ColorSpec? authored = isTarget ? authoredBase : null;
                var reactor = InstallReactor(g, abs, modulates, fade, selBase, selected, authored);
                if (isTarget) targetReactor = reactor;
            }
            return targetReactor;
        }

        private static StateTintReactor InstallReactor(Graphic graphic, StateColorSet absolutes,
            StateColorSet modulates, float fade, ColorSpec? selectedBase, bool selected,
            ColorSpec? authoredBase)
        {
            if (graphic == null) return null;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(absolutes, modulates, fade, selectedBase, selected, authoredBase);
            return reactor;
        }
    }
}
