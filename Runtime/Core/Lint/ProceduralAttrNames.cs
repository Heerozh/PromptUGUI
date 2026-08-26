namespace PromptUGUI.Lint
{
    /// <summary>
    /// The visual attributes <c>&lt;Frame&gt;</c> draws itself, in one place because two rule
    /// families now ask about them from opposite directions: <see cref="PureContainerVisualAttrRules"/>
    /// asks "will this tag silently drop them?", and <see cref="MaskAttributeRules"/> asks "does this
    /// Frame end up with a Graphic of its own?".
    /// </summary>
    /// <remarks>
    /// A hand-kept mirror of the <c>[UIAttr]</c> setters in <c>Frame.cs</c> — <c>Core/Lint</c> is the
    /// pure-C# subset the CLI compiles outside Unity, so it cannot reflect over the control registry
    /// to derive this. <c>ProceduralAttrNamesTests</c> guards the mirror against drift, the same way
    /// <c>BuiltinTagsTests</c> guards <c>BuiltinTags</c>.
    /// </remarks>
    public static class ProceduralAttrNames
    {
        /// <summary>
        /// Writing any of these makes Frame lazily attach a <c>ProceduralPanel</c> — every one of
        /// them routes through Frame's <c>Panel</c> getter. Presence is what counts, not the value:
        /// <c>radius=""</c> is an author resetting the radius, and it still attaches the panel.
        /// </summary>
        public static readonly string[] PanelAttaching =
        {
            "color", "radius", "borderWidth", "borderColor", "glow", "glowColor",
            "glass", "frost", "depth", "dispersion", "lightAngle", "lightIntensity",
            "saturation", "noise",
        };

        /// <summary>
        /// <see cref="PanelAttaching"/> plus <c>weld</c>. Weld is deliberately not above: it builds a
        /// <c>GlassGroupPanel</c> on a <em>child</em> (<c>Graphic</c> is
        /// <c>[DisallowMultipleComponent]</c>, and the carrier may already need a panel of its own
        /// for the group-level parameters), so a Frame with only <c>weld=</c> still has no Graphic.
        /// On a layout-only container every one of these is dropped just the same.
        /// </summary>
        public static readonly string[] All =
        {
            "color", "radius", "borderWidth", "borderColor", "glow", "glowColor",
            "glass", "frost", "depth", "dispersion", "lightAngle", "lightIntensity",
            "saturation", "noise", "weld",
        };
    }
}
