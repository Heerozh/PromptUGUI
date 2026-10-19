using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Sizes a <c>&lt;Popover&gt;</c>'s panel in the layout pass, the way <c>ContentSizeFitter</c> does
    /// (spec 2026-10-19 §5.8): the panel's own <c>VerticalLayoutGroup</c> reports the content, the
    /// popover turns it into a size (hug / a number / <c>clamp(min, hug, max)</c>). Sitting on the same
    /// GameObject as the group makes the panel its own layout root, so a content change reaches it in the
    /// same canvas update. When the size changes while open the popover is re-clamped — from
    /// <c>SetLayoutVertical</c> only, once both axes are final.
    /// </summary>
    [ExecuteAlways]
    internal sealed class PopoverFitter : UIBehaviour, ILayoutSelfController
    {
        internal Popover Owner;

        public void SetLayoutHorizontal()
        {
            if (Owner != null) Owner.FitAxis(0);
        }

        public void SetLayoutVertical()
        {
            if (Owner == null) return;
            var changed = Owner.FitAxis(1);
            Owner.AfterFit(changed);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetDirty();
        }

        protected override void OnDisable()
        {
            SetDirty();
            base.OnDisable();
        }

        internal void SetDirty()
        {
            if (!IsActive()) return;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
