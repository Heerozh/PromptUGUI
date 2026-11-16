using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The empty stand-in that keeps a lifted element's slot in its layout group while the element
    /// itself leaves the flow (<c>LayoutElement.ignoreLayout</c>, what <c>flow="false"</c> uses) —
    /// spec 2026-09-16-scrolllist-drag-reorder §5.2, shared with drag-and-drop (2026-11-16 §7.2).
    /// A fresh one per session, gone at its end: never pooled, never left inactive in the parent
    /// (<c>ScrollList.Rebuild</c> assumes row <i>i</i> is sibling <i>i</i>).
    /// </summary>
    internal static class FlowPlaceholder
    {
        /// <summary>
        /// A graphic-less RectTransform named <paramref name="name"/>, appended under
        /// <paramref name="parent"/>, sized exactly like <paramref name="like"/> whether the group reads
        /// the rect or the LayoutElement.
        /// </summary>
        internal static RectTransform Create(RectTransform like, Transform parent, string name)
        {
            var placeholder = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            placeholder.SetParent(parent, false);
            CopyGeometry(like, placeholder);
            return placeholder;
        }

        /// <summary>
        /// Takes the placeholder away. Off the parent FIRST: in play mode Destroy is deferred to the end
        /// of the frame, and a placeholder still parented would keep its slot through the layout rebuild
        /// that follows.
        /// </summary>
        internal static void Remove(RectTransform placeholder)
        {
            if (placeholder == null) return;
            placeholder.SetParent(null, false);
            if (UnityEngine.Application.isPlaying) Object.Destroy(placeholder.gameObject);
            else Object.DestroyImmediate(placeholder.gameObject);
        }

        private static void CopyGeometry(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.sizeDelta = from.sizeDelta;
            to.anchoredPosition = from.anchoredPosition;
            var src = from.GetComponent<LayoutElement>();
            if (src == null) return;
            // Both size channels copied, so the group sizes the placeholder exactly like the element
            // whether it reads LayoutElement (childControl*) or the rect (sizeDelta above).
            var le = to.gameObject.AddComponent<LayoutElement>();
            le.minWidth = src.minWidth;
            le.minHeight = src.minHeight;
            le.preferredWidth = src.preferredWidth;
            le.preferredHeight = src.preferredHeight;
            le.flexibleWidth = src.flexibleWidth;
            le.flexibleHeight = src.flexibleHeight;
            le.layoutPriority = src.layoutPriority;
        }
    }
}
