using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Variants;
using UnityEngine.UI;

namespace PromptUGUI.Application.Navigation
{
    internal static class ExplicitNavigationResolver
    {
        public static void Resolve(Screen screen, IReadOnlyDictionary<ElementNode, Control> nodeMap, VariantStore variants)
        {
            foreach (var kv in nodeMap)
            {
                var node = kv.Key;
                var control = kv.Value;
                if (control.GameObject == null) continue;
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
                var nav = new UnityEngine.UI.Navigation
                {
                    mode = UnityEngine.UI.Navigation.Mode.Explicit,
                    selectOnUp = up != null ? Sel(screen, up) : sel.FindSelectableOnUp(),
                    selectOnDown = down != null ? Sel(screen, down) : sel.FindSelectableOnDown(),
                    selectOnLeft = left != null ? Sel(screen, left) : sel.FindSelectableOnLeft(),
                    selectOnRight = right != null ? Sel(screen, right) : sel.FindSelectableOnRight(),
                };
                sel.navigation = nav;
            }
        }

        private static Selectable Sel(Screen screen, string id)
            => screen.Get(id).GameObject.GetComponent<Selectable>(); // missing id throws KeyNotFoundException (spec §11)
    }
}
