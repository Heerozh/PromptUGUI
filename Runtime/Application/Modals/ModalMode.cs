namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// dialog 的显示行为,由每次 <see cref="UI.Modal.OpenAsync"/> 选择。
    /// </summary>
    public enum ModalMode
    {
        /// <summary>立刻压到显示栈顶。默认。</summary>
        Popup = 0,

        /// <summary>等显示栈清空后作为新栈底显示;多个 Queued 之间 FIFO。</summary>
        Queued = 1,
    }
}
