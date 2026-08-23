using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// Runtime controls for <c>&lt;Frame glass="true"&gt;</c> panels.
        ///
        /// <para>A glass panel samples a blurred copy of what the capture camera rendered — the game
        /// world plus every Screen Space-Camera canvas on it. Overlay canvases are not in that
        /// picture (uGUI has no grab pass), so a glass panel never sees its own siblings. The usual
        /// arrangement needs no configuration at all: leave the glass Screen on the default Overlay
        /// canvas, and put any UI that should show up blurred behind it on a
        /// <c>CanvasMode.Camera</c> Screen.</para>
        /// </summary>
        public static class Glass
        {
            /// <summary>
            /// Master switch, meant to be wired to a quality setting. Turning it off stops the
            /// capture and blur work entirely and makes every glass panel draw as a plain
            /// translucent panel — no material churn, no canvas rebuild.
            /// </summary>
            public static bool Enabled
            {
                get => GlassRuntime.Enabled;
                set => GlassRuntime.Enabled = value;
            }

            /// <summary>
            /// Camera whose output is captured as the glass backdrop. <c>null</c> (the default)
            /// means <see cref="Camera.main"/>. Set this for split-screen or multi-camera setups.
            /// </summary>
            public static Camera Camera
            {
                get => GlassRuntime.Camera;
                set => GlassRuntime.Camera = value;
            }

            /// <summary>
            /// True while a blurred backdrop is actually being published. False means every glass
            /// panel is currently drawing its fallback — no URP, a non-URP pipeline, no capture
            /// camera, or <see cref="Enabled"/> turned off.
            /// </summary>
            public static bool IsActive =>
                Shader.GetGlobalFloat(GlassRuntime.BackdropAvailableProperty) > 0.5f;
        }
    }
}
