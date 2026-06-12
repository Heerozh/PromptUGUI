using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// A resolved colour value: solid, or a two-stop vertical gradient (Top → Bottom).
    /// Produced by <c>UI.Theme.ResolveSpec</c> (arriving in a later task); applied by <c>ColorApplier</c>
    /// (vertex-tint slot) or the TMP vertex-gradient path in <c>Text</c>.
    /// Solid values keep <see cref="Bottom"/> == <see cref="Top"/> so consumers that
    /// only need one colour can read Top unconditionally.
    /// </summary>
    internal readonly struct ColorSpec
    {
        public readonly Color Top;
        public readonly Color Bottom;
        public readonly bool IsGradient;

        private ColorSpec(Color top, Color bottom, bool isGradient)
        {
            Top = top;
            Bottom = bottom;
            IsGradient = isGradient;
        }

        public static ColorSpec Solid(Color c) => new ColorSpec(c, c, false);
        public static ColorSpec Gradient(Color top, Color bottom) => new ColorSpec(top, bottom, true);

        /// <summary>Component-wise multiply (modulate) a tint colour into both stops.</summary>
        public ColorSpec Multiply(Color m) => new ColorSpec(Top * m, Bottom * m, IsGradient);
    }
}
