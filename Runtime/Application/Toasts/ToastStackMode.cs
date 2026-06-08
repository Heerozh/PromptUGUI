namespace PromptUGUI.Application.Toasts
{
    /// <summary>Toast 的堆叠行为，由每次 <see cref="UI.Toast.Show"/> 选择。</summary>
    public enum ToastStackMode
    {
        /// <summary>"继承全局 <see cref="UI.Toast.DefaultStackMode"/>" 的哨兵，仅作 Show 参数缺省值用。</summary>
        Default = 0,

        /// <summary>立刻浮现，旧的被顶离基准锚点，多条共存。</summary>
        Stacked = 1,

        /// <summary>排队，等当前可见 toast 全部消失后才单独浮现（FIFO）。</summary>
        Sequential = 2,
    }
}
