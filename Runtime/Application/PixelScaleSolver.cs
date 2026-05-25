using UnityEngine;

namespace PromptUGUI.Application
{
    internal static class PixelScaleSolver
    {
        // raw = min(screenW/designW, screenH/designH)  (fit-inside)
        // raw >= 1  -> floor(raw)                       (integer 1, 2, 3, ...)
        // raw <  1  -> 1 / 2^ceil(log2(1/raw))          (snap to 0.5, 0.25, 0.125, ...)
        // Degenerate input (any axis <= 0) -> 1 (safe fallback).
        public static float Solve(Vector2 screen, Vector2 design)
        {
            if (screen.x <= 0f || screen.y <= 0f || design.x <= 0f || design.y <= 0f)
                return 1f;
            float raw = Mathf.Min(screen.x / design.x, screen.y / design.y);
            if (raw >= 1f) return Mathf.Floor(raw);
            int n = Mathf.CeilToInt(Mathf.Log(1f / raw, 2f));
            return 1f / Mathf.Pow(2f, n);
        }
    }
}
