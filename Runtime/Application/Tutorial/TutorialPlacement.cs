using UnityEngine;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// 气泡+手指四向避让(spec §5.3),全部在 overlay 本地坐标(中心原点)。
    /// 纯函数,EditMode 可测。gap = 手指长度 + 间距,气泡中心 = 目标边缘 + gap + 半气泡。
    /// </summary>
    internal static class TutorialPlacement
    {
        internal readonly struct Result
        {
            public readonly Side Side;
            public readonly Vector2 BubblePos;   // 气泡中心
            public readonly Vector2 FingerPos;   // 手指中心(气泡与目标之间的 gap 中点)
            public readonly float FingerAngle;   // Z 旋转;素材默认朝上(0°=指上)
            public Result(Side s, Vector2 b, Vector2 f, float a)
            { Side = s; BubblePos = b; FingerPos = f; FingerAngle = a; }
        }

        internal static Result Choose(Rect overlay, Rect target, Vector2 bubbleSize,
            float gap, Side place)
        {
            if (place != Side.Auto) return Build(place, target, bubbleSize, gap, overlay);

            Side best = Side.Top;
            float bestScore = float.MinValue;
            foreach (var s in new[] { Side.Top, Side.Bottom, Side.Left, Side.Right })
            {
                var r = Build(s, target, bubbleSize, gap, overlay);
                var bubble = new Rect(r.BubblePos - bubbleSize / 2f, bubbleSize);
                float overflow = Overflow(bubble, overlay);
                // 零溢出里选剩余空间最大;有溢出则溢出最小
                float room = s switch
                {
                    Side.Top => overlay.yMax - target.yMax,
                    Side.Bottom => target.yMin - overlay.yMin,
                    Side.Left => target.xMin - overlay.xMin,
                    _ => overlay.xMax - target.xMax,
                };
                float score = overflow > 0f ? -1000f - overflow : room;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return Build(best, target, bubbleSize, gap, overlay);
        }

        private static Result Build(Side s, Rect t, Vector2 b, float gap, Rect overlay)
        {
            Vector2 c = t.center, bubble, finger;
            float angle;
            switch (s)
            {
                case Side.Top:
                    bubble = new Vector2(c.x, t.yMax + gap + b.y / 2f);
                    finger = new Vector2(c.x, t.yMax + gap / 2f);
                    angle = 180f; break;
                case Side.Bottom:
                    bubble = new Vector2(c.x, t.yMin - gap - b.y / 2f);
                    finger = new Vector2(c.x, t.yMin - gap / 2f);
                    angle = 0f; break;
                case Side.Left:
                    bubble = new Vector2(t.xMin - gap - b.x / 2f, c.y);
                    finger = new Vector2(t.xMin - gap / 2f, c.y);
                    angle = -90f; break;
                default:   // Right
                    bubble = new Vector2(t.xMax + gap + b.x / 2f, c.y);
                    finger = new Vector2(t.xMax + gap / 2f, c.y);
                    angle = 90f; break;
            }
            // 沿副轴夹紧让气泡不出屏(主轴出屏由 Auto 评分排除)
            bubble.x = Mathf.Clamp(bubble.x, overlay.xMin + b.x / 2f, overlay.xMax - b.x / 2f);
            bubble.y = Mathf.Clamp(bubble.y, overlay.yMin + b.y / 2f, overlay.yMax - b.y / 2f);
            return new Result(s, bubble, finger, angle);
        }

        private static float Overflow(Rect r, Rect bounds) =>
            Mathf.Max(0f, bounds.xMin - r.xMin) + Mathf.Max(0f, r.xMax - bounds.xMax)
            + Mathf.Max(0f, bounds.yMin - r.yMin) + Mathf.Max(0f, r.yMax - bounds.yMax);
    }
}
