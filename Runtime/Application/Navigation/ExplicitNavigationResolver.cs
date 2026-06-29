using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Variants;
using UnityEngine;
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
        /// <param name="confineRoot">
        /// Non-null = confine mode (a modal). uGUI's <c>Automatic</c> navigation searches the
        /// WHOLE scene, so a modal button's geometric neighbour can be a control on the page
        /// behind the modal; pressing a direction then escapes the modal and the focus trap
        /// snaps back to the first focusable, leaving the user stuck on the first button.
        /// In confine mode EVERY focusable under <paramref name="confineRoot"/> is converted to
        /// <c>Explicit</c> navigation whose neighbours are computed by the same geometric scoring
        /// uGUI uses, but restricted to Selectables inside this subtree — so directional nav can
        /// never leave the modal. Null = full-screen page: legacy behaviour (Automatic kept).
        /// </param>
        public static void Resolve(Screen screen, IReadOnlyDictionary<ElementNode, Control> nodeMap,
                                   VariantStore variants, HashSet<ElementNode> inactiveNodes = null,
                                   GameObject confineRoot = null)
        {
            // Candidate pool for confine mode: only Selectables inside the modal subtree.
            List<Selectable> pool = null;
            if (confineRoot != null)
            {
                // Confined neighbours are geometric, so the layout must be current. At modal open
                // the overlay canvas is not sized yet and the HStack hasn't positioned its buttons;
                // on ReSolve the preceding attribute/scale pass has dirtied the layout. A full
                // canvas update sizes the canvas AND rebuilds the layout synchronously.
                Canvas.ForceUpdateCanvases();
                pool = new List<Selectable>(confineRoot.GetComponentsInChildren<Selectable>(false));
            }

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
                if (sel == null) continue; // non-Selectable control: skip

                var navMode = VariantResolver.ResolveAttribute(node, "nav", variants);
                var up = VariantResolver.ResolveAttribute(node, "navUp", variants);
                var down = VariantResolver.ResolveAttribute(node, "navDown", variants);
                var left = VariantResolver.ResolveAttribute(node, "navLeft", variants);
                var right = VariantResolver.ResolveAttribute(node, "navRight", variants);
                bool hasExplicit = up != null || down != null || left != null || right != null;

                if (navMode == "none")
                {
                    sel.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                    continue;
                }

                if (confineRoot == null)
                {
                    // ── Full-screen page: only touch controls that opt in via nav attributes. ──
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
                    sel.navigation = new UnityEngine.UI.Navigation
                    {
                        mode = UnityEngine.UI.Navigation.Mode.Explicit,
                        selectOnUp = Sel(screen, up) ?? sel.FindSelectableOnUp(),
                        selectOnDown = Sel(screen, down) ?? sel.FindSelectableOnDown(),
                        selectOnLeft = Sel(screen, left) ?? sel.FindSelectableOnLeft(),
                        selectOnRight = Sel(screen, right) ?? sel.FindSelectableOnRight(),
                    };
                }
                else
                {
                    // ── Modal: cage EVERY focusable to in-subtree neighbours (no scene-wide escape). ──
                    // Author-declared targets (by id) still win; unspecified directions fall to the
                    // subtree-confined geometric neighbour instead of uGUI's scene-wide search.
                    var t = sel.transform;
                    sel.navigation = new UnityEngine.UI.Navigation
                    {
                        mode = UnityEngine.UI.Navigation.Mode.Explicit,
                        selectOnUp = Sel(screen, up) ?? FindInSubtree(sel, t.rotation * Vector3.up, pool),
                        selectOnDown = Sel(screen, down) ?? FindInSubtree(sel, t.rotation * Vector3.down, pool),
                        selectOnLeft = Sel(screen, left) ?? FindInSubtree(sel, t.rotation * Vector3.left, pool),
                        selectOnRight = Sel(screen, right) ?? FindInSubtree(sel, t.rotation * Vector3.right, pool),
                    };
                }
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

        // Subtree-confined mirror of uGUI's Selectable.FindSelectable(dir): picks the best
        // candidate in direction <paramref name="dir"/> using the SAME scoring (dot / distance²
        // from the source rect edge), but only among <paramref name="pool"/> (the modal subtree).
        private static Selectable FindInSubtree(Selectable from, Vector3 dir, List<Selectable> pool)
        {
            if (pool == null) return null;
            dir = dir.normalized;
            var fromRt = from.transform as RectTransform;
            Vector3 localDir = Quaternion.Inverse(from.transform.rotation) * dir;
            Vector3 pos = from.transform.TransformPoint(GetPointOnRectEdge(fromRt, localDir));

            float maxScore = Mathf.NegativeInfinity;
            Selectable best = null;
            for (int i = 0; i < pool.Count; i++)
            {
                var sel = pool[i];
                if (sel == from || sel == null) continue;
                if (!sel.IsInteractable() || sel.navigation.mode == UnityEngine.UI.Navigation.Mode.None) continue;

                var selRt = sel.transform as RectTransform;
                Vector3 selCenter = selRt != null ? (Vector3)selRt.rect.center : Vector3.zero;
                Vector3 myVector = sel.transform.TransformPoint(selCenter) - pos;
                float dot = Vector3.Dot(dir, myVector);
                if (dot <= 0f) continue;

                float score = dot / myVector.sqrMagnitude;
                if (score > maxScore)
                {
                    maxScore = score;
                    best = sel;
                }
            }
            return best;
        }

        // Mirror of uGUI's Selectable.GetPointOnRectEdge.
        private static Vector3 GetPointOnRectEdge(RectTransform rect, Vector2 dir)
        {
            if (rect == null) return Vector3.zero;
            if (dir != Vector2.zero) dir /= Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            var r = rect.rect;
            return r.center + Vector2.Scale(r.size, dir * 0.5f);
        }
    }
}
