using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The <c>CanvasRenderer</c> colour of a <see cref="Graphic"/>: the multiplier layer uGUI keeps
    /// apart from <see cref="Graphic.color"/> (its own ColorTint transition drives it), multiplied
    /// into the vertex colours at batch time. <c>*Modulate</c> lives here so that a graphic's own
    /// colour stays with whoever set it — its <c>color=</c>, a Variant, a theme, code.
    /// </summary>
    internal static class CanvasTint
    {
        private static readonly List<TMP_SubMeshUI> s_subMeshes = new List<TMP_SubMeshUI>();

        internal static Color Get(Graphic g) => g.canvasRenderer.GetColor();

        /// <summary>
        /// Sets the multiplier, on a TMP text's sub-meshes too: glyphs from a fallback font or an
        /// inline sprite live in a <see cref="TMP_SubMeshUI"/> with a <c>CanvasRenderer</c> of its
        /// own, which TMP syncs to the parent's only when it regenerates the mesh — and a multiplier
        /// change regenerates nothing. A sub-mesh created later is synced by that regeneration.
        /// </summary>
        internal static void Set(Graphic g, Color c)
        {
            g.canvasRenderer.SetColor(c);
            if (!(g is TMP_Text tmp)) return;
            tmp.GetComponentsInChildren(true, s_subMeshes);
            foreach (var sub in s_subMeshes)
                if (sub.textComponent == tmp) sub.canvasRenderer.SetColor(c);
            s_subMeshes.Clear();
        }
    }
}
