using System;
using System.Threading;
using R3;
using UnityEngine.Networking;

namespace PromptUGUI.Application.Modals
{
    public sealed class MarkdownBoxRequest : ModalRequest<bool>
    {
        public string Text;                       // markdown 源文
        public string Title;                      // null/空 → 隐藏标题行
        public Action<string> OnLinkClicked;      // null → 默认 UI.Markdown.HandleLink

        /// <summary>非 null 时忽略 <see cref="Text"/>:先显示 <see cref="LoadingText"/>,
        /// loader 完成后热替换;关窗(任何通道)自动取消传入的 ct。</summary>
        public Func<CancellationToken, UnityEngine.Awaitable<string>> Loader;
        public string LoadingText = "*Loading…*";

        public override string XmlSrc => MarkdownBox.XmlSrc;

        public override void Bind(IScreen screen, Action<bool> close)
        {
            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            var md = screen.Get<PromptUGUI.Controls.Markdown>("markdown");
            if (Loader != null)
            {
                md.Text = LoadingText;
                var cts = new CancellationTokenSource();
                // 关窗(×/backdrop/ESC/外部 ct)→ Screen Dispose → 取消加载
                Disposable.Create(() => cts.Cancel()).AddTo(screen);
                _ = FillAsync(md, Loader, cts.Token);
            }
            else
            {
                md.Text = Text ?? "";
            }

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

        private static async UnityEngine.Awaitable FillAsync(
            PromptUGUI.Controls.Markdown md,
            Func<CancellationToken, UnityEngine.Awaitable<string>> loader,
            CancellationToken ct)
        {
            string result;
            try
            {
                result = await loader(ct);
            }
            catch (OperationCanceledException)
            {
                return;                              // 关窗正常路径
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                UnityEngine.Debug.LogWarning($"MarkdownBox loader failed: {ex.Message}");
                md.Text = "**Failed to load.**\n\n" + ex.Message;
                return;
            }
            if (ct.IsCancellationRequested) return;  // 迟到的结果:控件已销毁,不得触碰
            md.Text = result;
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
            CancellationToken ct = default)
            => await UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = markdown,
                Title = title,
                OnLinkClicked = onLinkClicked,
                Configure = configure,
            }, mode, ct);

        /// <summary>延迟内容:先开窗显示占位 loading,loader 完成后热替换;
        /// 关窗自动取消 loader 的 ct。鉴权内容用此重载走游戏自己的网络栈。</summary>
        public static async UnityEngine.Awaitable Open(
            Func<CancellationToken, UnityEngine.Awaitable<string>> loader,
            string title = null,
            Action<string> onLinkClicked = null,
            string loadingText = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            CancellationToken ct = default)
        {
            var req = new MarkdownBoxRequest
            {
                Loader = loader,
                Title = title,
                OnLinkClicked = onLinkClicked,
                Configure = configure,
            };
            if (loadingText != null) req.LoadingText = loadingText;
            await UI.Modal.OpenAsync(req, mode, ct);
        }

        /// <summary>裸 GET 便捷重载(无鉴权;镜像 UseWebImageResolver 的取数模式)。</summary>
        public static UnityEngine.Awaitable OpenUrl(
            string url,
            string title = null,
            Action<string> onLinkClicked = null,
            string loadingText = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            CancellationToken ct = default)
            => Open(ct2 => FetchAsync(url, ct2),
                title, onLinkClicked, loadingText, mode, configure, ct);

        private static async UnityEngine.Awaitable<string> FetchAsync(
            string url, CancellationToken ct)
        {
            using var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            var acs = new UnityEngine.AwaitableCompletionSource<bool>();
            op.completed += _ => acs.TrySetResult(true);
            using var reg = ct.Register(() => req.Abort());
            if (!op.isDone) await acs.Awaitable;
            ct.ThrowIfCancellationRequested();
            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException($"{url}: {req.error}");
            return req.downloadHandler.text;
        }
    }
}
