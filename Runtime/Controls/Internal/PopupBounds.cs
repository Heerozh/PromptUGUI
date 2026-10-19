using System;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Where a suggestion list may go (spec 2026-10-18 §6.8): the root canvas's rect, shrunk into the
    /// device safe area, and — while the on-screen keyboard is up — lifted off the bottom by its height.
    /// Only the keyboard's height is used: <c>TouchScreenKeyboard.area</c>'s origin differs between
    /// platforms, and a portrait keyboard always sits at the bottom.
    /// </summary>
    internal static class PopupBounds
    {
        /// <summary>Test injection: the whole bounds, in the root canvas's local space.</summary>
        internal static Func<Rect> BoundsOverride;

        /// <summary>Test injection: the keyboard height in device pixels (0 = no keyboard).</summary>
        internal static Func<float> KeyboardHeightOverride;

        internal static Rect For(Canvas root)
        {
            if (BoundsOverride != null) return BoundsOverride();
            if (root == null) return default;
            var full = ((RectTransform)root.transform).rect;
            var sf = SafeAreaTracker.ScaleFactorOverride != null ? SafeAreaTracker.ScaleFactorOverride() : root.scaleFactor;
            return Compute(SafeAreaTracker.ApplySafeInset(full, root.scaleFactor), full, KeyboardHeight(), sf);
        }

        /// <summary>
        /// The safe rect with the keyboard taken off the bottom: a keyboard <paramref name="keyboardPx"/>
        /// device pixels tall covers the canvas up to <c>full.yMin + keyboardPx / scaleFactor</c>.
        /// </summary>
        internal static Rect Compute(Rect safe, Rect full, float keyboardPx, float scaleFactor)
        {
            if (keyboardPx <= 0f || scaleFactor <= 0f) return safe;
            var top = full.yMin + keyboardPx / scaleFactor;
            if (top <= safe.yMin) return safe;
            return Rect.MinMaxRect(safe.xMin, Mathf.Min(top, safe.yMax), safe.xMax, safe.yMax);
        }

        private static float KeyboardHeight()
        {
            if (KeyboardHeightOverride != null) return KeyboardHeightOverride();
            return TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
        }

        internal static void ResetForTests()
        {
            BoundsOverride = null;
            KeyboardHeightOverride = null;
        }
    }
}
