using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A layout controller that controls nothing. Its whole job is to keep a node that has no layout
    /// of its own from <b>breaking the layout chain</b> through it.
    ///
    /// <para>uGUI walks two chains and both stop at a node with no <c>ILayoutGroup</c> /
    /// <c>ILayoutController</c>: <c>LayoutRebuilder.MarkLayoutForRebuild</c> walks UP only while each
    /// parent is a layout group, so a dirty row inside such a node never reaches the group above it;
    /// and <c>PerformLayoutControl</c> skips the entire subtree of a node with no controller, so a
    /// <c>ContentSizeFitter</c> under it never runs in the parent's pass. A container that sits
    /// between a group and its content — <see cref="Collapsible"/>'s body, which owns a size of its
    /// own and must NOT position its child — needs the chain without the control.</para>
    ///
    /// <para><c>ScrollRect</c> happens to be an <c>ILayoutGroup</c> too, so a body with
    /// <c>maxHeight</c> would link and one without would not. This makes it uniform.</para>
    /// </summary>
    internal sealed class LayoutLink : UIBehaviour, ILayoutGroup
    {
        public void SetLayoutHorizontal() { }
        public void SetLayoutVertical() { }
    }
}
