using System;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared landing point for <c>rotation</c> / <c>flip</c> on <c>&lt;Image&gt;</c> /
    /// <c>&lt;Icon&gt;</c> / <c>&lt;RawImage&gt;</c> (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §3). The two attributes drive one component, so each
    /// setter replays both values through here.
    ///
    /// <para>The component is attached lazily and only when the values actually do something, so the
    /// overwhelmingly common case (neither attribute written) costs nothing. Once attached it is
    /// disabled rather than destroyed when the values return to identity, which keeps a Variant flip
    /// back and forth idempotent — the same rule <c>ClampFitter</c> follows.</para>
    /// </summary>
    internal static class RotateFlipApplier
    {
        public static void Apply(Graphic graphic, float rotation, string flip)
        {
            if (graphic == null) return;

            ParseFlip(flip, out var flipX, out var flipY);
            var effect = graphic.GetComponent<RotateFlipEffect>();

            if (effect == null)
            {
                if (RotateFlipEffect.IsIdentityValues(rotation, flipX, flipY)) return;
                effect = graphic.gameObject.AddComponent<RotateFlipEffect>();
            }

            effect.Rotation = rotation;
            effect.FlipX = flipX;
            effect.FlipY = flipY;
            effect.enabled = !effect.IsIdentity;
            graphic.SetVerticesDirty();
        }

        private static void ParseFlip(string value, out bool flipX, out bool flipY)
        {
            flipX = false;
            flipY = false;
            if (string.IsNullOrEmpty(value)) return;

            switch (value)
            {
                case "none": return;
                case "x": flipX = true; return;
                case "y": flipY = true; return;
                case "xy": flipX = true; flipY = true; return;
                default:
                    throw new ArgumentException(
                        $"flip '{value}' must be 'x' (horizontal), 'y' (vertical), 'xy' (both) or 'none'");
            }
        }
    }
}
