namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A Graphic that greys itself from the inside, instead of having its material swapped for
    /// <c>UI-Grayscale</c> by <c>DisabledGrayscaleController</c>.
    ///
    /// <para>Both implementors own their material and would lose everything it carries if it were
    /// swapped: a <see cref="ProceduralPanel"/> would lose its shape, border, glow and glass, and an
    /// <see cref="FxImage"/> its blur and glow — and in both cases the next parameter change would
    /// write the material straight back, silently undoing the greying. So the controller hands the
    /// state to the graphic and lets it fold the desaturation into what it already draws.</para>
    /// </summary>
    internal interface ISelfGrayscale
    {
        public void SetDisabledGrayscale(bool value);
    }
}
