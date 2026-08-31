namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Implemented by every control that accepts <c>width="hug"</c> / <c>height="hug"</c>
    /// (<c>HugRules.HugTags</c>: <c>&lt;VStack&gt;</c>, <c>&lt;HStack&gt;</c>, <c>&lt;Grid&gt;</c>,
    /// <c>&lt;ScrollList&gt;</c>). It answers the one question hug asks: <b>how big is my content on
    /// this axis</b> — which is not always this RectTransform's own preferred size. A layout-group
    /// container's content IS its own group's preferred size; a <c>&lt;ScrollList&gt;</c>'s is its
    /// inner content node, because the root is a fixed viewport that the content scrolls inside.
    ///
    /// <para>Read from inside the layout pass — by <see cref="ClampFitter"/> (free positioning) or
    /// <see cref="HugElement"/> (inside a layout group) — after uGUI has run
    /// <c>CalculateLayoutInput*</c> bottom-up, so reading a child group's <c>preferredWidth</c> /
    /// <c>preferredHeight</c> here is valid. Implementations must NOT route through
    /// <c>LayoutUtility.GetPreferredSize(this node)</c>: <see cref="HugElement"/> lives on that node
    /// and would answer its own question.</para>
    ///
    /// <para>Spec 2026-08-31-hug-reveal-flip-checked-design §1.4.1.</para>
    /// </summary>
    internal interface IHugContent
    {
        /// <param name="axis">0 = X (width), 1 = Y (height).</param>
        public float ContentSize(int axis);
    }
}
