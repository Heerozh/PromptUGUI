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
            if (!absolutes.HasAny && !modulates.HasAny && !selectedBase.HasValue)
            {
                // Nothing to drive any more (a theme dropped every state colour): every reactor under
                // this control stands down rather than keeping the last skin's values alive.
                foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
                    g.GetComponent<StateTintReactor>()?.Detach();
                return null;
            }

            selectable.transition = Selectable.Transition.None;

            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                StateSubtree.CollectBlocked(child as Control, blocked);
            var foreign = modulates.HasAny ? ForeignTinted(root, selectable) : null;

            var fade = StateTintReactor.DefaultFade;
            var target = selectable.targetGraphic;
            // A procedural surface parents its panel under the host whose Image it retired
            // (ProceduralSurface.EnsurePanel), so while the panel is the targetGraphic that host
            // Image is the one standing down. Structural, not a flag: the surface is reconciled
            // every pass and this has to follow it.
            var retired = target is ProceduralPanel panel ? panel.transform.parent : null;
            StateTintReactor targetReactor = null;
            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                var isTarget = ReferenceEquals(g, target);
                // The retired Image is nobody's target — not the control's (targetGraphic moved to
                // the panel) and not a fan-out one either: it draws nothing while retired, and the
                // reactor that drove it as the target must not keep writing it.
                if (!isTarget && retired != null && g is UnityEngine.UI.Image && g.transform == retired)
                {
                    g.GetComponent<StateTintReactor>()?.Detach();
                    continue;
                }
                // Descendants only matter for the fan-out multiplier: with no modulates, a descendant
                // reactor would be a no-op (identity). Skip them so we don't add idle MonoBehaviours
                // + OnState subscriptions. The targetGraphic always installs (it carries the absolutes
                // and the selection-aware base). A graphic another Selectable tints is skipped too —
                // see ForeignTinted.
                if (!isTarget && (!modulates.HasAny || (foreign != null && foreign.Contains(g))))
                {
                    // …but a graphic that USED to qualify must be told to stand down, or it keeps
                    // driving colour it no longer owns. Reconciled here rather than latched at
                    // install — the same rule the rest of this feature follows.
                    var stale = g.GetComponent<StateTintReactor>();
                    if (stale != null) stale.Detach();
                    continue;
                }
                // Absolutes + selectedBase apply ONLY to the control's base graphic (targetGraphic) —
                // fanning them out would paint label/icon the bg colour. Descendants get the multiplier only.
                var abs = isTarget ? absolutes : default;
                ColorSpec? selBase = isTarget ? selectedBase : null;
                // The control's color= describes ITS bg. A descendant's colour is its own control's,
                // and a fan-out reactor never touches it.
                ColorSpec? authored = isTarget ? authoredBase : null;
                var reactor = InstallReactor(g, abs, modulates, fade, selBase, selected, authored,
                    ownsFill: isTarget);
                if (isTarget) targetReactor = reactor;
            }
            return targetReactor;
        }

        /// <summary>
        /// Graphics under <paramref name="root"/> whose <c>CanvasRenderer</c> colour another
        /// Selectable already drives: a nested Selectable's ColorTint target (a Slider's handle, an
        /// InputField's bg) and a uGUI Toggle's check graphic (its isOn fade). The multiplier lives
        /// on that same colour, and two writers on it overwrite each other on every transition, so
        /// those graphics are left to their Selectable. Null when there are none.
        /// </summary>
        private static HashSet<Graphic> ForeignTinted(GameObject root, Selectable own)
        {
            HashSet<Graphic> set = null;
            foreach (var s in root.GetComponentsInChildren<Selectable>(includeInactive: true))
            {
                if (s == own) continue;
                if (s.transition == Selectable.Transition.ColorTint && s.targetGraphic != null)
                    (set ??= new HashSet<Graphic>()).Add(s.targetGraphic);
                if (s is UnityEngine.UI.Toggle toggle && toggle.graphic != null)
                    (set ??= new HashSet<Graphic>()).Add(toggle.graphic);
            }
            return set;
        }

        private static StateTintReactor InstallReactor(Graphic graphic, StateColorSet absolutes,
            StateColorSet modulates, float fade, ColorSpec? selectedBase, bool selected,
            ColorSpec? authoredBase, bool ownsFill)
        {
            if (graphic == null) return null;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(absolutes, modulates, fade, selectedBase, selected, authoredBase, ownsFill);
            return reactor;
        }
    }
}
