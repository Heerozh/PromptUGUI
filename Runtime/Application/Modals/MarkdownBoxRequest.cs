using System;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class MarkdownBoxRequest : ModalRequest<bool>
    {
        public string Text;                       // markdown 源文
        public string Title;                      // null/空 → 隐藏标题行
        public Action<string> OnLinkClicked;      // null → 默认 UI.Markdown.HandleLink

        public override string XmlSrc => MarkdownBox.XmlSrc;

        public override void Bind(IScreen screen, Action<bool> close)
        {
            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            var md = screen.Get<PromptUGUI.Controls.Markdown>("markdown");
            md.Text = Text ?? "";
            // C#9:lambda 无自然委托类型,不能写 `OnLinkClicked ?? (url => ...)`,
            // 在订阅内分支即可。传了 OnLinkClicked 则完全接管,不叠加默认分发。
            var onLink = OnLinkClicked;
            md.OnLinkClicked.Subscribe(url =>
            {
                if (onLink != null) onLink(url);
                else UI.Markdown.HandleLink(url);
            }).AddTo(screen);

            screen.Get<PromptUGUI.Controls.Btn>("close")
                .OnClick.Subscribe(_ => close(true)).AddTo(screen);

            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => close(true)).AddTo(screen);
        }

        public override bool TryEscape(out bool result)
        {
            result = true;   // 点背景都能关,ESC 行为一致
            return true;
        }
    }

    public static class MarkdownBox
    {
        // 必须带 .ui 后缀：Unity 只剥离 .ui.xml 文件名的最后 .xml。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MarkdownBox.ui";

        /// <summary>无按钮的富文本只读模态;关闭即完成(×/点背景/ESC 三通道)。</summary>
        public static async UnityEngine.Awaitable Open(
            string markdown,
            string title = null,
            Action<string> onLinkClicked = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default)
            => await UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = markdown,
                Title = title,
                OnLinkClicked = onLinkClicked,
                Configure = configure,
            }, mode, ct);
    }
}
