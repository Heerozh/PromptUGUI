using System;
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class MessageBoxRequest : ModalRequest<Btn>
    {
        public string Text;
        public Btn Buttons = Btn.OK;
        public string Icon;
        public string Title;
        public IReadOnlyList<(string label, Btn key)> CustomLabels;

        public override string XmlSrc => MessageBox.XmlSrc;

        public override void Bind(IScreen screen, Action<Btn> close)
        {
            screen.Get<PromptUGUI.Controls.Text>("text").TextValue = Text ?? "";

            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            try
            {
                var iconCtl = screen.Get<PromptUGUI.Controls.Icon>("icon");
                if (string.IsNullOrEmpty(Icon)) iconCtl.GameObject.SetActive(false);
                else iconCtl.Name = Icon;
            }
            catch (System.Collections.Generic.KeyNotFoundException) { /* icon element is optional */ }

            BindBtn(screen, "ok", Btn.OK, close);
            BindBtn(screen, "cancel", Btn.Cancel, close);
            BindBtn(screen, "yes", Btn.Yes, close);
            BindBtn(screen, "no", Btn.No, close);
            BindBtn(screen, "close", Btn.Close, close);
        }

        public override bool TryEscape(out Btn result)
        {
            if ((Buttons & Btn.Cancel) != 0) { result = Btn.Cancel; return true; }
            if ((Buttons & Btn.No) != 0) { result = Btn.No; return true; }
            if ((Buttons & Btn.Close) != 0) { result = Btn.Close; return true; }
            result = Btn.None;
            return false;
        }

        private void BindBtn(IScreen screen, string id, Btn flag, Action<Btn> close)
        {
            var btn = screen.Get<PromptUGUI.Controls.Btn>(id);
            if ((Buttons & flag) == 0) { btn.GameObject.SetActive(false); return; }

            if (CustomLabels != null)
            {
                for (var i = 0; i < CustomLabels.Count; i++)
                {
                    var (label, key) = CustomLabels[i];
                    if (key == flag && !string.IsNullOrEmpty(label)) { btn.Text = label; break; }
                }
            }
            btn.OnClick.Subscribe(_ => close(flag)).AddTo(screen);
        }
    }

    public static class MessageBox
    {
        // 必须带 .ui 后缀：Unity 只剥离 .ui.xml 文件名的最后 .xml，所以
        // Resources 里 asset 的查找名是 "MessageBox.ui" 而不是 "MessageBox"。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox.ui";

        public static UnityEngine.Awaitable<Btn> Open(
            string text, Btn buttons = Btn.OK, string icon = null, string title = null)
            => UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = text, Buttons = buttons, Icon = icon, Title = title,
            });

        public static UnityEngine.Awaitable<Btn> Open(
            string text,
            System.Collections.Generic.IEnumerable<(string label, Btn key)> buttons,
            string icon = null, string title = null)
        {
            var list = new System.Collections.Generic.List<(string, Btn)>(buttons);
            var mask = Btn.None;
            foreach (var (_, k) in list) mask |= k;
            return UI.Modal.OpenAsync(new MessageBoxRequest {
                Text = text, CustomLabels = list, Buttons = mask, Icon = icon, Title = title,
            });
        }
    }
}
