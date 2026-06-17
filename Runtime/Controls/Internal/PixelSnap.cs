using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    // 把 TMP 文本的「对齐感知参考点」吸到整数设备像素网格——补 Canvas.pixelPerfect
    // 漏掉的 TMP 字形对齐。见 spec 2026-06-17-pixel-position-snap (PPS-D1…D7)。
    [DisallowMultipleComponent]
    internal sealed class PixelSnap : UIBehaviour
    {
        // 参考点 = TMP 把文本块相对 rect 摆放所依据的边/中心（PPS-D4）。
        internal static Vector2 ReferencePoint(
            Rect rect, HorizontalAlignmentOptions h, VerticalAlignmentOptions v)
        {
            float x = h == HorizontalAlignmentOptions.Center ? rect.center.x
                    : h == HorizontalAlignmentOptions.Right ? rect.xMax
                    : rect.xMin;   // Left / Justified / Flush / Geometry
            float y = v == VerticalAlignmentOptions.Middle ? rect.center.y
                    : v == VerticalAlignmentOptions.Bottom ? rect.yMin
                    : rect.yMax;   // Top / Baseline / Capline / Geometry
            return new Vector2(x, y);
        }
    }
}
