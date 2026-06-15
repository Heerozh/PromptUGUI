using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;
        public string XmlSrcOverride;                       // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override void Bind(IScreen screen, Action<T> close)
        {
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            // 取消通道：×（背景 / ESC 在后续 Task 接）
            screen.Get<Btn>("close").OnClick.Subscribe(_ => close(null)).AddTo(screen);

            // null Items 视作空（Open(null,…) 不该在 Carousel.Rebuild 里 NRE；空列表「禁确认」是 Task 4）
            Items ??= System.Array.Empty<T>();

            var car = screen.Get<Carousel>("cards");
            car.BindItems(
                Observable.Return((IReadOnlyList<T>)Items),
                (IControl card, T item) =>
                {
                    BindCard?.Invoke(card, item);
                    // 卡片点击（A+C）在 Task 3 接：那时引入 per-card index + AttachCardClick
                }).AddTo(screen);

            var ok = screen.Get<Btn>("confirm");
            if (!string.IsNullOrEmpty(ConfirmLabel)) ok.Text = ConfirmLabel;
            ok.OnClick.Subscribe(_ =>
            {
                int cur = car.Current;
                if (cur >= 0 && cur < Items.Count) close(Items[cur]);
            }).AddTo(screen);
        }
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        public static UnityEngine.Awaitable<T> Open<T>(
            IReadOnlyList<T> items,
            Action<IControl, T> bind,
            string title = null,
            string confirmLabel = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
            => UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
            {
                Items = items,
                BindCard = bind,
                Title = title,
                ConfirmLabel = confirmLabel,
                Configure = configure,
            }, mode, ct);
    }
}
