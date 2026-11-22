namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The control that owns a <see cref="PuiButton"/> / <see cref="PuiToggle"/> / <see cref="PuiDropdown"/> and turns its
    /// user activations into sound cues (spec 2026-11-23-ui-sfx-cues §7.1). The Selectable only reports; the owner knows
    /// what the activation did — a click, a flip, an expand — and which kind and action that is. An internal button
    /// without an owner sink (a carousel dot, a slide-box card) raises nothing through this path.
    ///
    /// <para>Called for real input only: a left-button pointer click or a Submit past the focus-cursor wake gate, while the
    /// Selectable is active. <c>onClick.Invoke()</c> / <c>isOn =</c> from code never get here.</para>
    /// </summary>
    internal interface ISfxActivationSink
    {
        /// <summary>
        /// Before the Selectable handles the activation. <paramref name="interactable"/> false = it will refuse it — the
        /// owner raises <c>deny</c>.
        /// </summary>
        public void BeginSfxActivation(bool interactable);

        /// <summary>After the Selectable handled it (also when it threw).</summary>
        public void EndSfxActivation();
    }
}
