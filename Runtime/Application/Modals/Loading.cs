namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// "加载中" overlay 的门面。挡屏、转圈、不接受用户输入,由代码主动关闭。
    /// 不是 modal —— 坐在 dialog 栈之下,与 dialog 共存。
    /// </summary>
    public static class Loading
    {
        // .ui 后缀:Unity 只剥离 .ui.xml 文件名的最后 .xml
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";

        /// <summary>overlay 的 sortingOrder。须低于 <see cref="UI.Modal.SortingOrderBase"/>(默认 1000)。</summary>
        public static int SortingOrder { get; set; } = 500;

        /// <param name="configure">
        /// Optional post-bind hook receiving the live overlay <see cref="IScreen"/> — same shape as
        /// the dialog modals' <c>configure</c>. Runs after the builtin text bind, so it can reach
        /// any control (custom spinner, extra text, …) without subclassing.
        /// </param>
        public static LoadingHandle Open(string text = null, System.Action<IScreen> configure = null)
            => LoadingOverlay.Open(text, configure);
    }

    /// <summary>
    /// <see cref="Loading.Open"/> 返回的句柄。<see cref="Close"/> 关闭对应 overlay,幂等。
    /// </summary>
    public sealed class LoadingHandle
    {
        private readonly LoadingOverlay.LoadingEntry _entry;

        internal LoadingHandle(LoadingOverlay.LoadingEntry entry) => _entry = entry;

        public bool IsClosed => _entry.Closed;

        public void Close() => LoadingOverlay.CloseEntry(_entry);
    }
}
