using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The mechanics behind <c>&lt;Animation reveal&gt;</c> (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §2.4): measure the content, write the animating box
    /// on the host, and clip what does not fit yet. Kept apart from <c>AnimationDriver</c> because
    /// <c>&lt;Collapsible&gt;</c> drives its body through exactly the same three operations.
    ///
    /// <para>What makes this different from the transform channels: those move a proxy and never
    /// touch layout, while reveal owns the host's size on one axis, so the siblings around it move
    /// as it opens. That is the whole point — an inline fold pushes the content below it down.</para>
    /// </summary>
    internal static class RevealDriver
    {
        /// <summary>
        /// The content's size on <paramref name="axis"/>, measured now — never cached, so new rows
        /// or a locale switch are picked up by the next fire.
        /// </summary>
        /// <remarks>
        /// A TMP added by AddComponent under an inactive parent never runs Awake and reports a
        /// preferred size of 0 forever, so an inactive subtree is switched on for the duration of
        /// the measurement and switched back within the same frame (nothing renders in between).
        /// Falls back to the child's own rect when it reports no preferred size at all — a plain
        /// <c>&lt;Frame height="100"&gt;</c> is a legitimate thing to reveal.
        /// </remarks>
        public static float Measure(RectTransform child, int axis)
        {
            if (child == null) return 0f;

            var wasActive = child.gameObject.activeSelf;
            if (!wasActive) child.gameObject.SetActive(true);
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(child);
                var preferred = LayoutUtility.GetPreferredSize(child, axis);
                return preferred > 0f ? preferred : child.rect.size[axis];
            }
            finally
            {
                if (!wasActive) child.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Writes the current box on <paramref name="axis"/>. Inside a layout group that means a
        /// rigid <c>LayoutElement</c> (min = preferred, flexible 0) so the group hands over exactly
        /// this much and the siblings shuffle; under a free-positioning parent it is the rect itself.
        /// </summary>
        public static void ApplyBox(RectTransform host, int axis, float value, bool inLayoutGroup)
        {
            if (host == null) return;

            if (inLayoutGroup)
            {
                var le = host.GetComponent<LayoutElement>();
                if (le == null) le = host.gameObject.AddComponent<LayoutElement>();
                if (axis == 0)
                {
                    le.minWidth = value;
                    le.preferredWidth = value;
                    le.flexibleWidth = 0f;
                }
                else
                {
                    le.minHeight = value;
                    le.preferredHeight = value;
                    le.flexibleHeight = 0f;
                }
                LayoutRebuilder.MarkLayoutForRebuild(host);
                return;
            }

            var size = host.sizeDelta;
            if (Mathf.Approximately(size[axis], value)) return;
            size[axis] = value;
            host.sizeDelta = size;
        }

        /// <summary>
        /// Turns the host's clip on or off. Lazily added, then only enabled / disabled: a mask that
        /// is switched off costs nothing and stops breaking batching once the content fits again.
        /// </summary>
        public static void SetClip(GameObject host, bool on)
        {
            if (host == null) return;
            var mask = host.GetComponent<RectMask2D>();
            if (mask == null)
            {
                if (!on) return;
                mask = host.AddComponent<RectMask2D>();
            }
            mask.enabled = on;
        }
    }
}
