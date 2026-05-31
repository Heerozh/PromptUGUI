using UnityEngine;

namespace PromptUGUI.Application
{
    internal static class PixelScaleSolver
    {
        // raw = min(screenW/designW, screenH/designH)  (fit-inside)
        // raw >= 1  -> floor(raw)                       (integer 1, 2, 3, ...)
        // raw <  1  -> 1 / 2^ceil(log2(1/raw))          (snap to 0.5, 0.25, 0.125, ...)
        // Degenerate input (any axis <= 0) -> 1 (safe fallback).
        //
        // powerOfTwoOnly collapses the whole ladder to powers of two
        // (...0.25, 0.5, 1, 2, 4, 8...): the result is the largest power of two <= raw,
        // i.e. 2^floor(log2(raw)). For raw < 1 that equals the default 1/2^ceil(log2(1/raw))
        // (since -ceil(-x) == floor(x)), so only the magnify segment (raw >= 1) changes:
        // 3x -> 2x, 5x -> 4x, ... still fit-inside (rounds down, never overflows).
        public static float Solve(Vector2 screen, Vector2 design, bool powerOfTwoOnly = false)
        {
            if (screen.x <= 0f || screen.y <= 0f || design.x <= 0f || design.y <= 0f)
                return 1f;
            float raw = Mathf.Min(screen.x / design.x, screen.y / design.y);
            if (powerOfTwoOnly)
                // +epsilon absorbs float error in Log so exact powers (raw == 4, 8, ...)
                // don't drop a rung when log2 lands just under the integer exponent.
                return Mathf.Pow(2f, Mathf.Floor(Mathf.Log(raw, 2f) + 1e-5f));
            if (raw >= 1f) return Mathf.Floor(raw);
            int n = Mathf.CeilToInt(Mathf.Log(1f / raw, 2f));
            return 1f / Mathf.Pow(2f, n);
        }
    }
}
