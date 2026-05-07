using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout {
    public static class AnchorResolver {
        public static void Resolve(
            AnchorPreset preset,
            out Vector2 anchorMin, out Vector2 anchorMax, out Vector2 pivot) {
            float xMin, xMax, pivotX;
            switch (preset.H) {
                case AnchorHorizontal.Left:    xMin = 0f;   xMax = 0f;   pivotX = 0f;   break;
                case AnchorHorizontal.Center:  xMin = 0.5f; xMax = 0.5f; pivotX = 0.5f; break;
                case AnchorHorizontal.Right:   xMin = 1f;   xMax = 1f;   pivotX = 1f;   break;
                case AnchorHorizontal.Stretch: xMin = 0f;   xMax = 1f;   pivotX = 0.5f; break;
                default: throw new System.ArgumentOutOfRangeException();
            }

            float yMin, yMax, pivotY;
            switch (preset.V) {
                case AnchorVertical.Bottom:    yMin = 0f;   yMax = 0f;   pivotY = 0f;   break;
                case AnchorVertical.Center:    yMin = 0.5f; yMax = 0.5f; pivotY = 0.5f; break;
                case AnchorVertical.Top:       yMin = 1f;   yMax = 1f;   pivotY = 1f;   break;
                case AnchorVertical.Stretch:   yMin = 0f;   yMax = 1f;   pivotY = 0.5f; break;
                default: throw new System.ArgumentOutOfRangeException();
            }

            anchorMin = new Vector2(xMin, yMin);
            anchorMax = new Vector2(xMax, yMax);
            pivot     = new Vector2(pivotX, pivotY);
        }
    }
}
