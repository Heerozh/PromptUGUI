using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A raycast target that draws nothing. Put on a node that has to be FOUND by the pointer
    /// without being seen — <c>ScrollList</c>'s Content when <c>reorder</c> is on: a press in the
    /// gap between two click-through rows still has to reach the <see cref="ReorderDriver"/>
    /// sitting there, and uGUI only walks up from what the raycast hit (spec
    /// 2026-09-16-scrolllist-drag-reorder §5.1). The same idea as the geometry-less
    /// <c>ProceduralPanel</c> catcher behind <c>&lt;Frame raycastTarget="true"&gt;</c>, without the
    /// procedural surface it drags along.
    /// </summary>
    // Graphic's own [RequireComponent(typeof(CanvasRenderer))] does not carry over to a subclass
    // added via AddComponent at runtime (see ProceduralPanel) — without this the first canvas
    // rebuild throws MissingComponentException.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class HitCatcher : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
