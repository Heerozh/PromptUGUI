using System.Collections.Generic;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint for <c>blur</c> / <c>glow</c> / <c>glowColor</c> on the sprite graphics
    /// (spec 2026-09-02 §6).
    ///
    /// <para><c>&lt;Image&gt;</c> and <c>&lt;Icon&gt;</c> are the third shape of tag alongside
    /// <c>&lt;Frame&gt;</c> and <c>&lt;Decor&gt;</c>: they draw a glow without having a procedural
    /// surface, because theirs is cast from the sprite's own silhouette rather than from an SDF. So
    /// they accept exactly the glow pair and nothing else of the procedural set —
    /// <see cref="PureContainerVisualAttrRules"/> defers to <see cref="SupportedProceduralAttrs"/>
    /// for that, the same way it defers to <c>DecorRules</c>.</para>
    /// </summary>
    public static class ImageFxRules
    {
        /// <summary>The tags built on <c>FxImage</c>, and therefore the only ones where blur / glow
        /// do anything. <c>&lt;RawImage&gt;</c> is deliberately absent — M2.</summary>
        internal static readonly HashSet<string> FxTags = new()
        {
            "Image", "Icon",
        };

        /// <summary>The slice of <c>ProceduralAttrNames.NeedsPanel</c> that a sprite graphic really
        /// does draw. Everything else in that set (radius, borders, glass, weld) still has nowhere to
        /// land here and stays reported.</summary>
        internal static readonly HashSet<string> SupportedProceduralAttrs = new()
        {
            "glow", "glowColor",
        };
    }
}
