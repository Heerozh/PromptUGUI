namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// One user activation of a two-state control (a Toggle, a Tab; spec 2026-11-23-ui-sfx-cues §7.3, §7.4): what the
    /// value was before it, and whether it already raised its cue. A flip only counts while the window is open, only when
    /// it really changed the value — uGUI re-sends <c>onValueChanged(true)</c> for a re-click inside a group — and only once:
    /// a handler that flips the value back during the same click raises no second cue (P29). A flip the group makes on
    /// another member, or code makes outside the window, is not seen at all.
    /// </summary>
    internal sealed class SfxActivation
    {
        private bool _open;
        private bool _raised;
        private bool _before;

        public void Begin(bool valueBefore)
        {
            _open = true;
            _raised = false;
            _before = valueBefore;
        }

        public void End() => _open = false;

        /// <summary>True once per activation, for the first value that differs from the one it began with.</summary>
        public bool TakeFlip(bool value)
        {
            if (!_open || _raised || value == _before) return false;
            _raised = true;
            return true;
        }
    }
}
