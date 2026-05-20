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

        public static LoadingHandle Open(string text = null)
            => LoadingOverlay.Open(text);
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
