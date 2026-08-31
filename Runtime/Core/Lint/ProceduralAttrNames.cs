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
            "innerGlow", "innerGlowColor",
            "glass", "frost", "depth", "dispersion", "lightAngle", "lightIntensity",
            "saturation", "noise",
        };

        /// <summary>
        /// <see cref="PanelAttaching"/> plus <c>weld</c> and <c>seam</c>. Neither is above: weld builds a
        /// <c>GlassGroupPanel</c> on a <em>child</em> (<c>Graphic</c> is
        /// <c>[DisallowMultipleComponent]</c>, and the carrier may already need a panel of its own
        /// for the group-level parameters), and <c>seam</c> is a value that fused pane reads — so a
        /// Frame with only <c>weld=</c> / <c>seam=</c> still has no Graphic.
        /// On a layout-only container every one of these is dropped just the same.
        /// </summary>
        public static readonly string[] All =
        {
            "color", "radius", "borderWidth", "borderColor", "glow", "glowColor",
            "innerGlow", "innerGlowColor",
            "glass", "frost", "depth", "dispersion", "lightAngle", "lightIntensity",
            "saturation", "noise", "weld", "seam",
        };

        /// <summary>
        /// <see cref="All"/> minus <c>color</c>: the attributes that do nothing at all without a
        /// <c>ProceduralPanel</c>, and therefore nothing at all outside <c>&lt;Frame&gt;</c>.
        ///
        /// <para><c>color</c> is the one that drops out, because it is a plain tint on any control
        /// that carries an <c>Image</c>. That single exception is what separates a control like
        /// <c>&lt;Btn&gt;</c> (has an Image: <c>color</c> and <c>sprite</c> work, the rest is
        /// dropped) from a layout-only container (has nothing: all of it is dropped).</para>
        /// </summary>
        /// <summary>
        /// Shape for a layer INSIDE a control (spec §6) — <c>&lt;layer&gt;Radius</c>, one attribute
        /// per inner surface. Deliberately shape-only: glass on an inner layer samples the same
        /// backdrop as the layer beneath it and the two come out identical.
        ///
        /// <para>Because each inner surface is driven by exactly one attribute, a base-less
        /// <c>fillRadius.mobile</c> toggles that surface wholesale and therefore reverts on its own —
        /// which is what lets <see cref="VariantBaseRules"/> exempt it.</para>
        /// </summary>
        public static readonly string[] InnerLayerRadius =
        {
            "fillRadius", "handleRadius", "frameRadius", "maskRadius",
        };

        public static readonly string[] NeedsPanel =
        {
            "radius", "borderWidth", "borderColor", "glow", "glowColor",
            "innerGlow", "innerGlowColor",
            "glass", "frost", "depth", "dispersion", "lightAngle", "lightIntensity",
            "saturation", "noise", "weld", "seam",
        };
    }
}
