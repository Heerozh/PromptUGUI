using System;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class InputBoxRequest : ModalRequest<string>
    {
        public string Title;
        public string Message;
        public string Initial;
        public string Placeholder;
        public string ContentType;
        public string OkLabel;
        public string CancelLabel;

        public override string XmlSrc => InputBox.XmlSrc;

        public override void Bind(IScreen screen, Action<string> close)
        {
            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            // message 节点可选（覆盖 XML 可能删掉）
            try
            {
                var msgCtl = screen.Get<PromptUGUI.Controls.Text>("message");
                if (string.IsNullOrEmpty(Message)) msgCtl.GameObject.SetActive(false);
                else msgCtl.TextValue = Message;
            }
            catch (System.Collections.Generic.KeyNotFoundException) { /* message element is optional */ }

            var field = screen.Get<PromptUGUI.Controls.InputField>("field");
            if (!string.IsNullOrEmpty(ContentType)) field.ContentType = ContentType;
            if (Placeholder != null) field.Placeholder = Placeholder;
            field.TextValue = Initial ?? "";

            // 回车 = 确定；OnSubmit 直接给出当前文本。
            field.OnSubmit.Subscribe(v => close(v)).AddTo(screen);

            var ok = screen.Get<PromptUGUI.Controls.Btn>("ok");
            if (!string.IsNullOrEmpty(OkLabel)) ok.Text = OkLabel;
            ok.OnClick.Subscribe(_ => close(field.TextValue)).AddTo(screen);

            var cancel = screen.Get<PromptUGUI.Controls.Btn>("cancel");
            if (!string.IsNullOrEmpty(CancelLabel)) cancel.Text = CancelLabel;
            cancel.OnClick.Subscribe(_ => close(null)).AddTo(screen);
        }

        public override bool TryEscape(out string result)
        {
            result = null;   // ESC → 取消
            return true;
        }
    }

    public static class InputBox
    {
        // 必须带 .ui 后缀：Unity 只剥离 .ui.xml 文件名的最后 .xml。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/InputBox.ui";

        public static UnityEngine.Awaitable<string> Open(
            string title,
            string message = null,
            string initial = null,
            string placeholder = null,
            string contentType = null,
            string okLabel = null,
            string cancelLabel = null,
            ModalMode mode = ModalMode.Popup)
            => UI.Modal.OpenAsync(new InputBoxRequest
            {
                Title = title,
                Message = message,
                Initial = initial,
                Placeholder = placeholder,
                ContentType = contentType,
                OkLabel = okLabel,
                CancelLabel = cancelLabel,
            }, mode);
    }
}
