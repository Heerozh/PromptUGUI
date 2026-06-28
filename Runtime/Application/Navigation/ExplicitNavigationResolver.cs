using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Variants;
using UnityEngine.UI;

namespace PromptUGUI.Application.Navigation
{
    internal static class ExplicitNavigationResolver
    {
        /// <summary>
        /// Wires explicit nav directions for every Selectable control that declares
        /// <c>navUp/navDown/navLeft/navRight</c> or <c>nav="none"</c>.
        /// </summary>
        /// <param name="inactiveNodes">
        /// Nodes belonging to currently-inactive Add blocks (T5-M1).  These source nodes are
        /// skipped so hidden controls do not get their navigation overwritten.  Pass
        /// <c>null</c> at Screen.Open — no Add-block nodes are in <paramref name="nodeMap"/>
        /// yet at that point, so there is nothing to skip.
        /// </param>
        public static void Resolve(Screen screen, IReadOnlyDictionary<ElementNode, Control> nodeMap,
                                   VariantStore variants, HashSet<ElementNode> inactiveNodes = null)
        {
            foreach (var kv in nodeMap)
            {
                var node = kv.Key;
                var control = kv.Value;
                if (control.GameObject == null) continue;
                // T5-M1: skip controls in currently-inactive Add blocks — they are hidden and
                // writing nav to them is wasteful; any previous nav written while active is kept
                // on the Selectable but cannot be triggered while the GO is SetActive(false).
                if (inactiveNodes != null && inactiveNodes.Contains(node)) continue;

                var sel = control.GameObject.GetComponent<Selectable>();

                var navMode = VariantResolver.ResolveAttribute(node, "nav", variants);
                var up = VariantResolver.ResolveAttribute(node, "navUp", variants);
                var down = VariantResolver.ResolveAttribute(node, "navDown", variants);
                var left = VariantResolver.ResolveAttribute(node, "navLeft", variants);
                var right = VariantResolver.ResolveAttribute(node, "navRight", variants);

                bool hasExplicit = up != null || down != null || left != null || right != null;
                if (navMode == null && !hasExplicit) continue;
                if (sel == null) continue; // non-Selectable control: skip

                if (navMode == "none")
                {
                    sel.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                    continue;
                }
                if (!hasExplicit) continue;

                // Fill unspecified directions with geometric neighbours so writing only one
                // direction doesn't dead-end the other three (spec §7).
                // Sel() returns null when the target is not currently live (e.g. it lives in
                // an inactive variant Add block, or the id is a typo).  The ?? falls through
                // to the geometric neighbour so the direction is never dead-ended.
                // Inactive-block targets self-heal: ReSolve re-runs Resolve after
                // ActivateAddBlock (Screen.cs:778 then :806), so the wire-up is automatic.
                // Typo ids (declared nowhere) are also treated as geometric — the CLI lint rule
                // PUI-NAV-UNKNOWN-TARGET is the sole static detector for typos.
                var nav = new UnityEngine.UI.Navigation
                {
                    mode = UnityEngine.UI.Navigation.Mode.Explicit,
                    selectOnUp = Sel(screen, up) ?? sel.FindSelectableOnUp(),
                    selectOnDown = Sel(screen, down) ?? sel.FindSelectableOnDown(),
                    selectOnLeft = Sel(screen, left) ?? sel.FindSelectableOnLeft(),
                    selectOnRight = Sel(screen, right) ?? sel.FindSelectableOnRight(),
                };
                sel.navigation = nav;
            }
        }

        // Returns the Selectable for the control with the given id, or null if:
        //   (a) id is null (direction not specified by the author → caller uses geometric), or
        //   (b) the id is not currently live (inactive variant Add block or undeclared typo).
        // Both (a) and (b) leave the direction to the caller's geometric fallback.
        private static Selectable Sel(Screen screen, string id)
        {
            if (id == null) return null;
            if (!screen.TryGet(id, out var control)) return null;
            return control?.GameObject?.GetComponent<Selectable>();
        }
    }
}
