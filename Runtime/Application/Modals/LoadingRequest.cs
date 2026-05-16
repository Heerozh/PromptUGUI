using System;
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class LoadingRequest : ModalRequest<Unit>
    {
        public string Text;

        public override string XmlSrc => Loading.XmlSrc;

        public override void Bind(IScreen screen, Action<Unit> close)
        {
            try
            {
                var textCtl = screen.Get<PromptUGUI.Controls.Text>("text");
                if (string.IsNullOrEmpty(Text)) textCtl.GameObject.SetActive(false);
                else textCtl.TextValue = Text;
            }
            catch (KeyNotFoundException) { /* text element is optional */ }
        }
    }

    public sealed class LoadingHandle
    {
        private readonly IModalEntry _entry;
        private bool _closed;

        public bool IsClosed => _closed;

        internal LoadingHandle(IModalEntry entry) => _entry = entry;

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _entry.ResolveExternally();
        }
    }

    public static class Loading
    {
        // .ui 后缀对齐 MessageBox：Unity 只剥离 .ui.xml 文件名的最后 .xml
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";

        public static LoadingHandle Open(string text = null)
        {
            var entry = UI.Modal.EnqueueRequest(new LoadingRequest { Text = text });
            return new LoadingHandle(entry);
        }
    }
}
