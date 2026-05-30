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
        internal static void Install(
            GameObject root,
            Selectable selectable,
            IReadOnlyList<IControl> children,
            string hoverColor, string pressedColor, string selectedColor, string disabledColor)
        {
            var hasAny = !string.IsNullOrEmpty(hoverColor)
                         || !string.IsNullOrEmpty(pressedColor)
                         || !string.IsNullOrEmpty(selectedColor)
                         || !string.IsNullOrEmpty(disabledColor);
            if (!hasAny) return;

            selectable.transition = Selectable.Transition.None;

            Color? hover = string.IsNullOrEmpty(hoverColor) ? null : UI.Theme.Resolve(hoverColor);
            Color? pressed = string.IsNullOrEmpty(pressedColor) ? null : UI.Theme.Resolve(pressedColor);
            Color? selected = string.IsNullOrEmpty(selectedColor) ? null : UI.Theme.Resolve(selectedColor);
            Color? disabled = string.IsNullOrEmpty(disabledColor) ? null : UI.Theme.Resolve(disabledColor);

            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);

            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                InstallReactor(g, hover, pressed, selected, disabled);
            }
        }

        private static void CollectBlocked(Control control, HashSet<GameObject> blocked)
        {
            if (control == null) return;
            var optedOut = !control.StateReact;
            var nestedSource = control.GameObject != null
                               && control.GameObject.GetComponent<IStateSource>() != null;
            if (optedOut || nestedSource)
            {
                if (control.GameObject != null)
                {
                    foreach (var g in control.GameObject.GetComponentsInChildren<Graphic>(includeInactive: true))
                        blocked.Add(g.gameObject);
                    blocked.Add(control.GameObject);
                }
                return;
            }

            foreach (var child in control.Children)
                CollectBlocked(child as Control, blocked);
        }

        private static void InstallReactor(Graphic graphic, Color? hover, Color? pressed, Color? selected, Color? disabled)
        {
            if (graphic == null) return;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(hover, pressed, selected, disabled, StateTintReactor.DefaultFade);
        }
    }
}
