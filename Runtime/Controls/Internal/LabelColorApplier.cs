using PromptUGUI.Application;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Applies a label's text color from a theme token / hex / CSS name / gradient / <c>/alpha</c>
    /// string — identical to <c>&lt;Text color&gt;</c>: a gradient spec paints a vertical TMP
    /// <see cref="VertexGradient"/>, a solid sets <see cref="TMP_Text.color"/>. Shared by the
    /// labelled controls (Btn / Tab / Toggle / Dropdown). Empty / null is a no-op, so the control's
    /// default label color (<see cref="ProceduralBuilders.DefaultLabelColor"/>) is preserved.
    /// </summary>
    internal static class LabelColorApplier
    {
        public static void Apply(TMP_Text label, string value)
        {
            if (label == null || string.IsNullOrEmpty(value)) return;
            var spec = UI.Theme.ResolveSpec(value);
            GradientStopWarning.IfMoved(spec, label, "a label colour");
            if (spec.IsGradient)
            {
                label.enableVertexGradient = true;
                label.colorGradient = new VertexGradient(spec.Top, spec.Top, spec.Bottom, spec.Bottom);
                label.color = Color.white;
            }
            else
            {
                label.enableVertexGradient = false;
                label.color = spec.Top;
            }
        }
    }
}
